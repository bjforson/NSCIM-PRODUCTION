using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NickScanCentralImagingPortal.API.Hubs;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Monitoring
{
    /// <summary>
    /// Extension methods for registering monitoring services
    /// </summary>
    public static class MonitoringServiceExtensions
    {
        /// <summary>
        /// Register comprehensive monitoring services
        /// </summary>
        public static IServiceCollection AddComprehensiveMonitoring(this IServiceCollection services, bool registerHostedService = true)
        {
            // Register as singleton for injection. Staging verification can skip
            // the hosted loop while keeping monitoring dependencies resolvable.
            services.AddSingleton<NickScanCentralImagingPortal.Services.Monitoring.ComprehensiveHealthCheckService>();
            services.AddSingleton<NickScanCentralImagingPortal.Core.Interfaces.IComprehensiveHealthCheckService>(provider =>
                provider.GetRequiredService<NickScanCentralImagingPortal.Services.Monitoring.ComprehensiveHealthCheckService>());
            if (registerHostedService)
            {
                services.AddHostedService<NickScanCentralImagingPortal.Services.Monitoring.ComprehensiveHealthCheckService>(provider =>
                    provider.GetRequiredService<NickScanCentralImagingPortal.Services.Monitoring.ComprehensiveHealthCheckService>());
            }

            return services;
        }

        /// <summary>
        /// Register monitoring services with SignalR
        /// </summary>
        public static IServiceCollection AddMonitoringWithSignalR(this IServiceCollection services, bool registerHostedService = true)
        {
            // Add SignalR
            services.AddSignalR(options =>
            {
                options.EnableDetailedErrors = true;
                options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
            });

            // Register monitoring services
            services.AddComprehensiveMonitoring(registerHostedService);

            return services;
        }

        /// <summary>
        /// Configure monitoring middleware and endpoints
        /// </summary>
        public static IApplicationBuilder UseComprehensiveMonitoring(this IApplicationBuilder app)
        {
            // Configure SignalR hub
            app.UseRouting();
            app.UseEndpoints(endpoints =>
            {
                endpoints.MapHub<MonitoringHub>("/monitoringhub");
            });

            return app;
        }
    }

    /// <summary>
    /// Background service to broadcast monitoring updates via SignalR
    /// </summary>
    public class MonitoringBroadcastService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<MonitoringBroadcastService> _logger;
        private readonly TimeSpan _broadcastInterval = TimeSpan.FromSeconds(30);

        public MonitoringBroadcastService(
            IServiceProvider serviceProvider,
            ILogger<MonitoringBroadcastService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Monitoring Broadcast Service starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await BroadcastMonitoringUpdates();
                    await Task.Delay(_broadcastInterval, stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    _logger.LogInformation("📡 Monitoring Broadcast Service stopping...");
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error in monitoring broadcast service");
                    await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken);
                }
            }
        }

        private async Task BroadcastMonitoringUpdates()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var hubContext = scope.ServiceProvider.GetRequiredService<IHubContext<MonitoringHub>>();
                var healthCheckService = scope.ServiceProvider.GetRequiredService<NickScanCentralImagingPortal.Services.Monitoring.ComprehensiveHealthCheckService>();

                // Broadcast system status update
                await MonitoringHub.BroadcastSystemStatusUpdate(hubContext, healthCheckService);

                // Check for unhealthy services and send alerts
                var serviceStatuses = healthCheckService.GetServiceStatuses();
                foreach (var service in serviceStatuses)
                {
                    if (service.Value.Status == NickScanCentralImagingPortal.Core.Interfaces.HealthStatus.Unhealthy)
                    {
                        await MonitoringHub.BroadcastAlert(
                            hubContext,
                            "ServiceFailure",
                            $"Service {service.Key} is unhealthy: {service.Value.ErrorMessage}",
                            service.Key,
                            "High"
                        );
                    }
                    else if (service.Value.Status == NickScanCentralImagingPortal.Core.Interfaces.HealthStatus.Degraded)
                    {
                        await MonitoringHub.BroadcastAlert(
                            hubContext,
                            "ServiceDegraded",
                            $"Service {service.Key} is degraded",
                            service.Key,
                            "Medium"
                        );
                    }
                }

                _logger.LogDebug("📡 Monitoring updates broadcasted successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error broadcasting monitoring updates");
            }
        }
    }
}
