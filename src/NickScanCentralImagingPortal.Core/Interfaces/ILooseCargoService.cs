using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service interface for loose cargo business logic
    /// </summary>
    public interface ILooseCargoService
    {
        /// <summary>
        /// Search loose cargo records with filters and pagination
        /// </summary>
        Task<LooseCargoSearchResponse> SearchAsync(LooseCargoSearchRequest request);

        /// <summary>
        /// Get loose cargo statistics
        /// </summary>
        Task<LooseCargoStatistics> GetStatisticsAsync();

        /// <summary>
        /// Get loose cargo detail by ID (with manifest items)
        /// </summary>
        Task<LooseCargoDetailDto?> GetDetailAsync(int id);

        /// <summary>
        /// Get loose cargo detail by declaration number
        /// </summary>
        Task<LooseCargoDetailDto?> GetDetailByDeclarationNumberAsync(string declarationNumber);

        /// <summary>
        /// Get recent loose cargo records
        /// </summary>
        Task<List<BOEDocument>> GetRecentRecordsAsync(int days = 7);

        /// <summary>
        /// Validate loose cargo record
        /// </summary>
        Task<(bool isValid, List<string> errors)> ValidateRecordAsync(BOEDocument document);
    }
}

