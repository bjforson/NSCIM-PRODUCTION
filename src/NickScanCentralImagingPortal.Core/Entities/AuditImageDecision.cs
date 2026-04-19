using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Per-image audit verdict, child of <see cref="AuditDecision"/>.
    ///
    /// Background (deferred plan, request 1): Image analysis records N decisions
    /// per container — one per image — but audit historically recorded only ONE
    /// Approve/Reject per container. This child entity gives audit the same
    /// granularity as analysis: an auditor can mark each image individually,
    /// and the parent <see cref="AuditDecision"/> row keeps the rolled-up
    /// verdict (any child Rejected → parent Rejected).
    ///
    /// Why a child table instead of widening AuditDecision: SubmissionWorker
    /// and ImageAnalysisDashboardController both treat AuditDecision as one row
    /// per container today. Keeping the parent row + the rollup unchanged means
    /// downstream code stays untouched while the new per-image data lands as
    /// children.
    ///
    /// Backfill policy: legacy AuditDecision rows have no child rows. Queries
    /// that want per-image data should LEFT JOIN and treat NULL children as
    /// "auditor pre-dates per-image audit; only the rollup is available."
    /// </summary>
    [Table("AuditImageDecisions")]
    public class AuditImageDecision
    {
        [Key]
        public int Id { get; set; }

        /// <summary>FK to the parent <see cref="AuditDecision"/>.</summary>
        public int AuditDecisionId { get; set; }

        [ForeignKey(nameof(AuditDecisionId))]
        public AuditDecision? AuditDecision { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        /// <summary>
        /// Scanner type for the image being audited (e.g. "FS6000-Main",
        /// "FS6000-Side", "ASE"). Same scheme as ImageAnalysisDecision.ScannerType
        /// so the two can be joined when assembling the audit-vs-analysis view.
        /// </summary>
        [Required]
        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        /// <summary>
        /// 0-based ordering of the image within the container's image set.
        /// Mirrors the ImageAnalysisDecision ordering used by the analyst UI so
        /// "first image the analyst saw" maps to "first image the auditor reviewed."
        /// </summary>
        public int ImageIndex { get; set; }

        /// <summary>
        /// "Approved" or "Rejected". Independent of the analyst's call — auditors
        /// make their own per-image judgement, they're not constrained to agree
        /// with the analyst on every image. Rollup logic on the parent
        /// <see cref="AuditDecision"/> treats any Rejected child as a Rejected
        /// container.
        /// </summary>
        [Required]
        [StringLength(20)]
        public string Decision { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Notes { get; set; }

        [Required]
        [StringLength(100)]
        public string AuditedBy { get; set; } = string.Empty;

        public DateTime AuditedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
