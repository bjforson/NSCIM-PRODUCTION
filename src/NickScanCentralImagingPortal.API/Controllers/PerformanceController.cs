using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Controller for performance monitoring and metrics
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class PerformanceController : ControllerBase
    {
        private readonly IPerformanceMonitoringService _performanceService;
        private readonly ILogger<PerformanceController> _logger;

        public PerformanceController(
            IPerformanceMonitoringService performanceService,
            ILogger<PerformanceController> logger)
        {
            _performanceService = performanceService;
            _logger = logger;
        }

        /// <summary>
        /// Get current performance summary
        /// </summary>
        /// <returns>Performance summary with health status and alerts</returns>
        [HttpGet("summary")]
        public ActionResult<PerformanceSummary> GetPerformanceSummary()
        {
            try
            {
                var summary = _performanceService.GetPerformanceSummary();
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting performance summary");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get all current performance metrics
        /// </summary>
        /// <returns>Dictionary of all performance metrics</returns>
        [HttpGet("metrics")]
        public ActionResult<Dictionary<string, PerformanceMetric>> GetAllMetrics()
        {
            try
            {
                var metrics = _performanceService.GetCurrentMetrics();
                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting performance metrics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get performance metrics for a specific service
        /// </summary>
        /// <param name="serviceName">Name of the service</param>
        /// <returns>List of metrics for the service</returns>
        [HttpGet("metrics/{serviceName}")]
        public ActionResult<List<PerformanceMetric>> GetServiceMetrics(string serviceName)
        {
            try
            {
                var metrics = _performanceService.GetMetricsForService(serviceName);
                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting metrics for service {ServiceName}", serviceName);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get system health status
        /// </summary>
        /// <returns>Current system health status</returns>
        [HttpGet("health")]
        public ActionResult<object> GetSystemHealth()
        {
            try
            {
                var summary = _performanceService.GetPerformanceSummary();
                return Ok(new
                {
                    Status = summary.SystemHealth,
                    Timestamp = summary.Timestamp,
                    Alerts = summary.PerformanceAlerts,
                    MetricsCount = summary.TotalMetrics
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system health");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get memory usage metrics
        /// </summary>
        /// <returns>Memory usage information</returns>
        [HttpGet("memory")]
        public ActionResult<object> GetMemoryMetrics()
        {
            try
            {
                var metrics = _performanceService.GetCurrentMetrics();
                var memoryMetrics = metrics
                    .Where(m => m.Key.Contains("Memory"))
                    .ToDictionary(kvp => kvp.Key, kvp => new
                    {
                        Value = kvp.Value.Value,
                        Unit = kvp.Value.Unit,
                        Timestamp = kvp.Value.Timestamp
                    });

                return Ok(memoryMetrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting memory metrics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get database performance metrics
        /// </summary>
        /// <returns>Database performance information</returns>
        [HttpGet("database")]
        public ActionResult<object> GetDatabaseMetrics()
        {
            try
            {
                var metrics = _performanceService.GetCurrentMetrics();
                var dbMetrics = metrics
                    .Where(m => m.Key.Contains("Database"))
                    .ToDictionary(kvp => kvp.Key, kvp => new
                    {
                        Value = kvp.Value.Value,
                        Unit = kvp.Value.Unit,
                        Timestamp = kvp.Value.Timestamp
                    });

                return Ok(dbMetrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting database metrics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get CPU and system resource metrics
        /// </summary>
        /// <returns>System resource information</returns>
        [HttpGet("system")]
        public ActionResult<object> GetSystemMetrics()
        {
            try
            {
                var metrics = _performanceService.GetCurrentMetrics();
                var systemMetrics = metrics
                    .Where(m => m.Key.Contains("System") || m.Key.Contains("CPU") || m.Key.Contains("GC"))
                    .ToDictionary(kvp => kvp.Key, kvp => new
                    {
                        Value = kvp.Value.Value,
                        Unit = kvp.Value.Unit,
                        Timestamp = kvp.Value.Timestamp
                    });

                return Ok(systemMetrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system metrics");
                return StatusCode(500, "Internal server error");
            }
        }
    }
}
