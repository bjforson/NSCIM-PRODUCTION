using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Entity for storing container image annotations
    /// </summary>
    public class ContainerAnnotation
    {
        [Key]
        public long Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string Type { get; set; } = string.Empty; // Rectangle, Circle, Arrow, Text

        public double X1 { get; set; }
        public double Y1 { get; set; }
        public double X2 { get; set; }
        public double Y2 { get; set; }

        [MaxLength(20)]
        public string Color { get; set; } = "#ff0000";

        public int Width { get; set; } = 2;

        [MaxLength(1000)]
        public string? Text { get; set; }

        [MaxLength(2000)]
        public string? Comment { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(100)]
        public string CreatedBy { get; set; } = "System";

        public DateTime? UpdatedAt { get; set; }

        [MaxLength(100)]
        public string? UpdatedBy { get; set; }

        public bool IsDeleted { get; set; } = false;

        public DateTime? DeletedAt { get; set; }

        [MaxLength(100)]
        public string? DeletedBy { get; set; }

        // ── Controlled-vocabulary finding categories (Gap 1a — AI training flywheel) ──
        // Per-annotation finding type. A drawn box can be tagged with a security
        // category, a revenue category, both, or neither. Both nullable so the
        // existing freeform Text / Comment fields keep working.
        public int? ThreatCategoryId { get; set; }
        public int? RevenueAnomalyCategoryId { get; set; }

        // ── Decision linkage (Gap 2 — AI training flywheel) ──
        // Optional FK to the parent ImageAnalysisDecision. When set, this row
        // is part of the typed annotation set for that decision (the canonical
        // store going forward). When null, the row is "free-floating" — either
        // legacy data created before Gap 2 landed, or annotations drawn outside
        // a decision flow. Existing reads (drawing tools, image overlays) treat
        // both kinds the same: they query by ContainerNumber. The decision
        // linkage exists so the COCO export and any future per-decision query
        // can join cleanly without depending on a free-text JSON blob.
        public int? ImageAnalysisDecisionId { get; set; }
    }
}
