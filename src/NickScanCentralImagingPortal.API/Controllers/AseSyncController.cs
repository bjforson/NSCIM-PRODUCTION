using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ASE;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// ASE (Automated Scanning Equipment) sync monitoring and management
    /// </summary>
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/[controller]")]
    public class AseSyncController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly AseConfiguration _aseConfig;
        private readonly ILogger<AseSyncController> _logger;

        public AseSyncController(
            ApplicationDbContext context,
            IOptions<AseConfiguration> aseConfig,
            ILogger<AseSyncController> logger)
        {
            _context = context;
            _aseConfig = aseConfig.Value;
            _logger = logger;
        }

        /// <summary>
        /// Get comprehensive ASE sync statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<ActionResult<AseSyncStatistics>> GetSyncStatistics()
        {
            try
            {
                // Local database statistics
                var localStats = await _context.AseScans
                    .GroupBy(x => 1)
                    .Select(g => new
                    {
                        TotalRecords = g.Count(),
                        EarliestScan = g.Min(s => s.ScanTime),
                        LatestScan = g.Max(s => s.ScanTime),
                        MinInspectionId = g.Min(s => s.InspectionId),
                        MaxInspectionId = g.Max(s => s.InspectionId)
                    })
                    .FirstOrDefaultAsync();

                // Latest sync log
                var latestSyncLog = await _context.AseSyncLogs
                    .OrderByDescending(l => l.LastSyncTime)
                    .FirstOrDefaultAsync();

                // Records synced today
                var today = DateTime.Today;
                var syncedToday = await _context.AseScans
                    .CountAsync(s => s.CreatedAt >= today);

                // External database statistics (if accessible)
                int? externalTotalRecords = null;
                int? externalTransmissionRecords = null;
                int? pendingSyncRecords = null;

                try
                {
                    using var connection = new SqlConnection(_aseConfig.ConnectionString);
                    await connection.OpenAsync();

                    // Total records in external DB
                    var totalCmd = new SqlCommand("SELECT COUNT(*) FROM InspectionCore WHERE TimeStamp >= @StartDate", connection);
                    totalCmd.Parameters.AddWithValue("@StartDate", _aseConfig.StartDate);
                    externalTotalRecords = (int?)await totalCmd.ExecuteScalarAsync();

                    // Records with Transmission images (what we sync)
                    var transmissionQuery = @"
                        SELECT COUNT(DISTINCT ic.InspectionID) 
                        FROM InspectionCore ic
                        INNER JOIN InspectionCustomField icf ON ic.InspectionID = icf.InspectionID
                        INNER JOIN InspectionObject iobj ON ic.InspectionUuid = iobj.InspectionUuid
                        WHERE icf.FieldNameID = 7
                            AND iobj.DisplayName = 'Transmission'
                            AND ic.TimeStamp >= @StartDate";

                    var transmissionCmd = new SqlCommand(transmissionQuery, connection);
                    transmissionCmd.Parameters.AddWithValue("@StartDate", _aseConfig.StartDate);
                    externalTransmissionRecords = (int?)await transmissionCmd.ExecuteScalarAsync();

                    // Pending sync records (not yet synced)
                    if (localStats != null)
                    {
                        var pendingQuery = @"
                            SELECT COUNT(DISTINCT ic.InspectionID) 
                            FROM InspectionCore ic
                            INNER JOIN InspectionCustomField icf ON ic.InspectionID = icf.InspectionID
                            INNER JOIN InspectionCargo icargo ON ic.InspectionID = icargo.InspectionID
                            INNER JOIN InspectionObject iobj ON ic.InspectionUuid = iobj.InspectionUuid
                            WHERE icf.FieldNameID = 7
                                AND iobj.DisplayName = 'Transmission'
                                AND ic.TimeStamp >= @StartDate
                                AND ic.InspectionID > @LastSyncedId";

                        var pendingCmd = new SqlCommand(pendingQuery, connection);
                        pendingCmd.Parameters.AddWithValue("@StartDate", _aseConfig.StartDate);
                        pendingCmd.Parameters.AddWithValue("@LastSyncedId", localStats.MaxInspectionId);
                        pendingSyncRecords = (int?)await pendingCmd.ExecuteScalarAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not connect to external ASE database for statistics");
                }

                var statistics = new AseSyncStatistics
                {
                    // Local statistics
                    LocalTotalRecords = localStats?.TotalRecords ?? 0,
                    LocalEarliestScan = localStats?.EarliestScan,
                    LocalLatestScan = localStats?.LatestScan,
                    LocalMinInspectionId = localStats?.MinInspectionId ?? 0,
                    LocalMaxInspectionId = localStats?.MaxInspectionId ?? 0,
                    SyncedToday = syncedToday,

                    // External statistics
                    ExternalTotalRecords = externalTotalRecords,
                    ExternalTransmissionRecords = externalTransmissionRecords,
                    PendingSyncRecords = pendingSyncRecords ?? 0,

                    // Sync status
                    LastSyncTime = latestSyncLog?.LastSyncTime,
                    LastSyncedInspectionId = latestSyncLog?.LastSyncedInspectionId ?? 0,
                    LastSyncStatus = latestSyncLog?.SyncStatus ?? "Never synced",
                    LastSyncRecordsProcessed = latestSyncLog?.RecordsProcessed ?? 0,

                    // Configuration
                    SyncEnabled = _aseConfig.EnableRealTimeSync,
                    SyncIntervalMinutes = _aseConfig.SyncInterval.TotalMinutes,
                    BatchSize = _aseConfig.BatchSize,
                    StartDate = _aseConfig.StartDate,

                    // Health status
                    IsHealthy = pendingSyncRecords.HasValue && pendingSyncRecords.Value == 0 && latestSyncLog != null,
                    SyncPercentage = externalTransmissionRecords.HasValue && externalTransmissionRecords.Value > 0
                        ? (double)(localStats?.TotalRecords ?? 0) / externalTransmissionRecords.Value * 100
                        : null
                };

                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ASE sync statistics");
                return StatusCode(500, new { error = "Error retrieving ASE sync statistics", message = ex.Message });
            }
        }

        /// <summary>
        /// Get recent sync history
        /// </summary>
        [HttpGet("history")]
        public async Task<ActionResult<List<AseSyncLogDto>>> GetSyncHistory([FromQuery] int count = 20)
        {
            try
            {
                var logs = await _context.AseSyncLogs
                    .OrderByDescending(l => l.LastSyncTime)
                    .Take(count)
                    .Select(l => new AseSyncLogDto
                    {
                        LastSyncTime = l.LastSyncTime,
                        LastSyncedInspectionId = l.LastSyncedInspectionId,
                        RecordsProcessed = l.RecordsProcessed,
                        SyncStatus = l.SyncStatus
                    })
                    .ToListAsync();

                return Ok(logs);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ASE sync history");
                return StatusCode(500, new { error = "Error retrieving sync history", message = ex.Message });
            }
        }

        /// <summary>
        /// Manually trigger ASE sync (for testing/debugging)
        /// </summary>
        [HttpPost("trigger")]
        public async Task<ActionResult<object>> TriggerSync()
        {
            try
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var syncService = scope.ServiceProvider.GetRequiredService<IAseDatabaseSyncService>();

                _logger.LogInformation("Manual ASE sync triggered by {User}", User.Identity?.Name ?? "Unknown");

                await syncService.SyncDataAsync();

                // Get updated statistics
                var latestSyncLog = await _context.AseSyncLogs
                    .OrderByDescending(l => l.LastSyncTime)
                    .FirstOrDefaultAsync();

                return Ok(new
                {
                    message = "ASE sync completed successfully",
                    lastSyncTime = latestSyncLog?.LastSyncTime,
                    lastSyncedInspectionId = latestSyncLog?.LastSyncedInspectionId,
                    syncStatus = latestSyncLog?.SyncStatus ?? "Completed"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering manual ASE sync");
                return StatusCode(500, new { error = "Error triggering sync", message = ex.Message });
            }
        }
    }

    public class AseSyncStatistics
    {
        // Local database statistics
        public int LocalTotalRecords { get; set; }
        public DateTime? LocalEarliestScan { get; set; }
        public DateTime? LocalLatestScan { get; set; }
        public int LocalMinInspectionId { get; set; }
        public int LocalMaxInspectionId { get; set; }
        public int SyncedToday { get; set; }

        // External database statistics
        public int? ExternalTotalRecords { get; set; }
        public int? ExternalTransmissionRecords { get; set; }
        public int PendingSyncRecords { get; set; }

        // Sync status
        public DateTime? LastSyncTime { get; set; }
        public int LastSyncedInspectionId { get; set; }
        public string LastSyncStatus { get; set; } = "";
        public int LastSyncRecordsProcessed { get; set; }

        // Configuration
        public bool SyncEnabled { get; set; }
        public double SyncIntervalMinutes { get; set; }
        public int BatchSize { get; set; }
        public DateTime StartDate { get; set; }

        // Health
        public bool IsHealthy { get; set; }
        public double? SyncPercentage { get; set; }
    }

    public class AseSyncLogDto
    {
        public DateTime LastSyncTime { get; set; }
        public int LastSyncedInspectionId { get; set; }
        public int RecordsProcessed { get; set; }
        public string SyncStatus { get; set; } = "";
    }
}

