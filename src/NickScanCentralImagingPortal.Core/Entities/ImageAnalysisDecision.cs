using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Stores image analysis decisions for the Image Analysis feature
    /// Localized to Image Analysis page only
    /// </summary>
    [Table("ImageAnalysisDecisions")]
    public class ImageAnalysisDecision
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty; // "FS6000" or "ASE"

        [Required]
        [StringLength(20)]
        public string Decision { get; set; } = string.Empty; // "Normal" or "Abnormal"

        [StringLength(500)]
        public string? Comments { get; set; }

        [StringLength(500)]
        public string? Tags { get; set; } // Comma-separated tags/labels

        // ⚠ DEPRECATED 1.12.0 — use ContainerAnnotation rows linked via
        // ImageAnalysisDecisionId instead. Still written by SaveDecision and
        // still read by the legacy Blazor draw tools / image overlay components,
        // but the COCO export now prefers typed rows. Will be dropped in a
        // future release once the UI components are switched to query
        // ContainerAnnotation directly.
        [Obsolete("Use ContainerAnnotation rows linked via ImageAnalysisDecisionId. Scheduled for removal once UI components migrate.")]
        public string? SuspiciousAreas { get; set; } // JSON array of rectangles: [{"x":0,"y":0,"width":0,"height":0,"createdBy":"user"}]

        [StringLength(100)]
        public string ReviewedBy { get; set; } = string.Empty;

        public DateTime ReviewedAt { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // For grouping (consolidated vs non-consolidated)
        [StringLength(100)]
        public string? GroupIdentifier { get; set; }

        public bool IsConsolidated { get; set; }

        // ── Controlled-vocabulary finding categories (Gap 1a — AI training flywheel) ──
        // Both nullable. A decision may carry a security finding, a revenue finding,
        // both, or neither. The free-text Tags / Comments columns above remain
        // available for operator nuance, but the structured FK is what feeds the
        // training-data export.
        public int? ThreatCategoryId { get; set; }
        public int? RevenueAnomalyCategoryId { get; set; }

        // ── Image split tracking (links decision back to which split was used) ──
        /// <summary>References image_split_jobs.id (by value, no FK).</summary>
        public Guid? SplitJobId { get; set; }

        /// <summary>References image_split_results.id — the specific split crop used for this analysis.</summary>
        public Guid? SplitResultId { get; set; }

        /// <summary>Strategy name of the chosen split (e.g. "claude_vision", "steel_wall_midpoint").</summary>
        [StringLength(50)]
        public string? SplitChoiceStrategy { get; set; }
    }
}

