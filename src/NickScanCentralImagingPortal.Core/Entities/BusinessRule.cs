using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Business rules for validation and workflow enforcement
    /// Rules define conditions and actions that are applied to container data
    /// </summary>
    [Table("BusinessRules")]
    public class BusinessRule
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Name of the business rule
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string Name { get; set; } = string.Empty;

        /// <summary>
        /// Detailed description of what the rule does
        /// </summary>
        [Required]
        [MaxLength(1000)]
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Category of the rule (e.g., "Container Validation", "Document Validation", "Image Analysis")
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Priority level: Critical, High, Medium, Low
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Priority { get; set; } = "Medium";

        /// <summary>
        /// Condition expression that determines when the rule applies
        /// Can be SQL-like or pseudo-code expression
        /// </summary>
        [Required]
        [MaxLength(2000)]
        public string ConditionExpression { get; set; } = string.Empty;

        /// <summary>
        /// Action type: Block, Reject, Warn, Flag, Notify, Filter/Exclude
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string ActionType { get; set; } = string.Empty;

        /// <summary>
        /// Message to display when the rule is triggered
        /// </summary>
        [Required]
        [MaxLength(1000)]
        public string ActionMessage { get; set; } = string.Empty;

        /// <summary>
        /// Is this rule currently active/enabled?
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Execution order (lower numbers execute first)
        /// </summary>
        public int ExecutionOrder { get; set; } = 0;

        /// <summary>
        /// When was this rule created
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Username of who created this rule
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>
        /// When was this rule last updated
        /// </summary>
        public DateTime? UpdatedAt { get; set; }

        /// <summary>
        /// Username of who last updated this rule
        /// </summary>
        [MaxLength(100)]
        public string? UpdatedBy { get; set; }
    }
}

