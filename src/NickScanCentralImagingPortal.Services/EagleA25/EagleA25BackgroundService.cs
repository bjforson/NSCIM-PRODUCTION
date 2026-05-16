using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NickScanCentralImagingPortal.Services.EagleA25
{
    public class EagleA25BackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<EagleA25BackgroundService> _logger;
        private readonly EagleA25Configuration _config;

        public EagleA25BackgroundService(
            IServiceScopeFactory scopeFactory,
            ILogger<EagleA25BackgroundService> logger,
            IOptions<EagleA25Configuration> config)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _config = config.Value;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_config.Enabled)
            {
                _logger.LogInformation("[EAGLE-A25] Sync disabled; set EagleA25:Enabled=true to start ingestion");
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    for (var batch = 1; batch <= Math.Max(1, _config.MaxCatchUpBatchesPerCycle); batch++)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var sync = scope.ServiceProvider.GetRequiredService<IEagleA25SyncService>();
                        var result = await sync.SyncAsync(stoppingToken);
                        _logger.LogInformation(
                            "[EAGLE-A25] Sync complete: batch={Batch}, read={Read}, inserted={Inserted}, updated={Updated}, assets={AssetsRead}/{AssetsInserted}/{AssetsUpdated}, last={Last}",
                            batch,
                            result.ScansRead,
                            result.ScansInserted,
                            result.ScansUpdated,
                            result.AssetsRead,
                            result.AssetsInserted,
                            result.AssetsUpdated,
                            result.LastSyncedAccession);

                        if (result.ScansRead < Math.Max(1, _config.BatchSize))
                        {
                            break;
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[EAGLE-A25] Sync cycle failed");
                }

                var delay = TimeSpan.FromMinutes(Math.Max(1, _config.SyncIntervalMinutes));
                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
