using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ASE;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize(Policy = "ScannerOperator")]
    [ApiController]
    [Route("api/[controller]")]
    public class AseController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AseController> _logger;

        public AseController(ApplicationDbContext context, ILogger<AseController> logger)
        {
            _context = context;
            _logger = logger;
        }

        [HttpGet("scans")]
        public async Task<ActionResult<IEnumerable<AseScan>>> GetScans(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? containerNumber = null,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            try
            {
                // ✅ MEMORY OPTIMIZATION: Default to last 30 days if no date range provided
                // Prevents loading entire table into memory
                var defaultStartDate = startDate.HasValue
                    ? DateTime.SpecifyKind(startDate.Value, DateTimeKind.Utc)
                    : DateTime.UtcNow.AddDays(-30);
                var defaultEndDate = endDate.HasValue
                    ? DateTime.SpecifyKind(endDate.Value.Date.AddDays(1).AddTicks(-1), DateTimeKind.Utc)
                    : DateTime.UtcNow;

                var query = _context.AseScans
                    .Where(s => s.ScanTime >= defaultStartDate && s.ScanTime <= defaultEndDate)
                    .AsQueryable();

                if (!string.IsNullOrEmpty(containerNumber))
                {
                    query = query.Where(s => s.ContainerNumber != null && s.ContainerNumber.Contains(containerNumber));
                }

                var totalCount = await query.CountAsync();
                var scans = await query
                    .OrderByDescending(s => s.ScanTime)
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .ToListAsync();

                return Ok(new
                {
                    Data = scans,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ASE scans");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("scans/{inspectionId}")]
        public async Task<ActionResult<AseScan>> GetScan(int inspectionId)
        {
            try
            {
                var scan = await _context.AseScans
                    .FirstOrDefaultAsync(s => s.InspectionId == inspectionId);

                if (scan == null)
                {
                    return NotFound($"ASE scan with InspectionID {inspectionId} not found");
                }

                return Ok(scan);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ASE scan {InspectionId}", inspectionId);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("sync-status")]
        public async Task<ActionResult<object>> GetSyncStatus()
        {
            try
            {
                var lastSync = await _context.AseSyncLogs
                    .OrderByDescending(x => x.LastSyncTime)
                    .FirstOrDefaultAsync();

                var totalScans = await _context.AseScans.CountAsync();

                // Get scans for TODAY (based on actual scan time, not sync/insert time)
                // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) for "today's" count
                // ScanTime from ASE database might be in UTC or local timezone depending on database settings
                // We'll use local timezone for "today" (midnight to midnight in local time)
                var now = DateTime.UtcNow;
                var todayStartUtc = DateTime.SpecifyKind(now.Date, DateTimeKind.Utc);
                var todayEndUtc = todayStartUtc.AddDays(1);

                var todayScansLocal = await _context.AseScans
                    .Where(s => s.ScanTime >= todayStartUtc && s.ScanTime < todayEndUtc)
                    .CountAsync();

                var todayScansUtc = await _context.AseScans
                    .Where(s => s.ScanTime >= todayStartUtc && s.ScanTime < todayEndUtc)
                    .CountAsync();

                // Also get 24-hour rolling window for reference
                var last24Hours = now.AddDays(-1);
                var scans24Hours = await _context.AseScans
                    .Where(s => s.ScanTime >= last24Hours)
                    .CountAsync();

                // ✅ Use calendar day (local timezone) as "today's" count
                // This gives a fixed period from 12:00 AM to 11:59:59 PM
                var todayScans = todayScansLocal; // Use calendar day in local timezone

                // Get recent scans to diagnose timezone (for debugging)
                var recentScans = await _context.AseScans
                    .OrderByDescending(s => s.ScanTime)
                    .Take(5)
                    .Select(s => new { s.ScanTime, s.SyncedAt, s.ContainerNumber })
                    .ToListAsync();

                // ✅ Check configuration status
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var aseConfig = configuration.GetSection("ASE");
                var connectionString = aseConfig["ConnectionString"] ?? "";
                var enableRealTimeSync = aseConfig.GetValue<bool>("EnableRealTimeSync", true);
                var hasPasswordEnvVar = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NICKSCAN_ASE_PASSWORD"));
                var hasPasswordPlaceholder = connectionString.Contains("***USE_ENV_VAR") || connectionString.Contains("***USE_ENV");

                return Ok(new
                {
                    LastSyncTime = lastSync?.LastSyncTime,
                    LastSyncedInspectionId = lastSync?.LastSyncedInspectionId,
                    TotalScans = totalScans,
                    RecentScans24h = scans24Hours, // 24-hour rolling window (for reference)
                    TodayScans = todayScans, // ✅ Calendar day (12:00 AM to 11:59:59 PM local time)
                    TodayScansLocal = todayScansLocal, // Calendar day in local timezone
                    TodayScansUtc = todayScansUtc, // Calendar day in UTC (for comparison)
                    RecentScans = recentScans, // For debugging timezone
                    SyncStatus = lastSync?.SyncStatus ?? "Unknown",
                    Configuration = new
                    {
                        EnableRealTimeSync = enableRealTimeSync,
                        HasPasswordEnvironmentVariable = hasPasswordEnvVar,
                        HasPasswordPlaceholder = hasPasswordPlaceholder,
                        ConnectionStringConfigured = !string.IsNullOrEmpty(connectionString),
                        CanSync = enableRealTimeSync && !hasPasswordPlaceholder && hasPasswordEnvVar
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ASE sync status");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("diagnose-sync")]
        public async Task<ActionResult<object>> DiagnoseSync()
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var aseConfig = configuration.GetSection("ASE");
                var connectionString = aseConfig["ConnectionString"] ?? "";

                // Check if password placeholder needs to be replaced
                if (connectionString.Contains("***USE_ENV_VAR") || connectionString.Contains("***USE_ENV"))
                {
                    var asePassword = Environment.GetEnvironmentVariable("NICKSCAN_ASE_PASSWORD");
                    if (!string.IsNullOrEmpty(asePassword))
                    {
                        connectionString = connectionString
                            .Replace("***USE_ENV_VAR_NICKSCAN_ASE_PASSWORD***", asePassword)
                            .Replace("***USE_ENV_VAR***", asePassword)
                            .Replace("***USE_ENV***", asePassword);
                    }
                }

                if (string.IsNullOrEmpty(connectionString))
                {
                    return BadRequest(new { error = "Connection string is not configured" });
                }

                // Get last synced ID
                var lastSync = await _context.AseSyncLogs
                    .OrderByDescending(x => x.LastSyncTime)
                    .FirstOrDefaultAsync();
                var lastSyncedId = lastSync?.LastSyncedInspectionId ?? 0;
                var startDate = aseConfig.GetValue<DateTime>("StartDate", DateTime.Parse("2025-09-01"));

                // Test connection and run diagnostic queries
                using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
                await connection.OpenAsync();

                var diagnostics = new
                {
                    lastSyncedInspectionId = lastSyncedId,
                    startDate = startDate.ToString("yyyy-MM-dd"),
                    filters = new
                    {
                        fieldNameId = 7,
                        displayName = "Transmission",
                        inspectionIdGreaterThan = lastSyncedId,
                        timeStampGreaterThanOrEqual = startDate
                    }
                };

                // Check total records with InspectionID > LastSyncedId
                var totalQuery = @"
                    SELECT COUNT(*) as TotalCount,
                           MAX(ic.InspectionID) as MaxInspectionID,
                           MIN(ic.InspectionID) as MinInspectionID
                    FROM InspectionCore ic
                    WHERE ic.InspectionID > @LastSyncedId
                      AND ic.TimeStamp >= @StartDate";

                int totalCount = 0;
                using (var command = new Microsoft.Data.SqlClient.SqlCommand(totalQuery, connection))
                {
                    command.Parameters.AddWithValue("@StartDate", startDate);
                    command.Parameters.AddWithValue("@LastSyncedId", lastSyncedId);
                    using var reader = await command.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        totalCount = reader.GetInt32(0);
                    }
                }

                // Check records matching FieldNameID=7
                var fieldNameQuery = @"
                    SELECT COUNT(*) as TotalCount
                    FROM InspectionCore ic
                    INNER JOIN InspectionCustomField icf ON ic.InspectionID = icf.InspectionID
                    WHERE icf.FieldNameID = 7
                      AND ic.InspectionID > @LastSyncedId
                      AND ic.TimeStamp >= @StartDate";

                int fieldNameCount = 0;
                using (var command = new Microsoft.Data.SqlClient.SqlCommand(fieldNameQuery, connection))
                {
                    command.Parameters.AddWithValue("@StartDate", startDate);
                    command.Parameters.AddWithValue("@LastSyncedId", lastSyncedId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        fieldNameCount = (int)result;
                    }
                }

                // Check records matching DisplayName='Transmission'
                var displayNameQuery = @"
                    SELECT COUNT(*) as TotalCount
                    FROM InspectionCore ic
                    INNER JOIN InspectionObject iobj ON ic.InspectionUuid = iobj.InspectionUuid
                    WHERE iobj.DisplayName = 'Transmission'
                      AND ic.InspectionID > @LastSyncedId
                      AND ic.TimeStamp >= @StartDate";

                int displayNameCount = 0;
                using (var command = new Microsoft.Data.SqlClient.SqlCommand(displayNameQuery, connection))
                {
                    command.Parameters.AddWithValue("@StartDate", startDate);
                    command.Parameters.AddWithValue("@LastSyncedId", lastSyncedId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        displayNameCount = (int)result;
                    }
                }

                // Check records matching ALL filters
                var allFiltersQuery = @"
                    SELECT COUNT(*) as TotalCount
                    FROM InspectionCore ic
                    INNER JOIN InspectionCustomField icf ON ic.InspectionID = icf.InspectionID
                    INNER JOIN InspectionObject iobj ON ic.InspectionUuid = iobj.InspectionUuid
                    WHERE icf.FieldNameID = 7
                      AND iobj.DisplayName = 'Transmission'
                      AND ic.InspectionID > @LastSyncedId
                      AND ic.TimeStamp >= @StartDate";

                int allFiltersCount = 0;
                using (var command = new Microsoft.Data.SqlClient.SqlCommand(allFiltersQuery, connection))
                {
                    command.Parameters.AddWithValue("@StartDate", startDate);
                    command.Parameters.AddWithValue("@LastSyncedId", lastSyncedId);
                    var result = await command.ExecuteScalarAsync();
                    if (result != null)
                    {
                        allFiltersCount = (int)result;
                    }
                }

                return Ok(new
                {
                    success = true,
                    diagnostics = new
                    {
                        lastSyncedInspectionId = lastSyncedId,
                        startDate = startDate.ToString("yyyy-MM-dd"),
                        filters = new
                        {
                            fieldNameId = 7,
                            displayName = "Transmission",
                            inspectionIdGreaterThan = lastSyncedId,
                            timeStampGreaterThanOrEqual = startDate
                        },
                        totalRecordsWithoutFilters = totalCount,
                        recordsWithFieldNameId7 = fieldNameCount,
                        recordsWithDisplayNameTransmission = displayNameCount,
                        recordsMatchingAllFilters = allFiltersCount,
                        conclusion = allFiltersCount > 0
                            ? $"Found {allFiltersCount} records matching all filters. Sync should proceed."
                            : totalCount > 0
                                ? $"Found {totalCount} records but none match all filters. Check if FieldNameID=7 and DisplayName='Transmission' are correct."
                                : "No new records found in ASE database since last sync."
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error diagnosing ASE sync");
                return StatusCode(500, new { error = "Error diagnosing sync", message = ex.Message });
            }
        }

        [HttpPost("test-connection")]
        public async Task<ActionResult<object>> TestConnection()
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var aseConfig = configuration.GetSection("ASE");
                var connectionString = aseConfig["ConnectionString"] ?? "";

                // Check if password placeholder needs to be replaced
                if (connectionString.Contains("***USE_ENV_VAR") || connectionString.Contains("***USE_ENV"))
                {
                    var asePassword = Environment.GetEnvironmentVariable("NICKSCAN_ASE_PASSWORD");
                    if (!string.IsNullOrEmpty(asePassword))
                    {
                        connectionString = connectionString
                            .Replace("***USE_ENV_VAR_NICKSCAN_ASE_PASSWORD***", asePassword)
                            .Replace("***USE_ENV_VAR***", asePassword)
                            .Replace("***USE_ENV***", asePassword);
                    }
                }

                if (string.IsNullOrEmpty(connectionString))
                {
                    return BadRequest(new { error = "Connection string is not configured" });
                }

                // Test connection
                using var connection = new Microsoft.Data.SqlClient.SqlConnection(connectionString);
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    await connection.OpenAsync();
                    stopwatch.Stop();

                    // Test query
                    var testQuery = "SELECT COUNT(*) FROM InspectionCore";
                    using var command = new Microsoft.Data.SqlClient.SqlCommand(testQuery, connection);
                    var recordCount = await command.ExecuteScalarAsync();

                    return Ok(new
                    {
                        success = true,
                        message = "Successfully connected to ASE database",
                        connectionTimeMs = stopwatch.ElapsedMilliseconds,
                        totalRecords = recordCount,
                        server = connection.DataSource,
                        database = connection.Database,
                        connectionStringConfigured = true
                    });
                }
                catch (Microsoft.Data.SqlClient.SqlException sqlEx)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        error = "SQL Connection Error",
                        message = sqlEx.Message,
                        number = sqlEx.Number,
                        state = sqlEx.State,
                        server = sqlEx.Server,
                        connectionTimeMs = stopwatch.ElapsedMilliseconds
                    });
                }
                catch (Exception ex)
                {
                    return StatusCode(500, new
                    {
                        success = false,
                        error = "Connection Error",
                        message = ex.Message,
                        connectionTimeMs = stopwatch.ElapsedMilliseconds
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing ASE database connection");
                return StatusCode(500, new { error = "Error testing connection", message = ex.Message });
            }
        }

        [HttpPost("trigger-sync")]
        public async Task<ActionResult<object>> TriggerSync()
        {
            try
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IAseDatabaseSyncService>();

                _logger.LogInformation("Manual ASE sync triggered by {User}", User.Identity?.Name ?? "System");

                await syncService.SyncDataAsync();

                // Get updated statistics
                var lastSync = await _context.AseSyncLogs
                    .OrderByDescending(x => x.LastSyncTime)
                    .FirstOrDefaultAsync();

                return Ok(new
                {
                    message = "ASE sync completed",
                    lastSyncTime = lastSync?.LastSyncTime,
                    lastSyncedInspectionId = lastSync?.LastSyncedInspectionId,
                    syncStatus = lastSync?.SyncStatus ?? "Completed"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering manual ASE sync");
                return StatusCode(500, new { error = "Error triggering sync", message = ex.Message });
            }
        }

        // 2026-04-19: removed [AllowAnonymous] + fake-empty fallback.
        [HttpGet("stats")]
        public async Task<ActionResult<object>> GetStats()
        {
            try
            {
                // Only query last 30 days for performance (instead of all-time grouping)
                var thirtyDaysAgo = DateTime.SpecifyKind(DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);

                var stats = await _context.AseScans
                    .Where(s => s.ScanTime >= thirtyDaysAgo) // Filter first for performance
                    .GroupBy(s => s.ScanTime.Date)
                    .Select(g => new
                    {
                        Date = g.Key,
                        Count = g.Count(),
                        ContainerCount = g.Count(s => !string.IsNullOrEmpty(s.ContainerNumber)),
                        ImageCount = g.Count(s => s.ScanImage != null)
                    })
                    .OrderByDescending(s => s.Date)
                    .Take(30)
                    .ToListAsync();

                _logger.LogInformation("Retrieved ASE stats for last 30 days: {DayCount} days", stats.Count);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ASE stats");
                return StatusCode(500, new { error = "Failed to load ASE stats" });
            }
        }
    }
}

