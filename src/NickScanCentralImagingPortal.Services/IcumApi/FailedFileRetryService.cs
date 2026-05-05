using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    /// <summary>
    /// Background service that automatically retries failed file processing
    /// Phase 2.2: Dead-Letter Queue with exponential backoff
    /// </summary>
    public class FailedFileRetryService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<FailedFileRetryService> _logger;
        private readonly IConfiguration _configuration;
        private readonly ICUMSMetrics? _metrics; // ✅ PHASE 3.1: Optional metrics
        private const string SERVICE_ID = "[FAILED-FILE-RETRY]";

        private readonly TimeSpan _processingInterval;
        private readonly int _maxRetriesPerCycle;
        private readonly TimeSpan _maxRetryDelay;

        // Audit 8.13 (Sprint 5G2 follow-up): heartbeat state. ProcessFailedFilesAsync
        // writes these and ExecuteAsync reads them for the per-iteration summary.
        private int _cycleCount = 0;
        private int _lastCycleSuccess = 0;
        private int _lastCycleFailure = 0;
        private int _lastCycleAbandoned = 0;

        public FailedFileRetryService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<FailedFileRetryService> logger,
            IConfiguration configuration,
            ICUMSMetrics? metrics = null) // ✅ PHASE 3.1: Optional metrics injection
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _configuration = configuration;
            _metrics = metrics;

            // Configuration
            _processingInterval = TimeSpan.FromMinutes(
                _configuration.GetValue<int>("ICUMS:FailedFileRetry:IntervalMinutes", 5));
            _maxRetriesPerCycle = _configuration.GetValue<int>("ICUMS:FailedFileRetry:MaxRetriesPerCycle", 10);
            _maxRetryDelay = TimeSpan.FromHours(
                _configuration.GetValue<int>("ICUMS:FailedFileRetry:MaxRetryDelayHours", 24));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{ServiceId} ✅ Failed File Retry Service starting (Interval: {Interval} minutes)",
                SERVICE_ID, _processingInterval.TotalMinutes);

            // Wait for application to fully start
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Audit 8.10 (Sprint 5G2 follow-up): mint per-cycle CorrelationId
                // so every log line emitted during this iteration carries the
                // same key.
                using var _cycleScope = _logger.BeginCycle(nameof(FailedFileRetryService));
                // Audit 8.13 (Sprint 5G2 follow-up): track elapsed for heartbeat.
                var _cycleStartedAt = DateTime.UtcNow;
                _cycleCount++;
                _lastCycleSuccess = 0;
                _lastCycleFailure = 0;
                _lastCycleAbandoned = 0;
                int _failedThisCycle = 0;
                try
                {
                    await ProcessFailedFilesAsync(stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("{ServiceId} Service cancellation requested", SERVICE_ID);
                    break;
                }
                catch (Exception ex)
                {
                    _failedThisCycle = 1;
                    _logger.LogError(ex, "{ServiceId} Error in retry processing cycle", SERVICE_ID);
                }

                // Audit 8.13 (Sprint 5G2 follow-up): per-iteration heartbeat.
                // processed = retries successfully kicked off; skipped = files
                // abandoned (gone from disk / orphan record); failed = per-file
                // exceptions plus loop-level exceptions.
                _logger.LogIterationSummary(
                    "FAILED-FILE-RETRY",
                    _cycleCount,
                    DateTime.UtcNow - _cycleStartedAt,
                    itemsProcessed: _lastCycleSuccess,
                    itemsSkipped: _lastCycleAbandoned,
                    itemsFailed: _lastCycleFailure + _failedThisCycle);

                // Wait before next cycle
                await Task.Delay(_processingInterval, stoppingToken);
            }

            _logger.LogInformation("{ServiceId} Failed File Retry Service stopped", SERVICE_ID);
        }

        private async Task ProcessFailedFilesAsync(CancellationToken cancellationToken)
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var repository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();

            // First, check for files that were successfully processed (status changed to "Completed")
            await CheckForResolvedFilesAsync(repository);

            // Get pending retries
            var pendingRetries = await repository.GetPendingRetriesAsync(_maxRetriesPerCycle);

            if (!pendingRetries.Any())
            {
                _logger.LogDebug("{ServiceId} No pending retries found", SERVICE_ID);
                return;
            }

            _logger.LogInformation("{ServiceId} Processing {Count} pending retries", SERVICE_ID, pendingRetries.Count);

            var successCount = 0;
            var failureCount = 0;
            var abandonedCount = 0;

            foreach (var failedFile in pendingRetries)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                try
                {
                    // Check if file still exists
                    if (!File.Exists(failedFile.FilePath))
                    {
                        _logger.LogWarning("{ServiceId} File no longer exists: {FilePath}. Marking as abandoned.",
                            SERVICE_ID, failedFile.FilePath);
                        await repository.MarkFailedFileAbandonedAsync(failedFile.Id, "File no longer exists on disk");
                        abandonedCount++;
                        continue;
                    }

                    // Get the downloaded file record
                    var downloadedFile = await repository.GetFileByIdAsync(failedFile.DownloadedFileId);
                    if (downloadedFile == null)
                    {
                        _logger.LogWarning("{ServiceId} DownloadedFile record not found for ID {FileId}. Marking as abandoned.",
                            SERVICE_ID, failedFile.DownloadedFileId);
                        await repository.MarkFailedFileAbandonedAsync(failedFile.Id, "DownloadedFile record not found");
                        abandonedCount++;
                        continue;
                    }

                    // Calculate next retry delay with exponential backoff
                    var retryOptions = RetryPolicy.CreateFileOperationRetryPolicy();
                    var nextRetryDelay = RetryPolicy.CalculateDelay(failedFile.RetryCount + 1, retryOptions);

                    // Cap at max delay
                    if (nextRetryDelay > _maxRetryDelay)
                    {
                        nextRetryDelay = _maxRetryDelay;
                    }

                    var nextRetryAt = DateTime.UtcNow.Add(nextRetryDelay);

                    // Update retry info
                    await repository.UpdateFailedFileRetryAsync(
                        failedFile.Id,
                        failedFile.RetryCount + 1,
                        nextRetryAt);

                    _logger.LogInformation("{ServiceId} Retrying file {FileName} (Attempt {Attempt}/{MaxRetries}, Next retry: {NextRetry})",
                        SERVICE_ID, failedFile.FileName, failedFile.RetryCount + 1, failedFile.MaxRetries, nextRetryAt);

                    // Reset file status to Pending so the main ingestion service will pick it up
                    // The ingestion service will process it and mark it as Completed or Failed
                    // We'll check in the next cycle if it was successful
                    await repository.UpdateFileProcessingStatusAsync(downloadedFile.Id, "Pending", null);

                    _logger.LogInformation("{ServiceId} ✅ File {FileName} reset to Pending for retry (Attempt {Attempt}/{MaxRetries})",
                        SERVICE_ID, failedFile.FileName, failedFile.RetryCount + 1, failedFile.MaxRetries);

                    // ✅ PHASE 3.1: Record metrics
                    _metrics?.RecordFileRetried();

                    // Don't mark as resolved immediately - let the main ingestion service process it
                    // We'll check in the next cycle if the file status changed to "Completed"
                    successCount++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} Error processing failed file {FileName}: {Error}",
                        SERVICE_ID, failedFile.FileName, ex.Message);
                    failureCount++;
                }
            }

            _logger.LogInformation("{ServiceId} Retry cycle completed. Success: {Success}, Failed: {Failed}, Abandoned: {Abandoned}",
                SERVICE_ID, successCount, failureCount, abandonedCount);

            // Audit 8.13 (Sprint 5G2 follow-up): publish per-cycle counts to
            // ExecuteAsync's heartbeat emitter.
            _lastCycleSuccess = successCount;
            _lastCycleFailure = failureCount;
            _lastCycleAbandoned = abandonedCount;
        }

        private async Task CheckForResolvedFilesAsync(IIcumDownloadsRepository repository)
        {
            // Check for files in "Retrying" status that have been successfully processed
            // This is done by checking if the corresponding DownloadedFile status is "Completed"
            var retryingFiles = await repository.GetRetryingFilesAsync(100);

            var resolvedCount = 0;
            foreach (var failedFile in retryingFiles)
            {
                var downloadedFile = await repository.GetFileByIdAsync(failedFile.DownloadedFileId);
                if (downloadedFile != null && downloadedFile.ProcessingStatus == "Completed")
                {
                    await repository.MarkFailedFileResolvedAsync(failedFile.Id);
                    resolvedCount++;
                    _logger.LogInformation("{ServiceId} ✅ File {FileName} successfully resolved after retry",
                        SERVICE_ID, failedFile.FileName);
                }
            }

            if (resolvedCount > 0)
            {
                _logger.LogInformation("{ServiceId} Marked {Count} files as resolved", SERVICE_ID, resolvedCount);
            }
        }
    }
}

