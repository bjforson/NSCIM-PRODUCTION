using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Services;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [AllowAnonymous] // Health check should be accessible without auth for monitoring
    [ApiController]
    [Route("api/[controller]")]
    public class HealthController : ControllerBase
    {
        private readonly IImageProcessingOrchestrator _orchestrator;
        private readonly ILogger<HealthController> _logger;

        public HealthController(IImageProcessingOrchestrator orchestrator, ILogger<HealthController> logger)
        {
            _orchestrator = orchestrator;
            _logger = logger;
        }

        [HttpGet]
        public async Task<ActionResult<HealthCheckResponse>> GetHealth()
        {
            try
            {
                var healthStatus = await _orchestrator.GetSystemHealthAsync();
                var overallHealth = healthStatus.Values.All(v => v);

                var response = new HealthCheckResponse
                {
                    OverallStatus = overallHealth ? "Healthy" : "Unhealthy",
                    Timestamp = DateTime.UtcNow,
                    Services = healthStatus
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get health status");
                return StatusCode(500, new HealthCheckResponse
                {
                    OverallStatus = "Unhealthy",
                    Timestamp = DateTime.UtcNow,
                    Error = ex.Message
                });
            }
        }

        [HttpGet("scanners")]
        public async Task<ActionResult<Dictionary<string, bool>>> GetScannerHealth()
        {
            try
            {
                var healthStatus = await _orchestrator.GetSystemHealthAsync();
                var scannerHealth = healthStatus.Where(kvp => kvp.Key != "Database").ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
                return Ok(scannerHealth);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get scanner health status");
                return StatusCode(500, "Internal server error");
            }
        }
    }

    /// <summary>
    /// Health check response model for API endpoints
    /// </summary>
    public class HealthCheckResponse
    {
        public string OverallStatus { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public Dictionary<string, bool> Services { get; set; } = new Dictionary<string, bool>();
        public string? Error { get; set; }
    }
}
