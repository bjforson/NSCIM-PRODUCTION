using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class PredictivePreloadBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PredictivePreloadBackgroundService> _logger;
    private readonly PredictivePreloadOptions _options;
    private readonly PredictivePreloadState _state;

    public PredictivePreloadBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<PredictivePreloadOptions> options,
        PredictivePreloadState state,
        ILogger<PredictivePreloadBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _state = state;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled || !_options.BackgroundEnabled)
        {
            _logger.LogInformation("Predictive preload background service disabled");
            return;
        }

        var startupDelay = TimeSpan.FromSeconds(Math.Max(0, _options.StartupDelaySeconds));
        if (startupDelay > TimeSpan.Zero)
        {
            await Task.Delay(startupDelay, stoppingToken);
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, _options.IntervalSeconds)));
        do
        {
            if (_state.IsRunning)
            {
                _logger.LogDebug("Skipping predictive preload pass because previous pass is still running");
                continue;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<IPredictivePreloadService>();
                await service.RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                if (_state.IsRunning)
                {
                    _state.MarkFailed(ex);
                }

                _logger.LogWarning(ex, "Predictive preload background pass failed");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
