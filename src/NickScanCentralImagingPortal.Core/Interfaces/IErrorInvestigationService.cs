namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for investigating errors and proposing fixes
    /// </summary>
    public interface IErrorInvestigationService
    {
        /// <summary>
        /// Process a group of similar errors and create/update investigation
        /// </summary>
        Task ProcessErrorGroupAsync(NickScanCentralImagingPortal.Core.Models.ErrorGroupDto errorGroup, CancellationToken cancellationToken);

        /// <summary>
        /// Perform deep investigation on an error investigation
        /// </summary>
        Task InvestigateErrorAsync(long investigationId, CancellationToken cancellationToken);

        /// <summary>
        /// Generate fix proposals for an investigation
        /// </summary>
        Task GenerateFixProposalsAsync(long investigationId, CancellationToken cancellationToken);

        /// <summary>
        /// Get investigation details with all related data
        /// </summary>
        Task<ErrorInvestigationDto?> GetInvestigationAsync(long investigationId, CancellationToken cancellationToken);

        /// <summary>
        /// Get all investigations with filtering
        /// </summary>
        Task<List<ErrorInvestigationDto>> GetInvestigationsAsync(
            string? status = null,
            string? priority = null,
            string? search = null,
            int page = 1,
            int pageSize = 50,
            CancellationToken cancellationToken = default);

        /// <summary>
        /// Approve a fix proposal
        /// </summary>
        Task<ApprovalResult> ApproveFixProposalAsync(long investigationId, long proposalId, string username, string? notes);

        /// <summary>
        /// Reject a fix proposal
        /// </summary>
        Task<RejectionResult> RejectFixProposalAsync(long investigationId, long proposalId, string username, string reason);

        /// <summary>
        /// Ignore an investigation (mark as ignored)
        /// </summary>
        Task<IgnoreResult> IgnoreInvestigationAsync(long investigationId, string username, string? reason);
    }

    public class ApprovalResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string? BranchName { get; set; }
    }

    public class RejectionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class IgnoreResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
    }

    /// <summary>
    /// DTO for error investigation
    /// </summary>
    public class ErrorInvestigationDto
    {
        public long Id { get; set; }
        public string InvestigationGroupId { get; set; } = string.Empty;
        public string ErrorPattern { get; set; } = string.Empty;
        public string? ErrorCode { get; set; }
        public string? ServiceId { get; set; }
        public string? Operation { get; set; }
        public string? ExceptionType { get; set; }
        public int OccurrenceCount { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public string Status { get; set; } = string.Empty;
        public string Priority { get; set; } = string.Empty;
        public string? InvestigationSummary { get; set; }
        public string? InvestigationDetails { get; set; }
        public string? SampleErrorMessage { get; set; }
        public string? SampleStackTrace { get; set; }
        public bool HasProposedFix { get; set; }
        public List<FixProposalDto> FixProposals { get; set; } = new();
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// DTO for fix proposal
    /// </summary>
    public class FixProposalDto
    {
        public long Id { get; set; }
        public long ErrorInvestigationId { get; set; }
        public string FixType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? Rationale { get; set; }
        public string? ImpactAssessment { get; set; }
        public string? CodeChanges { get; set; }
        public string? ConfigurationChanges { get; set; }
        public string? AffectedFiles { get; set; }
        public string RiskLevel { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }
}

