using System;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.DTOs.AiWorkflow;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.AiWorkflow;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Phase 0–3: assistive AI endpoints (feature-flagged), ops triage, image stub assist, training export, ICUMS hints.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AiWorkflowController : ControllerBase
    {
        private readonly IOptions<AiWorkflowOptions> _options;
        private readonly IAiImageAssistService _imageAssist;
        private readonly IOpsLogTriageService _opsTriage;
        private readonly IAiDatasetExportService _export;
        private readonly IcumsCompletenessHintService _icumsHints;
        private readonly ApplicationDbContext _db;

        public AiWorkflowController(
            IOptions<AiWorkflowOptions> options,
            IAiImageAssistService imageAssist,
            IOpsLogTriageService opsTriage,
            IAiDatasetExportService export,
            IcumsCompletenessHintService icumsHints,
            ApplicationDbContext db)
        {
            _options = options;
            _imageAssist = imageAssist;
            _opsTriage = opsTriage;
            _export = export;
            _icumsHints = icumsHints;
            _db = db;
        }

        [HttpGet("status")]
        [HasPermission(Permissions.ControllersAiWorkflow)]
        public ActionResult<object> GetStatus()
        {
            var o = _options.Value;
            return Ok(new
            {
                o.Enabled,
                o.ImageAssistEnabled,
                o.OpsTriageEnabled,
                o.IcumsHintsEnabled,
                TrainingExportEnabled = o.TrainingExportEnabled,
                o.DefaultModelId,
                o.ExportRootPath,
                o.AutonomousShadowModeOnly
            });
        }

        [HttpPost("ops/log-triage")]
        [HasPermission(Permissions.ControllersAiWorkflow)]
        public async Task<ActionResult<OpsTriageResultDto>> OpsLogTriage([FromQuery] int maxItems = 20, CancellationToken cancellationToken = default)
        {
            var result = await _opsTriage.TriageRecentAsync(maxItems, cancellationToken);
            return Ok(result);
        }

        [HttpPost("image/suggestions/generate")]
        [HasPermission(Permissions.ControllersAiWorkflow)]
        public async Task<ActionResult<object>> GenerateImageSuggestions([FromBody] GenerateSuggestionsRequest body, CancellationToken cancellationToken = default)
        {
            if (body.GroupId == Guid.Empty)
                return BadRequest(new { error = "GroupId is required" });
            var rows = await _imageAssist.GenerateStubSuggestionsForGroupAsync(body.GroupId, cancellationToken);
            return Ok(new { count = rows.Count, ids = rows.Select(r => r.Id).ToList() });
        }

        [HttpPost("training/export")]
        [HasPermission(Permissions.ControllersAiWorkflow)]
        public async Task<ActionResult<object>> ExportTrainingSnapshot([FromBody] ExportTrainingRequest body, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(body.Name))
                return BadRequest(new { error = "Name is required" });
            var user = User?.Identity?.Name ?? "api";
            var snap = await _export.CreateSnapshotAsync(body.Name.Trim(), user, body.FromUtc, body.ToUtc, body.OptInOnly, cancellationToken);
            return Ok(new { snap.Id, snap.RowCountEstimate, snap.ExportPath, snap.ChecksumSha256 });
        }

        [HttpGet("icums/completeness-hints")]
        [HasPermission(Permissions.ControllersAiWorkflow)]
        public async Task<ActionResult<object>> IcumsCompletenessHints([Required] string containerNumber, CancellationToken cancellationToken = default)
        {
            var result = await _icumsHints.GetHintsAsync(containerNumber.Trim(), cancellationToken);
            if (result == null)
                return Ok(new { disabled = true, message = "AiWorkflow.IcumsHintsEnabled is false" });
            return Ok(result);
        }

        /// <summary>List AI suggestions with filtering for the analyst UI.</summary>
        [HttpGet("image/suggestions")]
        [HasPermission(Permissions.ControllersAiWorkflow)]
        public async Task<ActionResult> ListSuggestions(
            [FromQuery] string status = "pending",
            [FromQuery] string? containerNumber = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            CancellationToken cancellationToken = default)
        {
            var query = _db.AiImageAnalysisSuggestions.AsNoTracking().AsQueryable();

            if (status == "pending")
                query = query.Where(s => s.ResolvedAtUtc == null);
            else if (status == "resolved")
                query = query.Where(s => s.ResolvedAtUtc != null);

            if (!string.IsNullOrWhiteSpace(containerNumber))
                query = query.Where(s => s.ContainerNumber.Contains(containerNumber));

            var total = await query.CountAsync(cancellationToken);
            var items = await query
                .OrderByDescending(s => s.CreatedAtUtc)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return Ok(items);
        }

        /// <summary>Resolve a suggestion with the analyst's decision.</summary>
        [HttpPost("image/suggestions/{id:long}/resolve")]
        [HasPermission(Permissions.ControllersAiWorkflow)]
        public async Task<ActionResult> ResolveSuggestion(long id, [FromBody] ResolveSuggestionRequest body, CancellationToken cancellationToken = default)
        {
            var suggestion = await _db.AiImageAnalysisSuggestions.FindAsync(new object[] { id }, cancellationToken);
            if (suggestion == null)
                return NotFound(new { error = "Suggestion not found" });

            suggestion.HumanFinalDecision = body.Decision;
            suggestion.HumanReviewedBy = User?.Identity?.Name ?? "analyst";
            suggestion.ResolvedAtUtc = DateTime.UtcNow;
            suggestion.CorrectionReason = body.CorrectionReason;
            suggestion.ResolvedDiffersFromSuggestion =
                suggestion.SuggestedDecision != null &&
                !string.Equals(suggestion.SuggestedDecision, body.Decision, StringComparison.OrdinalIgnoreCase);
            suggestion.EligibleForTrainingExport = true;

            await _db.SaveChangesAsync(cancellationToken);
            return Ok(new { resolved = true, id, decision = body.Decision });
        }

        /// <summary>Shadow mode evaluation metrics — compares AI suggestions vs human decisions.</summary>
        [HttpGet("shadow/metrics")]
        [HasPermission(Permissions.ControllersAiWorkflow)]
        public async Task<ActionResult> GetShadowMetrics(
            [FromQuery] DateTime? fromUtc = null,
            [FromQuery] DateTime? toUtc = null,
            [FromQuery] string? scannerType = null,
            CancellationToken cancellationToken = default)
        {
            var query = _db.AiImageAnalysisSuggestions.AsNoTracking()
                .Where(s => s.ResolvedAtUtc != null && s.SuggestedDecision != null);

            if (fromUtc.HasValue) query = query.Where(s => s.CreatedAtUtc >= fromUtc.Value);
            if (toUtc.HasValue) query = query.Where(s => s.CreatedAtUtc <= toUtc.Value);
            if (!string.IsNullOrWhiteSpace(scannerType)) query = query.Where(s => s.ScannerType == scannerType);

            var all = await query.ToListAsync(cancellationToken);

            var total = all.Count;
            var agreed = all.Count(s => s.ResolvedDiffersFromSuggestion == false);
            var overridden = all.Count(s => s.ResolvedDiffersFromSuggestion == true);
            var pending = await _db.AiImageAnalysisSuggestions.AsNoTracking()
                .CountAsync(s => s.ResolvedAtUtc == null, cancellationToken);

            var avgConfidenceWhenCorrect = all.Where(s => s.ResolvedDiffersFromSuggestion == false && s.Confidence.HasValue)
                .Select(s => s.Confidence!.Value).DefaultIfEmpty(0).Average();
            var avgConfidenceWhenWrong = all.Where(s => s.ResolvedDiffersFromSuggestion == true && s.Confidence.HasValue)
                .Select(s => s.Confidence!.Value).DefaultIfEmpty(0).Average();

            // By scanner type
            var byScannerType = all.GroupBy(s => s.ScannerType).Select(g => new
            {
                ScannerType = g.Key,
                Total = g.Count(),
                Agreed = g.Count(s => s.ResolvedDiffersFromSuggestion == false),
                Overridden = g.Count(s => s.ResolvedDiffersFromSuggestion == true),
                AgreementRate = g.Count() > 0 ? Math.Round((double)g.Count(s => s.ResolvedDiffersFromSuggestion == false) / g.Count() * 100, 1) : 0
            }).ToList();

            // By model
            var byModel = all.GroupBy(s => s.ModelId).Select(g => new
            {
                ModelId = g.Key,
                Total = g.Count(),
                Agreed = g.Count(s => s.ResolvedDiffersFromSuggestion == false),
                AgreementRate = g.Count() > 0 ? Math.Round((double)g.Count(s => s.ResolvedDiffersFromSuggestion == false) / g.Count() * 100, 1) : 0
            }).ToList();

            // Trend (daily)
            var dailyTrend = all.GroupBy(s => s.ResolvedAtUtc!.Value.Date).OrderBy(g => g.Key).Select(g => new
            {
                Date = g.Key.ToString("yyyy-MM-dd"),
                Total = g.Count(),
                Agreed = g.Count(s => s.ResolvedDiffersFromSuggestion == false),
                Overridden = g.Count(s => s.ResolvedDiffersFromSuggestion == true)
            }).ToList();

            // Decision confusion matrix
            var aiNormalHumanNormal = all.Count(s => s.SuggestedDecision == "Normal" && s.HumanFinalDecision == "Normal");
            var aiNormalHumanAbnormal = all.Count(s => s.SuggestedDecision == "Normal" && s.HumanFinalDecision == "Abnormal");
            var aiAbnormalHumanNormal = all.Count(s => s.SuggestedDecision == "Abnormal" && s.HumanFinalDecision == "Normal");
            var aiAbnormalHumanAbnormal = all.Count(s => s.SuggestedDecision == "Abnormal" && s.HumanFinalDecision == "Abnormal");

            // Top correction reasons
            var topCorrections = all.Where(s => !string.IsNullOrWhiteSpace(s.CorrectionReason))
                .GroupBy(s => s.CorrectionReason).OrderByDescending(g => g.Count()).Take(10)
                .Select(g => new { Reason = g.Key, Count = g.Count() }).ToList();

            return Ok(new
            {
                Summary = new
                {
                    TotalResolved = total,
                    PendingReview = pending,
                    Agreed = agreed,
                    Overridden = overridden,
                    AgreementRate = total > 0 ? Math.Round((double)agreed / total * 100, 1) : 0,
                    OverrideRate = total > 0 ? Math.Round((double)overridden / total * 100, 1) : 0,
                    AvgConfidenceWhenCorrect = Math.Round(avgConfidenceWhenCorrect * 100, 1),
                    AvgConfidenceWhenWrong = Math.Round(avgConfidenceWhenWrong * 100, 1)
                },
                ConfusionMatrix = new { aiNormalHumanNormal, aiNormalHumanAbnormal, aiAbnormalHumanNormal, aiAbnormalHumanAbnormal },
                ByScannerType = byScannerType,
                ByModel = byModel,
                DailyTrend = dailyTrend,
                TopCorrectionReasons = topCorrections
            });
        }

        public sealed class ResolveSuggestionRequest
        {
            public string? Decision { get; set; }
            public string? CorrectionReason { get; set; }
        }

        public sealed class GenerateSuggestionsRequest
        {
            public Guid GroupId { get; set; }
        }

        public sealed class ExportTrainingRequest
        {
            public string Name { get; set; } = string.Empty;
            public DateTime? FromUtc { get; set; }
            public DateTime? ToUtc { get; set; }
            public bool OptInOnly { get; set; } = true;
        }
    }
}
