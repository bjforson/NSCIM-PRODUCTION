using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.API.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Dashboard;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly ThrottledLogger _logger;
        private readonly IComprehensiveDashboardService _comprehensiveDashboardService;
        private const string SERVICE_ID = "DASHBOARD-API";

        public DashboardController(
            ILogger<DashboardController> logger,
            IComprehensiveDashboardService comprehensiveDashboardService)
        {
            _logger = new ThrottledLogger(logger, SERVICE_ID);
            _comprehensiveDashboardService = comprehensiveDashboardService;
        }

        /// <summary>
        /// Get comprehensive dashboard data - captures every aspect of the system
        /// </summary>
        [HttpGet("comprehensive")]
        [ResponseCache(Duration = 30, VaryByQueryKeys = new string[] { })]
        [ProducesResponseType(200, Type = typeof(ComprehensiveDashboardData))]
        [ProducesResponseType(500)]
        public async Task<ActionResult<ComprehensiveDashboardData>> GetComprehensiveDashboard()
        {
            try
            {
                _logger.LogInfo("GetComprehensiveDashboard", "Fetching comprehensive dashboard data");
                var data = await _comprehensiveDashboardService.GetComprehensiveDashboardDataAsync();
                return Ok(data);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetComprehensiveDashboard", "Error fetching comprehensive dashboard data", ex);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        [HttpGet("enhanced-data")]
        [ResponseCache(Duration = 30)]
        public async Task<IActionResult> GetEnhancedDashboardData()
        {
            try
            {
                var data = await _comprehensiveDashboardService.GetComprehensiveDashboardDataAsync();
                return Ok(new
                {
                    ScansToday = data.Scanners?.Values.Sum(s => s.Performance?.ImagesProcessed24h ?? 0) ?? 0,
                    ActiveScanners = data.Scanners?.Count(s => s.Value.Status == "Online") ?? 0,
                    ThroughputDaily = data.ContainerValidation?.Throughput?.Daily ?? 0.0,
                    PendingReview = data.ContainerValidation?.Pipeline?.Stages?.Values.Sum(s => s.Count) ?? 0,
                    ScannerActivity = data.Scanners?.Select(s => new
                    {
                        ScannerId = s.Key,
                        s.Value.ScannerType,
                        ScansToday = s.Value.Performance?.ImagesProcessed24h ?? 0,
                        LastScanTime = s.Value.Health?.LastScan,
                        s.Value.Status
                    }) ?? Enumerable.Empty<object>(),
                    RecentActivity = (object?)data.RecentActivity ?? Array.Empty<object>(),
                    GeneratedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = "Failed to load dashboard data", details = ex.Message });
            }
        }
    }
}
