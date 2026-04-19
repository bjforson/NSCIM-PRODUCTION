using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Shared repository for common container data queries across multiple services
    /// Eliminates duplication and provides centralized caching
    /// </summary>
    public interface IContainerDataRepository
    {
        /// <summary>
        /// Gets all scanner containers from all scanner types (FS6000, ASE)
        /// </summary>
        /// <param name="useCache">Whether to use cached data (default: true)</param>
        /// <returns>List of all scanner containers</returns>
        Task<List<ScannerContainerData>> GetAllScannerContainersAsync(bool useCache = true);

        /// <summary>
        /// Gets all container numbers that have ICUMS/BOE data
        /// </summary>
        /// <param name="useCache">Whether to use cached data (default: true)</param>
        /// <returns>Set of container numbers with ICUMS data</returns>
        Task<HashSet<string>> GetContainersWithICUMSDataAsync(bool useCache = true);

        /// <summary>
        /// Finds containers from the provided list that don't have ICUMS data
        /// </summary>
        /// <param name="containerNumbers">Container numbers to check</param>
        /// <returns>List of containers missing ICUMS data</returns>
        Task<List<string>> FindMissingICUMSContainersAsync(List<string> containerNumbers);

        /// <summary>
        /// Checks if both application and ICUMS databases are accessible
        /// </summary>
        /// <returns>True if both databases can be connected to</returns>
        Task<bool> CanConnectToDatabasesAsync();

        /// <summary>
        /// Gets scanner containers for a specific scanner type
        /// </summary>
        /// <param name="scannerType">Scanner type (FS6000, ASE, etc.)</param>
        /// <param name="useCache">Whether to use cached data</param>
        /// <returns>List of containers from specified scanner</returns>
        Task<List<ScannerContainerData>> GetScannerContainersByScannerTypeAsync(string scannerType, bool useCache = true);

        /// <summary>
        /// Clears all cached data (use after major data changes)
        /// </summary>
        void ClearCache();
    }
}

