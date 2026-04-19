using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.ImageSplitter;

/// <summary>
/// Monitors the external Python image splitter service health.
/// Logs warnings when the service is unreachable so operators can investigate.
/// The Python service itself is managed as a Windows Service via NSSM
/// (see services/image-splitter/install-service.bat).
/// </summary>
public class ImageSplitterHealthMonitorService : BackgroundService
{
    private readonly IImageSplitterService _splitterService;
    private readonly ILogger<ImageSplitterHealthMonitorService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromMinutes(5);
    private bool _lastHealthy = true;

    public ImageSplitterHealthMonitorService(
        IImageSplitterService splitterService,
        ILogger<ImageSplitterHealthMonitorService> logger)
    {
        _splitterService = splitterService;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Wait for startup to settle
        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var healthy = await _splitterService.IsHealthyAsync(stoppingToken);

                if (!healthy && _lastHealthy)
                {
                    _logger.LogWarning("[IMAGE-SPLITTER-MONITOR] Image Splitter service at localhost:5320 is UNREACHABLE. " +
                        "Ensure the NSCIM_ImageSplitter Windows service is running (nssm start NSCIM_ImageSplitter)");
                }
                else if (healthy && !_lastHealthy)
                {
                    _logger.LogInformation("[IMAGE-SPLITTER-MONITOR] Image Splitter service is back online");
                }

                _lastHealthy = healthy;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[IMAGE-SPLITTER-MONITOR] Health check error");
            }

            await Task.Delay(CheckInterval, stoppingToken);
        }
    }
}
