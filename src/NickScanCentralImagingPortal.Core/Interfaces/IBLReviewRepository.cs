using NickScanCentralImagingPortal.Core.DTOs.BLReview;
using NickScanCentralImagingPortal.Core.Entities.Review;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Repository for BL (Bill of Lading) review operations
    /// </summary>
    public interface IBLReviewRepository
    {
        /// <summary>
        /// Get BL groups with completeness filtering
        /// Only returns BLs where containers have scanner + ICUMS + images
        /// </summary>
        Task<List<BLGroupDto>> GetBLGroupsAsync(string? status = null, int page = 1, int pageSize = 20);

        /// <summary>
        /// Get detailed information for a specific BL
        /// </summary>
        Task<BLDetailsDto?> GetBLDetailsAsync(string masterBlNumber);

        /// <summary>
        /// Save or update a BL review (supports partial reviews)
        /// </summary>
        Task<BLReviewRecord> SaveReviewAsync(BLReviewSubmission submission);

        /// <summary>
        /// Get review history for a BL
        /// </summary>
        Task<List<BLReviewRecord>> GetReviewHistoryAsync(string masterBlNumber);

        /// <summary>
        /// Get statistics for dashboard
        /// </summary>
        Task<BLReviewStatistics> GetStatisticsAsync();

        /// <summary>
        /// Check if a container is complete (has scanner + ICUMS + images)
        /// </summary>
        Task<bool> IsContainerCompleteAsync(string containerNumber);
    }

    public class BLReviewStatistics
    {
        public int TotalBLs { get; set; }
        public int PendingBLs { get; set; }
        public int InProgressBLs { get; set; }
        public int CompletedBLs { get; set; }
        public int TotalContainers { get; set; }
        public int CompleteContainers { get; set; }
    }
}

