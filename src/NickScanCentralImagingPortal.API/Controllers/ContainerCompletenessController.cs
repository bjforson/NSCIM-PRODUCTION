using System.Data.Common;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.Logging;
using NickScanCentralImagingPortal.Services.Monitoring;
using NickScanCentralImagingPortal.Services.Permissions;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ContainerCompletenessController : ControllerBase
    {
        private readonly IContainerCompletenessService _completenessService;
        private readonly ApplicationDbContext _dbContext;
        private readonly ThrottledLogger _logger;
        private readonly IPermissionService _permissionService;
        private readonly IConfiguration _configuration;
        private readonly IServiceProvider _serviceProvider;
        private readonly ComprehensiveHealthCheckService? _healthCheckService;
        private const string SERVICE_ID = "COMPLETENESS-API";

        public ContainerCompletenessController(
            IContainerCompletenessService completenessService,
            ApplicationDbContext dbContext,
            IPermissionService permissionService,
            IConfiguration configuration,
            IServiceProvider serviceProvider,
            ILogger<ContainerCompletenessController> logger)
        {
            _completenessService = completenessService;
            _dbContext = dbContext;
            _permissionService = permissionService;
            _configuration = configuration;
            _serviceProvider = serviceProvider;
            _logger = new ThrottledLogger(logger, SERVICE_ID);

            // Try to get health check service (may not be available)
            _healthCheckService = serviceProvider.GetService<ComprehensiveHealthCheckService>();
        }

        /// <summary>
        /// Check if the current user has permission to access completeness endpoints.
        /// Accepts either the API permission (containers.completeness.view) or the page permission (pages.validation.completeness).
        /// </summary>
        private async Task<bool> HasCompletenessAccessAsync()
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return false;
            }

            // Check if user has either the API permission or the page permission
            var hasApiPermission = await _permissionService.HasPermissionAsync(username, Permissions.ContainersCompletenessView);
            var hasPagePermission = await _permissionService.HasPermissionAsync(username, Permissions.PagesValidationCompleteness);

            return hasApiPermission || hasPagePermission;
        }

        /// <summary>
        /// Get completeness statistics
        /// </summary>
        [HttpGet("stats")]
        [ProducesResponseType(200)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<CompletenessStatsResponse>> GetStats()
        {
            // Check permission - accept either API or page permission
            if (!await HasCompletenessAccessAsync())
            {
                return StatusCode(403, new { error = "User does not have permission to access completeness statistics" });
            }

            try
            {
                _logger.LogInfo("GetStats", "Fetching completeness statistics");

                // ✅ PERFORMANCE FIX: Use database-level aggregation instead of loading all records
                // Get most recent scan per container using window function approach
                // ✅ SQL Server 2014 FIX: Semicolon required before WITH clause
                // ✅ FIX: Filter out invalid/placeholder container numbers
                var statsQuery = @"
WITH mostrecentscans AS (
    SELECT *,
           ROW_NUMBER() OVER (
               PARTITION BY containernumber, scannertype 
               ORDER BY scandate DESC, createdat DESC
           ) as rownum
    FROM containercompletenessstatuses
    WHERE containernumber IS NOT NULL 
      AND containernumber != ''
      AND LENGTH(containernumber) >= 8
      AND containernumber NOT IN ('XXXX', 'SSSS', 'Unknown', 'PLACEHOLDER', 'CONTAINER')
      AND containernumber NOT LIKE '% %'
      AND containernumber ~ '^[A-Za-z][A-Za-z][A-Za-z][A-Za-z]'
)
SELECT 
    COUNT(*)::int as totalcontainers,
    SUM(CASE WHEN status = 'Complete' THEN 1 ELSE 0 END)::int as completecontainers,
    SUM(CASE WHEN status = 'Missing' THEN 1 ELSE 0 END)::int as missingcontainers,
    SUM(CASE WHEN status = 'Failed' THEN 1 ELSE 0 END)::int as failedrequests,
    SUM(CASE WHEN status = 'Requested' THEN 1 ELSE 0 END)::int as requestedcontainers,
    SUM(CASE WHEN scannertype = 'FS6000' THEN 1 ELSE 0 END)::int as fs6000total,
    SUM(CASE WHEN scannertype = 'FS6000' AND status = 'Complete' THEN 1 ELSE 0 END)::int as fs6000complete,
    SUM(CASE WHEN scannertype = 'FS6000' AND status = 'Missing' THEN 1 ELSE 0 END)::int as fs6000missing,
    SUM(CASE WHEN scannertype = 'ASE' THEN 1 ELSE 0 END)::int as asetotal,
    SUM(CASE WHEN scannertype = 'ASE' AND status = 'Complete' THEN 1 ELSE 0 END)::int as asecomplete,
    SUM(CASE WHEN scannertype = 'ASE' AND status = 'Missing' THEN 1 ELSE 0 END)::int as asemissing,
    MAX(lastcheckedat) as lastchecktime
FROM mostrecentscans
WHERE rownum = 1
";

                // Use raw SQL with manual mapping for better performance
                var connection = _dbContext.Database.GetDbConnection();
                var wasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!wasOpen) await connection.OpenAsync();

                StatsResult? statsResult = null;
                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = statsQuery;
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        statsResult = new StatsResult
                        {
                            TotalContainers = reader.GetInt32(0),
                            CompleteContainers = reader.GetInt32(1),
                            MissingContainers = reader.GetInt32(2),
                            FailedRequests = reader.GetInt32(3),
                            RequestedContainers = reader.GetInt32(4),
                            FS6000Total = reader.GetInt32(5),
                            FS6000Complete = reader.GetInt32(6),
                            FS6000Missing = reader.GetInt32(7),
                            ASETotal = reader.GetInt32(8),
                            ASEComplete = reader.GetInt32(9),
                            ASEMissing = reader.GetInt32(10),
                            LastCheckTime = reader.IsDBNull(11) ? (DateTime?)null : reader.GetDateTime(11)
                        };
                    }
                }
                finally
                {
                    if (!wasOpen) await connection.CloseAsync();
                }

                var stats = new CompletenessStatsResponse
                {
                    TotalContainers = statsResult?.TotalContainers ?? 0,
                    CompleteContainers = statsResult?.CompleteContainers ?? 0,
                    MissingContainers = statsResult?.MissingContainers ?? 0,
                    FailedRequests = statsResult?.FailedRequests ?? 0,
                    RequestedContainers = statsResult?.RequestedContainers ?? 0,
                    CompletenessRate = statsResult != null && statsResult.TotalContainers > 0
                        ? (double)statsResult.CompleteContainers / statsResult.TotalContainers * 100
                        : 0,

                    FS6000Total = statsResult?.FS6000Total ?? 0,
                    FS6000Complete = statsResult?.FS6000Complete ?? 0,
                    FS6000Missing = statsResult?.FS6000Missing ?? 0,

                    ASETotal = statsResult?.ASETotal ?? 0,
                    ASEComplete = statsResult?.ASEComplete ?? 0,
                    ASEMissing = statsResult?.ASEMissing ?? 0,

                    LastCheckTime = statsResult?.LastCheckTime ?? DateTime.Now,
                    ServiceRunning = await GetServiceRunningStatusAsync()
                };

                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetStats", "Error fetching completeness statistics", ex);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        // Helper class for raw SQL query result
        private class StatsResult
        {
            public int TotalContainers { get; set; }
            public int CompleteContainers { get; set; }
            public int MissingContainers { get; set; }
            public int FailedRequests { get; set; }
            public int RequestedContainers { get; set; }
            public int FS6000Total { get; set; }
            public int FS6000Complete { get; set; }
            public int FS6000Missing { get; set; }
            public int ASETotal { get; set; }
            public int ASEComplete { get; set; }
            public int ASEMissing { get; set; }
            public DateTime? LastCheckTime { get; set; }
        }

        /// <summary>
        /// Get containers with missing ICUMS data
        /// </summary>
        [HttpGet("missing")]
        [ProducesResponseType(200)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<List<ContainerCompletenessStatus>>> GetMissingContainers(
            [FromQuery] string? scannerType = null,
            [FromQuery] int? maxAgeDays = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 100)
        {
            // Check permission - accept either API or page permission
            if (!await HasCompletenessAccessAsync())
            {
                return StatusCode(403, new { error = "User does not have permission to access completeness data" });
            }

            try
            {
                _logger.LogInfo("GetMissingContainers", "Fetching missing containers - Page: {Page}, Size: {PageSize}", page, pageSize);

                // ✅ FIX: Use raw SQL with window function to avoid EF Core projection translation issues
                // Build WHERE clause and parameters
                var whereConditions = new List<string> { "status = 'Missing'" };
                var sqlParameters = new List<object>();
                var paramIndex = 0;

                if (!string.IsNullOrEmpty(scannerType))
                {
                    whereConditions.Add("scannertype = {" + paramIndex + "}");
                    sqlParameters.Add(scannerType);
                    paramIndex++;
                }

                if (maxAgeDays.HasValue)
                {
                    var cutoffDate = DateTime.UtcNow.AddDays(-maxAgeDays.Value);
                    whereConditions.Add("scandate >= {" + paramIndex + "}");
                    sqlParameters.Add(cutoffDate);
                    paramIndex++;
                }

                var whereClause = string.Join(" AND ", whereConditions);
                var skip = (page - 1) * pageSize;

                // Add pagination parameters
                var skipParamIndex = paramIndex++;
                var pageSizeParamIndex = paramIndex;

                // Use FromSqlRaw with proper entity mapping - EF Core will map the results automatically
                // ✅ SQL Server 2014 FIX: Semicolon required before WITH clause
                // ✅ FIX: Filter out invalid/placeholder container numbers
                var sqlQuery = @"
WITH mostrecentscans AS (
    SELECT id, containernumber, scannertype, inspectionid, scandate, hasicumsdata, icumsdatadate,
           boedocumentid, clearancetype, status, scannerdatacompleteness, icumsdatacompleteness,
           imagedatacompleteness, overallcompleteness, hasscannerdata, hasimagedata, isconsolidated,
           totalhousebls, completehousebls, consolidationdetails, groupidentifier, createdat,
           updatedat, errormessage, retrycount, lastcheckedat, workflowstage,
           ROW_NUMBER() OVER (
               PARTITION BY containernumber, scannertype 
               ORDER BY scandate DESC, createdat DESC
           ) as rownum
    FROM containercompletenessstatuses
    WHERE " + whereClause + @"
      AND containernumber IS NOT NULL
      AND containernumber != ''
      AND LENGTH(containernumber) >= 8
      AND containernumber NOT IN ('XXXX', 'SSSS', 'Unknown', 'PLACEHOLDER', 'CONTAINER')
      AND containernumber NOT LIKE '% %'
      AND containernumber ~ '^[A-Za-z][A-Za-z][A-Za-z][A-Za-z]'
)
SELECT id, containernumber, scannertype, inspectionid, scandate, hasicumsdata, icumsdatadate,
       boedocumentid, clearancetype, status, scannerdatacompleteness, icumsdatacompleteness,
       imagedatacompleteness, overallcompleteness, hasscannerdata, hasimagedata, isconsolidated,
       totalhousebls, completehousebls, consolidationdetails, groupidentifier, createdat,
       updatedat, errormessage, retrycount, lastcheckedat, workflowstage
FROM mostrecentscans
WHERE rownum = 1
ORDER BY scandate DESC
LIMIT {" + pageSizeParamIndex + @"} OFFSET {" + skipParamIndex + @"}";

                sqlParameters.Add(skip);
                sqlParameters.Add(pageSize);

                var missing = await _dbContext.Set<ContainerCompletenessStatus>()
                    .FromSqlRaw(sqlQuery, sqlParameters.ToArray())
                    .ToListAsync();
#pragma warning restore EF1002

                return Ok(missing);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetMissingContainers", "Error fetching missing containers", ex);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        /// <summary>
        /// Get complete containers (with ICUMS data)
        /// </summary>
        [HttpGet("complete")]
        [ProducesResponseType(200)]
        [ProducesResponseType(403)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<List<ContainerCompletenessStatus>>> GetCompleteContainers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            // Check permission - accept either API or page permission
            if (!await HasCompletenessAccessAsync())
            {
                return StatusCode(403, new { error = "User does not have permission to access completeness data" });
            }

            try
            {
                _logger.LogInfo("GetCompleteContainers", "Fetching complete containers - Page: {Page}, Size: {PageSize}", page, pageSize);

                // ✅ FIX: Use raw SQL with window function to avoid EF Core projection translation issues
                var skip = (page - 1) * pageSize;

                // Use FromSqlRaw with proper entity mapping - EF Core will map the results automatically
                // ✅ SQL Server 2014 FIX: Semicolon required before WITH clause
                // ✅ FIX: Filter out invalid/placeholder container numbers
                // Safe: Uses parameterized queries for skip and pageSize
#pragma warning disable EF1002 // Method 'FromSqlRaw' inserts interpolated strings directly into the SQL
                var sqlQuery = @"
WITH mostrecentscans AS (
    SELECT id, containernumber, scannertype, inspectionid, scandate, hasicumsdata, icumsdatadate,
           boedocumentid, clearancetype, status, scannerdatacompleteness, icumsdatacompleteness,
           imagedatacompleteness, overallcompleteness, hasscannerdata, hasimagedata, isconsolidated,
           totalhousebls, completehousebls, consolidationdetails, groupidentifier, createdat,
           updatedat, errormessage, retrycount, lastcheckedat, workflowstage,
           ROW_NUMBER() OVER (
               PARTITION BY containernumber, scannertype 
               ORDER BY scandate DESC, createdat DESC
           ) as rownum
    FROM containercompletenessstatuses
    WHERE status = 'Complete'
      AND containernumber IS NOT NULL
      AND containernumber != ''
      AND LENGTH(containernumber) >= 8
      AND containernumber NOT IN ('XXXX', 'SSSS', 'Unknown', 'PLACEHOLDER', 'CONTAINER')
      AND containernumber NOT LIKE '% %'
      AND containernumber ~ '^[A-Za-z][A-Za-z][A-Za-z][A-Za-z]'
)
SELECT id, containernumber, scannertype, inspectionid, scandate, hasicumsdata, icumsdatadate,
       boedocumentid, clearancetype, status, scannerdatacompleteness, icumsdatacompleteness,
       imagedatacompleteness, overallcompleteness, hasscannerdata, hasimagedata, isconsolidated,
       totalhousebls, completehousebls, consolidationdetails, groupidentifier, createdat,
       updatedat, errormessage, retrycount, lastcheckedat, workflowstage
FROM mostrecentscans
WHERE rownum = 1
ORDER BY updatedat DESC
LIMIT {1} OFFSET {0}";

                var mostRecentComplete = await _dbContext.Set<ContainerCompletenessStatus>()
                    .FromSqlRaw(sqlQuery, skip, pageSize)
                    .ToListAsync();
#pragma warning restore EF1002

                return Ok(mostRecentComplete);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetCompleteContainers", "Error fetching complete containers", ex);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        /// <summary>
        /// Trigger manual completeness check
        /// </summary>
        [HttpPost("trigger-check")]
        [HasPermission(Permissions.ContainersCompletenessManage)]
        [ProducesResponseType(200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<TriggerCheckResponse>> TriggerCheck()
        {
            try
            {
                _logger.LogInfo("TriggerCheck", "Triggering manual completeness check");

                await _completenessService.CheckContainerCompletenessAsync(CancellationToken.None);

                return Ok(new TriggerCheckResponse
                {
                    Success = true,
                    Message = "Completeness check triggered successfully",
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("TriggerCheck", "Error triggering completeness check", ex);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        /// <summary>
        /// Get service status
        /// </summary>
        [HttpGet("service-status")]
        [ProducesResponseType(200)]
        [ProducesResponseType(403)]
        public async Task<ActionResult<ServiceStatusResponse>> GetServiceStatus()
        {
            // Check permission - accept either API or page permission
            if (!await HasCompletenessAccessAsync())
            {
                return StatusCode(403, new { error = "User does not have permission to access completeness service status" });
            }

            try
            {
                var isRunning = await GetServiceRunningStatusAsync();
                var checkIntervalMinutes = _configuration.GetValue<int>("BackgroundServices:ContainerCompletenessService:CheckIntervalMinutes", 5);
                var lastCheckTime = await GetLastServiceCheckTimeAsync();
                var nextCheckTime = lastCheckTime.AddMinutes(checkIntervalMinutes);

                return Ok(new ServiceStatusResponse
                {
                    IsRunning = isRunning,
                    LastCheckTime = lastCheckTime,
                    NextCheckTime = nextCheckTime,
                    CheckInterval = TimeSpan.FromMinutes(checkIntervalMinutes)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError("GetServiceStatus", "Error getting service status", ex);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        /// <summary>
        /// Request BOE data for a specific container
        /// </summary>
        [HttpPost("request-boe/{containerNumber}")]
        [HasPermission(Permissions.ContainersCompletenessManage)]
        [ProducesResponseType(200)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<ActionResult> RequestBOE(string containerNumber)
        {
            try
            {
                _logger.LogInfo("RequestBOE", "Requesting BOE for container: {ContainerNumber}", new { ContainerNumber = containerNumber });

                // Find the completeness status
                var status = await _dbContext.ContainerCompletenessStatuses
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (status == null)
                {
                    return NotFound($"Container {containerNumber} not found in completeness tracking");
                }

                // Update status to "Requested"
                status.Status = "Requested";
                status.RetryCount++;
                status.UpdatedAt = DateTime.UtcNow;

                await _dbContext.SaveChangesAsync();

                // TODO: Actually queue BOE request to ICUMS Download Queue

                return Ok(new { message = $"BOE request queued for {containerNumber}" });
            }
            catch (Exception ex)
            {
                _logger.LogError("RequestBOE", "Error requesting BOE for container: {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        /// <summary>
        /// Bulk request BOE data for multiple containers
        /// </summary>
        [HttpPost("request-boe-bulk")]
        [HasPermission(Permissions.ContainersCompletenessManage)]
        [ProducesResponseType(200)]
        [ProducesResponseType(500)]
        public async Task<ActionResult> RequestBOEBulk([FromBody] List<string> containerNumbers)
        {
            try
            {
                _logger.LogInfo("RequestBOEBulk", "Bulk requesting BOE for {Count} containers", new { Count = containerNumbers.Count });

                var updated = 0;
                foreach (var containerNumber in containerNumbers)
                {
                    var status = await _dbContext.ContainerCompletenessStatuses
                        .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                    if (status != null)
                    {
                        status.Status = "Requested";
                        status.RetryCount++;
                        status.UpdatedAt = DateTime.UtcNow;
                        updated++;
                    }
                }

                await _dbContext.SaveChangesAsync();

                return Ok(new { message = $"BOE requests queued for {updated} containers", count = updated });
            }
            catch (Exception ex)
            {
                _logger.LogError("RequestBOEBulk", "Error bulk requesting BOE", ex);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        /// <summary>
        /// Get complete records ready for image analysis (Scanner + ICUMS + Images all present)
        /// Grouped by Container (consolidated) or BOE (non-consolidated)
        /// </summary>
        [HttpGet("image-analysis")]
        [ProducesResponseType(200)]
        [ProducesResponseType(403)]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<ImageAnalysisResponse>> GetCompleteRecordsForAnalysis(
            [FromQuery] string? cargoType = null, // "consolidated" or "non-consolidated"
            [FromQuery] string? scannerType = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            // Check permission - accept either API or page permission
            if (!await HasCompletenessAccessAsync())
            {
                return StatusCode(403, new { error = "User does not have permission to access completeness data" });
            }

            try
            {
                _logger.LogInfo("GetCompleteRecordsForAnalysis", "✅ Endpoint called: /api/ContainerCompleteness/image-analysis?page={Page}&pageSize={PageSize}", page, pageSize);
                _logger.LogInfo("GetCompleteRecordsForAnalysis", "Fetching complete records for image analysis");

                // ✅ PERFORMANCE FIX: Use database-level filtering and grouping
                // ✅ FIX: Filter out invalid/placeholder container numbers
                var baseQuery = _dbContext.ContainerCompletenessStatuses
                    .Where(s => (s.WorkflowStage == "ImageAnalysis" ||
                                (s.WorkflowStage == null && s.Status == "Complete")) &&
                                s.HasScannerData &&
                                s.HasICUMSData &&
                                s.HasImageData &&
                                !string.IsNullOrEmpty(s.ContainerNumber) &&
                                s.ContainerNumber.Length >= 8 &&
                                !new[] { "XXXX", "SSSS", "Unknown", "PLACEHOLDER", "CONTAINER" }.Contains(s.ContainerNumber) &&
                                !s.ContainerNumber.Contains(" ") &&
                                s.ContainerNumber.Length >= 4 &&
                                char.IsLetter(s.ContainerNumber[0]) &&
                                char.IsLetter(s.ContainerNumber[1]) &&
                                char.IsLetter(s.ContainerNumber[2]) &&
                                char.IsLetter(s.ContainerNumber[3]));

                // ✅ OPTIMIZED: Get most recent scan per container using database query
                var mostRecentQuery = from s in baseQuery
                                      group s by new { s.ContainerNumber, s.ScannerType } into g
                                      select g.OrderByDescending(x => x.ScanDate)
                                               .ThenByDescending(x => x.CreatedAt)
                                               .First();

                var completeRecords = await mostRecentQuery.ToListAsync();

                // Apply filters
                if (!string.IsNullOrEmpty(cargoType))
                {
                    bool isConsolidated = cargoType.ToLower() == "consolidated";
                    completeRecords = completeRecords.Where(r => r.IsConsolidated == isConsolidated).ToList();
                }

                if (!string.IsNullOrEmpty(scannerType))
                {
                    completeRecords = completeRecords.Where(r => r.ScannerType == scannerType).ToList();
                }

                // Group by GroupIdentifier (Container# for consolidated, BOE# for non-consolidated)
                var allGroupedRecords = completeRecords
                    .Where(r => !string.IsNullOrEmpty(r.GroupIdentifier))
                    .GroupBy(r => r.GroupIdentifier)
                    .Select(g => new ImageAnalysisGroup
                    {
                        GroupIdentifier = g.Key!,
                        IsConsolidated = g.First().IsConsolidated,
                        ScannerType = g.First().ScannerType,
                        ScanDate = g.First().ScanDate,
                        CreatedAt = g.Max((ContainerCompletenessStatus r) => r.CreatedAt), // ✅ Use max CreatedAt for newest record in group
                        TotalRecords = g.Count(),
                        TotalHouseBLs = g.First().TotalHouseBLs,
                        CompleteHouseBLs = g.First().CompleteHouseBLs,
                        ConsolidationDetails = g.First().ConsolidationDetails,
                        ImageCount = g.Sum((ContainerCompletenessStatus r) => r.ImageDataCompleteness), // Placeholder
                        CompletionPercentage = g.First().OverallCompleteness,
                        Containers = g.Select((ContainerCompletenessStatus r) => r.ContainerNumber).Distinct().ToList()
                    })
                    // ✅ FIX: Order by CreatedAt DESC (newest first) to show most recent records at top
                    .OrderByDescending((ImageAnalysisGroup g) => g.CreatedAt)
                    .ThenByDescending((ImageAnalysisGroup g) => g.ScanDate)
                    .ToList();

                // ✅ Calculate totals BEFORE pagination
                var totalGroups = allGroupedRecords.Count;
                var consolidatedCount = allGroupedRecords.Where((ImageAnalysisGroup g) => g.IsConsolidated).Count();
                var nonConsolidatedCount = allGroupedRecords.Where((ImageAnalysisGroup g) => !g.IsConsolidated).Count();

                // ✅ Apply pagination (or return all if pageSize = -1)
                var pagedGroups = pageSize == -1
                    ? allGroupedRecords // Return ALL records
                    : allGroupedRecords
                        .Skip((page - 1) * pageSize)
                        .Take(pageSize)
                        .ToList();

                var response = new ImageAnalysisResponse
                {
                    TotalGroups = totalGroups, // ✅ FIXED: Total count before pagination
                    ConsolidatedCount = consolidatedCount,
                    NonConsolidatedCount = nonConsolidatedCount,
                    Groups = pagedGroups,
                    Page = page,
                    PageSize = pageSize
                };

                _logger.LogInfo("GetCompleteRecordsForAnalysis", "Returning {Count} groups out of {Total} total",
                    pagedGroups.Count, totalGroups);

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetCompleteRecordsForAnalysis", "Error fetching image analysis data", ex);
                return StatusCode(500, new { error = "Internal server error", details = ex.Message });
            }
        }

        /// <summary>
        /// Sync WorkflowStage to 'ImageAnalysis' for all records that are complete and have all required data.
        /// Safe to run repeatedly (idempotent).
        /// </summary>
        [HttpPost("workflow/sync-image-analysis")]
        [HasPermission(Permissions.ContainersCompletenessManage)]
        [ProducesResponseType(200)]
        public async Task<ActionResult> SyncWorkflowStageForImageAnalysis()
        {
            try
            {
                // ✅ FIX: Filter out invalid/placeholder container numbers
                var sql = @"
UPDATE containercompletenessstatuses
SET workflowstage = 'ImageAnalysis', updatedat = now() AT TIME ZONE 'UTC'
WHERE status = 'Complete'
  AND hasscannerdata = true
  AND hasicumsdata = true
  AND hasimagedata = true
  AND (workflowstage IS NULL OR workflowstage NOT IN ('ImageAnalysis', 'Audit', 'PendingSubmission', 'Submitted', 'Completed'))
  AND containernumber IS NOT NULL
  AND containernumber != ''
  AND LENGTH(containernumber) >= 8
  AND containernumber NOT IN ('XXXX', 'SSSS', 'Unknown', 'PLACEHOLDER', 'CONTAINER')
  AND containernumber NOT LIKE '% %'
  AND containernumber ~ '^[A-Za-z][A-Za-z][A-Za-z][A-Za-z]';";

                var affected = await _dbContext.Database.ExecuteSqlRawAsync(sql);
                _logger.LogInfo("SyncWorkflowStageForImageAnalysis", "Workflow sync updated {Count} record(s)", affected);
                return Ok(new { success = true, updated = affected });
            }
            catch (Exception ex)
            {
                _logger.LogError("SyncWorkflowStageForImageAnalysis", "Error syncing workflow stage", ex);
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Get actual service running status
        /// </summary>
        private Task<bool> GetServiceRunningStatusAsync()
        {
            try
            {
                // Check if service is enabled in configuration
                var isEnabled = _configuration.GetValue<bool>("BackgroundServices:ContainerCompletenessService:Enabled", true);
                if (!isEnabled)
                {
                    return Task.FromResult(false);
                }

                // Try to get status from health check service first
                if (_healthCheckService != null)
                {
                    var serviceStatus = _healthCheckService.GetServiceStatus("ContainerCompletenessService");
                    if (serviceStatus.Status != Core.Interfaces.HealthStatus.Unknown)
                    {
                        return Task.FromResult(serviceStatus.Status == Core.Interfaces.HealthStatus.Healthy ||
                               serviceStatus.Status == Core.Interfaces.HealthStatus.Degraded);
                    }
                }

                // Fallback: Check if IHostedService is registered and running
                var hostedServices = _serviceProvider.GetServices<IHostedService>();
                var completenessService = hostedServices.FirstOrDefault(s =>
                    s.GetType().Name.Contains("ContainerCompleteness", StringComparison.OrdinalIgnoreCase));

                if (completenessService != null)
                {
                    // Service is registered, assume it's running if enabled
                    return Task.FromResult(true);
                }

                // If service is not found, check if it's the interface service (which is always available)
                return Task.FromResult(_completenessService != null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("GetServiceRunningStatus", "Error checking service status, defaulting to enabled", ex);
                // Default to true if we can't determine status
                return Task.FromResult(true);
            }
        }

        /// <summary>
        /// Get last service check time from database or health check service
        /// </summary>
        private async Task<DateTime> GetLastServiceCheckTimeAsync()
        {
            try
            {
                // Try to get from health check service
                if (_healthCheckService != null)
                {
                    var serviceStatus = _healthCheckService.GetServiceStatus("ContainerCompletenessService");
                    if (serviceStatus.LastChecked != DateTime.MinValue)
                    {
                        return serviceStatus.LastChecked;
                    }
                }

                // Fallback: Get most recent LastCheckedAt from database
                var lastCheck = await _dbContext.ContainerCompletenessStatuses
                    .Where(s => s.LastCheckedAt != null)
                    .OrderByDescending(s => s.LastCheckedAt)
                    .Select(s => s.LastCheckedAt!.Value)
                    .FirstOrDefaultAsync();

                if (lastCheck != default)
                {
                    return lastCheck;
                }

                // Default: return current time minus check interval
                var checkIntervalMinutes = _configuration.GetValue<int>("BackgroundServices:ContainerCompletenessService:CheckIntervalMinutes", 5);
                return DateTime.UtcNow.AddMinutes(-checkIntervalMinutes);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("GetLastServiceCheckTime", "Error getting last check time, using default", ex);
                var checkIntervalMinutes = _configuration.GetValue<int>("BackgroundServices:ContainerCompletenessService:CheckIntervalMinutes", 5);
                return DateTime.UtcNow.AddMinutes(-checkIntervalMinutes);
            }
        }
    }

    // Response DTOs
    public class CompletenessStatsResponse
    {
        public int TotalContainers { get; set; }
        public int CompleteContainers { get; set; }
        public int MissingContainers { get; set; }
        public int FailedRequests { get; set; }
        public int RequestedContainers { get; set; }
        public double CompletenessRate { get; set; }

        public int FS6000Total { get; set; }
        public int FS6000Complete { get; set; }
        public int FS6000Missing { get; set; }

        public int ASETotal { get; set; }
        public int ASEComplete { get; set; }
        public int ASEMissing { get; set; }

        public DateTime LastCheckTime { get; set; }
        public bool ServiceRunning { get; set; }
    }

    public class TriggerCheckResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public DateTime Timestamp { get; set; }
    }

    public class ServiceStatusResponse
    {
        public bool IsRunning { get; set; }
        public DateTime LastCheckTime { get; set; }
        public DateTime NextCheckTime { get; set; }
        public TimeSpan CheckInterval { get; set; }
    }

    public class ImageAnalysisResponse
    {
        public int TotalGroups { get; set; }
        public int ConsolidatedCount { get; set; }
        public int NonConsolidatedCount { get; set; }
        public List<ImageAnalysisGroup> Groups { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
    }

    public class ImageAnalysisGroup
    {
        public string GroupIdentifier { get; set; } = string.Empty; // Container# or BOE#
        public bool IsConsolidated { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanDate { get; set; }
        public DateTime CreatedAt { get; set; } // ✅ Added for sorting by newest first
        public int TotalRecords { get; set; }
        public int? TotalHouseBLs { get; set; }
        public int? CompleteHouseBLs { get; set; }
        public string? ConsolidationDetails { get; set; }
        public int ImageCount { get; set; }
        public int CompletionPercentage { get; set; }
        public List<string> Containers { get; set; } = new();
    }
}

