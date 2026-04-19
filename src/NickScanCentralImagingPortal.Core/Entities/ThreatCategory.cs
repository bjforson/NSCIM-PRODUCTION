using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Controlled vocabulary of security-domain inspection findings (weapons, drugs,
    /// contraband, hazmat). Backs the ThreatCategoryId foreign key on
    /// ImageAnalysisDecision and ContainerAnnotation so analyst findings are captured
    /// against a fixed list instead of free text. Lives in a lookup table so categories
    /// can be deactivated without breaking historical decision rows.
    ///
    /// Companion to RevenueAnomalyCategory: a single decision/annotation may carry one,
    /// both, or neither.
    /// </summary>
    [Table("ThreatCategories")]
    public class ThreatCategory
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Stable machine code, e.g. "weapon_firearm". Never rename — historical rows
        /// reference it by FK id, but human-facing tools display the code too.
        /// </summary>
        [Required]
        [StringLength(64)]
        public string Code { get; set; } = string.Empty;

        /// <summary>
        /// Human-facing label shown to analysts in the dropdown.
        /// </summary>
        [Required]
        [StringLength(120)]
        public string DisplayName { get; set; } = string.Empty;

        /// <summary>
        /// Operational definition / one-line guidance for analysts.
        /// </summary>
        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// Soft-deactivation flag. Inactive rows stay in the table so existing decision
        /// rows still resolve, but the dropdown hides them.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Display order in the dropdown. Lower values come first.
        /// </summary>
        public int SortOrder { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
