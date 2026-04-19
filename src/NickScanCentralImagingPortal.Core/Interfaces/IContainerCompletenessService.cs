using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service interface for managing container data completeness between scanners and ICUMS
    /// </summary>
    public interface IContainerCompletenessService
    {
        /// <summary>
        /// Checks the completeness status of all processed containers
        /// </summary>
        /// <param name="stoppingToken">Cancellation token</param>
        /// <returns>Number of containers processed</returns>
        Task CheckContainerCompletenessAsync(CancellationToken stoppingToken);

        /// <summary>
        /// Gets the completeness status for a specific container
        /// </summary>
        /// <param name="containerNumber">Container number to check</param>
        /// <param name="scannerType">Scanner type</param>
        /// <returns>Completeness status or null if not found</returns>
        Task<ContainerCompletenessStatus?> GetContainerCompletenessStatusAsync(string containerNumber, string scannerType);

        /// <summary>
        /// Updates the completeness status for a container
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <param name="scannerType">Scanner type</param>
        /// <param name="hasICUMSData">Whether ICUMS data exists</param>
        /// <param name="status">Current status</param>
        /// <param name="errorMessage">Error message if any</param>
        /// <returns>Updated completeness status</returns>
        Task<ContainerCompletenessStatus> UpdateContainerCompletenessStatusAsync(
            string containerNumber,
            string scannerType,
            bool hasICUMSData,
            string status,
            string? errorMessage = null);

        /// <summary>
        /// Gets all containers with missing ICUMS data
        /// </summary>
        /// <returns>List of containers needing ICUMS data</returns>
        Task<List<ContainerCompletenessStatus>> GetContainersWithMissingICUMSDataAsync();

        /// <summary>
        /// Gets containers that need manual BOE requests
        /// </summary>
        /// <returns>List of containers requiring manual BOE requests</returns>
        Task<List<ContainerCompletenessStatus>> GetContainersNeedingManualBOERequestsAsync();

        /// <summary>
        /// Gets pre-computed completeness data for API consumption (fast read-only access)
        /// </summary>
        /// <param name="page">Page number</param>
        /// <param name="pageSize">Page size</param>
        /// <param name="search">Search term</param>
        /// <param name="scannerType">Filter by scanner type</param>
        /// <param name="status">Filter by status</param>
        /// <returns>Paged result with pre-computed completeness data</returns>
        Task<(List<ContainerCompletenessStatus> Data, int TotalCount)> GetPreComputedCompletenessDataAsync(
            int page = 1,
            int pageSize = 50,
            string? search = null,
            string? scannerType = null,
            string? status = null);
    }
}
