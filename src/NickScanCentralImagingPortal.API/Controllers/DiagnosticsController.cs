using System.IO;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/[controller]")]
    public class DiagnosticsController : ControllerBase
    {
        private readonly ILogger<DiagnosticsController> _logger;
        private readonly IcumDownloadsDbContext _icumContext;
        private readonly ApplicationDbContext _appContext;
        private readonly IConfiguration _configuration;
        private readonly IHttpClientFactory _httpClientFactory;

        public DiagnosticsController(
            ILogger<DiagnosticsController> logger,
            IcumDownloadsDbContext icumContext,
            ApplicationDbContext appContext,
            IConfiguration configuration,
            IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _icumContext = icumContext;
            _appContext = appContext;
            _configuration = configuration;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet("system")]
        public async Task<ActionResult<SystemDiagnostics>> GetSystemDiagnostics()
        {
            try
            {
                _logger.LogInformation("[DIAGNOSTICS] Running system diagnostics");

                var diagnostics = new SystemDiagnostics
                {
                    CheckedAt = DateTime.UtcNow
                };

                // 1. Database connection check
                try
                {
                    diagnostics.DatabaseConnected = await _appContext.Database.CanConnectAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[DIAGNOSTICS] Database connection check failed");
                    diagnostics.DatabaseConnected = false;
                }

                // 2. File system access check (ICUMS Downloads path or temp)
                try
                {
                    var downloadsPath = _configuration["ICUMS:DownloadsPath"] ?? Path.GetTempPath();
                    diagnostics.FileSystemAccessible = Directory.Exists(downloadsPath);
                    if ((diagnostics.FileSystemAccessible != true) && !string.IsNullOrEmpty(downloadsPath))
                    {
                        // Try to create and delete to verify write access
                        var testPath = Path.Combine(downloadsPath, ".diagnostics_test");
                        try
                        {
                            Directory.CreateDirectory(downloadsPath);
                            System.IO.File.WriteAllText(testPath, "test");
                            System.IO.File.Delete(testPath);
                            diagnostics.FileSystemAccessible = true;
                        }
                        catch (UnauthorizedAccessException uaEx)
                        {
                            // Round-1 audit C-3: previously silent. The "write
                            // access OK?" probe used to claim success even on
                            // permission denial, which made operators trust an
                            // unwritable folder.
                            _logger.LogWarning(uaEx, "[DIAGNOSTICS] Write access denied on {Path}", downloadsPath);
                            diagnostics.FileSystemAccessible = false;
                        }
                        catch (IOException ioEx)
                        {
                            _logger.LogWarning(ioEx, "[DIAGNOSTICS] IO error during write probe on {Path}", downloadsPath);
                            diagnostics.FileSystemAccessible = false;
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[DIAGNOSTICS] File system check failed");
                    diagnostics.FileSystemAccessible = false;
                }

                // 3. ICUMS API reachability check
                try
                {
                    var icumsBaseUrl = _configuration["ICUMS:BaseUrl"];
                    if (string.IsNullOrEmpty(icumsBaseUrl))
                    {
                        diagnostics.IcumsApiReachable = null; // Not configured
                    }
                    else
                    {
                        // 2026-04-27: was `new HttpClient()` per call — socket exhaustion risk
                        // (especially on the diagnostics path which gets polled by the health UI).
                        var client = _httpClientFactory.CreateClient("Diagnostics");
                        client.Timeout = TimeSpan.FromSeconds(10);
                        await client.GetAsync($"{icumsBaseUrl.TrimEnd('/')}/");
                        diagnostics.IcumsApiReachable = true; // Got a response = server is reachable
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[DIAGNOSTICS] ICUMS API reachability check failed");
                    diagnostics.IcumsApiReachable = false;
                }

                return Ok(diagnostics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DIAGNOSTICS] Error running system diagnostics");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 1.13.0 — CMR upgrade lifecycle health check.
        ///
        /// Returns counts that let you watch the CMR→IM/EX upgrade flow over time:
        /// total CMR rows in the pre-declaration backlog, oldest CMR row, total
        /// rows that have been upgraded (have provenance set), upgrades in the
        /// last 24 hours, and any "stuck" half-state CMR rows (CMR clearance type
        /// + non-empty declaration number, which after 1.13.0 should be zero
        /// because the implicit upgrade handler catches them on ingest).
        ///
        /// AdminOnly. Designed to be polled by a dashboard or grep'd from cron.
        /// </summary>
        [HttpGet("cmr-lifecycle")]
        public async Task<ActionResult<CmrLifecycleHealth>> GetCmrLifecycleHealth()
        {
            try
            {
                var nowUtc = DateTime.UtcNow;
                var oneDayAgo = nowUtc.AddDays(-1);
                var sevenDaysAgo = nowUtc.AddDays(-7);

                var pendingCmr = await _icumContext.BOEDocuments
                    .CountAsync(b => b.ClearanceType == "CMR");

                var pendingNoDeclaration = await _icumContext.BOEDocuments
                    .CountAsync(b => b.ClearanceType == "CMR"
                                  && (b.DeclarationNumber == null || b.DeclarationNumber == ""));

                var stuckHalfState = await _icumContext.BOEDocuments
                    .CountAsync(b => b.ClearanceType == "CMR"
                                  && b.DeclarationNumber != null
                                  && b.DeclarationNumber != "");

                var upgradedTotal = await _icumContext.BOEDocuments
                    .CountAsync(b => b.OriginalClearanceType != null);

                var upgradedLast24h = await _icumContext.BOEDocuments
                    .CountAsync(b => b.CmrUpgradedAt != null && b.CmrUpgradedAt >= oneDayAgo);

                var upgradedLast7d = await _icumContext.BOEDocuments
                    .CountAsync(b => b.CmrUpgradedAt != null && b.CmrUpgradedAt >= sevenDaysAgo);

                var oldestPendingCmr = await _icumContext.BOEDocuments
                    .Where(b => b.ClearanceType == "CMR"
                             && (b.DeclarationNumber == null || b.DeclarationNumber == ""))
                    .OrderBy(b => b.CreatedAt)
                    .Select(b => (DateTime?)b.CreatedAt)
                    .FirstOrDefaultAsync();

                var byOriginalType = await _icumContext.BOEDocuments
                    .Where(b => b.OriginalClearanceType != null)
                    .GroupBy(b => new { b.OriginalClearanceType, b.ClearanceType })
                    .Select(g => new CmrUpgradeBreakdown
                    {
                        OriginalClearanceType = g.Key.OriginalClearanceType ?? "",
                        CurrentClearanceType = g.Key.ClearanceType ?? "",
                        Count = g.Count()
                    })
                    .ToListAsync();

                return Ok(new CmrLifecycleHealth
                {
                    CheckedAt = nowUtc,
                    PendingCmrTotal = pendingCmr,
                    PendingCmrWithoutDeclaration = pendingNoDeclaration,
                    StuckHalfStateCmr = stuckHalfState,
                    OldestPendingCmrCreatedAt = oldestPendingCmr,
                    OldestPendingAgeHours = oldestPendingCmr.HasValue
                        ? Math.Round((nowUtc - oldestPendingCmr.Value).TotalHours, 2)
                        : (double?)null,
                    UpgradedTotal = upgradedTotal,
                    UpgradedLast24Hours = upgradedLast24h,
                    UpgradedLast7Days = upgradedLast7d,
                    UpgradeBreakdown = byOriginalType,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DIAGNOSTICS] CMR lifecycle health check failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// 1.14.0 — Record completeness health check.
        ///
        /// Returns the current state of the record-completeness reconciliation loop:
        /// total records, per-status counts, the integrity gap (records that are
        /// missing containers ICUMS says should exist), reconciliation worker stats
        /// (last tick timing, throughput counters), and the archive rule settings.
        ///
        /// AdminOnly. Designed to be polled by a dashboard or grep'd from cron.
        /// This is the single most valuable readout for measuring whether the
        /// record-anchored model is catching integrity gaps in real time.
        /// </summary>
        [HttpGet("record-completeness")]
        public async Task<ActionResult<RecordCompletenessHealth>> GetRecordCompletenessHealth()
        {
            try
            {
                var nowUtc = DateTime.UtcNow;

                var totalRecords = await _appContext.RecordCompletenessStatuses.CountAsync();

                var byStatusRaw = await _appContext.RecordCompletenessStatuses
                    .GroupBy(r => r.Status)
                    .Select(g => new { Status = g.Key, Count = g.Count() })
                    .ToListAsync();
                var byStatus = byStatusRaw.ToDictionary(x => x.Status, x => x.Count);

                // Integrity gap: records with > 1 expected container and how they're covered
                var multiContainer = await _appContext.RecordCompletenessStatuses
                    .Where(r => r.TotalExpectedContainers > 1)
                    .Select(r => new
                    {
                        r.TotalExpectedContainers,
                        r.ContainersReady,
                        r.ContainersScanned,
                        r.ContainersAwaitingScan,
                    })
                    .ToListAsync();

                var multiContainerRecords = multiContainer.Count;
                var fullyCovered = multiContainer.Count(r => r.ContainersAwaitingScan == 0);
                var partiallyCovered = multiContainer.Count(r => r.ContainersAwaitingScan > 0 && r.ContainersAwaitingScan < r.TotalExpectedContainers);
                var allMissing = multiContainer.Count(r => r.ContainersAwaitingScan == r.TotalExpectedContainers);
                var totalExpectedContainers = multiContainer.Sum(r => r.TotalExpectedContainers);
                var containersObserved = multiContainer.Sum(r => r.TotalExpectedContainers - r.ContainersAwaitingScan);
                var containersGap = totalExpectedContainers - containersObserved;

                var settings = await _appContext.AnalysisSettings.AsNoTracking().FirstOrDefaultAsync();
                var archiveAfterDays = settings?.RecordArchiveAfterDays ?? 30;
                var intervalMinutes = settings?.RecordReconciliationIntervalMinutes ?? 30;
                var enabled = settings?.RecordReconciliationEnabled ?? true;

                var state = await _appContext.RecordReconciliationStates
                    .AsNoTracking()
                    .FirstOrDefaultAsync(s => s.Id == 1);

                var archiveCutoff = nowUtc.AddDays(-archiveAfterDays);
                var eligibleForArchive = await _appContext.RecordCompletenessStatuses
                    .Where(r => (r.Status == "Pending" || r.Status == "PartiallyReady")
                             && r.LastNewContainerAtUtc != null
                             && r.LastNewContainerAtUtc < archiveCutoff
                             && r.ArchivedAtUtc == null)
                    .CountAsync();

                return Ok(new RecordCompletenessHealth
                {
                    CheckedAt = nowUtc,
                    TotalRecords = totalRecords,
                    ByStatus = byStatus,
                    IntegrityGap = new RecordIntegrityGap
                    {
                        MultiContainerRecords = multiContainerRecords,
                        FullyCovered = fullyCovered,
                        PartiallyCovered = partiallyCovered,
                        AllMissing = allMissing,
                        TotalExpectedContainers = totalExpectedContainers,
                        ContainersObserved = containersObserved,
                        ContainersGap = containersGap,
                    },
                    Reconciliation = new RecordReconciliationStats
                    {
                        Enabled = enabled,
                        IntervalMinutes = intervalMinutes,
                        LastWatermarkUtc = state?.LastWatermarkUtc,
                        LastTickAtUtc = state?.LastTickAtUtc,
                        LastTickDurationMs = state?.LastTickDurationMs,
                        RecordsCreatedTotal = state?.RecordsCreatedTotal ?? 0,
                        RecordsUpdatedTotal = state?.RecordsUpdatedTotal ?? 0,
                        ContainersPromotedTotal = state?.ContainersPromotedTotal ?? 0,
                        RecordsArchivedTotal = state?.RecordsArchivedTotal ?? 0,
                    },
                    ArchiveRule = new RecordArchiveRule
                    {
                        CutoffDays = archiveAfterDays,
                        EligibleForArchive = eligibleForArchive,
                    },
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DIAGNOSTICS] Record completeness health check failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("container/{containerNumber}")]
        public async Task<ActionResult<ContainerDiagnostics>> DiagnoseContainer(string containerNumber)
        {
            try
            {
                _logger.LogInformation("[DIAGNOSTICS] Diagnosing container: {ContainerNumber}", containerNumber);

                var diagnostics = new ContainerDiagnostics
                {
                    ContainerNumber = containerNumber,
                    CheckedAt = DateTime.UtcNow
                };

                // 1. Check DownloadedFiles table
                var downloadedFiles = await _icumContext.DownloadedFiles
                    .Where(f => f.FileName.Contains(containerNumber))
                    .OrderByDescending(f => f.DownloadDate)
                    .Take(5)
                    .Select(f => new
                    {
                        f.Id,
                        f.FileName,
                        f.FilePath,
                        f.DownloadDate,
                        f.ProcessingStatus,
                        f.ErrorMessage,
                        f.RecordCount
                    })
                    .ToListAsync();

                diagnostics.DownloadedFilesCount = downloadedFiles.Count;
                diagnostics.DownloadedFiles = downloadedFiles.Select(f => new DownloadedFileInfo
                {
                    Id = f.Id,
                    FileName = f.FileName,
                    FilePath = f.FilePath,
                    DownloadDate = f.DownloadDate,
                    ProcessingStatus = f.ProcessingStatus,
                    ErrorMessage = f.ErrorMessage,
                    RecordCount = f.RecordCount ?? 0
                }).ToList();

                // 2. Check BOEDocuments table
                var boeDocuments = await _icumContext.BOEDocuments
                    .Where(b => b.ContainerNumber == containerNumber)
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(5)
                    .Select(b => new
                    {
                        b.Id,
                        b.ContainerNumber,
                        b.DeclarationNumber,
                        b.BlNumber,
                        b.ClearanceType,
                        b.CreatedAt,
                        b.DownloadedFileId
                    })
                    .ToListAsync();

                diagnostics.BOEDocumentsCount = boeDocuments.Count;
                diagnostics.BOEDocuments = boeDocuments.Select(b => new BOEDocumentInfo
                {
                    Id = b.Id,
                    ContainerNumber = b.ContainerNumber,
                    DeclarationNumber = b.DeclarationNumber,
                    BlNumber = b.BlNumber,
                    ClearanceType = b.ClearanceType,
                    CreatedAt = b.CreatedAt,
                    DownloadedFileId = b.DownloadedFileId
                }).ToList();

                // 3. Check ContainerCompletenessStatus table
                var completenessStatus = await _appContext.ContainerCompletenessStatuses
                    .FirstOrDefaultAsync(c => c.ContainerNumber == containerNumber);

                if (completenessStatus != null)
                {
                    diagnostics.HasMapping = true;
                    diagnostics.MappingStatus = completenessStatus.Status;
                    diagnostics.MappingCreatedAt = completenessStatus.CreatedAt;
                }

                // 4. Check scanner data
                var fs6000Data = await _appContext.FS6000Scans
                    .AnyAsync(s => s.ContainerNumber == containerNumber);
                var aseData = await _appContext.AseScans
                    .AnyAsync(s => s.ContainerNumber == containerNumber);

                diagnostics.HasFS6000Data = fs6000Data;
                diagnostics.HasASEData = aseData;

                // 5. Check download queue
                var queueItem = await _icumContext.ICUMSDownloadQueue
                    .Where(q => q.ContainerNumber == containerNumber)
                    .OrderByDescending(q => q.QueuedAt)
                    .FirstOrDefaultAsync();

                if (queueItem != null)
                {
                    diagnostics.IsInQueue = true;
                    diagnostics.QueueStatus = queueItem.Status;
                    diagnostics.QueuedAt = queueItem.QueuedAt;
                    diagnostics.ProcessedAt = queueItem.CompletedAt;
                }

                // 6. Check physical files
                var downloadsPath = _configuration["ICUMS:DownloadsPath"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads";
                if (Directory.Exists(downloadsPath))
                {
                    var files = Directory.GetFiles(downloadsPath, $"*{containerNumber}*.json", SearchOption.AllDirectories);
                    diagnostics.PhysicalFilesCount = files.Length;
                    diagnostics.PhysicalFiles = files.Take(5).Select(f => new PhysicalFileInfo
                    {
                        FileName = Path.GetFileName(f),
                        FilePath = f,
                        FileSize = new FileInfo(f).Length,
                        LastModified = new FileInfo(f).LastWriteTime
                    }).ToList();
                }

                // 7. Summary
                diagnostics.Summary = GenerateSummary(diagnostics);

                return Ok(diagnostics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DIAGNOSTICS] Error diagnosing container: {ContainerNumber}", containerNumber);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        private string GenerateSummary(ContainerDiagnostics diag)
        {
            var issues = new List<string>();

            if (diag.PhysicalFilesCount > 0 && diag.DownloadedFilesCount == 0)
                issues.Add("❌ Physical files exist but not tracked in DownloadedFiles table");

            if (diag.DownloadedFilesCount > 0 && diag.BOEDocumentsCount == 0)
                issues.Add("❌ Files downloaded but not ingested into BOEDocuments table");

            if (diag.BOEDocumentsCount > 0 && !diag.HasMapping)
                issues.Add("⚠️ BOE data exists but no ContainerDataMapping created");

            if (diag.DownloadedFiles.Any(f => f.ProcessingStatus == "Pending"))
                issues.Add("⏳ Files are pending processing (waiting for ingestion service)");

            if (diag.DownloadedFiles.Any(f => f.ProcessingStatus == "Failed"))
                issues.Add("❌ Some files failed processing - check error messages");

            if (!diag.HasFS6000Data && !diag.HasASEData)
                issues.Add("⚠️ No scanner data found (FS6000 or ASE)");

            if (issues.Any())
                return string.Join("\n", issues);

            return "✅ All data present and properly linked";
        }
    }

    public class ContainerDiagnostics
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime CheckedAt { get; set; }

        // Physical files
        public int PhysicalFilesCount { get; set; }
        public List<PhysicalFileInfo> PhysicalFiles { get; set; } = new();

        // Downloaded files tracking
        public int DownloadedFilesCount { get; set; }
        public List<DownloadedFileInfo> DownloadedFiles { get; set; } = new();

        // BOE documents (ingested data)
        public int BOEDocumentsCount { get; set; }
        public List<BOEDocumentInfo> BOEDocuments { get; set; } = new();

        // Container mapping
        public bool HasMapping { get; set; }
        public string? MappingStatus { get; set; }
        public DateTime? MappingCreatedAt { get; set; }

        // Scanner data
        public bool HasFS6000Data { get; set; }
        public bool HasASEData { get; set; }

        // Queue status
        public bool IsInQueue { get; set; }
        public string? QueueStatus { get; set; }
        public DateTime? QueuedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }

        // Summary
        public string Summary { get; set; } = string.Empty;
    }

    public class PhysicalFileInfo
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime LastModified { get; set; }
    }

    public class DownloadedFileInfo
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public DateTime DownloadDate { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public int RecordCount { get; set; }
    }

    public class BOEDocumentInfo
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string? DeclarationNumber { get; set; }
        public string? BlNumber { get; set; }
        public string? ClearanceType { get; set; }
        public DateTime CreatedAt { get; set; }
        public int? DownloadedFileId { get; set; }
    }

    public class SystemDiagnostics
    {
        public DateTime CheckedAt { get; set; }
        public bool? DatabaseConnected { get; set; }
        public bool? FileSystemAccessible { get; set; }
        public bool? IcumsApiReachable { get; set; }
    }

    // ── CMR upgrade lifecycle health (1.13.0) ──────────────────────────────
    public class CmrLifecycleHealth
    {
        public DateTime CheckedAt { get; set; }
        /// <summary>Total rows currently sitting at clearancetype='CMR' (any sub-state).</summary>
        public int PendingCmrTotal { get; set; }
        /// <summary>The "classic" CMR backlog: clearancetype='CMR' AND no declaration yet.</summary>
        public int PendingCmrWithoutDeclaration { get; set; }
        /// <summary>Should be 0 after 1.13.0. Counts CMR rows with a non-empty declaration number — the implicit upgrade handler is supposed to catch these on ingest.</summary>
        public int StuckHalfStateCmr { get; set; }
        /// <summary>When the oldest still-pending CMR row first landed in NSCIM.</summary>
        public DateTime? OldestPendingCmrCreatedAt { get; set; }
        public double? OldestPendingAgeHours { get; set; }
        /// <summary>Total rows that have ever been upgraded (originalclearancetype IS NOT NULL).</summary>
        public int UpgradedTotal { get; set; }
        public int UpgradedLast24Hours { get; set; }
        public int UpgradedLast7Days { get; set; }
        public List<CmrUpgradeBreakdown> UpgradeBreakdown { get; set; } = new();
    }

    public class CmrUpgradeBreakdown
    {
        public string OriginalClearanceType { get; set; } = string.Empty;
        public string CurrentClearanceType { get; set; } = string.Empty;
        public int Count { get; set; }
    }

    // ── Record completeness health (1.14.0) ────────────────────────────────
    public class RecordCompletenessHealth
    {
        public DateTime CheckedAt { get; set; }
        public int TotalRecords { get; set; }
        public Dictionary<string, int> ByStatus { get; set; } = new();
        public RecordIntegrityGap IntegrityGap { get; set; } = new();
        public RecordReconciliationStats Reconciliation { get; set; } = new();
        public RecordArchiveRule ArchiveRule { get; set; } = new();
    }

    public class RecordIntegrityGap
    {
        /// <summary>Records with more than one expected container.</summary>
        public int MultiContainerRecords { get; set; }
        /// <summary>Multi-container records where every expected container has been observed at least once.</summary>
        public int FullyCovered { get; set; }
        /// <summary>Multi-container records where SOME but not all expected containers have been observed.</summary>
        public int PartiallyCovered { get; set; }
        /// <summary>Multi-container records where NO expected containers have been observed yet.</summary>
        public int AllMissing { get; set; }
        public int TotalExpectedContainers { get; set; }
        public int ContainersObserved { get; set; }
        public int ContainersGap { get; set; }
    }

    public class RecordReconciliationStats
    {
        public bool Enabled { get; set; }
        public int IntervalMinutes { get; set; }
        public DateTime? LastWatermarkUtc { get; set; }
        public DateTime? LastTickAtUtc { get; set; }
        public int? LastTickDurationMs { get; set; }
        public long RecordsCreatedTotal { get; set; }
        public long RecordsUpdatedTotal { get; set; }
        public long ContainersPromotedTotal { get; set; }
        public long RecordsArchivedTotal { get; set; }
    }

    public class RecordArchiveRule
    {
        public int CutoffDays { get; set; }
        public int EligibleForArchive { get; set; }
    }
}

