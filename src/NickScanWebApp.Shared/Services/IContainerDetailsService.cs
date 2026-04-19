using NickScanWebApp.Shared.Models;

namespace NickScanWebApp.Shared.Services
{
    /// <summary>
    /// Service for fetching and caching container details from the API
    /// </summary>
    public interface IContainerDetailsService
    {
        /// <summary>
        /// Get basic container information (cached for 5 minutes)
        /// </summary>
        Task<ContainerBasicInfo?> GetBasicInfoAsync(string containerNumber);

        /// <summary>
        /// Get full container summary (scanner + ICUMS + clearance fields) from GET /api/containerdetails/full
        /// </summary>
        Task<ContainerFullDetails?> GetFullDetailsAsync(string containerNumber);

        /// <summary>
        /// Get paginated scanner data for a container (cached for 2 minutes)
        /// </summary>
        Task<PagedResult<ScannerDataRecord>?> GetScannerDataAsync(string containerNumber, int page = 1, int pageSize = 50);

        /// <summary>
        /// Get full scanner data record (cached for 5 minutes)
        /// </summary>
        Task<FullScannerDataRecord?> GetFullScannerDataAsync(string containerNumber);

        /// <summary>
        /// Get paginated ICUMS data for a container (cached for 2 minutes)
        /// </summary>
        Task<PagedResult<ICUMSDataRecord>?> GetICUMSDataAsync(string containerNumber, int page = 1, int pageSize = 50);

        /// <summary>
        /// Get full BOE data record (cached for 5 minutes)
        /// </summary>
        Task<FullBOEDataRecord?> GetFullBOEDataAsync(string containerNumber);

        /// <summary>
        /// Get image metadata for a container (cached for 5 minutes)
        /// </summary>
        Task<List<ImageMetadata>?> GetImageMetadataAsync(string containerNumber);

        /// <summary>
        /// Get full image with tools (cached for 10 minutes)
        /// </summary>
        Task<ImageWithTools?> GetFullImageAsync(int imageId);

        /// <summary>
        /// Search container data (cached for 1 minute)
        /// </summary>
        Task<UnifiedSearchResults?> SearchContainerDataAsync(string containerNumber, string query);

        /// <summary>
        /// Clear all cached data for a specific container
        /// </summary>
        void ClearContainerCache(string containerNumber);
    }
}

