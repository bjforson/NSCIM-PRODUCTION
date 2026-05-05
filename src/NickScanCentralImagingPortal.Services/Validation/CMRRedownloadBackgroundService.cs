using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.Services.Validation
{
    /// <summary>
    /// Background service to process CMR re-download queue
    /// </summary>
    public class CMRRedownloadBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CMRRedownloadBackgroundService> _logger;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _processingInterval;

        // Audit 8.13 (Sprint 5G2 follow-up): heartbeat state. ProcessRedownloadQueueAsync
        // writes these and ExecuteAsync reads them for the per-iteration summary.
        private int _cycleCount = 0;
        private int _lastSuccessful = 0;
        private int _lastSkipped = 0;
        private int _lastFailed = 0;

        public CMRRedownloadBackgroundService(
            IServiceProvider serviceProvider,
            ILogger<CMRRedownloadBackgroundService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;

            // Get processing interval from configuration (default: 5 minutes)
            var intervalMinutes = _configuration.GetValue<int>("BackgroundServices:CMRRedownloadBackgroundService:ProcessIntervalMinutes", 5);
            _processingInterval = TimeSpan.FromMinutes(intervalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CMR Redownload Background Service started. Processing interval: {Interval}", _processingInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                // Audit 8.10 (Sprint 5G2 follow-up): mint per-cycle CorrelationId
                // so every log line emitted during this iteration carries the
                // same key.
                using var _cycleScope = _logger.BeginCycle(nameof(CMRRedownloadBackgroundService));
                // Audit 8.13 (Sprint 5G2 follow-up): track elapsed for heartbeat.
                var _cycleStartedAt = DateTime.UtcNow;
                _cycleCount++;
                _lastSuccessful = 0;
                _lastSkipped = 0;
                _lastFailed = 0;
                int _failedThisCycle = 0;
                try
                {
                    await ProcessRedownloadQueueAsync();
                }
                catch (Exception ex)
                {
                    _failedThisCycle = 1;
                    _logger.LogError(ex, "Error in CMR Redownload Background Service");
                }

                // Audit 8.13 (Sprint 5G2 follow-up): per-iteration heartbeat.
                // processed = items successfully redownloaded; skipped = items
                // intentionally not re-fetched this cycle; failed = per-item
                // failures plus loop-level exceptions.
                _logger.LogIterationSummary(
                    "CMR-REDOWNLOAD",
                    _cycleCount,
                    DateTime.UtcNow - _cycleStartedAt,
                    itemsProcessed: _lastSuccessful,
                    itemsSkipped: _lastSkipped,
                    itemsFailed: _lastFailed + _failedThisCycle);

                await Task.Delay(_processingInterval, stoppingToken);
            }

            _logger.LogInformation("CMR Redownload Background Service stopped");
        }

        private async Task ProcessRedownloadQueueAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var redownloadService = scope.ServiceProvider.GetRequiredService<ICMRRedownloadService>();

            try
            {
                _logger.LogDebug("Processing CMR re-download queue");

                var result = await redownloadService.ProcessRedownloadQueueAsync();

                if (result.TotalProcessed > 0)
                {
                    _logger.LogInformation("CMR re-download queue processed - Total: {Total}, Success: {Success}, Failed: {Failed}, Skipped: {Skipped}",
                        result.TotalProcessed, result.Successful, result.Failed, result.Skipped);

                    if (result.Errors.Any())
                    {
                        _logger.LogWarning("CMR re-download errors: {Errors}", string.Join("; ", result.Errors));
                    }
                }
                else
                {
                    _logger.LogDebug("No CMR re-download items to process");
                }

                // Audit 8.13 (Sprint 5G2 follow-up): publish per-cycle counts
                // to ExecuteAsync's heartbeat emitter.
                _lastSuccessful = result.Successful;
                _lastSkipped = result.Skipped;
                _lastFailed = result.Failed;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing CMR re-download queue");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("CMR Redownload Background Service is stopping...");
            await base.StopAsync(cancellationToken);
        }
    }
}
