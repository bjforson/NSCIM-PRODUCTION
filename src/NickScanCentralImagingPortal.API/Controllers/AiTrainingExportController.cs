using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Services.AiTraining;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Admin endpoint for the AI training flywheel COCO export (Gap 4).
    ///
    /// Joins ImageAnalysisDecisions + ContainerAnnotations + ManifestSnapshots
    /// into a COCO Object Detection JSON corpus suitable for training a YOLO /
    /// DETR / Faster R-CNN model offline. Output schema mirrors the PhD
    /// reference at C:\AI\sample_training_export.json so a single trainer
    /// pipeline can consume both NSCIM and the standalone Python module.
    ///
    /// Two endpoints, same logic, different transport:
    ///
    ///   GET /api/admin/ai-training/coco/summary    Returns a small summary JSON
    ///                                              (counts only, no payload).
    ///                                              Cheap and useful for the
    ///                                              admin dashboard.
    ///
    ///   GET /api/admin/ai-training/coco/download    Returns the full COCO JSON
    ///                                                as application/json with
    ///                                                a Content-Disposition
    ///                                                attachment header so
    ///                                                browsers save it as a file.
    ///
    /// Both accept ?from=, ?to=, ?includeUncategorized=, ?maxRows= query
    /// parameters. The defaults skip Pending and uncategorised rows so the
    /// output is automatically training-clean.
    ///
    /// Permission: gated behind the existing <c>"AdminOnly"</c> policy
    /// registered in Program.cs, matching every other admin controller.
    /// The DynamicAuthorizationPolicyProvider only resolves policies
    /// prefixed with "Permission:", so attaching a raw permission constant
    /// directly fails at runtime — see 1.10.1 CHANGELOG entry. "AdminOnly"
    /// is the right policy for admin tooling, and the audience for
    /// training data export is the same set of users who already have
    /// database admin access.
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/admin/ai-training/coco")]
    public class AiTrainingExportController : ControllerBase
    {
        private readonly CocoExportService _exporter;
        private readonly ILogger<AiTrainingExportController> _logger;

        public AiTrainingExportController(
            CocoExportService exporter,
            ILogger<AiTrainingExportController> logger)
        {
            _exporter = exporter;
            _logger = logger;
        }

        [HttpGet("summary")]
        public async Task<ActionResult<object>> GetSummary(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] bool includeUncategorized = false,
            [FromQuery] int maxRows = 50_000,
            CancellationToken cancellationToken = default)
        {
            var result = await _exporter.BuildAsync(from, to, includeUncategorized, maxRows, cancellationToken);

            return Ok(new
            {
                imageCount = result.ImageCount,
                annotationCount = result.AnnotationCount,
                categoryCount = result.CategoryCount,
                from,
                to,
                includeUncategorized,
                maxRows,
                generatedAtUtc = DateTime.UtcNow,
            });
        }

        [HttpGet("download")]
        public async Task<IActionResult> Download(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null,
            [FromQuery] bool includeUncategorized = false,
            [FromQuery] int maxRows = 50_000,
            CancellationToken cancellationToken = default)
        {
            var result = await _exporter.BuildAsync(from, to, includeUncategorized, maxRows, cancellationToken);

            var json = JsonSerializer.Serialize(result.Coco, new JsonSerializerOptions
            {
                WriteIndented = true,
            });

            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            var fileName = $"nscim-coco-{DateTime.UtcNow:yyyyMMddHHmmss}.json";

            _logger.LogInformation(
                "AI training COCO export downloaded: {Images} images, {Annotations} annotations, {Bytes} bytes",
                result.ImageCount, result.AnnotationCount, bytes.Length);

            return File(bytes, "application/json", fileName);
        }
    }
}
