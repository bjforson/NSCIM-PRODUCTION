namespace NickScanCentralImagingPortal.Core.Models
{
    public enum ClearanceType
    {
        CMR,    // No BOE data required - needs Rotation Number, Container Number, BL/House BL
        IMEX    // BOE data required - needs BOE Number, Container Number, BL/House BL
    }

    public enum ValidationStatus
    {
        Pending,            // Waiting for validation
        InReview,           // Currently being reviewed
        Validated,          // Passed validation but not yet approved
        Approved,           // Approved for ICUMS submission
        Rejected,           // Rejected with reason
        PendingSubmission,  // Audit done, awaiting ICUMS submission
        Submitted           // Successfully submitted to ICUMS
    }

    public class ContainerValidationModel
    {
        // Identity
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string? AlternativeContainerNumber { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public ClearanceType? ClearanceType { get; set; } // ✅ FIX: Nullable - only set when BOEDocument exists
        public DateTime ScanDateTime { get; set; }

        // Validation Status
        public ValidationStatus Status { get; set; }
        public int DataCompletenessPercentage { get; set; }
        public bool IsReadyForSubmission { get; set; }
        public List<ValidationError> ValidationErrors { get; set; } = new();

        // Data Sources Completeness
        public ScannerDataCompleteness ScannerCompleteness { get; set; } = new();
        public ICUMSDataCompleteness ICUMSCompleteness { get; set; } = new();
        public ImageDataCompleteness ImageCompleteness { get; set; } = new();

        // Business Rules
        public BusinessRuleValidationResult BusinessRules { get; set; } = new();

        // Audit Trail
        public DateTime CreatedAt { get; set; }
        public DateTime? ValidatedAt { get; set; }
        public string? ValidatedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? RejectedAt { get; set; }
        public string? RejectedBy { get; set; }
        public string? RejectionReason { get; set; }
    }

    public class ValidationError
    {
        public string Field { get; set; } = string.Empty;
        public string ErrorMessage { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty; // Error, Warning, Info
    }

    public class ScannerDataCompleteness
    {
        public bool HasScannerData { get; set; }
        public bool IsDataComplete { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanDateTime { get; set; }
        public int CompletenessScore { get; set; }
        public List<string> MissingFields { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();
    }

    public class ICUMSDataCompleteness
    {
        // Common fields
        public bool HasContainerNumber { get; set; }
        public bool HasBLNumber { get; set; }
        public bool HasHouseBL { get; set; }

        // CMR specific fields
        public bool HasRotationNumber { get; set; }

        // IM/EX specific fields
        public bool HasBOENumber { get; set; }

        // Calculated properties
        public ClearanceType RequiredClearanceType { get; set; }
        public int CompletenessScore { get; set; }
        public List<string> MissingFields { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();
        public bool IsCompleteForClearanceType { get; set; }
    }

    public class ImageDataCompleteness
    {
        public bool HasImage { get; set; }
        public bool IsImageValid { get; set; }
        public string ImagePath { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string ImageFormat { get; set; } = string.Empty;
        public int CompletenessScore { get; set; }
        public List<string> ValidationErrors { get; set; } = new();
        public ContainerImageQualityMetrics QualityMetrics { get; set; } = new();
    }

    // ImageQualityMetrics is defined in ImageProcessingModels.cs

    /// <summary>
    /// Image quality metrics for container validation
    /// </summary>
    public class ContainerImageQualityMetrics
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public double AspectRatio { get; set; }
        public string QualityRating { get; set; } = string.Empty; // Poor, Fair, Good, Excellent
        public List<string> QualityIssues { get; set; } = new();
        public List<string> QualityStrengths { get; set; } = new();
    }

    /// <summary>
    /// Helper class for combining scanner data from different sources
    /// </summary>
    public class ScannerContainer
    {
        public Guid Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanDateTime { get; set; }
    }

    public class BusinessRuleValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> FailedRules { get; set; } = new();
        public List<string> PassedRules { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public int Score { get; set; }
    }

    public class ValidationSummaryStats
    {
        public int TotalContainers { get; set; }
        public int PendingValidation { get; set; }
        public int InReview { get; set; }
        public int Validated { get; set; }
        public int ValidationErrors { get; set; }
        public int Approved { get; set; }
        public int Rejected { get; set; }
        public int Submitted { get; set; }

        // Clearance type breakdown
        public int CMRContainers { get; set; }
        public int IMEXContainers { get; set; }

        // Scanner type breakdown
        public int FS6000Containers { get; set; }
        public int ASEContainers { get; set; }
    }

    public class ContainerValidationDetails
    {
        public int ContainerId { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public ClearanceType ClearanceType { get; set; }
        public ValidationStatus ValidationStatus { get; set; }
        public ScannerDataInfo ScannerData { get; set; } = new();
        public ICUMSDataInfo ICUMSData { get; set; } = new();
        public BusinessRuleValidationResult BusinessRules { get; set; } = new();
        public List<ValidationError> ValidationErrors { get; set; } = new();
        public List<AnnotationData> Annotations { get; set; } = new();
    }

    public class ScannerDataInfo
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }

    public class ICUMSDataInfo
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public ClearanceType ClearanceType { get; set; }
        public string? DeclarationNumber { get; set; }
        public string? BOENumber { get; set; }
        public string? RotationNumber { get; set; }
        public string? BLNumber { get; set; }
        public string? HouseBL { get; set; }
        public Dictionary<string, object> FieldData { get; set; } = new();
        public DateTime? LastUpdated { get; set; }
    }

    public class BusinessRuleValidation
    {
        public bool IsValid { get; set; }
        public List<string> FailedRules { get; set; } = new();
        public List<string> PassedRules { get; set; } = new();
    }

    public class AnnotationData
    {
        public long Id { get; set; }
        public string Type { get; set; } = string.Empty; // Rectangle, Circle, Arrow, Text
        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }
        public string Color { get; set; } = string.Empty;
        public int Width { get; set; }
        public string? Text { get; set; }
        public string? Comment { get; set; }
        public DateTime CreatedAt { get; set; }
        public string CreatedBy { get; set; } = string.Empty;
    }

    public class PagedResult<T>
    {
        public List<T> Data { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }

    public class ValidationFilter
    {
        public ClearanceType? ClearanceType { get; set; }
        public ValidationStatus? Status { get; set; }
        public string? ScannerType { get; set; }
        public string? SearchTerm { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int? MinCompletenessScore { get; set; }
        public int? MaxCompletenessScore { get; set; }
    }

    public class PaginationOptions
    {
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 10;
        public string? SortBy { get; set; }
        public bool SortDescending { get; set; } = true;
    }
}
