using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.DTOs.Reports;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.Reports;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API controller for generating various system reports
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize] // Require authentication for all endpoints
    public class ReportsController : ControllerBase
    {
        private readonly IReportsService _reportsService;
        private readonly ILogger<ReportsController> _logger;

        public ReportsController(
            IReportsService reportsService,
            ILogger<ReportsController> logger)
        {
            _reportsService = reportsService;
            _logger = logger;
        }

        /// <summary>
        /// Get container summary report
        /// </summary>
        [HttpGet("container-summary")]
        public async Task<ActionResult<ContainerSummaryReportDto>> GetContainerSummary(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.Now.AddDays(-30);
                endDate ??= DateTime.Now;

                _logger.LogInformation("Generating container summary report from {StartDate} to {EndDate}",
                    startDate, endDate);

                var report = await _reportsService.GetContainerSummaryReportAsync(startDate.Value, endDate.Value);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating container summary report");
                return StatusCode(500, "Failed to generate container summary report");
            }
        }

        /// <summary>
        /// Get scanner performance report
        /// </summary>
        [HttpGet("scanner-performance")]
        public async Task<ActionResult<ScannerPerformanceReportDto>> GetScannerPerformance(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.Now.AddDays(-7);
                endDate ??= DateTime.Now;

                _logger.LogInformation("Generating scanner performance report from {StartDate} to {EndDate}",
                    startDate, endDate);

                var report = await _reportsService.GetScannerPerformanceReportAsync(startDate.Value, endDate.Value);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating scanner performance report");
                return StatusCode(500, "Failed to generate scanner performance report");
            }
        }

        /// <summary>
        /// Get ICUMS activity report
        /// </summary>
        [HttpGet("icums-activity")]
        public async Task<ActionResult<ICUMSActivityReportDto>> GetICUMSActivity(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.Now.AddDays(-7);
                endDate ??= DateTime.Now;

                _logger.LogInformation("Generating ICUMS activity report from {StartDate} to {EndDate}",
                    startDate, endDate);

                var report = await _reportsService.GetICUMSActivityReportAsync(startDate.Value, endDate.Value);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating ICUMS activity report");
                return StatusCode(500, "Failed to generate ICUMS activity report");
            }
        }

        /// <summary>
        /// Get user activity report
        /// </summary>
        [HttpGet("user-activity")]
        public async Task<ActionResult<UserActivityReportDto>> GetUserActivity(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.Now.AddDays(-30);
                endDate ??= DateTime.Now;

                _logger.LogInformation("Generating user activity report from {StartDate} to {EndDate}",
                    startDate, endDate);

                var report = await _reportsService.GetUserActivityReportAsync(startDate.Value, endDate.Value);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating user activity report");
                return StatusCode(500, "Failed to generate user activity report");
            }
        }

        /// <summary>
        /// Get vehicle imports report
        /// </summary>
        [HttpGet("vehicle-imports")]
        public async Task<ActionResult<VehicleImportsReportDto>> GetVehicleImports(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.Now.AddDays(-30);
                endDate ??= DateTime.Now;

                _logger.LogInformation("Generating vehicle imports report from {StartDate} to {EndDate}",
                    startDate, endDate);

                var report = await _reportsService.GetVehicleImportsReportAsync(startDate.Value, endDate.Value);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating vehicle imports report");
                return StatusCode(500, "Failed to generate vehicle imports report");
            }
        }

        /// <summary>
        /// Get validation summary report
        /// </summary>
        [HttpGet("validation-summary")]
        public async Task<ActionResult<ValidationSummaryReportDto>> GetValidationSummary(
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.Now.AddDays(-30);
                endDate ??= DateTime.Now;

                _logger.LogInformation("Generating validation summary report from {StartDate} to {EndDate}",
                    startDate, endDate);

                var report = await _reportsService.GetValidationSummaryReportAsync(startDate.Value, endDate.Value);
                return Ok(report);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating validation summary report");
                return StatusCode(500, "Failed to generate validation summary report");
            }
        }

        /// <summary>
        /// Export report in specified format
        /// </summary>
        [HttpGet("{reportType}/export")]
        public async Task<IActionResult> ExportReport(
            string reportType,
            [FromQuery] string format = "excel",
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                startDate ??= DateTime.Now.AddDays(-30);
                endDate ??= DateTime.Now;

                _logger.LogInformation("Exporting {ReportType} report as {Format} from {StartDate} to {EndDate}",
                    reportType, format, startDate, endDate);

                var exportResult = await _reportsService.ExportReportAsync(reportType, format, startDate.Value, endDate.Value);

                return File(exportResult.Data, exportResult.ContentType, exportResult.FileName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting {ReportType} report as {Format}", reportType, format);
                return StatusCode(500, $"Failed to export {reportType} report");
            }
        }

        /// <summary>
        /// Get available report types
        /// </summary>
        [HttpGet("types")]
        public ActionResult<List<ReportTypeDto>> GetReportTypes()
        {
            try
            {
                var reportTypes = new List<ReportTypeDto>
                {
                    new() { Type = "container-summary", Name = "Container Summary", Description = "Overview of container scans and processing" },
                    new() { Type = "scanner-performance", Name = "Scanner Performance", Description = "Scanner statistics and uptime metrics" },
                    new() { Type = "icums-activity", Name = "ICUMS Activity", Description = "ICUMS download and submission metrics" },
                    new() { Type = "user-activity", Name = "User Activity", Description = "User login and action logs" },
                    new() { Type = "vehicle-imports", Name = "Vehicle Imports", Description = "VIN extraction and tracking" },
                    new() { Type = "validation-summary", Name = "Validation Summary", Description = "Data completeness analysis" }
                };

                return Ok(reportTypes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting report types");
                return StatusCode(500, "Failed to get report types");
            }
        }
    }
}

