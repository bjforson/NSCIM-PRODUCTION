using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Service for tracking queue publishing metrics
    /// Thread-safe in-memory metrics storage with automatic cleanup
    /// Tracks publishing operations, success rates, retry counts, and performance metrics
    /// </summary>
    public class QueuePublishingMetricsService
    {
        private readonly ILogger<QueuePublishingMetricsService> _logger;
        private readonly ConcurrentDictionary<DateTime, PublishingOperation> _operations;
        private readonly object _cleanupLock = new object();
        private DateTime _lastCleanup = DateTime.UtcNow;
        private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(1);
        private static readonly TimeSpan MetricsRetentionWindow = TimeSpan.FromHours(24);

        public QueuePublishingMetricsService(ILogger<QueuePublishingMetricsService> logger)
        {
            _logger = logger;
            _operations = new ConcurrentDictionary<DateTime, PublishingOperation>();
        }

        /// <summary>
        /// Record a publish operation attempt
        /// </summary>
        public void RecordPublishAttempt(
            bool success,
            int retryCount,
            string scannerType,
            TimeSpan duration,
            bool skipped = false,
            string? errorMessage = null)
        {
            try
            {
                var operation = new PublishingOperation
                {
                    Timestamp = DateTime.UtcNow,
                    Success = success,
                    RetryCount = retryCount,
                    ScannerType = scannerType,
                    Duration = duration,
                    Skipped = skipped,
                    ErrorMessage = errorMessage
                };

                // Use timestamp as key (rounded to millisecond for uniqueness)
                var key = operation.Timestamp;
                _operations.TryAdd(key, operation);

                // Periodic cleanup (every hour)
                if (DateTime.UtcNow - _lastCleanup > CleanupInterval)
                {
                    CleanupOldMetrics();
                }
            }
            catch (Exception ex)
            {
                // Fail-safe: Don't break publishing if metrics fail
                _logger.LogWarning(ex, "[QUEUE-PUBLISHING-METRICS] Error recording publish attempt");
            }
        }

        /// <summary>
        /// Get aggregated metrics for the specified time window
        /// </summary>
        public QueuePublishingMetrics GetMetrics(TimeSpan? timeWindow = null)
        {
            try
            {
                var window = timeWindow ?? MetricsRetentionWindow;
                var cutoff = DateTime.UtcNow - window;

                // Filter operations within time window
                var recentOperations = _operations.Values
                    .Where(op => op.Timestamp >= cutoff)
                    .ToList();

                if (recentOperations.Count == 0)
                {
                    return new QueuePublishingMetrics
                    {
                        TimeWindow = window,
                        StartTime = cutoff,
                        EndTime = DateTime.UtcNow
                    };
                }

                var metrics = new QueuePublishingMetrics
                {
                    TimeWindow = window,
                    StartTime = cutoff,
                    EndTime = DateTime.UtcNow,
                    TotalAttempts = recentOperations.Count,
                    SuccessfulPublishes = recentOperations.Count(op => op.Success && !op.Skipped),
                    FailedPublishes = recentOperations.Count(op => !op.Success && !op.Skipped),
                    SkippedPublishes = recentOperations.Count(op => op.Skipped),
                    TotalRetries = recentOperations.Sum(op => op.RetryCount),
                    AverageRetryCount = recentOperations.Where(op => !op.Skipped).Any()
                        ? recentOperations.Where(op => !op.Skipped).Average(op => op.RetryCount)
                        : 0,
                    ByScannerType = recentOperations
                        .Where(op => !op.Skipped)
                        .GroupBy(op => op.ScannerType)
                        .ToDictionary(g => g.Key, g => g.Count()),
                    RecentFailures = recentOperations
                        .Where(op => !op.Success && !op.Skipped)
                        .OrderByDescending(op => op.Timestamp)
                        .Take(10)
                        .Select(op => new PublishingFailure
                        {
                            Timestamp = op.Timestamp,
                            ScannerType = op.ScannerType,
                            RetryCount = op.RetryCount,
                            ErrorMessage = op.ErrorMessage,
                            Duration = op.Duration
                        })
                        .ToList()
                };

                // Calculate success rate
                var validAttempts = metrics.SuccessfulPublishes + metrics.FailedPublishes;
                metrics.SuccessRate = validAttempts > 0
                    ? (metrics.SuccessfulPublishes * 100.0 / validAttempts)
                    : 100.0;

                // Calculate average duration
                var operationsWithDuration = recentOperations.Where(op => !op.Skipped).ToList();
                if (operationsWithDuration.Any())
                {
                    metrics.AveragePublishDuration = TimeSpan.FromMilliseconds(
                        operationsWithDuration.Average(op => op.Duration.TotalMilliseconds));
                }

                return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QUEUE-PUBLISHING-METRICS] Error getting metrics");
                return new QueuePublishingMetrics
                {
                    TimeWindow = timeWindow ?? MetricsRetentionWindow,
                    StartTime = DateTime.UtcNow - (timeWindow ?? MetricsRetentionWindow),
                    EndTime = DateTime.UtcNow
                };
            }
        }

        /// <summary>
        /// Get success rate for the specified time window
        /// </summary>
        public double GetSuccessRate(TimeSpan? timeWindow = null)
        {
            var metrics = GetMetrics(timeWindow);
            return metrics.SuccessRate;
        }

        /// <summary>
        /// Get recent failures
        /// </summary>
        public List<PublishingFailure> GetRecentFailures(int count = 10, TimeSpan? timeWindow = null)
        {
            var metrics = GetMetrics(timeWindow);
            return metrics.RecentFailures.Take(count).ToList();
        }

        /// <summary>
        /// Cleanup old metrics (keep only last 24 hours)
        /// </summary>
        private void CleanupOldMetrics()
        {
            lock (_cleanupLock)
            {
                try
                {
                    var cutoff = DateTime.UtcNow - MetricsRetentionWindow;
                    var keysToRemove = _operations.Keys
                        .Where(key => key < cutoff)
                        .ToList();

                    foreach (var key in keysToRemove)
                    {
                        _operations.TryRemove(key, out _);
                    }

                    _lastCleanup = DateTime.UtcNow;

                    if (keysToRemove.Count > 0)
                    {
                        _logger.LogDebug("[QUEUE-PUBLISHING-METRICS] Cleaned up {Count} old metrics entries", keysToRemove.Count);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[QUEUE-PUBLISHING-METRICS] Error during cleanup");
                }
            }
        }

        /// <summary>
        /// Get metrics by scanner type
        /// </summary>
        public Dictionary<string, QueuePublishingMetrics> GetMetricsByScannerType(TimeSpan? timeWindow = null)
        {
            var window = timeWindow ?? MetricsRetentionWindow;
            var cutoff = DateTime.UtcNow - window;

            var recentOperations = _operations.Values
                .Where(op => op.Timestamp >= cutoff)
                .GroupBy(op => op.ScannerType)
                .ToDictionary(g => g.Key, g => g.ToList());

            var result = new Dictionary<string, QueuePublishingMetrics>();

            foreach (var scannerGroup in recentOperations)
            {
                var operations = scannerGroup.Value;
                var scannerMetrics = new QueuePublishingMetrics
                {
                    TimeWindow = window,
                    StartTime = cutoff,
                    EndTime = DateTime.UtcNow,
                    TotalAttempts = operations.Count,
                    SuccessfulPublishes = operations.Count(op => op.Success && !op.Skipped),
                    FailedPublishes = operations.Count(op => !op.Success && !op.Skipped),
                    SkippedPublishes = operations.Count(op => op.Skipped),
                    TotalRetries = operations.Sum(op => op.RetryCount),
                    AverageRetryCount = operations.Where(op => !op.Skipped).Any()
                        ? operations.Where(op => !op.Skipped).Average(op => op.RetryCount)
                        : 0
                };

                var validAttempts = scannerMetrics.SuccessfulPublishes + scannerMetrics.FailedPublishes;
                scannerMetrics.SuccessRate = validAttempts > 0
                    ? (scannerMetrics.SuccessfulPublishes * 100.0 / validAttempts)
                    : 100.0;

                var operationsWithDuration = operations.Where(op => !op.Skipped).ToList();
                if (operationsWithDuration.Any())
                {
                    scannerMetrics.AveragePublishDuration = TimeSpan.FromMilliseconds(
                        operationsWithDuration.Average(op => op.Duration.TotalMilliseconds));
                }

                result[scannerGroup.Key] = scannerMetrics;
            }

            return result;
        }
    }

    /// <summary>
    /// Individual publishing operation record
    /// </summary>
    internal class PublishingOperation
    {
        public DateTime Timestamp { get; set; }
        public bool Success { get; set; }
        public int RetryCount { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public TimeSpan Duration { get; set; }
        public bool Skipped { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// Aggregated queue publishing metrics
    /// </summary>
    public class QueuePublishingMetrics
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
        public List<PublishingFailure> RecentFailures { get; set; } = new();
    }

    /// <summary>
    /// Publishing failure record
    /// </summary>
    public class PublishingFailure
    {
        public DateTime Timestamp { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public int RetryCount { get; set; }
        public string? ErrorMessage { get; set; }
        public TimeSpan Duration { get; set; }
    }
}

