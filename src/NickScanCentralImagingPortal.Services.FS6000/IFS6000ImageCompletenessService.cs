using NickScanCentralImagingPortal.Core.Entities.FS6000;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public interface IFS6000ImageCompletenessService
    {
        /// <summary>
        /// Validates that a scan has associated images
        /// </summary>
        Task<bool> ValidateScanHasImageAsync(Guid scanId);

        /// <summary>
        /// Gets scans that are missing images
        /// </summary>
        Task<List<FS6000Scan>> GetScansWithoutImagesAsync(int limit = 100);

        /// <summary>
        /// Gets image completeness statistics
        /// </summary>
        Task<FS6000ImageCompletenessStats> GetImageCompletenessStatsAsync();

        /// <summary>
        /// Updates image completeness flags for a scan
        /// </summary>
        Task UpdateScanImageCompletenessAsync(Guid scanId);

        /// <summary>
        /// Backfills missing images for scans that don't have them
        /// </summary>
        Task<int> BackfillMissingImagesAsync(DateTime? fromDate = null, DateTime? toDate = null);

        /// <summary>
        /// Validates all scans and updates their image completeness status
        /// </summary>
        Task<int> ValidateAllScansImageCompletenessAsync();
    }

    public class FS6000ImageCompletenessStats
    {
        public int TotalScans { get; set; }
        public int ScansWithImages { get; set; }
        public int ScansWithoutImages { get; set; }
        public int TotalImages { get; set; }
        public double CompletenessPercentage { get; set; }
        public DateTime? OldestScanWithoutImage { get; set; }
        public DateTime? NewestScanWithoutImage { get; set; }
        public Dictionary<string, int> ImageCountDistribution { get; set; } = new();
    }
}
