using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;

namespace NickScanCentralImagingPortal.Services.CameraEvidence
{
    public sealed class CameraEvidenceWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IOptionsMonitor<CameraEvidenceOptions> _options;
        private readonly ILogger<CameraEvidenceWorker> _logger;

        public CameraEvidenceWorker(
            IServiceScopeFactory scopeFactory,
            IOptionsMonitor<CameraEvidenceOptions> options,
            ILogger<CameraEvidenceWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _options = options;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                var delay = TimeSpan.FromSeconds(Math.Clamp(_options.CurrentValue.WorkerPollSeconds, 1, 300));
                try
                {
                    if (_options.CurrentValue.Enabled)
                    {
                        using var scope = _scopeFactory.CreateScope();
                        var service = scope.ServiceProvider.GetRequiredService<ICameraEvidenceService>();
                        var processed = await service.ProcessPendingWorkAsync(stoppingToken);
                        if (processed > 0)
                        {
                            _logger.LogInformation("Camera evidence worker processed {Count} work item(s)", processed);
                        }
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Camera evidence worker cycle failed");
                }

                await Task.Delay(delay, stoppingToken);
            }
        }
    }
}
