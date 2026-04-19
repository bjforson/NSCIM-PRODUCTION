using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Infrastructure.Repositories;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API Controller for handling Consolidated vs Non-Consolidated Cargo
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class ConsolidatedCargoController : ControllerBase
    {
        private readonly IcumDownloadsDbContext _icumDbContext;
        private readonly ILogger<ConsolidatedCargoController> _logger;
        private readonly ThrottledLogger _throttledLogger;
        private const string SERVICE_ID = "[CONSOLIDATED-CARGO-API]";

        public ConsolidatedCargoController(
            IcumDownloadsDbContext icumDbContext,
            ILogger<ConsolidatedCargoController> logger)
        {
            _icumDbContext = icumDbContext;
            _logger = logger;
            _throttledLogger = new ThrottledLogger(logger, SERVICE_ID);
        }

        /// <summary>
        /// Get non-consolidated cargo (grouped by Master BL/Declaration)
        /// Shows one Master BL with all its containers
        /// </summary>
        [ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "clearanceType", "page", "pageSize" })]
        [HttpGet("non-consolidated")]
        public async Task<ActionResult<List<NonConsolidatedCargoGroup>>> GetNonConsolidatedCargo(
            [FromQuery] string? clearanceType = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                _throttledLogger.LogInfo("GetNonConsolidatedCargo", "Fetching non-consolidated cargo - ClearanceType: {ClearanceType}, Page: {Page}", new { clearanceType, page });

                var queries = new ConsolidatedCargoQueries(_icumDbContext);
                var groups = await queries.GetNonConsolidatedCargoGroupsAsync(clearanceType, pageSize);

                _throttledLogger.LogInfo("GetNonConsolidatedCargo", "Found {Count} non-consolidated cargo groups", groups.Count);

                return Ok(groups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching non-consolidated cargo");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get consolidated cargo (grouped by Container)
        /// Shows one container with all its House BLs
        /// </summary>
        [ResponseCache(Duration = 60, VaryByQueryKeys = new[] { "containerNumber", "clearanceType", "page", "pageSize" })]
        [HttpGet("consolidated")]
        public async Task<ActionResult<List<ConsolidatedCargoGroup>>> GetConsolidatedCargo(
            [FromQuery] string? containerNumber = null,
            [FromQuery] string? clearanceType = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                _throttledLogger.LogInfo("GetConsolidatedCargo", "Fetching consolidated cargo - Container: {Container}, ClearanceType: {ClearanceType}",
                    new { containerNumber, clearanceType });

                var queries = new ConsolidatedCargoQueries(_icumDbContext);
                var groups = await queries.GetConsolidatedCargoGroupsAsync(masterBL: null, containerNumber: containerNumber, clearanceType: clearanceType, limit: pageSize);

                _throttledLogger.LogInfo("GetConsolidatedCargo", "Found {Count} consolidated containers", groups.Count);

                return Ok(groups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching consolidated cargo");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get all containers under a specific Declaration/Master BL
        /// </summary>
        [HttpGet("declaration/{declarationNumber}/containers")]
        public async Task<ActionResult<List<string>>> GetContainersByDeclaration(string declarationNumber)
        {
            try
            {
                var queries = new ConsolidatedCargoQueries(_icumDbContext);
                var containers = await queries.GetContainersByDeclarationAsync(declarationNumber);

                _throttledLogger.LogInfo("GetContainersByDeclaration", "Found {Count} containers for declaration {Declaration}",
                    containers.Count, declarationNumber);

                return Ok(containers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching containers for declaration {DeclarationNumber}", declarationNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get all House BLs under a specific container
        /// </summary>
        [ResponseCache(Duration = 60)]
        [HttpGet("container/{containerNumber}/housebls")]
        public async Task<ActionResult<List<HouseBLDetail>>> GetHouseBLsByContainer(string containerNumber)
        {
            try
            {
                var queries = new ConsolidatedCargoQueries(_icumDbContext);
#pragma warning disable CS0618 // 'ConsolidatedCargoQueries.GetHouseBLsByContainerAsync(string)' is obsolete
                var houseBLs = await queries.GetHouseBLsByContainerAsync(containerNumber);
#pragma warning restore CS0618

                _throttledLogger.LogInfo("GetHouseBLsByContainer", "Found {Count} House BLs for container {Container}",
                    houseBLs.Count, containerNumber);

                return Ok(houseBLs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching House BLs for container {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get statistics on consolidated vs non-consolidated cargo
        /// </summary>
        [HttpGet("statistics")]
        public async Task<ActionResult<ConsolidatedCargoStatistics>> GetStatistics()
        {
            try
            {
                var stats = new ConsolidatedCargoStatistics();

                stats.TotalConsolidated = await _icumDbContext.BOEDocuments
                    .Where(b => b.IsConsolidated)
                    .CountAsync();

                stats.TotalNonConsolidated = await _icumDbContext.BOEDocuments
                    .Where(b => !b.IsConsolidated)
                    .CountAsync();

                stats.TotalRecords = stats.TotalConsolidated + stats.TotalNonConsolidated;

                stats.ConsolidatedPercentage = stats.TotalRecords > 0
                    ? (stats.TotalConsolidated * 100.0 / stats.TotalRecords)
                    : 0;

                stats.UniqueConsolidatedContainers = await _icumDbContext.BOEDocuments
                    .Where(b => b.IsConsolidated)
                    .Select(b => b.ContainerNumber)
                    .Distinct()
                    .CountAsync();

                stats.UniqueNonConsolidatedDeclarations = await _icumDbContext.BOEDocuments
                    .Where(b => !b.IsConsolidated)
                    .Select(b => b.DeclarationNumber)
                    .Distinct()
                    .CountAsync();

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching consolidated cargo statistics");
                return StatusCode(500, "Internal server error");
            }
        }
    }

    /// <summary>
    /// Statistics for consolidated cargo
    /// </summary>
    public class ConsolidatedCargoStatistics
    {
        public int TotalRecords { get; set; }
        public int TotalConsolidated { get; set; }
        public int TotalNonConsolidated { get; set; }
        public double ConsolidatedPercentage { get; set; }
        public int UniqueConsolidatedContainers { get; set; }
        public int UniqueNonConsolidatedDeclarations { get; set; }
    }
}







