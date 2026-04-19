using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ContainerCompleteness;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Health check endpoint for Container Scan Queue
    /// Provides queue statistics, health status, and monitoring data
    /// </summary>
    [AllowAnonymous] // Health check should be accessible without auth for monitoring
    [ApiController]
    [Route("api/[controller]")]
    public class QueueHealthController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IConfiguration _configuration;
        private readonly ILogger<QueueHealthController> _logger;
        private readonly QueuePublishingMetricsService? _metricsService;
        private readonly QueuePublishingAlertService? _alertService;

        public QueueHealthController(
            ApplicationDbContext context,
            IConfiguration configuration,
            ILogger<QueueHealthController> logger,
            QueuePublishingMetricsService? metricsService = null,
            QueuePublishingAlertService? alertService = null)
        {
            _context = context;
            _configuration = configuration;
            _logger = logger;
            _metricsService = metricsService;
            _alertService = alertService;
        }

        /// <summary>
        /// Get overall queue health status
        /// </summary>
        [HttpGet]
        [ResponseCache(Duration = 15)]
        public async Task<ActionResult<QueueHealthResponse>> GetQueueHealth()
        {
            try
            {
                var stats = await GetQueueStatisticsAsync();
                var healthStatus = DetermineHealthStatus(stats);

                return Ok(new QueueHealthResponse
                {
                    Status = healthStatus,
                    Timestamp = DateTime.UtcNow,
                    Statistics = stats,
                    Alerts = GetAlerts(stats)
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get queue health status");
                return StatusCode(500, new QueueHealthResponse
                {
                    Status = "Error",
                    Timestamp = DateTime.UtcNow,
                    Error = ex.Message
                });
            }
        }

        /// <summary>
        /// Get queue statistics
        /// </summary>
        [HttpGet("statistics")]
        [ResponseCache(Duration = 15)]
        public async Task<ActionResult<QueueStatisticsResponse>> GetStatistics()
        {
            try
            {
                var stats = await GetQueueStatisticsAsync();
                return Ok(new QueueStatisticsResponse
                {
                    Timestamp = DateTime.UtcNow,
                    Statistics = stats
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get queue statistics");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get stuck items (processing >30 minutes)
        /// </summary>
        [HttpGet("stuck")]
        public async Task<ActionResult<List<QueueItemResponse>>> GetStuckItems()
        {
            try
            {
                var timeoutMinutes = 30;
                var cutoffTime = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

                var stuckItems = await _context.ContainerScanQueues
                    .Where(q => q.Status == "Processing" && q.ProcessedAt.HasValue && q.ProcessedAt < cutoffTime)
                    .OrderBy(q => q.ProcessedAt)
                    .Select(q => new QueueItemResponse
                    {
                        Id = q.Id,
                        ContainerNumber = q.ContainerNumber,
                        ScannerType = q.ScannerType,
                        InspectionId = q.InspectionId,
                        Status = q.Status,
                        ProcessedAt = q.ProcessedAt,
                        StuckMinutes = (int)(DateTime.UtcNow - q.ProcessedAt!.Value).TotalMinutes
                    })
                    .ToListAsync();

                return Ok(stuckItems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get stuck items");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get failed items
        /// </summary>
        [HttpGet("failed")]
        public async Task<ActionResult<List<QueueItemResponse>>> GetFailedItems([FromQuery] int limit = 20)
        {
            try
            {
                var failedItems = await _context.ContainerScanQueues
                    .Where(q => q.Status == "Failed")
                    .OrderByDescending(q => q.CreatedAt)
                    .Take(limit)
                    .Select(q => new QueueItemResponse
                    {
                        Id = q.Id,
                        ContainerNumber = q.ContainerNumber,
                        ScannerType = q.ScannerType,
                        InspectionId = q.InspectionId,
                        Status = q.Status,
                        RetryCount = q.RetryCount,
                        MaxRetries = q.MaxRetries,
                        ErrorMessage = q.ErrorMessage,
                        CreatedAt = q.CreatedAt
                    })
                    .ToListAsync();

                return Ok(failedItems);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get failed items");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get queue publishing health status (Phase 3: Monitoring &amp; Alerting)
        /// </summary>
        [HttpGet("publishing")]
        public Task<ActionResult<QueuePublishingHealthResponse>> GetPublishingHealth()
        {
            try
            {
                if (_metricsService == null)
                {
                    return Task.FromResult<ActionResult<QueuePublishingHealthResponse>>(StatusCode(503, new QueuePublishingHealthResponse
                    {
                        Status = "ServiceUnavailable",
                        Timestamp = DateTime.UtcNow,
                        Error = "Queue publishing metrics service is not available"
                    }));
                }

                var metrics = _metricsService.GetMetrics();
                var alerts = _alertService?.GetCurrentAlerts() ?? new List<string>();
                var healthStatus = DeterminePublishingHealthStatus(metrics, alerts);

                var response = new QueuePublishingHealthResponse
                {
                    Status = healthStatus,
                    Timestamp = DateTime.UtcNow,
                    Metrics = new QueuePublishingMetricsDto
                    {
                        TimeWindow = metrics.TimeWindow,
                        StartTime = metrics.StartTime,
                        EndTime = metrics.EndTime,
                        TotalAttempts = metrics.TotalAttempts,
                        SuccessfulPublishes = metrics.SuccessfulPublishes,
                        FailedPublishes = metrics.FailedPublishes,
                        SkippedPublishes = metrics.SkippedPublishes,
                        TotalRetries = metrics.TotalRetries,
                        AverageRetryCount = metrics.AverageRetryCount,
                        SuccessRate = metrics.SuccessRate,
                        AveragePublishDuration = metrics.AveragePublishDuration,
                        ByScannerType = metrics.ByScannerType,
                        RecentFailures = metrics.RecentFailures.Select(f => new PublishingFailureDto
                        {
                            Timestamp = f.Timestamp,
                            ScannerType = f.ScannerType,
                            RetryCount = f.RetryCount,
                            ErrorMessage = f.ErrorMessage,
                            Duration = f.Duration
                        }).ToList()
                    },
                    Alerts = alerts
                };

                return Task.FromResult<ActionResult<QueuePublishingHealthResponse>>(Ok(response));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get queue publishing health status");
                return Task.FromResult<ActionResult<QueuePublishingHealthResponse>>(StatusCode(500, new QueuePublishingHealthResponse
                {
                    Status = "Error",
                    Timestamp = DateTime.UtcNow,
                    Error = ex.Message
                }));
            }
        }

        /// <summary>
        /// Get queue items with filtering, sorting, and pagination
        /// </summary>
        [HttpGet("items")]
        public async Task<ActionResult<PagedQueueItemsResponse>> GetQueueItems(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] string? scannerType = null,
            [FromQuery] string? status = null,
            [FromQuery] string? sortBy = "QueuedAt",
            [FromQuery] bool sortDescending = true,
            [FromQuery] string? search = null)
        {
            try
            {
                var query = _context.ContainerScanQueues.AsQueryable();

                // Apply filters
                if (!string.IsNullOrEmpty(scannerType) && scannerType != "All")
                {
                    query = query.Where(q => q.ScannerType == scannerType);
                }

                if (!string.IsNullOrEmpty(status) && status != "All")
                {
                    query = query.Where(q => q.Status == status);
                }

                if (!string.IsNullOrEmpty(search))
                {
                    query = query.Where(q => q.ContainerNumber.Contains(search) ||
                                           (q.InspectionId != null && q.InspectionId.Contains(search)));
                }

                // Apply sorting
                query = sortBy switch
                {
                    "ContainerNumber" => sortDescending ? query.OrderByDescending(q => q.ContainerNumber) : query.OrderBy(q => q.ContainerNumber),
                    "ScannerType" => sortDescending ? query.OrderByDescending(q => q.ScannerType) : query.OrderBy(q => q.ScannerType),
                    "Status" => sortDescending ? query.OrderByDescending(q => q.Status) : query.OrderBy(q => q.Status),
                    "ScanDate" => sortDescending ? query.OrderByDescending(q => q.ScanDate) : query.OrderBy(q => q.ScanDate),
                    "QueuedAt" => sortDescending ? query.OrderByDescending(q => q.QueuedAt) : query.OrderBy(q => q.QueuedAt),
                    "ProcessedAt" => sortDescending ? query.OrderByDescending(q => q.ProcessedAt) : query.OrderBy(q => q.ProcessedAt),
                    "CompletedAt" => sortDescending ? query.OrderByDescending(q => q.CompletedAt) : query.OrderBy(q => q.CompletedAt),
                    "RetryCount" => sortDescending ? query.OrderByDescending(q => q.RetryCount) : query.OrderBy(q => q.RetryCount),
                    _ => query.OrderByDescending(q => q.QueuedAt)
                };

                var totalCount = await query.CountAsync();
                var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

                var items = await query
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(q => new QueueItemDetailResponse
                    {
                        Id = q.Id,
                        ContainerNumber = q.ContainerNumber,
                        ScannerType = q.ScannerType,
                        InspectionId = q.InspectionId,
                        ScanDate = q.ScanDate,
                        Status = q.Status,
                        Priority = q.Priority,
                        RetryCount = q.RetryCount,
                        MaxRetries = q.MaxRetries,
                        ErrorMessage = q.ErrorMessage,
                        QueuedAt = q.QueuedAt,
                        ProcessedAt = q.ProcessedAt,
                        CompletedAt = q.CompletedAt,
                        Metadata = q.Metadata,
                        CreatedAt = q.CreatedAt,
                        UpdatedAt = q.UpdatedAt
                    })
                    .ToListAsync();

                return Ok(new PagedQueueItemsResponse
                {
                    Items = items,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = totalPages
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get queue items");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Determine publishing health status based on metrics and alerts
        /// </summary>
        private string DeterminePublishingHealthStatus(QueuePublishingMetrics metrics, List<string> alerts)
        {
            // Check for critical alerts
            if (alerts.Any(a => a.Contains("[Critical]", StringComparison.OrdinalIgnoreCase)))
            {
                return "Critical";
            }

            // Check success rate
            if (metrics.TotalAttempts > 0)
            {
                var criticalThreshold = _configuration.GetValue<int>("QueueHealth:SuccessRateWarningThreshold", 95);
                var warningThreshold = _configuration.GetValue<int>("QueueHealth:SuccessRateCriticalThreshold", 99);
                if (metrics.SuccessRate < criticalThreshold)
                    return "Critical";
                if (metrics.SuccessRate < warningThreshold)
                    return "Warning";
            }

            // Check for warnings
            if (alerts.Any(a => a.Contains("[Warning]", StringComparison.OrdinalIgnoreCase)))
            {
                return "Warning";
            }

            return "Healthy";
        }

        private async Task<QueueStatistics> GetQueueStatisticsAsync()
        {
            var stats = new QueueStatistics();

            // Status distribution (DB-level aggregation)
            var statusGroups = await _context.ContainerScanQueues
                .GroupBy(q => q.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            stats.TotalPending = statusGroups.FirstOrDefault(s => s.Status == "Pending")?.Count ?? 0;
            stats.TotalProcessing = statusGroups.FirstOrDefault(s => s.Status == "Processing")?.Count ?? 0;
            stats.TotalCompleted = statusGroups.FirstOrDefault(s => s.Status == "Completed")?.Count ?? 0;
            stats.TotalFailed = statusGroups.FirstOrDefault(s => s.Status == "Failed")?.Count ?? 0;

            // By scanner type (DB-level aggregation)
            var scannerGroups = await _context.ContainerScanQueues
                .GroupBy(q => q.ScannerType)
                .Select(g => new { ScannerType = g.Key, Count = g.Count() })
                .ToListAsync();

            stats.ByScannerTypeFS6000 = scannerGroups.FirstOrDefault(s => s.ScannerType == "FS6000")?.Count ?? 0;
            stats.ByScannerTypeASE = scannerGroups.FirstOrDefault(s => s.ScannerType == "ASE")?.Count ?? 0;

            // Processing rate (last hour) — DB-level count + average, no materialization
            var oneHourAgo = DateTime.UtcNow.AddHours(-1);
            var completedQuery = _context.ContainerScanQueues
                .Where(q => q.Status == "Completed" && q.CompletedAt >= oneHourAgo);

            stats.ItemsProcessedLastHour = await completedQuery.CountAsync();
            if (stats.ItemsProcessedLastHour > 0)
            {
                // PostgreSQL: use EXTRACT(EPOCH FROM ...) for date diff — load only timestamps, not full entities
                var timestamps = await completedQuery
                    .Where(q => q.CompletedAt.HasValue)
                    .Select(q => new { q.QueuedAt, q.CompletedAt })
                    .Take(500) // cap for average calculation
                    .ToListAsync();
                if (timestamps.Any())
                {
                    stats.AverageProcessingTimeSeconds = (int)timestamps
                        .Average(t => (t.CompletedAt!.Value - t.QueuedAt).TotalSeconds);
                }
            }

            // Average wait time for pending items — DB-level aggregation
            if (stats.TotalPending > 0)
            {
                var oldestPending = await _context.ContainerScanQueues
                    .Where(q => q.Status == "Pending")
                    .OrderBy(q => q.QueuedAt)
                    .Select(q => q.QueuedAt)
                    .FirstOrDefaultAsync();

                if (oldestPending != default)
                {
                    stats.OldestPendingQueuedAt = oldestPending;
                    stats.AverageWaitTimeMinutes = (int)(DateTime.UtcNow - oldestPending).TotalMinutes;
                }
            }

            // Success rate (last 24 hours) — DB-level count, no materialization
            var oneDayAgo = DateTime.UtcNow.AddDays(-1);
            var recentTotal = await _context.ContainerScanQueues
                .CountAsync(q => q.CreatedAt >= oneDayAgo);

            if (recentTotal > 0)
            {
                var completedCount = await _context.ContainerScanQueues
                    .CountAsync(q => q.CreatedAt >= oneDayAgo && q.Status == "Completed");
                stats.SuccessRate = (double)completedCount / recentTotal * 100;
            }

            return stats;
        }

        private string DetermineHealthStatus(QueueStatistics stats)
        {
            // Health criteria:
            // - Healthy: < 1000 pending, < 10 failed, avg wait < 60 seconds
            // - Warning: 1000-5000 pending, 10-50 failed, avg wait 60-300 seconds
            // - Critical: > 5000 pending, > 50 failed, avg wait > 300 seconds

            if (stats.TotalPending > 5000 || stats.TotalFailed > 50)
            {
                return "Critical";
            }

            if (stats.TotalPending > 1000 || stats.TotalFailed > 10 ||
                (stats.AverageWaitTimeMinutes.HasValue && stats.AverageWaitTimeMinutes > 5))
            {
                return "Warning";
            }

            return "Healthy";
        }

        private List<string> GetAlerts(QueueStatistics stats)
        {
            var alerts = new List<string>();

            if (stats.TotalPending > 5000)
            {
                alerts.Add($"CRITICAL: Queue depth is very high ({stats.TotalPending} pending items)");
            }
            else if (stats.TotalPending > 1000)
            {
                alerts.Add($"WARNING: Queue depth is high ({stats.TotalPending} pending items)");
            }

            if (stats.TotalFailed > 50)
            {
                alerts.Add($"CRITICAL: {stats.TotalFailed} failed items need attention");
            }
            else if (stats.TotalFailed > 10)
            {
                alerts.Add($"WARNING: {stats.TotalFailed} failed items");
            }

            if (stats.AverageWaitTimeMinutes.HasValue && stats.AverageWaitTimeMinutes > 5)
            {
                alerts.Add($"WARNING: Average wait time is high ({stats.AverageWaitTimeMinutes} minutes)");
            }

            var successRateWarning = _configuration.GetValue<int>("QueueHealth:SuccessRateWarningThreshold", 95);
            if (stats.SuccessRate < successRateWarning)
            {
                alerts.Add($"WARNING: Success rate is low ({stats.SuccessRate:F1}%)");
            }

            return alerts;
        }
    }

    // Response models
    public class QueueHealthResponse
    {
        public string Status { get; set; } = "Unknown";
        public DateTime Timestamp { get; set; }
        public QueueStatistics? Statistics { get; set; }
        public List<string> Alerts { get; set; } = new List<string>();
        public string? Error { get; set; }
    }

    public class QueueStatisticsResponse
    {
        public DateTime Timestamp { get; set; }
        public QueueStatistics Statistics { get; set; } = new QueueStatistics();
    }

    public class QueueStatistics
    {
        public int TotalPending { get; set; }
        public int TotalProcessing { get; set; }
        public int TotalCompleted { get; set; }
        public int TotalFailed { get; set; }
        public int ByScannerTypeFS6000 { get; set; }
        public int ByScannerTypeASE { get; set; }
        public int ItemsProcessedLastHour { get; set; }
        public int? AverageProcessingTimeSeconds { get; set; }
        public int? AverageWaitTimeMinutes { get; set; }
        public double SuccessRate { get; set; }
        public DateTime? OldestPendingQueuedAt { get; set; }
    }

    public class QueueItemResponse
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public string? InspectionId { get; set; }
        public string Status { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime? CreatedAt { get; set; }
        public int? StuckMinutes { get; set; }
    }

    public class QueueItemDetailResponse
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public string? InspectionId { get; set; }
        public DateTime ScanDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Metadata { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class PagedQueueItemsResponse
    {
        public List<QueueItemDetailResponse> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    /// <summary>
    /// Queue publishing health response (Phase 3: Monitoring &amp; Alerting)
    /// </summary>
    public class QueuePublishingHealthResponse
    {
        public string Status { get; set; } = "Unknown"; // Healthy, Warning, Critical
        public DateTime Timestamp { get; set; }
        public QueuePublishingMetricsDto? Metrics { get; set; }
        public List<string> Alerts { get; set; } = new List<string>();
        public string? Error { get; set; }
    }

    /// <summary>
    /// Queue publishing metrics DTO
    /// </summary>
    public class QueuePublishingMetricsDto
    {
        public TimeSpan TimeWindow { get; set; }
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public int TotalAttempts { get; set; }
        public int SuccessfulPublishes { get; set; }
        public int FailedPublishes { get; set; }
        public int SkippedPublishes { get; set; }
        public int TotalRetries { get; set; }
        public double AverageRetryCount { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan AveragePublishDuration { get; set; }
        public Dictionary<string, int> ByScannerType { get; set; } = new();
        public List<PublishingFailureDto> RecentFailures { get; set; } = new();
    }

    /// <summary>
    /// Publishing failure DTO
    /// </summary>
    public class PublishingFailureDto
    {
        public DateTime Timestamp { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
    }
}

