using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.Monitoring;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Comprehensive monitoring controller for system health and performance
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class MonitoringController : ControllerBase
    {
        private readonly ILogger<MonitoringController> _logger;
        private readonly IComprehensiveHealthCheckService _healthCheckService;
        private readonly ApplicationDbContext _dbContext;
        private readonly IEndpointUsageService? _endpointUsageService;

        public MonitoringController(
            ILogger<MonitoringController> logger,
            IComprehensiveHealthCheckService healthCheckService,
            ApplicationDbContext dbContext,
            IEndpointUsageService? endpointUsageService = null)
        {
            _logger = logger;
            _healthCheckService = healthCheckService;
            _dbContext = dbContext;
            _endpointUsageService = endpointUsageService;
        }

        /// <summary>
        /// Get comprehensive system health overview
        /// </summary>
        [HttpGet("health/overview")]
        public async Task<ActionResult<object>> GetSystemHealthOverview()
        {
            try
            {
                var healthSummary = _healthCheckService.GetSystemHealthSummary();
                var systemInfo = await GetSystemInformation();

                return Ok(new
                {
                    SystemHealth = healthSummary,
                    SystemInformation = systemInfo,
                    Timestamp = DateTime.UtcNow,
                    Uptime = GetSystemUptime()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system health overview");
                return StatusCode(500, new { Error = "Failed to get system health overview", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get detailed health status for all services
        /// </summary>
        [HttpGet("health/services")]
        public ActionResult<Dictionary<string, ServiceHealthStatus>> GetServicesHealth()
        {
            try
            {
                var serviceStatuses = _healthCheckService.GetServiceStatuses();
                return Ok(serviceStatuses);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting services health");
                return StatusCode(500, new { Error = "Failed to get services health", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get health status for a specific service
        /// </summary>
        [HttpGet("health/services/{serviceName}")]
        public ActionResult<ServiceHealthStatus> GetServiceHealth(string serviceName)
        {
            try
            {
                var serviceStatus = _healthCheckService.GetServiceStatus(serviceName);
                if (serviceStatus.Status == NickScanCentralImagingPortal.Core.Interfaces.HealthStatus.Unknown)
                {
                    return NotFound($"Service '{serviceName}' not found");
                }

                return Ok(serviceStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting service health for {ServiceName}", serviceName);
                return StatusCode(500, new { Error = "Failed to get service health", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get database statistics and health
        /// </summary>
        [HttpGet("database/statistics")]
        public async Task<ActionResult<object>> GetDatabaseStatistics()
        {
            try
            {
                _logger.LogInformation("📊 Getting database statistics...");

                // Use default values if queries fail (graceful degradation)
                int containerCount = 0, containerImageCount = 0, fs6000ScanCount = 0;
                int fs6000ImageCount = 0, aseScanCount = 0;
                int todayContainers = 0, todayFS6000Scans = 0, todayAseScans = 0;

                // 2026-04-19: was Containers.CountAsync() which queries an empty
                // legacy table. The operational "total containers" number users
                // expect on the dashboard is the count of tracked completeness
                // statuses (3.7k+ here, one per physical container under NSCIM).
                try { containerCount = await _dbContext.ContainerCompletenessStatuses.CountAsync(); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to get container count for monitoring"); }
                try
                {
                    // Check if ContainerImages table exists before querying
                    if (await TableExistsAsync("ContainerImages"))
                    {
                        containerImageCount = await _dbContext.ContainerImages.CountAsync();
                    }
                    else
                    {
                        _logger.LogDebug("ContainerImages table does not exist, skipping count query");
                    }
                }
                catch (SqlException sqlEx) when (sqlEx.Number == 208) // Invalid object name
                {
                    _logger.LogDebug("ContainerImages table does not exist in database: {Message}", sqlEx.Message);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get container image count for monitoring");
                }
                try { fs6000ScanCount = await _dbContext.FS6000Scans.CountAsync(); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to get FS6000 scan count for monitoring"); }
                try { fs6000ImageCount = await _dbContext.FS6000Images.CountAsync(); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to get FS6000 image count for monitoring"); }
                try { aseScanCount = await _dbContext.AseScans.CountAsync(); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to get ASE scan count for monitoring"); }

                // Get scans for TODAY (based on actual scan time, not database insert time)
                var todayStart = DateTime.UtcNow.Date;
                var todayEnd = todayStart.AddDays(1);

                try
                {
                    // 2026-04-19: same table switch as the total — count completeness
                    // statuses created today, which corresponds to "new containers
                    // picked up today" operationally.
                    todayContainers = await _dbContext.ContainerCompletenessStatuses
                        .Where(c => c.CreatedAt >= todayStart && c.CreatedAt < todayEnd)
                        .CountAsync();
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to get today's container count for monitoring"); }

                try
                {
                    todayFS6000Scans = await _dbContext.FS6000Scans
                        .Where(s => s.ScanTime >= todayStart && s.ScanTime < todayEnd)
                        .CountAsync();
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to get today's FS6000 scan count for monitoring"); }

                try
                {
                    todayAseScans = await _dbContext.AseScans
                        .Where(s => s.ScanTime >= todayStart && s.ScanTime < todayEnd)
                        .CountAsync();
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to get today's ASE scan count for monitoring"); }

                var response = new
                {
                    TotalContainers = containerCount,
                    TotalContainerImages = containerImageCount,
                    TotalFS6000Scans = fs6000ScanCount,
                    TotalFS6000Images = fs6000ImageCount,
                    TotalAseScans = aseScanCount,
                    TodayActivity = new
                    {
                        Containers = todayContainers,
                        FS6000Scans = todayFS6000Scans,
                        AseScans = todayAseScans,
                        TotalScans = todayFS6000Scans + todayAseScans
                    },
                    Timestamp = DateTime.UtcNow
                };

                _logger.LogInformation("✅ Database statistics retrieved successfully");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting database statistics: {Message}", ex.Message);
                return StatusCode(500, new { Error = "Failed to get database statistics", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get file system monitoring information
        /// </summary>
        [HttpGet("filesystem/status")]
        public ActionResult<object> GetFileSystemStatus()
        {
            try
            {
                var stagingPath = @"C:\Shared\NSCIM_PRODUCTION\Data\FS6000\Staging";
                var networkPath = @"Z:\23301FS01";

                var stagingExists = Directory.Exists(stagingPath);
                var networkExists = Directory.Exists(networkPath);

                var stagingInfo = stagingExists ? GetDirectoryInfo(stagingPath) : null;
                var networkInfo = networkExists ? GetDirectoryInfo(networkPath) : null;

                // Check disk space
                var drives = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => new
                    {
                        Name = d.Name,
                        TotalSizeGB = d.TotalSize / 1024 / 1024 / 1024,
                        AvailableFreeSpaceGB = d.AvailableFreeSpace / 1024 / 1024 / 1024,
                        UsedSpaceGB = (d.TotalSize - d.AvailableFreeSpace) / 1024 / 1024 / 1024,
                        UsagePercentage = (double)(d.TotalSize - d.AvailableFreeSpace) / d.TotalSize * 100
                    })
                    .ToList();

                return Ok(new
                {
                    Paths = new
                    {
                        StagingPath = new
                        {
                            Path = stagingPath,
                            Exists = stagingExists,
                            Info = stagingInfo
                        },
                        NetworkPath = new
                        {
                            Path = networkPath,
                            Exists = networkExists,
                            Info = networkInfo
                        }
                    },
                    DiskSpace = drives,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file system status");
                return StatusCode(500, new { Error = "Failed to get file system status", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get system performance metrics
        /// </summary>
        [HttpGet("performance/metrics")]
        public ActionResult<object> GetPerformanceMetrics()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var memoryUsageMB = process.WorkingSet64 / 1024 / 1024;
                var cpuUsageMs = process.TotalProcessorTime.TotalMilliseconds;

                // Get system-wide metrics
                var totalMemory = GC.GetTotalMemory(false) / 1024 / 1024; // MB
                var gcCollections = new
                {
                    Gen0 = GC.CollectionCount(0),
                    Gen1 = GC.CollectionCount(1),
                    Gen2 = GC.CollectionCount(2)
                };

                ThreadPool.GetAvailableThreads(out var workerThreads, out var completionPortThreads);

                return Ok(new
                {
                    ProcessMetrics = new
                    {
                        ProcessId = process.Id,
                        ProcessName = process.ProcessName,
                        MemoryUsageMB = memoryUsageMB,
                        CpuUsageMs = cpuUsageMs,
                        StartTime = process.StartTime,
                        ThreadCount = process.Threads.Count,
                        HandleCount = process.HandleCount
                    },
                    SystemMetrics = new
                    {
                        TotalMemoryMB = totalMemory,
                        GarbageCollections = gcCollections,
                        ThreadPoolThreads = ThreadPool.ThreadCount,
                        AvailableWorkerThreads = workerThreads,
                        AvailableCompletionPortThreads = completionPortThreads
                    },
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting performance metrics");
                return StatusCode(500, new { Error = "Failed to get performance metrics", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get recent system events and logs summary
        /// </summary>
        [HttpGet("events/recent")]
        public ActionResult<object> GetRecentEvents()
        {
            try
            {
                // This would typically read from a log aggregation system
                // For now, we'll return a summary based on what we can determine
                var healthSummary = _healthCheckService.GetSystemHealthSummary();

                var events = new List<object>();

                // Generate events based on service health
                foreach (var service in healthSummary.ServiceStatuses)
                {
                    var status = service.Value.Status;
                    var lastChecked = service.Value.LastChecked;

                    if (status == NickScanCentralImagingPortal.Core.Interfaces.HealthStatus.Unhealthy)
                    {
                        events.Add(new
                        {
                            Type = "Error",
                            Service = service.Key,
                            Message = service.Value.ErrorMessage ?? "Service is unhealthy",
                            Timestamp = lastChecked,
                            Severity = "High"
                        });
                    }
                    else if (status == NickScanCentralImagingPortal.Core.Interfaces.HealthStatus.Degraded)
                    {
                        events.Add(new
                        {
                            Type = "Warning",
                            Service = service.Key,
                            Message = "Service is degraded",
                            Timestamp = lastChecked,
                            Severity = "Medium"
                        });
                    }
                }

                return Ok(new
                {
                    Events = events.OrderByDescending(e => ((dynamic)e).Timestamp).Take(50),
                    TotalEvents = events.Count,
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent events");
                return StatusCode(500, new { Error = "Failed to get recent events", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get API performance metrics for the last 24 hours
        /// </summary>
        [HttpGet("api-metrics")]
        public async Task<ActionResult<object>> GetApiMetrics()
        {
            try
            {
                var since = DateTime.UtcNow.Date;
                int totalRequests = 0;
                double avgResponseMs = 0;
                int errorCount = 0;

                try
                {
                    var usageLogs = _dbContext.EndpointUsageLogs
                        .Where(u => u.Timestamp >= since);

                    totalRequests = await usageLogs.CountAsync();

                    if (totalRequests > 0)
                    {
                        avgResponseMs = await usageLogs.AverageAsync(u => u.ResponseTimeMs);
                        errorCount = await usageLogs.CountAsync(u => u.StatusCode >= 400);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to query endpoint usage logs for api-metrics");
                }

                return Ok(new
                {
                    TotalRequests = totalRequests,
                    AverageResponseTimeMs = Math.Round(avgResponseMs, 1),
                    ErrorCount = errorCount,
                    ErrorRate = totalRequests > 0 ? Math.Round(errorCount * 100.0 / totalRequests, 2) : 0,
                    ActiveHubConnections = 0,
                    Period = "today",
                    Timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting API metrics");
                return StatusCode(500, new { Error = "Failed to get API metrics", Message = ex.Message });
            }
        }

        /// <summary>
        /// Force a health check for all services
        /// </summary>
        [HttpPost("health/check-all")]
        public Task<ActionResult<object>> ForceHealthCheckAll()
        {
            try
            {
                _logger.LogInformation("Forcing comprehensive health check for all services");

                // Trigger health checks (this would typically be done by the background service)
                // For now, we'll return the current status
                var healthSummary = _healthCheckService.GetSystemHealthSummary();

                return Task.FromResult<ActionResult<object>>(Ok(new
                {
                    Message = "Health check initiated",
                    CurrentStatus = healthSummary,
                    Timestamp = DateTime.UtcNow
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error forcing health check");
                return Task.FromResult<ActionResult<object>>(StatusCode(500, new { Error = "Failed to force health check", Message = ex.Message }));
            }
        }

        #region Helper Methods

        private Task<object> GetSystemInformation()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var assembly = System.Reflection.Assembly.GetExecutingAssembly();
                var version = assembly.GetName().Version;

                return Task.FromResult<object>(new
                {
                    ApplicationName = "NickScan Central Imaging Portal",
                    Version = version?.ToString(),
                    Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
                    MachineName = Environment.MachineName,
                    OperatingSystem = Environment.OSVersion.ToString(),
                    ProcessorCount = Environment.ProcessorCount,
                    WorkingSetMB = process.WorkingSet64 / 1024 / 1024,
                    StartTime = process.StartTime,
                    ProcessId = process.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting system information");
                return Task.FromResult<object>(new { Error = "Failed to get system information" });
            }
        }

        private string GetSystemUptime()
        {
            try
            {
                var process = Process.GetCurrentProcess();
                var uptime = DateTime.UtcNow - process.StartTime;
                return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m {uptime.Seconds}s";
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get application uptime");
                return "Unknown";
            }
        }

        private object? GetDirectoryInfo(string path)
        {
            try
            {
                if (!Directory.Exists(path))
                    return null;

                var directory = new DirectoryInfo(path);

                // Only enumerate top-level files/dirs for performance (don't recurse)
                var files = directory.GetFiles("*", SearchOption.TopDirectoryOnly);
                var directories = directory.GetDirectories("*", SearchOption.TopDirectoryOnly);

                return new
                {
                    FileCount = files.Length,
                    DirectoryCount = directories.Length,
                    TotalSizeBytes = files.Sum(f => f.Length),
                    LastModified = directory.LastWriteTime,
                    Exists = true
                };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get directory info for path: {Path}", path);
                return new { Exists = false };
            }
        }

        /// <summary>
        /// Check if a table exists in the database
        /// </summary>
        private async Task<bool> TableExistsAsync(string tableName)
        {
            try
            {
                var connection = _dbContext.Database.GetDbConnection();
                var wasOpen = connection.State == System.Data.ConnectionState.Open;

                if (!wasOpen)
                {
                    await connection.OpenAsync();
                }

                try
                {
                    using var command = connection.CreateCommand();
                    command.CommandText = @"
                        SELECT COUNT(*) 
                        FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = @TableName";

                    var parameter = command.CreateParameter();
                    parameter.ParameterName = "@TableName";
                    parameter.Value = tableName;
                    command.Parameters.Add(parameter);

                    var result = await command.ExecuteScalarAsync();
                    return Convert.ToInt32(result) > 0;
                }
                finally
                {
                    // Only close if we opened it
                    if (!wasOpen && connection.State == System.Data.ConnectionState.Open)
                    {
                        await connection.CloseAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Failed to check if table {TableName} exists", tableName);
                return false;
            }
        }

        #endregion

        #region Endpoint Usage Monitoring

        /// <summary>
        /// Get usage statistics for a specific endpoint
        /// </summary>
        [HttpGet("endpoint-usage/{endpoint}")]
        public async Task<ActionResult<Core.Models.EndpointUsageStats>> GetEndpointUsageStats(
            string endpoint,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            if (_endpointUsageService == null)
            {
                return StatusCode(503, new { Error = "Endpoint usage service not available" });
            }

            try
            {
                var stats = await _endpointUsageService.GetUsageStatsAsync(endpoint, from, to);
                return Ok(stats);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting endpoint usage stats for {Endpoint}", endpoint);
                return StatusCode(500, new { Error = "Failed to get endpoint usage stats", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get list of callers for a specific endpoint
        /// </summary>
        [HttpGet("endpoint-usage/{endpoint}/callers")]
        [HttpGet("endpoint-callers")]
        public async Task<ActionResult<List<Core.Models.EndpointCaller>>> GetEndpointCallers(
            [FromRoute] string? endpoint = null,
            [FromQuery(Name = "ep")] string? ep = null,
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            if (_endpointUsageService == null)
            {
                return StatusCode(503, new { Error = "Endpoint usage service not available" });
            }

            try
            {
                var resolvedEndpoint = !string.IsNullOrWhiteSpace(ep) ? ep : endpoint;
                if (string.IsNullOrWhiteSpace(resolvedEndpoint))
                    return BadRequest(new { Error = "Endpoint parameter required (use ?ep= query param or route)" });

                var callers = await _endpointUsageService.GetCallersAsync(resolvedEndpoint, from, to);
                return Ok(callers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting endpoint callers for {Endpoint}", ep ?? endpoint);
                return StatusCode(500, new { Error = "Failed to get endpoint callers", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get usage statistics for all deprecated endpoints
        /// </summary>
        [HttpGet("deprecated-endpoints")]
        public async Task<ActionResult<Dictionary<string, int>>> GetDeprecatedEndpointUsage(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            if (_endpointUsageService == null)
            {
                return StatusCode(503, new { Error = "Endpoint usage service not available" });
            }

            try
            {
                var usage = await _endpointUsageService.GetDeprecatedEndpointUsageAsync(from, to);
                return Ok(usage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting deprecated endpoint usage");
                return StatusCode(500, new { Error = "Failed to get deprecated endpoint usage", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get detailed summary of deprecated endpoints
        /// </summary>
        [HttpGet("deprecated-endpoints/summary")]
        public async Task<ActionResult<List<Core.Models.DeprecatedEndpointSummary>>> GetDeprecatedEndpointSummary(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            if (_endpointUsageService == null)
            {
                return StatusCode(503, new { Error = "Endpoint usage service not available" });
            }

            try
            {
                var summary = await _endpointUsageService.GetDeprecatedEndpointSummaryAsync(from, to);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting deprecated endpoint summary");
                return StatusCode(500, new { Error = "Failed to get deprecated endpoint summary", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get usage statistics for all Phase 3 routes
        /// </summary>
        [HttpGet("phase3-routes")]
        public async Task<ActionResult<Dictionary<string, int>>> GetPhase3RouteUsage(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            if (_endpointUsageService == null)
            {
                return StatusCode(503, new { Error = "Endpoint usage service not available" });
            }

            try
            {
                var usage = await _endpointUsageService.GetPhase3RouteUsageAsync(from, to);
                return Ok(usage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Phase 3 route usage");
                return StatusCode(500, new { Error = "Failed to get Phase 3 route usage", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get detailed summary of Phase 3 routes
        /// </summary>
        [HttpGet("phase3-routes/summary")]
        public async Task<ActionResult<List<Core.Models.Phase3RouteSummary>>> GetPhase3RouteSummary(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            if (_endpointUsageService == null)
            {
                return StatusCode(503, new { Error = "Endpoint usage service not available" });
            }

            try
            {
                var summary = await _endpointUsageService.GetPhase3RouteSummaryAsync(from, to);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting Phase 3 route summary");
                return StatusCode(500, new { Error = "Failed to get Phase 3 route summary", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get usage trends for an endpoint over time
        /// </summary>
        [HttpGet("endpoint-usage/{endpoint}/trends")]
        public async Task<ActionResult<List<Core.Models.EndpointUsageTrend>>> GetEndpointUsageTrends(
            string endpoint,
            [FromQuery] int days = 30)
        {
            if (_endpointUsageService == null)
            {
                return StatusCode(503, new { Error = "Endpoint usage service not available" });
            }

            try
            {
                var trends = await _endpointUsageService.GetUsageTrendsAsync(endpoint, days);
                return Ok(trends);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting endpoint usage trends for {Endpoint}", endpoint);
                return StatusCode(500, new { Error = "Failed to get endpoint usage trends", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get list of endpoints that are safe to remove (zero usage for specified days)
        /// </summary>
        [HttpGet("safe-to-remove")]
        public async Task<ActionResult<List<string>>> GetSafeToRemoveEndpoints(
            [FromQuery] int daysWithZeroUsage = 30)
        {
            if (_endpointUsageService == null)
            {
                return StatusCode(503, new { Error = "Endpoint usage service not available" });
            }

            try
            {
                var safeToRemove = await _endpointUsageService.GetSafeToRemoveEndpointsAsync(daysWithZeroUsage);
                return Ok(safeToRemove);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting safe to remove endpoints");
                return StatusCode(500, new { Error = "Failed to get safe to remove endpoints", Message = ex.Message });
            }
        }

        /// <summary>
        /// Get summary of all endpoints with usage statistics
        /// </summary>
        [HttpGet("all-endpoints/summary")]
        public async Task<ActionResult<List<Core.Models.AllEndpointsSummary>>> GetAllEndpointsSummary(
            [FromQuery] DateTime? from = null,
            [FromQuery] DateTime? to = null)
        {
            if (_endpointUsageService == null)
            {
                return StatusCode(503, new { Error = "Endpoint usage service not available" });
            }

            try
            {
                var summary = await _endpointUsageService.GetAllEndpointsSummaryAsync(from, to);
                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all endpoints summary");
                return StatusCode(500, new { Error = "Failed to get all endpoints summary", Message = ex.Message });
            }
        }

        #endregion
    }
}
