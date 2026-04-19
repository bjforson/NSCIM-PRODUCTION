using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    /// <summary>
    /// A single scoring condition for the Decision Agent.
    /// Built-in conditions are seeded at migration time; dynamic conditions are added at runtime via the UI.
    /// Each condition has a weight (0.0–1.0) that contributes to the weighted average risk score.
    /// </summary>
    [Table("DecisionAgentConditions")]
    public class DecisionAgentCondition
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Human-readable display name.</summary>
        [Required, StringLength(100)]
        public string Name { get; set; } = string.Empty;

        /// <summary>Machine key: risk_red, multiple_housebl, has_vehicle, etc.</summary>
        [Required, StringLength(50)]
        public string ConditionKey { get; set; } = string.Empty;

        /// <summary>"BuiltIn" or "Dynamic". Built-in conditions cannot be deleted from UI.</summary>
        [Required, StringLength(30)]
        public string EvaluatorType { get; set; } = "BuiltIn";

        /// <summary>Weight for weighted average calculation (0.0–1.0).</summary>
        public double Weight { get; set; } = 0.1;

        /// <summary>Can be disabled without deleting.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Display ordering in UI.</summary>
        public int SortOrder { get; set; }

        // --- Dynamic condition fields (used when EvaluatorType = "Dynamic") ---
        // Also used by some BuiltIn conditions for configurable values (e.g., country lists, HS code lists)

        /// <summary>Field path to evaluate, e.g. "BOEDocument.GoodsDescription".</summary>
        [StringLength(200)]
        public string? DynamicFieldPath { get; set; }

        /// <summary>Operator: Contains, Equals, StartsWith, GreaterThan, Regex, Exists.</summary>
        [StringLength(20)]
        public string? DynamicOperator { get; set; }

        /// <summary>The value/pattern to match against. For list-based conditions, comma-separated values.</summary>
        [StringLength(500)]
        public string? DynamicValue { get; set; }

        /// <summary>Human-readable explanation of what this condition checks.</summary>
        [StringLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }
    }
}
