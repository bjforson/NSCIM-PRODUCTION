using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Models.Gateway;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for aggregating dashboard statistics from multiple data sources
    /// </summary>
    public interface IDashboardStatsService
    {
        /// <summary>
        /// Get comprehensive dashboard statistics
        /// </summary>
        /// <param name="includeContainers">Include container statistics</param>
        /// <param name="includeScanners">Include scanner statistics</param>
        /// <param name="includeICUMS">Include ICUMS statistics</param>
        /// <param name="includeValidation">Include validation statistics</param>
        /// <param name="includeImages">Include image processing statistics</param>
        /// <param name="includeTrends">Include trend data</param>
        /// <returns>Dashboard statistics</returns>
        Task<DashboardStats> GetDashboardStatsAsync(
            bool includeContainers = true,
            bool includeScanners = true,
            bool includeICUMS = true,
            bool includeValidation = true,
            bool includeImages = true,
            bool includeTrends = true);
    }
}

