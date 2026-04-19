using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.ASE;

namespace NickScanCentralImagingPortal.Services.ASE
{
    public class AseBackgroundService : IHostedService
    {
        private readonly ILogger<AseBackgroundService> _logger;
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly IOptions<AseConfiguration> _aseConfig;
        private CancellationTokenSource _cancellationTokenSource = new();
        private Task _syncTask = Task.CompletedTask;

        public AseBackgroundService(
            ILogger<AseBackgroundService> logger,
            IServiceScopeFactory serviceScopeFactory,
            IOptions<AseConfiguration> aseConfig)
        {
            _logger = logger;
            _serviceScopeFactory = serviceScopeFactory;
            _aseConfig = aseConfig;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            // ✅ FIX: Check if sync is enabled before starting
            if (!_aseConfig.Value.EnableRealTimeSync)
            {
                _logger.LogWarning("ASE Background Service is disabled (EnableRealTimeSync=false). Service will not start.");
                return Task.CompletedTask;
            }

            // ✅ FIX: Check if connection string is configured
            if (string.IsNullOrEmpty(_aseConfig.Value.ConnectionString))
            {
                _logger.LogWarning("ASE Background Service cannot start: ConnectionString is not configured. Please set ASE:ConnectionString in appsettings.json or environment variable.");
                return Task.CompletedTask;
            }

            _logger.LogInformation("ASE Background Service starting (EnableRealTimeSync=true, ConnectionString configured)");

            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _syncTask = RunSyncLoopAsync(_cancellationTokenSource.Token);

            return Task.CompletedTask;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("ASE Background Service stopping");

            _cancellationTokenSource.Cancel();

            try
            {
                await _syncTask;
            }
            catch (OperationCanceledException)
            {
                // Expected when cancellation is requested
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in ASE Background Service");
            }

            _logger.LogInformation("ASE Background Service stopped");
        }

        private async Task RunSyncLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    // Create a scope for each sync operation
                    using var scope = _serviceScopeFactory.CreateScope();
                    var syncService = scope.ServiceProvider.GetRequiredService<IAseDatabaseSyncService>();
                    await syncService.SyncDataAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during ASE sync cycle");
                }

                // Wait for the configured interval before next sync (read from database settings)
                try
                {
                    using (var scope = _serviceScopeFactory.CreateScope())
                    {
                        var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                        var syncIntervalMinutes = await settingsProvider.GetIntAsync("BackgroundServices", "ASE.SyncIntervalMinutes", 15);
                        _logger.LogDebug("⏰ Next ASE sync in {Interval} minutes (from settings)", syncIntervalMinutes);
                        await Task.Delay(TimeSpan.FromMinutes(syncIntervalMinutes), cancellationToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // Expected when cancellation is requested
                    break;
                }
            }
        }
    }
}
