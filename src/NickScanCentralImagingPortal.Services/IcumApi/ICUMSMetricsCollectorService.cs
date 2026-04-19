using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

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
                try
                {
                    await UpdateGaugesAsync();
                    await Task.Delay(_collectionInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("{ServiceId} Service cancellation requested", SERVICE_ID);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} Error updating ICUMS metrics", SERVICE_ID);
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error updating gauges", SERVICE_ID);
            }
        }
    }
}

