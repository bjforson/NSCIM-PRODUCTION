using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.FS6000;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize(Policy = "ScannerOperator")]
    [ApiController]
    [Route("api/[controller]")]
    public class FS6000Controller : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IFileSyncService _fileSyncService;
        private readonly IIngestionService _ingestionService;
        private readonly ILogger<FS6000Controller> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IOptions<FileSyncConfiguration> _fileSyncConfig;

        public FS6000Controller(
            ApplicationDbContext context,
            IFileSyncService fileSyncService,
            IIngestionService ingestionService,
            ILogger<FS6000Controller> logger,
            IServiceScopeFactory serviceScopeFactory,
            IOptions<FileSyncConfiguration> fileSyncConfig)
        {
            _context = context;
            _fileSyncService = fileSyncService;
            _ingestionService = ingestionService;
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _fileSyncConfig = fileSyncConfig;
        }

        [HttpGet("scans")]
        public async Task<ActionResult<FS6000ScanResponse>> GetScans(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? containerNumber = null,
            [FromQuery] string? syncStatus = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var defaultStartDate = startDate.HasValue
                    ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                    : DateTime.UtcNow.AddDays(-30);
                var defaultEndDate = endDate.HasValue
                    ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                    : DateTime.UtcNow;

                var query = _context.FS6000Scans
                    .Where(s => s.ScanTime >= defaultStartDate && s.ScanTime <= defaultEndDate)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(containerNumber))
                {
                    query = query.Where(s => s.ContainerNumber != null && s.ContainerNumber.Contains(containerNumber));
                }

                if (!string.IsNullOrEmpty(syncStatus))
                {
                    query = query.Where(s => s.SyncStatus == syncStatus);
                }

                var totalCount = await query.CountAsync();
                var scans = await query
                    .OrderByDescending(s => s.ScanTime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                // Map to DTO matching frontend expectations
                var scanDtos = scans.Select(s => new FS6000ScanDto
                {
                    Id = s.Id,
                    ContainerNumber = s.ContainerNumber,
                    ScanTime = s.ScanTime,
                    Origin = null, // FS6000 entity doesn't have Origin/Destination - these may come from ICUMS
                    Destination = null, // FS6000 entity doesn't have Origin/Destination - these may come from ICUMS
                    SyncStatus = s.SyncStatus
                }).ToList();

                return Ok(new FS6000ScanResponse
                {
                    Data = scanDtos,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving FS6000 scans");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("scans/{id}")]
        public async Task<ActionResult<FS6000Scan>> GetScan(Guid id)
        {
            try
            {
                var scan = await _context.FS6000Scans
                    .Include(s => s.Images)
                    .FirstOrDefaultAsync(s => s.Id == id);

                if (scan == null)
                {
                    return NotFound();
                }

                return Ok(scan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving FS6000 scan {ScanId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("scans/container/{containerNumber}")]
        public async Task<ActionResult<IEnumerable<FS6000Scan>>> GetScansByContainer(string containerNumber)
        {
            try
            {
                var scans = await _context.FS6000Scans
                    .Where(s => s.ContainerNumber == containerNumber)
                    .OrderByDescending(s => s.ScanTime)
                    .ToListAsync();

                return Ok(scans);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving FS6000 scans for container {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("sync/status")]
        public async Task<ActionResult<object>> GetSyncStatus()
        {
            try
            {
                var pendingCount = await _fileSyncService.GetPendingSyncCountAsync();
                var failedCount = await _fileSyncService.GetFailedSyncCountAsync();
                var isHealthy = await _fileSyncService.IsHealthyAsync();

                return Ok(new
                {
                    IsHealthy = isHealthy,
                    PendingSyncCount = pendingCount,
                    FailedSyncCount = failedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving sync status");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("ingestion/status")]
        public async Task<ActionResult<object>> GetIngestionStatus()
        {
            try
            {
                var pendingCount = await _ingestionService.GetPendingIngestionCountAsync();
                var failedCount = await _ingestionService.GetFailedIngestionCountAsync();
                var isHealthy = await _ingestionService.IsHealthyAsync();

                return Ok(new
                {
                    IsHealthy = isHealthy,
                    PendingIngestionCount = pendingCount,
                    FailedIngestionCount = failedCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ingestion status");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("process/folder")]
        public async Task<ActionResult> ProcessFolder([FromBody] ProcessFolderRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.FolderPath))
                {
                    return BadRequest("Folder path is required");
                }

                await _ingestionService.ProcessFolderAsync(request.FolderPath);
                return Ok(new { message = "Folder processing initiated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing folder {FolderPath}", request.FolderPath);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("statistics")]
        public async Task<ActionResult<object>> GetStatistics()
        {
            try
            {
                var totalScans = await _context.FS6000Scans.CountAsync();
                var completedScans = await _context.FS6000Scans.CountAsync(s => s.SyncStatus == "Completed");
                var pendingScans = await _context.FS6000Scans.CountAsync(s => s.SyncStatus == "Pending");
                var failedScans = await _context.FS6000Scans.CountAsync(s => s.SyncStatus == "Failed");

                var todayStart = DateTime.SpecifyKind(DateTime.UtcNow.Date, DateTimeKind.Utc);
                var todayEnd = todayStart.AddDays(1);
                var todayScans = await _context.FS6000Scans
                    .Where(s => s.ScanTime >= todayStart && s.ScanTime < todayEnd)
                    .CountAsync();

                var totalImages = await _context.FS6000Images.CountAsync();
                var totalFileProcessings = await _context.FS6000FileProcessings.CountAsync();
                var completedFileProcessings = await _context.FS6000FileProcessings.CountAsync(f => f.ProcessingStatus == "Completed");
                var failedFileProcessings = await _context.FS6000FileProcessings.CountAsync(f => f.ProcessingStatus == "Failed");

                return Ok(new
                {
                    Scans = new
                    {
                        Total = totalScans,
                        Completed = completedScans,
                        Pending = pendingScans,
                        Failed = failedScans,
                        Today = todayScans // Added: actual scans today
                    },
                    Images = new
                    {
                        Total = totalImages
                    },
                    FileProcessing = new
                    {
                        Total = totalFileProcessings,
                        Completed = completedFileProcessings,
                        Failed = failedFileProcessings
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving statistics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get daily statistics grouped by date (matches ASE pattern)
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-empty fallback.
        [HttpGet("stats")]
        public async Task<ActionResult<object>> GetStats(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                var queryStart = startDate.HasValue
                    ? DateTime.SpecifyKind(startDate.Value.Date, DateTimeKind.Utc)
                    : DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);
                var queryEnd = endDate.HasValue
                    ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1), DateTimeKind.Utc)
                    : DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(1), DateTimeKind.Utc);

                var stats = await _context.FS6000Scans
                    .Where(s => s.ScanTime >= queryStart && s.ScanTime < queryEnd)
                    .GroupBy(s => s.ScanTime.Date)
                    .Select(g => new
                    {
                        Date = g.Key,
                        Count = g.Count(),
                        ContainerCount = g.Count(s => !string.IsNullOrEmpty(s.ContainerNumber)),
                        ImageCount = g.Count(s => s.HasImage || s.ImageCount > 0) // Use HasImage property (matches ASE pattern)
                    })
                    .OrderByDescending(s => s.Date)
                    .ToListAsync();

                _logger.LogInformation("Retrieved FS6000 stats from {StartDate} to {EndDate}: {DayCount} days",
                    queryStart, queryEnd, stats.Count);

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving FS6000 stats");
                return StatusCode(500, new { error = "Failed to load FS6000 stats" });
            }
        }

        [HttpPost("sync/trigger")]
        public async Task<ActionResult> TriggerSync()
        {
            try
            {
                _logger.LogInformation("Manual file sync triggered");
                await _fileSyncService.StartSyncAsync();
                return Ok(new { message = "File sync triggered successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering file sync");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Run FS6000 startup diagnostics to identify why the service may not be running
        /// </summary>
        [HttpGet("diagnostics")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<FS6000DiagnosticReport>> RunDiagnostics()
        {
            try
            {
                _logger.LogInformation("Running FS6000 diagnostics via API endpoint");

                // Create a logger that captures output
                var diagnostics = new FS6000StartupDiagnostics(
                    _logger,
                    _fileSyncConfig,
                    _serviceScopeFactory);

                var report = await diagnostics.RunDiagnosticsAsync();

                // Check if background service is actually running
                var backgroundServiceRunning = false;
                try
                {
                    // Try to get service status - if services are available, they're likely running
                    var fileSyncHealthy = await _fileSyncService.IsHealthyAsync();
                    var ingestionHealthy = await _ingestionService.IsHealthyAsync();
                    backgroundServiceRunning = fileSyncHealthy && ingestionHealthy;
                }
                catch
                {
                    // Services may not be initialized if background service didn't start
                    backgroundServiceRunning = false;
                }

                var diagnosticReport = new FS6000DiagnosticReport
                {
                    Timestamp = DateTime.UtcNow,
                    ConfigurationValid = report.ConfigurationValid,
                    SourceDirectoryAccessible = report.SourceDirectoryAccessible,
                    DestinationDirectoryAccessible = report.DestinationDirectoryAccessible,
                    DatabaseAccessible = report.DatabaseAccessible,
                    ValidFoldersFound = report.ValidFoldersFound,
                    AllChecksPass = report.AllChecksPass,
                    BackgroundServiceRunning = backgroundServiceRunning,
                    ServiceCanStart = report.AllChecksPass,
                    Issues = new List<string>()
                };

                // Build list of issues
                if (!report.ConfigurationValid)
                    diagnosticReport.Issues.Add("❌ Configuration is invalid - check appsettings.json FS6000:FileSync section");

                if (!report.SourceDirectoryAccessible)
                    diagnosticReport.Issues.Add($"❌ Source directory not accessible: {_fileSyncConfig.Value.SourcePath} - Ensure Z:\\ drive is mounted");

                if (!report.DestinationDirectoryAccessible)
                    diagnosticReport.Issues.Add($"❌ Destination directory not accessible: {_fileSyncConfig.Value.DestinationPath} - Check permissions");

                if (!report.DatabaseAccessible)
                    diagnosticReport.Issues.Add("❌ Database not accessible - check connection string and database server");

                if (report.ValidFoldersFound == 0)
                    diagnosticReport.Issues.Add("⚠️ No valid folders found in source directory - service will start but may not process files");
                else if (report.ValidFoldersFound < 0)
                    diagnosticReport.Issues.Add("❌ Error checking folder structure - check source directory permissions");

                if (!backgroundServiceRunning && report.AllChecksPass)
                    diagnosticReport.Issues.Add("⚠️ All diagnostics passed but background service appears not running - check application logs for startup errors");

                if (diagnosticReport.Issues.Count == 0)
                    diagnosticReport.Issues.Add("✅ All checks passed - service should be running");

                return Ok(diagnosticReport);
            }
            catch (Exception ex)
            {
                // Don't leak stack trace / type names to the client. Full detail goes to the log.
                _logger.LogError(ex, "Error running FS6000 diagnostics");
                return StatusCode(500, new { error = "An internal error occurred while running FS6000 diagnostics. See server logs for details." });
            }
        }
    }

    public class ProcessFolderRequest
    {
        public string FolderPath { get; set; } = string.Empty;
    }

    // DTO matching frontend expectations
    public class FS6000ScanDto
    {
        public Guid Id { get; set; }
        public string? ContainerNumber { get; set; }
        public DateTime ScanTime { get; set; }
        public string? Origin { get; set; }
        public string? Destination { get; set; }
        public string? SyncStatus { get; set; }
    }

    // Paginated response DTO
    public class FS6000ScanResponse
    {
        public List<FS6000ScanDto> Data { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    // Diagnostic report DTO
    public class FS6000DiagnosticReport
    {
        public DateTime Timestamp { get; set; }
        public bool ConfigurationValid { get; set; }
        public bool SourceDirectoryAccessible { get; set; }
        public bool DestinationDirectoryAccessible { get; set; }
        public bool DatabaseAccessible { get; set; }
        public int ValidFoldersFound { get; set; }
        public bool AllChecksPass { get; set; }
        public bool BackgroundServiceRunning { get; set; }
        public bool ServiceCanStart { get; set; }
        public List<string> Issues { get; set; } = new();
    }
}
