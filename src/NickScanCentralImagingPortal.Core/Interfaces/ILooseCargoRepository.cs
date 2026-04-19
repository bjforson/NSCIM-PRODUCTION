using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Repository interface for loose cargo data access
    /// </summary>
    public interface ILooseCargoRepository
    {
        /// <summary>
        /// Get loose cargo records with filtering and pagination
        /// </summary>
        Task<(List<BOEDocument> records, int totalCount)> GetLooseCargoRecordsAsync(
            string? clearanceType = null,
            string? crmsLevel = null,
            string? searchTerm = null,
            int pageNumber = 1,
            int pageSize = 100,
            string? sortBy = null,
            bool sortDescending = false);

        /// <summary>
        /// Get loose cargo statistics
        /// </summary>
        Task<LooseCargoStatistics> GetStatisticsAsync();

        /// <summary>
        /// Get loose cargo record by ID
        /// </summary>
        Task<BOEDocument?> GetByIdAsync(int id);

        /// <summary>
        /// Get loose cargo record by declaration number
        /// </summary>
        Task<BOEDocument?> GetByDeclarationNumberAsync(string declarationNumber);

        /// <summary>
        /// Get manifest items for a loose cargo record
        /// </summary>
        Task<List<DownloadedManifestItem>> GetManifestItemsAsync(int boeDocumentId);

        /// <summary>
        /// Get recent loose cargo records (last N days)
        /// </summary>
        Task<List<BOEDocument>> GetRecentRecordsAsync(int days = 7);

        /// <summary>
        /// Check if declaration number exists in loose cargo
        /// </summary>
        Task<bool> ExistsByDeclarationNumberAsync(string declarationNumber);
    }
}

