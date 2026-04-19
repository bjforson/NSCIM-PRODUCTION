using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Background service that periodically cleans up old endpoint usage logs
    /// </summary>
    public class EndpointUsageCleanupBackgroundService : BackgroundService
    {
        private readonly IServiceScopeFactory _serviceScopeFactory;
        private readonly ILogger<EndpointUsageCleanupBackgroundService> _logger;
        private readonly int _daysToKeep;
        private const string SERVICE_ID = "[ENDPOINT-USAGE-CLEANUP]";
        private DateTime _lastCleanupTime = DateTime.UtcNow;
        private const int CLEANUP_INTERVAL_HOURS = 24; // Run cleanup once per day
        private DateTime _serviceStartTime = DateTime.UtcNow;
        private int _totalCleaned = 0;

        public EndpointUsageCleanupBackgroundService(
            IServiceScopeFactory serviceScopeFactory,
            ILogger<EndpointUsageCleanupBackgroundService> logger,
            IConfiguration configuration)
        {
            _serviceScopeFactory = serviceScopeFactory;
            _logger = logger;
            _daysToKeep = configuration.GetValue("Monitoring:EndpointUsageRetentionDays", 30);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _serviceStartTime = DateTime.UtcNow;
            _logger.LogInformation("{ServiceId} ✅ Endpoint Usage Cleanup Service starting at {StartTime}", SERVICE_ID, _serviceStartTime);

            // Wait for application to fully start
            await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            _logger.LogInformation("{ServiceId} ✅ Service initialization complete", SERVICE_ID);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Run cleanup if it's been more than CLEANUP_INTERVAL_HOURS since last cleanup
                    if ((DateTime.UtcNow - _lastCleanupTime).TotalHours >= CLEANUP_INTERVAL_HOURS)
                    {
                        await CleanupOldLogsAsync(stoppingToken);
                        _lastCleanupTime = DateTime.UtcNow;
                    }

                    // Check every hour if cleanup is needed
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("{ServiceId} Service cancellation requested", SERVICE_ID);
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} ❌ Error during cleanup cycle", SERVICE_ID);
                    // Wait before retrying on error
                    await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
                }
            }

            var totalUptime = DateTime.UtcNow - _serviceStartTime;
            _logger.LogInformation("{ServiceId} ✅ Endpoint Usage Cleanup Service stopping. Total uptime: {Uptime}, Total cleaned: {TotalCleaned} logs",
                SERVICE_ID, totalUptime, _totalCleaned);
        }

        private async Task CleanupOldLogsAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceScopeFactory.CreateScope();
                var endpointUsageService = scope.ServiceProvider.GetRequiredService<IEndpointUsageService>();

                _logger.LogInformation("{ServiceId} Starting periodic cleanup of old endpoint usage logs (keeping last {Days} days)", SERVICE_ID, _daysToKeep);

                var cleanedCount = await endpointUsageService.CleanupOldLogsAsync(daysToKeep: _daysToKeep);
                _totalCleaned += cleanedCount;

                if (cleanedCount > 0)
                {
                    _logger.LogInformation("{ServiceId} ✅ Cleaned up {Count} old endpoint usage logs (older than {Days} days)",
                        SERVICE_ID, cleanedCount, _daysToKeep);
                }
                else
                {
                    _logger.LogDebug("{ServiceId} No old logs to clean up", SERVICE_ID);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error cleaning up old endpoint usage logs", SERVICE_ID);
                // Don't throw - cleanup failures shouldn't stop the service
            }
        }
    }
}

