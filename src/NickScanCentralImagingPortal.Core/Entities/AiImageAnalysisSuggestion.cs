using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Stores AI assist output and human resolution for training lineage (Phase 0+).
    /// </summary>
    [Table("AiImageAnalysisSuggestions")]
    public class AiImageAnalysisSuggestion
    {
        [Key]
        public long Id { get; set; }

        public Guid? AnalysisGroupId { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        [StringLength(100)]
        public string? GroupIdentifier { get; set; }

        [StringLength(100)]
        public string ModelId { get; set; } = string.Empty;

        [StringLength(50)]
        public string ModelVersion { get; set; } = string.Empty;

        [StringLength(50)]
        public string FeatureVersion { get; set; } = string.Empty;

        /// <summary>JSON: ROI boxes, rank metadata, copilot snippets, etc.</summary>
        public string? SuggestionPayloadJson { get; set; }

        [StringLength(20)]
        public string? SuggestedDecision { get; set; }

        public double? Confidence { get; set; }

        /// <summary>Automation tier (0–4) per plan; assist is typically 2.</summary>
        public int Tier { get; set; } = 2;

        public bool ShadowMode { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [StringLength(20)]
        public string? HumanFinalDecision { get; set; }

        [StringLength(100)]
        public string? HumanReviewedBy { get; set; }

        public DateTime? ResolvedAtUtc { get; set; }

        [StringLength(500)]
        public string? CorrectionReason { get; set; }

        public bool? ResolvedDiffersFromSuggestion { get; set; }

        public bool EligibleForTrainingExport { get; set; }

        public bool DatasetOptIn { get; set; }
    }
}
