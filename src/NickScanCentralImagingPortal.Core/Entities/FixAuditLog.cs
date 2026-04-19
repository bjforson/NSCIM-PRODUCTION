using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Audit log for all actions taken on error investigations and fixes
    /// Provides complete audit trail for compliance and debugging
    /// </summary>
    [Table("FixAuditLogs")]
    public class FixAuditLog
    {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// Foreign key to ErrorInvestigation
        /// </summary>
        public long? ErrorInvestigationId { get; set; }

        /// <summary>
        /// Foreign key to FixProposal (if action was on a specific proposal)
        /// </summary>
        public long? FixProposalId { get; set; }

        /// <summary>
        /// Action type: InvestigationCreated, FixProposed, FixApproved, FixRejected, FixImplemented, FixVerified, InvestigationIgnored
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string ActionType { get; set; } = string.Empty;

        /// <summary>
        /// Username who performed the action (or "System" for automated actions)
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string PerformedBy { get; set; } = "System";

        /// <summary>
        /// Description of what was done
        /// </summary>
        [Required]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Additional details in JSON format
        /// </summary>
        public string? Details { get; set; }

        /// <summary>
        /// IP address of the user (if applicable)
        /// </summary>
        [MaxLength(50)]
        public string? IpAddress { get; set; }

        /// <summary>
        /// User agent/browser info (if applicable)
        /// </summary>
        [MaxLength(500)]
        public string? UserAgent { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("ErrorInvestigationId")]
        public virtual ErrorInvestigation? ErrorInvestigation { get; set; }

        [ForeignKey("FixProposalId")]
        public virtual FixProposal? FixProposal { get; set; }
    }
}

