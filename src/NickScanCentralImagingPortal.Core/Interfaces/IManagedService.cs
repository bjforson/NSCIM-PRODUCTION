using System.Threading;
using System.Threading.Tasks;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Interface for services managed by the Service Orchestrator
    /// </summary>
    public interface IManagedService
    {
        /// <summary>
        /// Gets the name of the service
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Gets the current status of the service
        /// </summary>
        ServiceStatus Status { get; }

        /// <summary>
        /// Starts the service
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the start operation</returns>
        Task StartAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Stops the service
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the stop operation</returns>
        Task StopAsync(CancellationToken cancellationToken);

        /// <summary>
        /// Checks if the service is healthy
        /// </summary>
        /// <returns>True if healthy, false otherwise</returns>
        Task<bool> IsHealthyAsync();

        /// <summary>
        /// Restarts the service
        /// </summary>
        /// <param name="cancellationToken">Cancellation token</param>
        /// <returns>Task representing the restart operation</returns>
        Task RestartAsync(CancellationToken cancellationToken);
    }

    /// <summary>
    /// Enumeration of possible service statuses
    /// </summary>
    public enum ServiceStatus
    {
        /// <summary>
        /// Service is stopped
        /// </summary>
        Stopped,

        /// <summary>
        /// Service is starting
        /// </summary>
        Starting,

        /// <summary>
        /// Service is running
        /// </summary>
        Running,

        /// <summary>
        /// Service is stopping
        /// </summary>
        Stopping,

        /// <summary>
        /// Service has failed
        /// </summary>
        Failed,

        /// <summary>
        /// Service is restarting
        /// </summary>
        Restarting
    }
}
