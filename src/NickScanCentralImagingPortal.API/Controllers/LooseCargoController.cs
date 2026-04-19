using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API Controller for Loose Cargo (Non-containerized cargo)
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class LooseCargoController : ControllerBase
    {
        private readonly ILooseCargoService _looseCargoService;
        private readonly ILogger<LooseCargoController> _logger;

        public LooseCargoController(
            ILooseCargoService looseCargoService,
            ILogger<LooseCargoController> logger)
        {
            _looseCargoService = looseCargoService;
            _logger = logger;
        }

        /// <summary>
        /// Get loose cargo records with filtering and pagination
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-empty fallback.
        [HttpGet]
        public async Task<IActionResult> GetLooseCargoRecords(
            [FromQuery] string? clearanceType = null,
            [FromQuery] string? crmsLevel = null,
            [FromQuery] string? search = null,
            [FromQuery] int pageNumber = 1,
            [FromQuery] int pageSize = 100,
            [FromQuery] string? sortBy = null,
            [FromQuery] bool sortDescending = false,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] string? countryOfOrigin = null,
            [FromQuery] string? regimeCode = null)
        {
            try
            {
                _logger.LogInformation("Fetching loose cargo records. Clearance: {Type}, CRMS: {Level}, Search: {Search}, Page: {Page}",
                    clearanceType ?? "All", crmsLevel ?? "All", search ?? "None", pageNumber);

                var request = new LooseCargoSearchRequest
                {
                    ClearanceType = clearanceType,
                    CrmsLevel = crmsLevel,
                    SearchTerm = search,
                    PageNumber = pageNumber,
                    PageSize = pageSize,
                    SortBy = sortBy,
                    SortDescending = sortDescending,
                    FromDate = fromDate,
                    ToDate = toDate,
                    CountryOfOrigin = countryOfOrigin,
                    RegimeCode = regimeCode
                };

                var response = await _looseCargoService.SearchAsync(request);

                _logger.LogInformation("Found {Count} loose cargo records (page {Page}, total {Total})",
                    response.Records.Count, response.PageNumber, response.TotalCount);

                return Ok(new
                {
                    success = true,
                    data = response.Records,
                    count = response.Records.Count,
                    totalCount = response.TotalCount,
                    pageNumber = response.PageNumber,
                    pageSize = response.PageSize,
                    totalPages = response.TotalPages,
                    hasPreviousPage = response.HasPreviousPage,
                    hasNextPage = response.HasNextPage,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching loose cargo records");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error fetching loose cargo records",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get loose cargo statistics
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-zero fallbacks.
        [HttpGet("stats")]
        public async Task<IActionResult> GetLooseCargoStats()
        {
            try
            {
                var stats = await _looseCargoService.GetStatisticsAsync();

                return Ok(new
                {
                    success = true,
                    data = new
                    {
                        totalRecords = stats.TotalRecords,
                        imports = stats.Imports,
                        exports = stats.Exports,
                        transit = stats.Transit,
                        highRisk = stats.HighRisk,
                        mediumRisk = stats.MediumRisk,
                        lowRisk = stats.LowRisk,
                        recentRecords = stats.RecentRecords,
                        totalDutyPaid = stats.TotalDutyPaid,
                        oldestRecord = stats.OldestRecord,
                        newestRecord = stats.NewestRecord
                    },
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching loose cargo statistics");
                return StatusCode(500, new { success = false, message = "Error fetching loose cargo statistics" });
            }
        }

        /// <summary>
        /// Get loose cargo detail by ID (with manifest items)
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-notfound fallback.
        [HttpGet("{id}")]
        public async Task<IActionResult> GetLooseCargoDetail(int id)
        {
            try
            {
                var detail = await _looseCargoService.GetDetailAsync(id);

                if (detail == null)
                {
                    return NotFound(new { success = false, message = "Loose cargo record not found" });
                }

                return Ok(new
                {
                    success = true,
                    data = detail,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching loose cargo detail for ID: {Id}", id);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error fetching loose cargo detail",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get loose cargo detail by declaration number (with manifest items)
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-notfound fallback.
        [HttpGet("declaration/{declarationNumber}")]
        public async Task<IActionResult> GetLooseCargoDetailByDeclaration(string declarationNumber)
        {
            try
            {
                var detail = await _looseCargoService.GetDetailByDeclarationNumberAsync(declarationNumber);

                if (detail == null)
                {
                    return NotFound(new { success = false, message = "Loose cargo record not found" });
                }

                return Ok(new
                {
                    success = true,
                    data = detail,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching loose cargo detail for declaration number: {DeclarationNumber}",
                    declarationNumber);
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error fetching loose cargo detail",
                    error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get recent loose cargo records
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-empty fallback.
        [HttpGet("recent")]
        public async Task<IActionResult> GetRecentLooseCargo([FromQuery] int days = 7)
        {
            try
            {
                var records = await _looseCargoService.GetRecentRecordsAsync(days);

                return Ok(new
                {
                    success = true,
                    data = records,
                    count = records.Count,
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching recent loose cargo records");
                return StatusCode(500, new
                {
                    success = false,
                    message = "Error fetching recent loose cargo records",
                    error = ex.Message
                });
            }
        }
    }
}
