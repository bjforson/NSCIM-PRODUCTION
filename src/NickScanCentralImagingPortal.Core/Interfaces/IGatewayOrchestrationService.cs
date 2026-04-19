using NickScanCentralImagingPortal.Core.Models.Gateway;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Gateway orchestration service - aggregates data from multiple sources
    /// </summary>
    public interface IGatewayOrchestrationService
    {
        /// <summary>
        /// Get complete container data from all sources
        /// </summary>
        /// <param name="containerNumber">Container identifier</param>
        /// <param name="options">Options controlling what data to include</param>
        /// <returns>Unified response with all requested data</returns>
        Task<ContainerCompleteResponse> GetContainerCompleteAsync(
            string containerNumber,
            GatewayRequestOptions options);

        /// <summary>
        /// Admin: Clear placeholder images from cache
        /// </summary>
        /// <param name="minSizeBytes">Minimum size - images smaller are considered placeholders</param>
        /// <returns>Number of cache entries deleted</returns>
        Task<int> ClearPlaceholderCacheAsync(int minSizeBytes);

        /// <summary>
        /// Admin: Get cache statistics
        /// </summary>
        Task<object> GetCacheStatsAsync();
    }
}

