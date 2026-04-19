using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.API.Services;
using Swashbuckle.AspNetCore.Annotations;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Performance Metrics and Monitoring
    /// </summary>
    /// <remarks>
    /// Provides real-time performance metrics for API monitoring and optimization
    /// </remarks>
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/[controller]")]
    [SwaggerTag("Performance metrics and monitoring endpoints")]
    public class PerformanceMetricsController : ControllerBase
    {
        private readonly IPerformanceMetricsService _metricsService;
        private readonly ILogger<PerformanceMetricsController> _logger;

        public PerformanceMetricsController(
            IPerformanceMetricsService metricsService,
            ILogger<PerformanceMetricsController> logger)
        {
            _metricsService = metricsService;
            _logger = logger;
        }

        /// <summary>
        /// Get overall API performance metrics
        /// </summary>
        /// <returns>Comprehensive performance statistics</returns>
        /// <remarks>
        /// **Metrics Included:**
        /// - Total requests and errors
        /// - Average/min/max response times
        /// - Requests per second
        /// - Error rate percentage
        /// - Response time percentiles (P50, P75, P90, P95, P99)
        /// - Status code distribution
        /// - Per-endpoint statistics
        /// 
        /// **Use Cases:**
        /// - Performance monitoring dashboards
        /// - Identifying slow endpoints
        /// - Tracking error rates
        /// - Capacity planning
        /// - SLA compliance monitoring
        /// </remarks>
        /// <response code="200">Returns performance metrics</response>
        /// <response code="401">Not authenticated</response>
        /// <response code="403">Not authorized - Admin only</response>
        [HttpGet]
        [SwaggerOperation(
            Summary = "Get API performance metrics",
            Description = "Returns comprehensive performance statistics including response times, error rates, and per-endpoint metrics",
            OperationId = "GetPerformanceMetrics",
            Tags = new[] { "PerformanceMetrics" }
        )]
        [ProducesResponseType(typeof(PerformanceMetrics), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status403Forbidden)]
        public ActionResult<PerformanceMetrics> GetMetrics()
        {
            try
            {
                var metrics = _metricsService.GetMetrics();
                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving performance metrics");
                return StatusCode(500, new { error = "Failed to retrieve metrics" });
            }
        }

        /// <summary>
        /// Get metrics for a specific endpoint
        /// </summary>
        /// <param name="endpoint">Endpoint path (e.g., /api/Users)</param>
        /// <param name="method">HTTP method (e.g., GET, POST)</param>
        /// <returns>Endpoint-specific metrics</returns>
        /// <remarks>
        /// **Example:**
        /// ```
        /// GET /api/PerformanceMetrics/endpoint?endpoint=/api/Users&amp;method=GET
        /// ```
        /// 
        /// **Returns:**
        /// - Request count for this endpoint
        /// - Error count and rate
        /// - Average/min/max response times
        /// </remarks>
        [HttpGet("endpoint")]
        [SwaggerOperation(
            Summary = "Get endpoint-specific metrics",
            Description = "Returns performance metrics for a specific API endpoint",
            OperationId = "GetEndpointMetrics",
            Tags = new[] { "PerformanceMetrics" }
        )]
        [ProducesResponseType(typeof(EndpointMetrics), StatusCodes.Status200OK)]
        public ActionResult<EndpointMetrics> GetEndpointMetrics([FromQuery] string endpoint, [FromQuery] string method)
        {
            try
            {
                var key = $"{method}:{endpoint}";
                var metrics = _metricsService.GetEndpointMetrics(key);
                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving endpoint metrics for {Endpoint}", endpoint);
                return StatusCode(500, new { error = "Failed to retrieve endpoint metrics" });
            }
        }

        /// <summary>
        /// Get top slowest endpoints
        /// </summary>
        /// <param name="count">Number of endpoints to return (default: 10)</param>
        /// <returns>List of slowest endpoints</returns>
        /// <remarks>
        /// Identifies endpoints with highest average response time for optimization
        /// </remarks>
        [HttpGet("slowest")]
        [SwaggerOperation(
            Summary = "Get slowest endpoints",
            Description = "Returns the slowest performing endpoints for optimization",
            OperationId = "GetSlowestEndpoints",
            Tags = new[] { "PerformanceMetrics" }
        )]
        [ProducesResponseType(typeof(List<EndpointMetrics>), StatusCodes.Status200OK)]
        public ActionResult<List<EndpointMetrics>> GetSlowestEndpoints([FromQuery] int count = 10)
        {
            try
            {
                var metrics = _metricsService.GetMetrics();
                var slowest = metrics.EndpointMetrics.Values
                    .OrderByDescending(e => e.AverageResponseTimeMs)
                    .Take(count)
                    .ToList();

                return Ok(slowest);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving slowest endpoints");
                return StatusCode(500, new { error = "Failed to retrieve slowest endpoints" });
            }
        }

        /// <summary>
        /// Get endpoints with highest error rates
        /// </summary>
        /// <param name="count">Number of endpoints to return (default: 10)</param>
        /// <returns>List of endpoints with most errors</returns>
        [HttpGet("errors")]
        [SwaggerOperation(
            Summary = "Get endpoints with highest error rates",
            Description = "Returns endpoints with the most errors for troubleshooting",
            OperationId = "GetErrorProneEndpoints",
            Tags = new[] { "PerformanceMetrics" }
        )]
        [ProducesResponseType(typeof(List<EndpointMetrics>), StatusCodes.Status200OK)]
        public ActionResult<List<EndpointMetrics>> GetErrorProneEndpoints([FromQuery] int count = 10)
        {
            try
            {
                var metrics = _metricsService.GetMetrics();
                var errorProne = metrics.EndpointMetrics.Values
                    .Where(e => e.ErrorCount > 0)
                    .OrderByDescending(e => e.ErrorRate)
                    .Take(count)
                    .ToList();

                return Ok(errorProne);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving error-prone endpoints");
                return StatusCode(500, new { error = "Failed to retrieve error-prone endpoints" });
            }
        }

        /// <summary>
        /// Reset all performance metrics
        /// </summary>
        /// <returns>Success message</returns>
        /// <remarks>
        /// **WARNING:** This will clear all collected metrics data.
        /// Use this to start fresh metrics collection after deployment or configuration changes.
        /// </remarks>
        [HttpPost("reset")]
        [SwaggerOperation(
            Summary = "Reset performance metrics",
            Description = "Clears all collected performance metrics data",
            OperationId = "ResetMetrics",
            Tags = new[] { "PerformanceMetrics" }
        )]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult ResetMetrics()
        {
            try
            {
                _metricsService.Reset();
                _logger.LogWarning("📊 Performance metrics reset by {User}", User.Identity?.Name ?? "Unknown");
                return Ok(new { message = "Performance metrics have been reset" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting performance metrics");
                return StatusCode(500, new { error = "Failed to reset metrics" });
            }
        }
    }
}

