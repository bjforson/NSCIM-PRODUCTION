using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ServiceLifecycle
{
    /// <summary>
    /// Startup service that discovers and registers all hosted services with the ServiceLifecycleManager
    /// </summary>
    public class ServiceLifecycleStartupService : IHostedService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ServiceLifecycleStartupService> _logger;
        private readonly IServiceLifecycleManager _lifecycleManager;

        public ServiceLifecycleStartupService(
            IServiceProvider serviceProvider,
            ILogger<ServiceLifecycleStartupService> logger,
            IServiceLifecycleManager lifecycleManager)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _lifecycleManager = lifecycleManager;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[ServiceLifecycleStartup] Discovering and registering hosted services...");

            try
            {
                // Register services that implement IManagedService.
                // For status lookup: managed service Name should match SystemAdminController.GetServiceDisplayName(type)
                // so that ServiceControlPanel can correlate API-reported status with lifecycle manager.
                // Get all registered IHostedService instances
                var hostedServices = _serviceProvider.GetServices<IHostedService>().ToList();

                _logger.LogInformation("[ServiceLifecycleStartup] Found {Count} hosted services", hostedServices.Count);

                foreach (var hostedService in hostedServices)
                {
                    try
                    {
                        var serviceType = hostedService.GetType();
                        var serviceName = serviceType.Name;

                        // Skip if already implements IManagedService
                        if (hostedService is IManagedService managedService)
                        {
                            _lifecycleManager.RegisterService(managedService);
                            _logger.LogInformation("[ServiceLifecycleStartup] Registered managed service: {ServiceName}", serviceName);
                        }
                        else
                        {
                            // For now, we'll only register services that already implement IManagedService
                            // Wrapping services requires more complex logic and should be done carefully
                            _logger.LogDebug("[ServiceLifecycleStartup] Skipping {ServiceName} - does not implement IManagedService", serviceName);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[ServiceLifecycleStartup] Error registering service {ServiceType}", hostedService.GetType().Name);
                    }
                }

                _logger.LogInformation("[ServiceLifecycleStartup] Service discovery complete");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ServiceLifecycleStartup] Error during service discovery");
            }

            await Task.CompletedTask;
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("[ServiceLifecycleStartup] Service lifecycle startup service stopping");
            return Task.CompletedTask;
        }
    }
}

