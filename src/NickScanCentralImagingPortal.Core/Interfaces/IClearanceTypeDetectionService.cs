using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for detecting and determining clearance types (CMR vs IM/EX) based on ICUMS data
    /// </summary>
    public interface IClearanceTypeDetectionService
    {
        /// <summary>
        /// Detects clearance type based on ICUMS data for a container
        /// </summary>
        /// <param name="containerNumber">Container number to check</param>
        /// <returns>Detected clearance type</returns>
        Task<ClearanceType> DetectClearanceTypeAsync(string containerNumber);

        /// <summary>
        /// Detects clearance type from BOE document data
        /// </summary>
        /// <param name="boeDocument">BOE document data</param>
        /// <returns>Detected clearance type</returns>
        ClearanceType DetectClearanceTypeFromBOE(BOEDocument boeDocument);

        /// <summary>
        /// Determines if a clearance type requires BOE data
        /// </summary>
        /// <param name="clearanceType">Clearance type to check</param>
        /// <returns>True if BOE data is required</returns>
        bool RequiresBOEData(ClearanceType clearanceType);

        /// <summary>
        /// Gets required fields for a specific clearance type
        /// </summary>
        /// <param name="clearanceType">Clearance type</param>
        /// <returns>List of required field names</returns>
        List<string> GetRequiredFields(ClearanceType clearanceType);

        /// <summary>
        /// Validates if all required fields are present for a clearance type
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <param name="clearanceType">Clearance type to validate against</param>
        /// <returns>Validation result with missing fields</returns>
        Task<ClearanceTypeValidationResult> ValidateRequiredFieldsAsync(string containerNumber, ClearanceType clearanceType);
    }

    public class ClearanceTypeValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> MissingFields { get; set; } = new();
        public List<string> PresentFields { get; set; } = new();
        public int CompletenessScore { get; set; }
        public string ValidationMessage { get; set; } = string.Empty;
    }
}
