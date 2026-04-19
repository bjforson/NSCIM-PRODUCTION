using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ServiceLifecycle
{
    /// <summary>
    /// Interface for managing the lifecycle of background services
    /// </summary>
    public interface IServiceLifecycleManager
    {
        /// <summary>
        /// Restarts a service by name
        /// </summary>
        /// <param name="serviceName">Name of the service to restart</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if restart was successful, false otherwise</returns>
        Task<bool> RestartServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Stops a service by name
        /// </summary>
        /// <param name="serviceName">Name of the service to stop</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if stop was successful, false otherwise</returns>
        Task<bool> StopServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts a service by name
        /// </summary>
        /// <param name="serviceName">Name of the service to start</param>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>True if start was successful, false otherwise</returns>
        Task<bool> StartServiceAsync(string serviceName, CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current status of a service
        /// </summary>
        /// <param name="serviceName">Name of the service</param>
        /// <returns>Current service status, or Stopped if service not found</returns>
        ServiceStatus GetServiceStatus(string serviceName);

        /// <summary>
        /// Checks if a service is healthy
        /// </summary>
        /// <param name="serviceName">Name of the service</param>
        /// <returns>True if service is healthy, false otherwise</returns>
        Task<bool> IsServiceHealthyAsync(string serviceName);

        /// <summary>
        /// Gets the status of all registered services
        /// </summary>
        /// <returns>Dictionary mapping service names to their status</returns>
        Dictionary<string, ServiceStatus> GetAllServiceStatuses();

        /// <summary>
        /// Registers a managed service with the lifecycle manager
        /// </summary>
        /// <param name="service">The managed service to register</param>
        void RegisterService(IManagedService service);

        /// <summary>
        /// Gets a managed service by name
        /// </summary>
        /// <param name="serviceName">Name of the service</param>
        /// <returns>The managed service, or null if not found</returns>
        IManagedService? GetService(string serviceName);
    }
}

