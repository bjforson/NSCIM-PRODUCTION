using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Enhanced container validation service with clearance type awareness
    /// </summary>
    public interface IContainerValidationService
    {
        // Core validation methods
        Task<PagedResult<ContainerValidationModel>> GetContainersForValidationAsync(
            ValidationFilter filter,
            PaginationOptions pagination);

        Task<ContainerValidationResult> ValidateContainerAsync(string containerNumber, ClearanceType clearanceType);
        Task<ValidationSummaryStats> GetValidationSummaryAsync();

        // Clearance type specific methods
        Task<List<ContainerValidationModel>> GetCMRContainersForValidationAsync();
        Task<List<ContainerValidationModel>> GetIMEXContainersForValidationAsync();

        // Data completeness with clearance type
        Task<ContainerCompletenessReport> GetCompletenessReportAsync(string containerNumber, ClearanceType clearanceType);
        Task<bool> IsReadyForSubmissionAsync(string containerNumber, ClearanceType clearanceType);

        // Business workflow
        Task<bool> ApproveForSubmissionAsync(string containerNumber, string approvedBy, ClearanceType clearanceType);
        Task<bool> RejectContainerAsync(string containerNumber, string rejectionReason, string rejectedBy);

        // Bulk operations
        Task<BulkValidationResult> ValidateAllPendingContainersAsync();
        Task<BulkApprovalResult> ApproveBulkContainersAsync(List<string> containerNumbers, string approvedBy);
        Task<BulkRejectionResult> RejectBulkContainersAsync(List<string> containerNumbers, string rejectionReason, string rejectedBy);
    }

    public class ContainerValidationResult
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public ClearanceType ClearanceType { get; set; }
        public ValidationStatus Status { get; set; }
        public int DataCompletenessScore { get; set; }
        public bool IsReadyForSubmission { get; set; }
        public ScannerDataCompleteness ScannerCompleteness { get; set; } = new();
        public ICUMSDataCompleteness ICUMSCompleteness { get; set; } = new();
        public ImageDataCompleteness ImageCompleteness { get; set; } = new();
        public BusinessRuleValidationResult BusinessRules { get; set; } = new();
        public List<ValidationError> ValidationErrors { get; set; } = new();
        public string ValidationMessage { get; set; } = string.Empty;
        public DateTime ValidatedAt { get; set; }
    }

    public class ContainerCompletenessReport
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public ClearanceType ClearanceType { get; set; }
        public int OverallCompletenessScore { get; set; }
        public bool IsComplete { get; set; }
        public ScannerDataCompleteness ScannerCompleteness { get; set; } = new();
        public ICUMSDataCompleteness ICUMSCompleteness { get; set; } = new();
        public ImageDataCompleteness ImageCompleteness { get; set; } = new();
        public List<string> MissingRequirements { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
        public DateTime GeneratedAt { get; set; }
    }

    public class BulkValidationResult
    {
        public int TotalProcessed { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public List<ContainerValidationResult> Results { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public DateTime ProcessedAt { get; set; }
    }

    public class BulkApprovalResult
    {
        public int TotalProcessed { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public List<string> ApprovedContainers { get; set; } = new();
        public List<string> FailedContainers { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public DateTime ProcessedAt { get; set; }
    }

    public class BulkRejectionResult
    {
        public int TotalProcessed { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public List<string> RejectedContainers { get; set; } = new();
        public List<string> FailedContainers { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public DateTime ProcessedAt { get; set; }
    }
}
