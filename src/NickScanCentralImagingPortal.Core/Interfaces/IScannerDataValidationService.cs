using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for validating scanner data completeness and quality
    /// </summary>
    public interface IScannerDataValidationService
    {
        /// <summary>
        /// Validates scanner data for a specific container
        /// </summary>
        /// <param name="containerNumber">Container number to validate</param>
        /// <returns>Scanner data completeness report</returns>
        Task<ScannerDataCompleteness> ValidateScannerDataAsync(string containerNumber);

        /// <summary>
        /// Gets all containers with scanner data
        /// </summary>
        /// <returns>List of containers with scanner data</returns>
        Task<List<ScannerContainer>> GetContainersWithScannerDataAsync();

        /// <summary>
        /// Gets scanner data for a specific container
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <returns>Scanner container data</returns>
        Task<ScannerContainer?> GetScannerDataAsync(string containerNumber);

        /// <summary>
        /// Validates scanner data quality
        /// </summary>
        /// <param name="scannerData">Scanner data to validate</param>
        /// <returns>Validation result</returns>
        Task<ScannerDataQualityResult> ValidateScannerDataQualityAsync(ScannerContainer scannerData);

        /// <summary>
        /// Gets scanner statistics
        /// </summary>
        /// <returns>Scanner statistics</returns>
        Task<ScannerValidationStatistics> GetScannerStatisticsAsync();
    }

    public class ScannerDataQualityResult
    {
        public bool IsValid { get; set; }
        public int QualityScore { get; set; }
        public List<string> QualityIssues { get; set; } = new();
        public List<string> QualityStrengths { get; set; } = new();
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanDateTime { get; set; }
        public Dictionary<string, object> AdditionalMetrics { get; set; } = new();
    }

    public class ScannerValidationStatistics
    {
        public int TotalScans { get; set; }
        public int FS6000Scans { get; set; }
        public int ASEScans { get; set; }
        public int ValidScans { get; set; }
        public int InvalidScans { get; set; }
        public DateTime LastScanDate { get; set; }
        public Dictionary<string, int> ScansByDay { get; set; } = new();
        public Dictionary<string, int> ScansByScannerType { get; set; } = new();
    }
}
