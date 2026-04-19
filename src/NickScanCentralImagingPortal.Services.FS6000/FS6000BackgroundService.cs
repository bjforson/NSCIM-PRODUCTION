using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public class FS6000BackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<FS6000BackgroundService> _logger;
        private readonly IOptions<FileSyncConfiguration> _config;
        private IServiceScope _scope;
        private const string SERVICE_ID = "[FS6000-BACKGROUND]";

        public FS6000BackgroundService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<FS6000BackgroundService> logger,
            IOptions<FileSyncConfiguration> config)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 FS6000 Background Service starting");
            _logger.LogInformation("================================================");

            try
            {
                // Run startup diagnostics
                var diagnostics = new FS6000StartupDiagnostics(
                    _logger,
                    _config,
                    _serviceScopeFactory);
                var report = await diagnostics.RunDiagnosticsAsync();

                if (!report.AllChecksPass)
                {
                    _logger.LogError("❌ FS6000 startup diagnostics FAILED - service will not start");
                    _logger.LogError("❌ Please check the diagnostic report above and fix the issues");
                    _logger.LogError("================================================");
                    return;
                }

                _logger.LogInformation("✅ All diagnostics passed - starting FS6000 services");
                _logger.LogInformation("================================================");

                // Create scope and keep it alive
                _scope = _serviceScopeFactory.CreateScope();
                var fileSyncService = _scope.ServiceProvider.GetRequiredService<IFileSyncService>();
                var ingestionService = _scope.ServiceProvider.GetRequiredService<IIngestionService>();

                // Start both services
                await fileSyncService.StartSyncAsync();
                await ingestionService.StartIngestionAsync();

                _logger.LogInformation("{ServiceId} ✅ FS6000 Background Service started successfully", SERVICE_ID);

                // Keep the service running
                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in FS6000 Background Service");
            }
            finally
            {
                _logger.LogInformation("FS6000 Background Service stopping");

                try
                {
                    if (_scope != null)
                    {
                        var fileSyncService = _scope.ServiceProvider.GetRequiredService<IFileSyncService>();
                        var ingestionService = _scope.ServiceProvider.GetRequiredService<IIngestionService>();

                        await fileSyncService.StopSyncAsync();
                        await ingestionService.StopIngestionAsync();

                        _scope.Dispose();
                        _scope = null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error stopping FS6000 services");
                }

                _logger.LogInformation("FS6000 Background Service stopped");
            }
        }
    }
}
