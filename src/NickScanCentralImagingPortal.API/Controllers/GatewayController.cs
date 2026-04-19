using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models.Gateway;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Gateway API - Unified endpoints that aggregate data from multiple sources
    /// Reduces frontend API calls by combining related data into single responses
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class GatewayController : ControllerBase
    {
        private readonly IGatewayOrchestrationService _orchestrator;
        private readonly IDashboardStatsService _dashboardStatsService;
        private readonly IGlobalSearchService _globalSearchService;
        private readonly IReportGenerationService _reportGenerationService;
        private readonly IBatchOperationService _batchOperationService;
        private readonly ILogger<GatewayController> _logger;

        public GatewayController(
            IGatewayOrchestrationService orchestrator,
            IDashboardStatsService dashboardStatsService,
            IGlobalSearchService globalSearchService,
            IReportGenerationService reportGenerationService,
            IBatchOperationService batchOperationService,
            ILogger<GatewayController> logger)
        {
            _orchestrator = orchestrator;
            _dashboardStatsService = dashboardStatsService;
            _globalSearchService = globalSearchService;
            _reportGenerationService = reportGenerationService;
            _batchOperationService = batchOperationService;
            _logger = logger;
        }

        /// <summary>
        /// Get complete container data including image, scanner data, ICUMS, and validation
        /// This replaces multiple API calls with a single unified request
        /// </summary>
        /// <param name="containerNumber">Container identifier</param>
        /// <param name="includeImage">Include image data (default: true)</param>
        /// <param name="includeScanner">Include scanner-specific data (default: true)</param>
        /// <param name="includeICUMS">Include ICUMS/BOE data (default: true)</param>
        /// <param name="includeValidation">Include validation status (default: true)</param>
        /// <param name="includeVehicles">Include vehicle data if available (default: false)</param>
        /// <param name="includeHistory">Include processing history (default: false)</param>
        /// <returns>Unified container data response</returns>
        [AllowAnonymous]
        [HttpGet("container/{containerNumber}")]
        [ProducesResponseType(200, Type = typeof(ContainerCompleteResponse))]
        [ProducesResponseType(404)]
        [ProducesResponseType(400)]
        public async Task<ActionResult<ContainerCompleteResponse>> GetContainerComplete(
            string containerNumber,
            [FromQuery] bool includeImage = true,
            [FromQuery] bool includeScanner = true,
            [FromQuery] bool includeICUMS = true,
            [FromQuery] bool includeValidation = true,
            [FromQuery] bool includeVehicles = false,
            [FromQuery] bool includeHistory = false)
        {
            if (string.IsNullOrWhiteSpace(containerNumber))
            {
                return BadRequest(new { message = "Container number is required" });
            }

            _logger.LogInformation(
                "Gateway: Container complete request for {Container}",
                containerNumber);

            var options = new GatewayRequestOptions
            {
                IncludeImage = includeImage,
                IncludeScannerData = includeScanner,
                IncludeICUMS = includeICUMS,
                IncludeValidation = includeValidation,
                IncludeVehicles = includeVehicles,
                IncludeHistory = includeHistory
            };

            var result = await _orchestrator.GetContainerCompleteAsync(containerNumber, options);

            if (!result.Available.HasAnyData)
            {
                _logger.LogWarning(
                    "Gateway: No data found for container {Container}",
                    containerNumber);

                return NotFound(new
                {
                    message = $"No data found for container {containerNumber}",
                    containerNumber,
                    errors = result.Errors,
                    warnings = result.Warnings
                });
            }

            _logger.LogInformation(
                "Gateway: Returning data for {Container} - HasImage={HasImage}, HasScanner={HasScanner}, HasICUMS={HasICUMS}, ResponseTime={Ms}ms",
                containerNumber,
                result.Available.HasImage,
                result.Available.HasScannerData,
                result.Available.HasICUMSData,
                result.ResponseTimeMs);

            return Ok(result);
        }

        /// <summary>
        /// Convenience endpoint to get just the image from the unified data
        /// Useful for img tags that need just the image bytes
        /// </summary>
        /// <param name="containerNumber">Container identifier</param>
        /// <returns>JPEG image bytes</returns>
        [AllowAnonymous]
        [HttpGet("container/{containerNumber}/image")]
        [ProducesResponseType(200, Type = typeof(FileContentResult))]
        [ProducesResponseType(404)]
        public async Task<ActionResult> GetContainerImage(string containerNumber)
        {
            if (string.IsNullOrWhiteSpace(containerNumber))
            {
                return BadRequest(new { message = "Container number is required" });
            }

            _logger.LogInformation(
                "Gateway: Image request for {Container}",
                containerNumber);

            var options = new GatewayRequestOptions
            {
                IncludeImage = true,
                IncludeScannerData = true,
                IncludeICUMS = false,
                IncludeValidation = false,
                IncludeVehicles = false,
                IncludeHistory = false
            };

            var result = await _orchestrator.GetContainerCompleteAsync(containerNumber, options);

            if (result.Scanner?.ImageBytes == null || result.Scanner.ImageBytes.Length == 0)
            {
                _logger.LogWarning(
                    "Gateway: No image found for container {Container}",
                    containerNumber);

                return NotFound(new { message = $"No image found for container {containerNumber}" });
            }

            _logger.LogInformation(
                "Gateway: Returning image for {Container} - Size={Size}KB",
                containerNumber,
                result.Scanner.ImageBytes.Length / 1024);

            return File(result.Scanner.ImageBytes, result.Scanner.MimeType);
        }

        /// <summary>
        /// Get dashboard statistics (Phase 2)
        /// </summary>
        /// <param name="includeContainers">Include container statistics</param>
        /// <param name="includeScanners">Include scanner statistics</param>
        /// <param name="includeICUMS">Include ICUMS statistics</param>
        /// <param name="includeValidation">Include validation statistics</param>
        /// <param name="includeImages">Include image processing statistics</param>
        /// <param name="includeTrends">Include trend data</param>
        /// <returns>Dashboard statistics</returns>
        [AllowAnonymous]
        [HttpGet("dashboard/stats")]
        [ProducesResponseType(200, Type = typeof(DashboardStats))]
        public async Task<ActionResult<DashboardStats>> GetDashboardStats(
            [FromQuery] bool includeContainers = true,
            [FromQuery] bool includeScanners = true,
            [FromQuery] bool includeICUMS = true,
            [FromQuery] bool includeValidation = true,
            [FromQuery] bool includeImages = true,
            [FromQuery] bool includeTrends = true)
        {
            try
            {
                _logger.LogInformation("Gateway: Dashboard stats requested (Phase 2)");

                var result = await _dashboardStatsService.GetDashboardStatsAsync(
                    includeContainers,
                    includeScanners,
                    includeICUMS,
                    includeValidation,
                    includeImages,
                    includeTrends);

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting dashboard stats");
                return StatusCode(500, new { Error = "Failed to retrieve dashboard statistics" });
            }
        }

        /// <summary>
        /// Global search across all entities (Phase 2)
        /// </summary>
        [AllowAnonymous]
        [HttpPost("search")]
        [ProducesResponseType(200, Type = typeof(GlobalSearchResponse))]
        public async Task<ActionResult<GlobalSearchResponse>> GlobalSearch(
            [FromBody] GlobalSearchRequest request)
        {
            try
            {
                _logger.LogInformation("Gateway: Global search requested for '{Query}' (Phase 2)", request.Query);

                if (string.IsNullOrWhiteSpace(request.Query))
                {
                    return BadRequest(new { Error = "Search query is required" });
                }

                var result = await _globalSearchService.SearchAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error performing global search");
                return StatusCode(500, new { Error = "Search failed" });
            }
        }

        /// <summary>
        /// Generate reports (Phase 2)
        /// </summary>
        [AllowAnonymous]
        [HttpPost("reports/generate")]
        [ProducesResponseType(200, Type = typeof(ReportGenerationResponse))]
        public async Task<ActionResult<ReportGenerationResponse>> GenerateReport(
            [FromBody] ReportGenerationRequest request)
        {
            try
            {
                _logger.LogInformation("Gateway: Report generation requested - {ReportType} in {Format}",
                    request.ReportType, request.Format);

                var result = await _reportGenerationService.GenerateReportAsync(request);

                if (result.Status == "Failed")
                {
                    return StatusCode(500, result);
                }

                // Return file for download
                if (result.FileData != null)
                {
                    return File(result.FileData, result.ContentType ?? "application/octet-stream", result.FileName);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating report");
                return StatusCode(500, new { Error = "Report generation failed" });
            }
        }

        /// <summary>
        /// Queue batch operation (Phase 2)
        /// </summary>
        [AllowAnonymous]
        [HttpPost("batch")]
        [ProducesResponseType(200, Type = typeof(BatchOperationResponse))]
        public async Task<ActionResult<BatchOperationResponse>> QueueBatchOperation(
            [FromBody] BatchOperationRequest request)
        {
            try
            {
                _logger.LogInformation("Gateway: Batch operation requested - {OperationType} for {Count} items",
                    request.OperationType, request.ContainerNumbers.Count);

                var result = await _batchOperationService.QueueBatchOperationAsync(request);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing batch operation");
                return StatusCode(500, new { Error = "Batch operation failed" });
            }
        }

        /// <summary>
        /// Get batch operation status (Phase 2)
        /// </summary>
        [AllowAnonymous]
        [HttpGet("batch/{batchId}")]
        [ProducesResponseType(200, Type = typeof(BatchOperationResponse))]
        public async Task<ActionResult<BatchOperationResponse>> GetBatchStatus(string batchId)
        {
            try
            {
                var result = await _batchOperationService.GetBatchStatusAsync(batchId);

                if (result.Status == "NotFound")
                {
                    return NotFound(new { Error = $"Batch {batchId} not found" });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting batch status");
                return StatusCode(500, new { Error = "Failed to get batch status" });
            }
        }

        /// <summary>
        /// Health check for gateway
        /// </summary>
        [AllowAnonymous]
        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            return Ok(new
            {
                status = "healthy",
                service = "Gateway API",
                timestamp = DateTime.UtcNow,
                version = "1.0.0-phase2",
                features = new[]
                {
                    "Container Complete Data",
                    "Dashboard Stats",
                    "Global Search",
                    "Report Generation",
                    "Batch Operations"
                }
            });
        }

        /// <summary>
        /// Admin endpoint to clear placeholder images from cache (Phase 2 Enhancement)
        /// </summary>
        /// <param name="minSizeBytes">Minimum size in bytes - images smaller than this are considered placeholders (default: 10000)</param>
        /// <returns>Number of placeholder caches cleared</returns>
        // 2026-04-19: auth enforced — admin cache wipe must be gated.
        [Authorize(Roles = "Admin,SuperAdmin")]
        [HttpDelete("admin/cache/placeholders")]
        [ProducesResponseType(200)]
        public async Task<ActionResult> ClearPlaceholderCache([FromQuery] int minSizeBytes = 10000)
        {
            try
            {
                _logger.LogInformation("Admin: Clearing placeholder cache (images < {MinSize} bytes)", minSizeBytes);

                // Use raw SQL to delete small cached images
                var deletedCount = await _orchestrator.ClearPlaceholderCacheAsync(minSizeBytes);

                _logger.LogInformation("Admin: Cleared {Count} placeholder images from cache", deletedCount);

                return Ok(new
                {
                    success = true,
                    deletedCount,
                    minSizeBytes,
                    timestamp = DateTime.UtcNow,
                    message = $"Cleared {deletedCount} placeholder images (< {minSizeBytes} bytes) from cache"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing placeholder cache");
                return StatusCode(500, new { Error = "Failed to clear placeholder cache", Details = ex.Message });
            }
        }

        /// <summary>
        /// Admin endpoint to get cache statistics (Phase 2 Enhancement)
        /// </summary>
        // 2026-04-19: auth enforced — admin cache stats must be gated.
        [Authorize(Roles = "Admin,SuperAdmin")]
        [HttpGet("admin/cache/stats")]
        [ProducesResponseType(200)]
        public async Task<ActionResult> GetCacheStats()
        {
            try
            {
                var stats = await _orchestrator.GetCacheStatsAsync();
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cache stats");
                return StatusCode(500, new { Error = "Failed to get cache stats" });
            }
        }
    }
}

