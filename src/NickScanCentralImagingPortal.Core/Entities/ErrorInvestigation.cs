using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Represents an error investigation created by the AI error monitoring system
    /// Groups similar errors together for batch approval and fixing
    /// </summary>
    [Table("ErrorInvestigations")]
    public class ErrorInvestigation
    {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// Unique identifier for this investigation group (groups similar errors)
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string InvestigationGroupId { get; set; } = string.Empty;

        /// <summary>
        /// Error pattern/fingerprint used to group similar errors (stored as TEXT in PostgreSQL)
        /// </summary>
        [Required]
        public string ErrorPattern { get; set; } = string.Empty;

        /// <summary>
        /// Error code (e.g., ERR_1000, ERR_5001)
        /// </summary>
        [MaxLength(50)]
        public string? ErrorCode { get; set; }

        /// <summary>
        /// Service/component where error occurred
        /// </summary>
        [MaxLength(200)]
        public string? ServiceId { get; set; }

        /// <summary>
        /// Operation/endpoint where error occurred
        /// </summary>
        [MaxLength(200)]
        public string? Operation { get; set; }

        /// <summary>
        /// Exception type (e.g., NullReferenceException, DbUpdateException)
        /// </summary>
        [MaxLength(200)]
        public string? ExceptionType { get; set; }

        /// <summary>
        /// Number of times this error has occurred
        /// </summary>
        public int OccurrenceCount { get; set; } = 1;

        /// <summary>
        /// First time this error was seen
        /// </summary>
        public DateTime FirstSeen { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Most recent occurrence
        /// </summary>
        public DateTime LastSeen { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Status of investigation
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "New"; // New, Investigating, Proposed, Approved, Rejected, Fixed, Ignored

        /// <summary>
        /// Priority level (Critical, High, Medium, Low)
        /// </summary>
        [MaxLength(20)]
        public string Priority { get; set; } = "Medium";

        /// <summary>
        /// AI-generated investigation summary
        /// </summary>
        public string? InvestigationSummary { get; set; }

        /// <summary>
        /// Detailed investigation findings (JSON format)
        /// Includes: root cause analysis, related code files, similar past errors, configuration issues
        /// </summary>
        public string? InvestigationDetails { get; set; }

        /// <summary>
        /// Related log entry IDs (comma-separated or JSON array)
        /// </summary>
        public string? RelatedLogIds { get; set; }

        /// <summary>
        /// Sample error message from one of the occurrences
        /// </summary>
        public string? SampleErrorMessage { get; set; }

        /// <summary>
        /// Sample stack trace from one of the occurrences
        /// </summary>
        public string? SampleStackTrace { get; set; }

        /// <summary>
        /// Whether AI has proposed a fix
        /// </summary>
        public bool HasProposedFix { get; set; } = false;

        /// <summary>
        /// Username who approved/rejected the fix
        /// </summary>
        [MaxLength(100)]
        public string? ApprovedBy { get; set; }

        /// <summary>
        /// When the fix was approved/rejected
        /// </summary>
        public DateTime? ApprovedAt { get; set; }

        /// <summary>
        /// Approval notes/comments
        /// </summary>
        public string? ApprovalNotes { get; set; }

        /// <summary>
        /// Git branch name where fix was implemented
        /// </summary>
        [MaxLength(200)]
        public string? FixBranchName { get; set; }

        /// <summary>
        /// When the fix was implemented
        /// </summary>
        public DateTime? FixedAt { get; set; }

        /// <summary>
        /// Whether the fix has been verified
        /// </summary>
        public bool IsVerified { get; set; } = false;

        /// <summary>
        /// When the fix was verified
        /// </summary>
        public DateTime? VerifiedAt { get; set; }

        /// <summary>
        /// Username who verified the fix
        /// </summary>
        [MaxLength(100)]
        public string? VerifiedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<FixProposal> FixProposals { get; set; } = new List<FixProposal>();
        public virtual ICollection<FixAuditLog> AuditLogs { get; set; } = new List<FixAuditLog>();
    }
}

