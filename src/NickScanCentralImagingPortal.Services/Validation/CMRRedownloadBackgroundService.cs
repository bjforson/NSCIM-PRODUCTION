using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

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
                try
                {
                    await ProcessRedownloadQueueAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in CMR Redownload Background Service");
                }

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
