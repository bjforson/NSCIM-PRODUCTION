using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// 1.15.0 — Record Completeness read API.
    ///
    /// Serves the record-level view of the ICUMS reconciliation state. One row
    /// per customs declaration (plus Pattern A "used cars in one container"
    /// grouping via ContainerGroupKey). This is the data source for the new
    /// /validation/record-completeness Blazor page.
    ///
    /// All endpoints are read-only. Writes happen via the
    /// RecordReconciliationWorker on its 30-minute loop. Guarded by the
    /// ImageAnalyst role policy so analysts and above can access it.
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "ImageAnalyst")]
    public class RecordCompletenessController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly ILogger<RecordCompletenessController> _logger;

        public RecordCompletenessController(ApplicationDbContext db, ILogger<RecordCompletenessController> logger)
        {
            _db = db;
            _logger = logger;
        }

        /// <summary>
        /// Paginated list of records. Supports filtering by status, search across
        /// declarationnumber / blnumber / rotationnumber, and ordering by
        /// (LastNewContainerAtUtc DESC) which groups "active" records at the top.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<RecordCompletenessListResponse>> GetList(
            [FromQuery] string? status = null,
            [FromQuery] string? search = null,
            [FromQuery] string? clearanceType = null,
            [FromQuery] bool onlyMultiContainer = false,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 500);

            try
            {
                var query = _db.RecordCompletenessStatuses.AsNoTracking();

                if (!string.IsNullOrWhiteSpace(status))
                {
                    // Accept comma-separated list for multi-status filters
                    var statuses = status.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    query = query.Where(r => statuses.Contains(r.Status));
                }

                if (!string.IsNullOrWhiteSpace(clearanceType))
                {
                    query = query.Where(r => r.ClearanceType == clearanceType);
                }

                if (onlyMultiContainer)
                {
                    query = query.Where(r => r.TotalExpectedContainers > 1);
                }

                if (!string.IsNullOrWhiteSpace(search))
                {
                    var pattern = $"%{search.Trim()}%";
                    query = query.Where(r =>
                        EF.Functions.ILike(r.DeclarationNumber, pattern) ||
                        EF.Functions.ILike(r.BlNumber ?? "", pattern) ||
                        EF.Functions.ILike(r.RotationNumber ?? "", pattern) ||
                        EF.Functions.ILike(r.ContainerGroupKey ?? "", pattern));
                }

                var total = await query.CountAsync();

                var records = await query
                    .OrderByDescending(r => r.LastNewContainerAtUtc ?? r.CreatedAtUtc)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(r => new RecordCompletenessSummary
                    {
                        Id = r.Id,
                        DeclarationNumber = r.DeclarationNumber,
                        ClearanceType = r.ClearanceType,
                        RegimeCode = r.RegimeCode,
                        RotationNumber = r.RotationNumber,
                        BlNumber = r.BlNumber,
                        ContainerGroupKey = r.ContainerGroupKey,
                        ScannerType = r.ScannerType,
                        TotalExpectedContainers = r.TotalExpectedContainers,
                        ContainersAwaitingScan = r.ContainersAwaitingScan,
                        ContainersScanned = r.ContainersScanned,
                        ContainersReady = r.ContainersReady,
                        ContainersDecided = r.ContainersDecided,
                        ContainersSubmitted = r.ContainersSubmitted,
                        ContainersNoImage = r.ContainersNoImage,
                        ContainersNoScan = r.ContainersNoScan,
                        Status = r.Status,
                        WorkflowStage = r.WorkflowStage,
                        FirstSeenUtc = r.FirstSeenUtc,
                        LastNewContainerAtUtc = r.LastNewContainerAtUtc,
                        ArchivedAtUtc = r.ArchivedAtUtc,
                        ArchivalReason = r.ArchivalReason,
                    })
                    .ToListAsync();

                return Ok(new RecordCompletenessListResponse
                {
                    Page = page,
                    PageSize = pageSize,
                    TotalCount = total,
                    TotalPages = (int)Math.Ceiling(total / (double)pageSize),
                    Results = records,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RecordCompleteness] List query failed");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>Single record by id, with its full expected-container list.</summary>
        [HttpGet("{id:int}")]
        public async Task<ActionResult<RecordCompletenessDetail>> GetById(int id)
        {
            var record = await _db.RecordCompletenessStatuses
                .AsNoTracking()
                .Include(r => r.ExpectedContainers)
                .FirstOrDefaultAsync(r => r.Id == id);

            if (record == null) return NotFound();

            return Ok(BuildDetail(record));
        }

        /// <summary>Lookup by declaration number — the operator-friendly entry point.</summary>
        [HttpGet("by-declaration/{declarationNumber}")]
        public async Task<ActionResult<RecordCompletenessDetail>> GetByDeclaration(string declarationNumber)
        {
            if (string.IsNullOrWhiteSpace(declarationNumber)) return BadRequest(new { error = "declarationNumber is required" });

            var record = await _db.RecordCompletenessStatuses
                .AsNoTracking()
                .Include(r => r.ExpectedContainers)
                .FirstOrDefaultAsync(r => r.DeclarationNumber == declarationNumber.Trim());

            if (record == null) return NotFound();

            return Ok(BuildDetail(record));
        }

        /// <summary>
        /// Summary counts across the whole table — drives the filter chip counts at
        /// the top of the Blazor page without requiring a separate query per status.
        /// </summary>
        [HttpGet("summary")]
        [HttpGet("~/api/record-completeness/summary")]
        public async Task<ActionResult<RecordCompletenessSummaryCounts>> GetSummary()
        {
            var counts = await _db.RecordCompletenessStatuses
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            var dict = counts.ToDictionary(x => x.Status, x => x.Count);

            var totalMultiContainer = await _db.RecordCompletenessStatuses
                .CountAsync(r => r.TotalExpectedContainers > 1);

            var integrityGap = await _db.RecordCompletenessStatuses
                .Where(r => r.TotalExpectedContainers > 1 && r.Status != "Archived")
                .SumAsync(r => (int?)r.ContainersAwaitingScan) ?? 0;

            return Ok(new RecordCompletenessSummaryCounts
            {
                TotalRecords = counts.Sum(c => c.Count),
                TotalMultiContainer = totalMultiContainer,
                IntegrityGapContainers = integrityGap,
                Pending = dict.GetValueOrDefault("Pending"),
                PartiallyReady = dict.GetValueOrDefault("PartiallyReady"),
                Ready = dict.GetValueOrDefault("Ready"),
                InAnalysis = dict.GetValueOrDefault("InAnalysis"),
                InAudit = dict.GetValueOrDefault("InAudit"),
                Submitted = dict.GetValueOrDefault("Submitted"),
                Completed = dict.GetValueOrDefault("Completed"),
                Archived = dict.GetValueOrDefault("Archived"),
            });
        }

        /// <summary>
        /// Oldest record currently stuck in 'InAudit' — used by the dashboard banner
        /// to surface audit SLA breaches (e.g. no auditor logged in).
        /// </summary>
        [HttpGet("oldest-in-audit")]
        public async Task<ActionResult<object>> GetOldestInAudit()
        {
            var inAudit = _db.RecordCompletenessStatuses.Where(r => r.Status == "InAudit");
            var total = await inAudit.CountAsync();
            if (total == 0)
                return Ok(new { OldestCreatedAtUtc = (DateTime?)null, TotalInAudit = 0 });

            var oldest = await inAudit.MinAsync(r => (DateTime?)r.UpdatedAtUtc);
            return Ok(new { OldestCreatedAtUtc = oldest, TotalInAudit = total });
        }

        private static RecordCompletenessDetail BuildDetail(NickScanCentralImagingPortal.Core.Entities.RecordCompletenessStatus r)
        {
            return new RecordCompletenessDetail
            {
                Id = r.Id,
                DeclarationNumber = r.DeclarationNumber,
                ClearanceType = r.ClearanceType,
                RegimeCode = r.RegimeCode,
                PrimaryBoeDocumentId = r.PrimaryBoeDocumentId,
                RotationNumber = r.RotationNumber,
                BlNumber = r.BlNumber,
                ContainerGroupKey = r.ContainerGroupKey,
                ScannerType = r.ScannerType,
                TotalExpectedContainers = r.TotalExpectedContainers,
                ContainersAwaitingScan = r.ContainersAwaitingScan,
                ContainersScanned = r.ContainersScanned,
                ContainersReady = r.ContainersReady,
                ContainersDecided = r.ContainersDecided,
                ContainersSubmitted = r.ContainersSubmitted,
                ContainersNoImage = r.ContainersNoImage,
                ContainersNoScan = r.ContainersNoScan,
                Status = r.Status,
                WorkflowStage = r.WorkflowStage,
                FirstSeenUtc = r.FirstSeenUtc,
                LastNewContainerAtUtc = r.LastNewContainerAtUtc,
                FirstReadyAtUtc = r.FirstReadyAtUtc,
                ArchivedAtUtc = r.ArchivedAtUtc,
                ArchivalReason = r.ArchivalReason,
                DeclarationsJson = r.DeclarationsJson,
                ExpectedContainers = r.ExpectedContainers
                    .OrderBy(c => c.ContainerNumber)
                    .Select(c => new RecordExpectedContainerDto
                    {
                        Id = c.Id,
                        ContainerNumber = c.ContainerNumber,
                        Status = c.Status,
                        HouseBl = c.HouseBl,
                        ConsigneeName = c.ConsigneeName,
                        InspectionId = c.InspectionId,
                        ScannerType = c.ScannerType,
                        FirstSeenUtc = c.FirstSeenUtc,
                        ScannedAtUtc = c.ScannedAtUtc,
                        BecameReadyUtc = c.BecameReadyUtc,
                        DecidedAtUtc = c.DecidedAtUtc,
                    })
                    .ToList(),
            };
        }
    }

    // ── DTOs ───────────────────────────────────────────────────────────────

    public class RecordCompletenessListResponse
    {
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public int TotalPages { get; set; }
        public List<RecordCompletenessSummary> Results { get; set; } = new();
    }

    public class RecordCompletenessSummary
    {
        public int Id { get; set; }
        public string DeclarationNumber { get; set; } = string.Empty;
        public string? ClearanceType { get; set; }
        public string? RegimeCode { get; set; }
        public string? RotationNumber { get; set; }
        public string? BlNumber { get; set; }
        public string? ContainerGroupKey { get; set; }
        public string? ScannerType { get; set; }
        public int TotalExpectedContainers { get; set; }
        public int ContainersAwaitingScan { get; set; }
        public int ContainersScanned { get; set; }
        public int ContainersReady { get; set; }
        public int ContainersDecided { get; set; }
        public int ContainersSubmitted { get; set; }
        public int ContainersNoImage { get; set; }
        public int ContainersNoScan { get; set; }
        public string Status { get; set; } = string.Empty;
        public string WorkflowStage { get; set; } = string.Empty;
        public DateTime FirstSeenUtc { get; set; }
        public DateTime? LastNewContainerAtUtc { get; set; }
        public DateTime? ArchivedAtUtc { get; set; }
        public string? ArchivalReason { get; set; }
    }

    public class RecordCompletenessDetail : RecordCompletenessSummary
    {
        public int? PrimaryBoeDocumentId { get; set; }
        public DateTime? FirstReadyAtUtc { get; set; }
        public string? DeclarationsJson { get; set; }
        public List<RecordExpectedContainerDto> ExpectedContainers { get; set; } = new();
    }

    public class RecordExpectedContainerDto
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? HouseBl { get; set; }
        public string? ConsigneeName { get; set; }
        public string? InspectionId { get; set; }
        public string? ScannerType { get; set; }
        public DateTime FirstSeenUtc { get; set; }
        public DateTime? ScannedAtUtc { get; set; }
        public DateTime? BecameReadyUtc { get; set; }
        public DateTime? DecidedAtUtc { get; set; }
    }

    public class RecordCompletenessSummaryCounts
    {
        public int TotalRecords { get; set; }
        public int TotalMultiContainer { get; set; }
        public int IntegrityGapContainers { get; set; }
        public int Pending { get; set; }
        public int PartiallyReady { get; set; }
        public int Ready { get; set; }
        public int InAnalysis { get; set; }
        public int InAudit { get; set; }
        public int Submitted { get; set; }
        public int Completed { get; set; }
        public int Archived { get; set; }
    }
}
