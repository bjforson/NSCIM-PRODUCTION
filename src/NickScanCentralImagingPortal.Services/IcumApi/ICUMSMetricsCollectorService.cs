using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    /// <summary>
    /// Background service that periodically updates ICUMS metrics gauges
    /// Phase 3.1: Collects queue depth, pending files, and throughput metrics
    /// </summary>
    public class ICUMSMetricsCollectorService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<ICUMSMetricsCollectorService> _logger;
        private readonly IConfiguration _configuration;
        private readonly ICUMSMetrics? _metrics;
        private const string SERVICE_ID = "[ICUMS-METRICS-COLLECTOR]";

        private readonly TimeSpan _collectionInterval;
        private DateTime _lastThroughputCalculation = DateTime.UtcNow;
        private long _lastFilesProcessed = 0;
        private long _lastDocumentsProcessed = 0;

        // Audit 8.13 (Sprint 5G2 follow-up): heartbeat state. UpdateGaugesAsync
        // writes _lastGaugesUpdated and ExecuteAsync reads it for the
        // per-iteration summary line.
        private int _cycleCount = 0;
        private int _lastGaugesUpdated = 0;

        public ICUMSMetricsCollectorService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<ICUMSMetricsCollectorService> logger,
            IConfiguration configuration,
            ICUMSMetrics? metrics = null)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _configuration = configuration;
            _metrics = metrics;

            _collectionInterval = TimeSpan.FromMinutes(
                _configuration.GetValue<int>("ICUMS:Metrics:CollectionIntervalMinutes", 1));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{ServiceId} ✅ ICUMS Metrics Collector Service starting (Interval: {Interval} minutes)",
                SERVICE_ID, _collectionInterval.TotalMinutes);

            // Wait for application to fully start
            await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Audit 8.10 (Sprint 5G2 follow-up): mint per-cycle CorrelationId
                // so every log line emitted during this iteration carries the
                // same key.
                using var _cycleScope = _logger.BeginCycle(nameof(ICUMSMetricsCollectorService));
                // Audit 8.13 (Sprint 5G2 follow-up): track elapsed for heartbeat.
                var _cycleStartedAt = DateTime.UtcNow;
                _cycleCount++;
                _lastGaugesUpdated = 0;
                int _failedThisCycle = 0;
                try
                {
                    await UpdateGaugesAsync();
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("{ServiceId} Service cancellation requested", SERVICE_ID);
                    break;
                }
                catch (Exception ex)
                {
                    _failedThisCycle = 1;
                    _logger.LogError(ex, "{ServiceId} Error updating ICUMS metrics", SERVICE_ID);
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }

                // Audit 8.13 (Sprint 5G2 follow-up): per-iteration heartbeat.
                // processed = number of gauge categories updated this cycle
                // (queue depth / failed queue / pending / processing /
                // throughput / memory = 6 on success, 0 on failure); failed =
                // exceptions caught at the outer or inner level.
                _logger.LogIterationSummary(
                    "ICUMS-METRICS-COLLECTOR",
                    _cycleCount,
                    DateTime.UtcNow - _cycleStartedAt,
                    itemsProcessed: _lastGaugesUpdated,
                    itemsSkipped: 0,
                    itemsFailed: _failedThisCycle);

                try
                {
                    await Task.Delay(_collectionInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }

            _logger.LogInformation("{ServiceId} ICUMS Metrics Collector Service stopped", SERVICE_ID);
        }

        private async Task UpdateGaugesAsync()
        {
            using var scope = _serviceScopeFactory.CreateScope();
            var downloadsRepository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();
            var queueRepository = scope.ServiceProvider.GetRequiredService<IICUMSDownloadQueueRepository>();

            try
            {
                // Update queue depth - get statistics which includes pending count
                var queueStats = await queueRepository.GetQueueStatisticsAsync();
                _metrics?.SetGauge(ICUMSMetrics.GAUGE_QUEUE_DEPTH, queueStats.TotalPending);

                // Update failed queue depth
                var pendingRetries = await downloadsRepository.GetPendingRetriesAsync(1000);
                _metrics?.SetGauge(ICUMSMetrics.GAUGE_FAILED_QUEUE_DEPTH, pendingRetries.Count);

                // Update pending files
                var pendingFiles = await downloadsRepository.GetPendingFilesAsync();
                _metrics?.SetGauge(ICUMSMetrics.GAUGE_PENDING_FILES, pendingFiles.Count);

                // Update processing files (files with status "Processing")
                var processingFiles = pendingFiles.Where(f => f.ProcessingStatus == "Processing").Count();
                _metrics?.SetGauge(ICUMSMetrics.GAUGE_PROCESSING_FILES, processingFiles);

                // Calculate throughput (files per minute)
                var now = DateTime.UtcNow;
                var timeSinceLastCalc = (now - _lastThroughputCalculation).TotalMinutes;

                if (timeSinceLastCalc >= 1.0) // Calculate every minute
                {
                    var currentFilesProcessed = _metrics?.GetCounter(ICUMSMetrics.COUNTER_FILES_PROCESSED) ?? 0;
                    var currentDocumentsProcessed = _metrics?.GetCounter(ICUMSMetrics.COUNTER_DOCUMENTS_PROCESTED) ?? 0;

                    var filesProcessedDelta = currentFilesProcessed - _lastFilesProcessed;
                    var documentsProcessedDelta = currentDocumentsProcessed - _lastDocumentsProcessed;

                    var filesPerMin = timeSinceLastCalc > 0 ? filesProcessedDelta / timeSinceLastCalc : 0;
                    var documentsPerMin = timeSinceLastCalc > 0 ? documentsProcessedDelta / timeSinceLastCalc : 0;

                    _metrics?.SetGauge(ICUMSMetrics.GAUGE_AVG_THROUGHPUT_FILES_PER_MIN, filesPerMin);
                    _metrics?.SetGauge(ICUMSMetrics.GAUGE_AVG_THROUGHPUT_DOCUMENTS_PER_MIN, documentsPerMin);

                    _lastFilesProcessed = currentFilesProcessed;
                    _lastDocumentsProcessed = currentDocumentsProcessed;
                    _lastThroughputCalculation = now;
                }

                // Update memory usage (if available)
                var memoryUsage = GC.GetTotalMemory(false) / 1024.0 / 1024.0; // MB
                _metrics?.SetGauge(ICUMSMetrics.GAUGE_MEMORY_USAGE_MB, memoryUsage);

                _logger.LogDebug("{ServiceId} Updated gauges: Queue={QueueDepth}, FailedQueue={FailedQueueDepth}, Pending={PendingFiles}, Processing={ProcessingFiles}",
                    SERVICE_ID, queueStats.TotalPending, pendingRetries.Count, pendingFiles.Count, processingFiles);

                // Audit 8.13 (Sprint 5G2 follow-up): publish gauges-updated
                // count to ExecuteAsync's heartbeat emitter. 6 = queue + failed
                // queue + pending + processing + throughput pair + memory.
                _lastGaugesUpdated = 6;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error updating gauges", SERVICE_ID);
            }
        }
    }
}

