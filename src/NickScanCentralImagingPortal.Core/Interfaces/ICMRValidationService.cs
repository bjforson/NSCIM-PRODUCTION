using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for validating CMR (Export) records to ensure they have required composite key fields
    /// </summary>
    public interface ICMRValidationService
    {
        /// <summary>
        /// Validates a CMR record has all required fields for composite key
        /// </summary>
        /// <param name="boeDocument">The BOE document to validate</param>
        /// <returns>Validation result with details</returns>
        Task<CMRValidationResult> ValidateCMRRecordAsync(BOEDocument boeDocument);

        /// <summary>
        /// Validates multiple CMR records in batch
        /// </summary>
        /// <param name="boeDocuments">List of BOE documents to validate</param>
        /// <returns>Batch validation results</returns>
        Task<CMRBatchValidationResult> ValidateCMRBatchAsync(List<BOEDocument> boeDocuments);

        /// <summary>
        /// Gets all CMR records that are missing required fields
        /// </summary>
        /// <returns>List of problematic CMR records</returns>
        Task<List<ProblematicCMRRecord>> GetProblematicCMRRecordsAsync();

        /// <summary>
        /// Gets statistics about CMR validation issues
        /// </summary>
        /// <returns>CMR validation statistics</returns>
        Task<CMRValidationStatistics> GetCMRValidationStatisticsAsync();
    }

    /// <summary>
    /// Result of CMR record validation
    /// </summary>
    public class CMRValidationResult
    {
        public bool IsValid { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ClearanceType { get; set; } = string.Empty;
        public List<string> MissingFields { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public string ValidationMessage { get; set; } = string.Empty;
        public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Result of batch CMR validation
    /// </summary>
    public class CMRBatchValidationResult
    {
        public int TotalRecords { get; set; }
        public int ValidRecords { get; set; }
        public int InvalidRecords { get; set; }
        public List<CMRValidationResult> ValidationResults { get; set; } = new();
        public List<string> SummaryWarnings { get; set; } = new();
        public DateTime ValidatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Problematic CMR record that needs attention
    /// </summary>
    public class ProblematicCMRRecord
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string DeclarationNumber { get; set; } = string.Empty;
        public string ClearanceType { get; set; } = string.Empty;
        public string? BlNumber { get; set; }
        public string? RotationNumber { get; set; }
        public List<string> MissingFields { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime LastUpdatedAt { get; set; }
        public bool NeedsRedownload { get; set; }
    }

    /// <summary>
    /// Statistics about CMR validation
    /// </summary>
    public class CMRValidationStatistics
    {
        public int TotalCMRRecords { get; set; }
        public int ValidCMRRecords { get; set; }
        public int InvalidCMRRecords { get; set; }
        public int MissingBlNumber { get; set; }
        public int MissingRotationNumber { get; set; }
        public int MissingBothFields { get; set; }
        public double ValidationSuccessRate { get; set; }
        public DateTime LastValidationRun { get; set; }
    }
}
