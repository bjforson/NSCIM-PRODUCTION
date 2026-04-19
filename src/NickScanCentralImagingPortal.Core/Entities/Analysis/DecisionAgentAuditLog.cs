using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    /// <summary>
    /// Every agent evaluation is logged here for full accountability and observability.
    /// Includes per-condition scoring breakdown, the final decision, and reversal tracking.
    /// </summary>
    [Table("DecisionAgentAuditLogs")]
    public class DecisionAgentAuditLog
    {
        [Key]
        public long Id { get; set; }

        /// <summary>FK to AnalysisGroups.</summary>
        public Guid GroupId { get; set; }

        [StringLength(150)]
        public string GroupIdentifier { get; set; } = string.Empty;

        /// <summary>Computed weighted risk score (0.0–1.0).</summary>
        public double TotalScore { get; set; }

        /// <summary>"Normal", "Abnormal", or "Skipped" (left for humans).</summary>
        [Required, StringLength(20)]
        public string Decision { get; set; } = "Skipped";

        /// <summary>Was this a shadow-mode-only evaluation (no real decisions created)?</summary>
        public bool IsShadowMode { get; set; }

        /// <summary>How far the agent progressed: "Decision", "Audit", "Submission", or "None" (shadow/skipped).</summary>
        [StringLength(30)]
        public string ProcessingDepthReached { get; set; } = "None";

        /// <summary>
        /// JSON array of per-condition results:
        /// [{ "conditionKey": "risk_red", "name": "CRMS Level Red", "matched": true, "weight": 0.20, "rawValue": "Red" }, ...]
        /// </summary>
        public string? ConditionResultsJson { get; set; }

        public int ContainerCount { get; set; }

        /// <summary>JSON array of container numbers processed.</summary>
        [StringLength(2000)]
        public string? ContainerNumbers { get; set; }

        /// <summary>JSON array of created ImageAnalysisDecision IDs (null if shadow/skipped).</summary>
        [StringLength(2000)]
        public string? DecisionIds { get; set; }

        /// <summary>If processing failed, the error message.</summary>
        [StringLength(1000)]
        public string? ErrorMessage { get; set; }

        /// <summary>How long the evaluation took in milliseconds.</summary>
        public long ProcessingTimeMs { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // --- Reversal tracking ---

        public DateTime? ReversedAtUtc { get; set; }

        [StringLength(100)]
        public string? ReversedBy { get; set; }

        [StringLength(500)]
        public string? ReversalReason { get; set; }
    }
}
