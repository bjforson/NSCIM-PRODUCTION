using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Controlled vocabulary of revenue-assurance inspection findings (undeclared
    /// goods, undervaluation, misclassification, transit diversion, concealment, etc.).
    /// Backs the RevenueAnomalyCategoryId foreign key on ImageAnalysisDecision and
    /// ContainerAnnotation.
    ///
    /// Orthogonal to ThreatCategory: a single decision/annotation may carry one, both,
    /// or neither. Security and revenue findings frequently co-occur in the same
    /// container and must be modelled independently.
    /// </summary>
    [Table("RevenueAnomalyCategories")]
    public class RevenueAnomalyCategory
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Stable machine code, e.g. "revenue_undeclared_goods". Never rename.
        /// </summary>
        [Required]
        [StringLength(64)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(120)]
        public string DisplayName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        public bool IsActive { get; set; } = true;

        public int SortOrder { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
