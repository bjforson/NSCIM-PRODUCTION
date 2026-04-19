using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Represents a proposed fix for an error investigation
    /// Can contain both code changes and configuration updates
    /// </summary>
    [Table("FixProposals")]
    public class FixProposal
    {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// Foreign key to ErrorInvestigation
        /// </summary>
        [Required]
        public long ErrorInvestigationId { get; set; }

        /// <summary>
        /// Type of fix: CodeChange, ConfigurationUpdate, or Both
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string FixType { get; set; } = "CodeChange"; // CodeChange, ConfigurationUpdate, Both

        /// <summary>
        /// Title/summary of the proposed fix
        /// </summary>
        [Required]
        [MaxLength(500)]
        public string Title { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of what the fix does and why
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Rationale for why this fix should work
        /// </summary>
        public string? Rationale { get; set; }

        /// <summary>
        /// Impact assessment (what will be affected)
        /// </summary>
        public string? ImpactAssessment { get; set; }

        /// <summary>
        /// Code changes in JSON format
        /// Structure: { "file": "path/to/file.cs", "changes": [{ "line": 123, "old": "...", "new": "..." }] }
        /// </summary>
        public string? CodeChanges { get; set; }

        /// <summary>
        /// Configuration changes in JSON format
        /// Structure: { "file": "appsettings.json", "section": "ICUMS", "key": "Timeout", "oldValue": "30", "newValue": "60" }
        /// </summary>
        public string? ConfigurationChanges { get; set; }

        /// <summary>
        /// Files that will be modified
        /// </summary>
        public string? AffectedFiles { get; set; } // JSON array of file paths

        /// <summary>
        /// Estimated risk level (Low, Medium, High)
        /// </summary>
        [MaxLength(20)]
        public string RiskLevel { get; set; } = "Medium";

        /// <summary>
        /// Status: Proposed, Approved, Rejected, Implemented
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Status { get; set; } = "Proposed";

        /// <summary>
        /// Username who approved/rejected
        /// </summary>
        [MaxLength(100)]
        public string? ApprovedBy { get; set; }

        /// <summary>
        /// When approved/rejected
        /// </summary>
        public DateTime? ApprovedAt { get; set; }

        /// <summary>
        /// Approval/rejection notes
        /// </summary>
        public string? ApprovalNotes { get; set; }

        /// <summary>
        /// When the fix was implemented
        /// </summary>
        public DateTime? ImplementedAt { get; set; }

        /// <summary>
        /// Git branch name where fix was implemented
        /// </summary>
        [MaxLength(200)]
        public string? BranchName { get; set; }

        /// <summary>
        /// Git commit hash
        /// </summary>
        [MaxLength(100)]
        public string? CommitHash { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        [ForeignKey("ErrorInvestigationId")]
        public virtual ErrorInvestigation? ErrorInvestigation { get; set; }
    }
}

