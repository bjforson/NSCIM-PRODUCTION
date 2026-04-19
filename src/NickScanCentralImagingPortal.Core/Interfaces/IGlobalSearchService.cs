using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Models.Gateway;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for global search across all data sources
    /// </summary>
    public interface IGlobalSearchService
    {
        /// <summary>
        /// Search across all data sources
        /// </summary>
        /// <param name="request">Search request with query and filters</param>
        /// <returns>Search results from all sources</returns>
        Task<GlobalSearchResponse> SearchAsync(GlobalSearchRequest request);
    }
}

