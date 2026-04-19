using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NickScanWebApp.Mobile.Controllers
{
    [ApiController]
    [Route("api/server")]
    [AllowAnonymous] // Allow unauthenticated access for version check
    public class ServerVersionController : ControllerBase
    {
        private static readonly DateTime ServerStartTime = DateTime.UtcNow;
        private static readonly string ServerStartTimeString = ServerStartTime.ToString("O");
        private static readonly string ServerInstanceId = Guid.NewGuid().ToString("N").Substring(0, 8);
        
        private readonly ILogger<ServerVersionController> _logger;

        public ServerVersionController(ILogger<ServerVersionController> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Returns the server start time to detect server restarts
        /// </summary>
        [HttpGet("version")]
        public IActionResult GetVersion()
        {
            _logger.LogDebug("Server version check requested - Uptime: {Uptime}s", (DateTime.UtcNow - ServerStartTime).TotalSeconds);
            
            return Ok(new
            {
                startTime = ServerStartTimeString,
                instanceId = ServerInstanceId,
                currentTime = DateTime.UtcNow.ToString("O"),
                uptime = Math.Round((DateTime.UtcNow - ServerStartTime).TotalSeconds, 2)
            });
        }

        /// <summary>
        /// Health check endpoint
        /// </summary>
        [HttpGet("health")]
        public IActionResult HealthCheck()
        {
            return Ok(new
            {
                status = "healthy",
                timestamp = DateTime.UtcNow.ToString("O")
            });
        }
    }
}

