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
        /// Resolve the logical container/group to the physical source scan identity.
        /// Optional because the Phase 2A API endpoints may not be deployed yet.
        /// </summary>
        Task<ScanAssetResolution?> ResolveScanAssetAsync(
            string containerNumber,
            string? groupIdentifier = null,
            int? analysisRecordId = null,
            Guid? splitJobId = null);

        /// <summary>
        /// Get scanner data while preserving resolver metadata and source-scan query hints.
        /// Falls back to the legacy container route when resolver-backed routes are absent.
        /// </summary>
        Task<PagedResult<ScannerDataRecord>?> GetScannerDataForResolvedScanAsync(
            string containerNumber,
            string? groupIdentifier = null,
            int page = 1,
            int pageSize = 50,
            ScanAssetResolution? resolution = null);

        /// <summary>
        /// Get full scanner data record (cached for 5 minutes)
        /// </summary>
        Task<FullScannerDataRecord?> GetFullScannerDataAsync(string containerNumber);

        /// <summary>
        /// Get paginated ICUMS data for a container (cached for 2 minutes)
        /// </summary>
        Task<PagedResult<ICUMSDataRecord>?> GetICUMSDataAsync(string containerNumber, int page = 1, int pageSize = 50);

        /// <summary>
        /// Get ICUMS data for the image-analysis workflow while preserving
        /// consolidated, declaration-keyed, and record-backed lookup semantics.
        /// </summary>
        Task<PagedResult<ICUMSDataRecord>?> GetImageAnalysisICUMSDataAsync(ImageAnalysisIcumsQuery query);

        /// <summary>
        /// Get full BOE data record (cached for 5 minutes)
        /// </summary>
        Task<FullBOEDataRecord?> GetFullBOEDataAsync(string containerNumber);

        /// <summary>
        /// Get image metadata for a container (cached for 5 minutes)
        /// </summary>
        Task<List<ImageMetadata>?> GetImageMetadataAsync(string containerNumber);

        /// <summary>
        /// Get image metadata while preserving resolver metadata and source-scan query hints.
        /// Falls back to the legacy container route when resolver-backed routes are absent.
        /// </summary>
        Task<List<ImageMetadata>?> GetImageMetadataForResolvedScanAsync(
            string containerNumber,
            string? groupIdentifier = null,
            ScanAssetResolution? resolution = null);

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

    public sealed class ImageAnalysisIcumsQuery
    {
        public string GroupIdentifier { get; set; } = string.Empty;
        public bool IsConsolidated { get; set; }
        public string? DeclarationNumber { get; set; }
        public string? RouteContainer { get; set; }
        public int? RecordCompletenessStatusId { get; set; }
        public string? RecordKey { get; set; }
        public bool PreferRecordBacked { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 1000;
    }
}

