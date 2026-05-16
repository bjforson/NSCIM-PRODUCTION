using System.Net.Sockets;
using System.Threading;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Unified queue publishing service for container scans with robust retry logic
    /// All scanner ingestion services use this service to push scans to the completeness queue
    /// This abstraction makes the system future-proof - new scanners just inject and use this service
    /// Features: Retry logic (5 attempts), exponential backoff, automatic recovery
    /// </summary>
    public class ContainerScanQueuePublisherService : IContainerScanQueuePublisher
    {
        private readonly IContainerScanQueueRepository _queueRepository;
        private readonly ILogger<ContainerScanQueuePublisherService> _logger;
        private readonly QueuePublishingMetricsService? _metricsService;
        private readonly IScannerWorkflowGate? _scannerWorkflowGate;
        private const int MAX_RETRIES = 5;
        private const int BASE_DELAY_MS = 1000; // 1 second

        public ContainerScanQueuePublisherService(
            IContainerScanQueueRepository queueRepository,
            ILogger<ContainerScanQueuePublisherService> logger,
            QueuePublishingMetricsService? metricsService = null,
            IScannerWorkflowGate? scannerWorkflowGate = null)
        {
            _queueRepository = queueRepository;
            _logger = logger;
            _metricsService = metricsService; // Optional for backward compatibility
            _scannerWorkflowGate = scannerWorkflowGate;
        }

        public async Task<int> PublishScanAsync(
            string containerNumber,
            string scannerType,
            string? inspectionId,
            DateTime scanDate,
            int priority = 0,
            string? metadata = null)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var startTime = DateTime.UtcNow;

                // Validate inputs
                if (string.IsNullOrWhiteSpace(containerNumber))
                {
                    _logger.LogWarning("[QUEUE-PUBLISHER] Skipping queue publish - ContainerNumber is null or empty");
                    _metricsService?.RecordPublishAttempt(false, 0, scannerType ?? "Unknown", stopwatch.Elapsed, skipped: true);
                    return 0;
                }

                if (string.IsNullOrWhiteSpace(scannerType))
                {
                    _logger.LogWarning("[QUEUE-PUBLISHER] Skipping queue publish - ScannerType is null or empty for container {ContainerNumber}", containerNumber);
                    _metricsService?.RecordPublishAttempt(false, 0, "Unknown", stopwatch.Elapsed, skipped: true);
                    return 0;
                }

                if (_scannerWorkflowGate?.IsAssignmentIntakeEnabled(scannerType) == false)
                {
                    _logger.LogInformation("[QUEUE-PUBLISHER] Skipping queue publish for disabled scanner workflow {ScannerType} ({ContainerNumber})",
                        scannerType, containerNumber);
                    _metricsService?.RecordPublishAttempt(false, 0, scannerType, stopwatch.Elapsed, skipped: true);
                    return 0;
                }

                // Create queue item
                var queueItem = new ContainerScanQueue
                {
                    ContainerNumber = containerNumber.Trim(),
                    ScannerType = scannerType.Trim(),
                    InspectionId = inspectionId?.Trim(),
                    ScanDate = scanDate,
                    Status = ContainerScanQueueStatus.Pending,
                    Priority = priority,
                    MaxRetries = 3,
                    Metadata = metadata
                };

                // Add to queue with retry logic (repository handles deduplication)
                int retryCountUsed = 0;
                var queueId = await ExecuteWithRetryAsync(
                    async () => await _queueRepository.AddToQueueAsync(queueItem),
                    operationName: $"PublishScan({containerNumber}, {scannerType})",
                    onRetry: (retryCount) => { retryCountUsed = retryCount; });

                stopwatch.Stop();

                if (queueId > 0)
                {
                    _logger.LogDebug("[QUEUE-PUBLISHER] ✅ Published scan to queue: {ContainerNumber} ({ScannerType}, {InspectionId}) -> QueueId: {QueueId}",
                        containerNumber, scannerType, inspectionId, queueId);
                    _metricsService?.RecordPublishAttempt(true, retryCountUsed, scannerType, stopwatch.Elapsed);
                }

                return queueId;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Log error but don't throw - queue publishing failures shouldn't break scanner ingestion
                // Recovery service will handle missed scans
                _logger.LogError(ex, "[QUEUE-PUBLISHER] ❌ Error publishing scan to queue after all retries: {ContainerNumber} ({ScannerType}). Recovery service will handle this.",
                    containerNumber, scannerType);

                // Record failure with max retries attempted
                _metricsService?.RecordPublishAttempt(false, MAX_RETRIES - 1, scannerType, stopwatch.Elapsed, errorMessage: ex.Message);

                return 0; // Return 0 to indicate failure without throwing
            }
        }

        public async Task<int> PublishScansBatchAsync(List<ContainerScanInfo> scans)
        {
            if (scans == null || !scans.Any())
                return 0;

            var startTime = DateTime.UtcNow;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Filter out invalid scans
                var validScans = scans
                    .Where(s => !string.IsNullOrWhiteSpace(s.ContainerNumber) && !string.IsNullOrWhiteSpace(s.ScannerType))
                    .ToList();
                var disabledCount = validScans.Count(s => _scannerWorkflowGate?.IsAssignmentIntakeEnabled(s.ScannerType) == false);
                if (disabledCount > 0)
                {
                    validScans = validScans
                        .Where(s => _scannerWorkflowGate?.IsAssignmentIntakeEnabled(s.ScannerType) != false)
                        .ToList();
                    _logger.LogInformation("[QUEUE-PUBLISHER] Skipped {Count} scans because scanner workflow assignment intake is disabled", disabledCount);
                }

                if (!validScans.Any())
                {
                    _logger.LogWarning("[QUEUE-PUBLISHER] No valid scans to publish in batch");
                    stopwatch.Stop();
                    // Record skipped batch
                    foreach (var scan in scans)
                    {
                        _metricsService?.RecordPublishAttempt(false, 0, scan.ScannerType, stopwatch.Elapsed, skipped: true);
                    }
                    return 0;
                }

                // Convert to queue items
                var queueItems = validScans.Select(s => new ContainerScanQueue
                {
                    ContainerNumber = s.ContainerNumber.Trim(),
                    ScannerType = s.ScannerType.Trim(),
                    InspectionId = s.InspectionId?.Trim(),
                    ScanDate = s.ScanDate,
                    Status = ContainerScanQueueStatus.Pending,
                    Priority = s.Priority,
                    MaxRetries = 3,
                    Metadata = s.Metadata,
                    ScanImageAssetId = s.ScanImageAssetId,
                    OriginalScanRecordId = s.OriginalScanRecordId,
                    SourceContainerLabel = s.SourceContainerLabel,
                    ScanContainerPosition = s.ScanContainerPosition,
                    SplitJobId = s.SplitJobId,
                    SplitResultId = s.SplitResultId
                }).ToList();

                // Add batch to queue with retry logic (repository handles deduplication)
                int batchRetryCount = 0;
                var addedCount = await ExecuteWithRetryAsync(
                    async () => await _queueRepository.AddBatchToQueueAsync(queueItems),
                    operationName: $"PublishBatch({queueItems.Count} items)",
                    onRetry: (retryCount) => { batchRetryCount = retryCount; });

                stopwatch.Stop();

                _logger.LogInformation("[QUEUE-PUBLISHER] ✅ Published {AddedCount} scans to queue (from {TotalCount} provided, {SkippedCount} duplicates/invalid)",
                    addedCount, scans.Count, scans.Count - addedCount);

                // Record metrics for batch (one record per batch)
                var scannerType = validScans.FirstOrDefault()?.ScannerType ?? "Unknown";
                _metricsService?.RecordPublishAttempt(addedCount > 0, batchRetryCount, scannerType, stopwatch.Elapsed);

                return addedCount;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();

                // Log error but don't throw - queue publishing failures shouldn't break scanner ingestion
                // Recovery service will handle missed scans
                _logger.LogError(ex, "[QUEUE-PUBLISHER] ❌ Error publishing batch to queue after all retries: {Count} scans. Recovery service will handle these.",
                    scans.Count);

                // Record failure for batch
                var scannerType = scans.FirstOrDefault()?.ScannerType ?? "Unknown";
                _metricsService?.RecordPublishAttempt(false, MAX_RETRIES - 1, scannerType, stopwatch.Elapsed, errorMessage: ex.Message);

                return 0;
            }
        }

        public async Task<bool> IsScanQueuedAsync(string containerNumber, string scannerType, string? inspectionId)
        {
            try
            {
                return await _queueRepository.IsInQueueAsync(containerNumber, scannerType, inspectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[QUEUE-PUBLISHER] ❌ Error checking if scan is queued: {ContainerNumber} ({ScannerType})",
                    containerNumber, scannerType);
                return false; // Return false on error to allow publishing attempt
            }
        }

        /// <summary>
        /// Executes an operation with retry logic and exponential backoff
        /// Handles transient errors (timeouts, deadlocks, connection issues)
        /// </summary>
        private async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            string operationName,
            Action<int>? onRetry = null)
        {
            Exception? lastException = null;

            for (int attempt = 1; attempt <= MAX_RETRIES; attempt++)
            {
                try
                {
                    var result = await operation();

                    if (attempt > 1)
                    {
                        _logger.LogInformation("[QUEUE-PUBLISHER] ✅ {OperationName} succeeded on attempt {Attempt}/{MaxRetries}",
                            operationName, attempt, MAX_RETRIES);
                    }

                    // Notify of final retry count (attempt - 1, since first attempt is not a retry)
                    onRetry?.Invoke(attempt - 1);

                    return result;
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    // Check if this is a retryable (transient) error
                    if (!IsRetryableError(ex))
                    {
                        // Non-retryable error (validation, duplicate key, etc.)
                        _logger.LogWarning(ex, "[QUEUE-PUBLISHER] ⚠️ {OperationName} failed with non-retryable error on attempt {Attempt}: {Error}",
                            operationName, attempt, ex.Message);
                        throw; // Don't retry non-retryable errors
                    }

                    if (attempt >= MAX_RETRIES)
                    {
                        // All retries exhausted
                        _logger.LogError(ex, "[QUEUE-PUBLISHER] ❌ {OperationName} failed after {MaxRetries} attempts: {Error}",
                            operationName, MAX_RETRIES, ex.Message);
                        // Notify of final retry count (MAX_RETRIES - 1, since first attempt is not a retry)
                        onRetry?.Invoke(MAX_RETRIES - 1);
                        throw;
                    }

                    // Calculate exponential backoff delay: 1s, 2s, 4s, 8s, 16s
                    var delayMs = BASE_DELAY_MS * (int)Math.Pow(2, attempt - 1);

                    _logger.LogWarning(ex, "[QUEUE-PUBLISHER] ⚠️ {OperationName} failed on attempt {Attempt}/{MaxRetries} (transient error: {Error}). Retrying in {DelayMs}ms...",
                        operationName, attempt, MAX_RETRIES, ex.Message, delayMs);

                    await Task.Delay(delayMs, CancellationToken.None);
                }
            }

            // Should never reach here, but just in case
            throw lastException ?? new InvalidOperationException($"{operationName} failed after {MAX_RETRIES} attempts");
        }

        /// <summary>
        /// Determines if an exception is a retryable (transient) error
        /// </summary>
        private bool IsRetryableError(Exception ex)
        {
            // SQL Server transient errors
            if (ex is SqlException sqlEx)
            {
                // Common transient SQL Server error numbers:
                // -2: Timeout
                // 2: Connection timeout
                // 53: Network error
                // 1205: Deadlock victim
                // 1222: Lock request timeout
                // 2601: Duplicate key (unique index violation) - NOT retryable
                // 2627: Duplicate key (PRIMARY KEY violation) - NOT retryable
                return sqlEx.Number == -2 || sqlEx.Number == 2 || sqlEx.Number == 53 ||
                       sqlEx.Number == 1205 || sqlEx.Number == 1222;
            }

            // EF Core DbUpdateException with transient SQL exception
            if (ex is DbUpdateException dbEx && dbEx.InnerException is SqlException sqlInnerEx)
            {
                return IsRetryableError(sqlInnerEx);
            }

            // Network-related transient errors
            if (ex is TimeoutException || ex is SocketException)
            {
                return true;
            }

            // Task canceled with timeout message
            if (ex is TaskCanceledException && ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // Generic timeout/connection indicators
            var message = ex.Message.ToLowerInvariant();
            if (message.Contains("timeout") ||
                message.Contains("connection") ||
                message.Contains("network") ||
                message.Contains("deadlock"))
            {
                return true;
            }

            // All other errors are non-retryable
            return false;
        }
    }
}

