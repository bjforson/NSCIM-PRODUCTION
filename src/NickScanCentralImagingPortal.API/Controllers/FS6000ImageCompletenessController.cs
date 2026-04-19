using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Services.FS6000;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class FS6000ImageCompletenessController : ControllerBase
    {
        private readonly IFS6000ImageCompletenessService _imageCompletenessService;
        private readonly ILogger<FS6000ImageCompletenessController> _logger;

        public FS6000ImageCompletenessController(
            IFS6000ImageCompletenessService imageCompletenessService,
            ILogger<FS6000ImageCompletenessController> logger)
        {
            _imageCompletenessService = imageCompletenessService;
            _logger = logger;
        }

        /// <summary>
        /// Get image completeness statistics
        /// </summary>
        [HttpGet("stats")]
        public async Task<ActionResult<FS6000ImageCompletenessStats>> GetImageCompletenessStats()
        {
            try
            {
                var stats = await _imageCompletenessService.GetImageCompletenessStatsAsync();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image completeness stats");
                return StatusCode(500, new { error = "Failed to get image completeness stats", details = ex.Message });
            }
        }

        /// <summary>
        /// Get scans without images
        /// </summary>
        [HttpGet("scans-without-images")]
        public async Task<ActionResult> GetScansWithoutImages([FromQuery] int limit = 100)
        {
            try
            {
                var scans = await _imageCompletenessService.GetScansWithoutImagesAsync(limit);

                var result = scans.Select(s => new
                {
                    s.Id,
                    s.ContainerNumber,
                    s.ScanTime,
                    s.PicNumber,
                    s.HasImage,
                    s.ImageCount,
                    s.ImageValidationError,
                    s.CreatedAt,
                    s.ProcessedAt
                });

                return Ok(new
                {
                    count = scans.Count,
                    limit,
                    scans = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scans without images");
                return StatusCode(500, new { error = "Failed to get scans without images", details = ex.Message });
            }
        }

        /// <summary>
        /// Validate a specific scan has images
        /// </summary>
        [HttpGet("validate/{scanId}")]
        public async Task<ActionResult> ValidateScan(Guid scanId)
        {
            try
            {
                var hasImage = await _imageCompletenessService.ValidateScanHasImageAsync(scanId);

                // Update the scan's image completeness status
                await _imageCompletenessService.UpdateScanImageCompletenessAsync(scanId);

                return Ok(new
                {
                    scanId,
                    hasImage,
                    message = hasImage ? "Scan has images" : "Scan is missing images"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating scan {ScanId}", scanId);
                return StatusCode(500, new { error = "Failed to validate scan", details = ex.Message });
            }
        }

        /// <summary>
        /// Validate all scans and update their image completeness status
        /// </summary>
        [HttpPost("validate-all")]
        public async Task<ActionResult> ValidateAllScans()
        {
            try
            {
                _logger.LogInformation("Starting validation of all FS6000 scans");

                var updatedCount = await _imageCompletenessService.ValidateAllScansImageCompletenessAsync();

                return Ok(new
                {
                    message = "Validation completed successfully",
                    updatedCount,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating all scans");
                return StatusCode(500, new { error = "Failed to validate all scans", details = ex.Message });
            }
        }

        /// <summary>
        /// Backfill missing images for scans
        /// </summary>
        [HttpPost("backfill")]
        public async Task<ActionResult> BackfillMissingImages(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                _logger.LogInformation("Starting backfill for missing images from {FromDate} to {ToDate}",
                    fromDate?.ToString("yyyy-MM-dd") ?? "beginning",
                    toDate?.ToString("yyyy-MM-dd") ?? "now");

                var backfilledCount = await _imageCompletenessService.BackfillMissingImagesAsync(fromDate, toDate);

                return Ok(new
                {
                    message = "Backfill completed successfully",
                    backfilledCount,
                    fromDate,
                    toDate,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during backfill");
                return StatusCode(500, new { error = "Failed to backfill missing images", details = ex.Message });
            }
        }

        /// <summary>
        /// Get detailed report of image completeness
        /// </summary>
        [HttpGet("report")]
        public async Task<ActionResult> GetDetailedReport()
        {
            try
            {
                var stats = await _imageCompletenessService.GetImageCompletenessStatsAsync();
                var scansWithoutImages = await _imageCompletenessService.GetScansWithoutImagesAsync(10);

                return Ok(new
                {
                    summary = new
                    {
                        stats.TotalScans,
                        stats.ScansWithImages,
                        stats.ScansWithoutImages,
                        stats.TotalImages,
                        CompletenessPercentage = $"{stats.CompletenessPercentage:F2}%",
                        stats.OldestScanWithoutImage,
                        stats.NewestScanWithoutImage
                    },
                    imageCountDistribution = stats.ImageCountDistribution,
                    recentScansWithoutImages = scansWithoutImages.Select(s => new
                    {
                        s.ContainerNumber,
                        s.ScanTime,
                        s.ImageValidationError
                    }),
                    generatedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating detailed report");
                return StatusCode(500, new { error = "Failed to generate report", details = ex.Message });
            }
        }
    }
}
