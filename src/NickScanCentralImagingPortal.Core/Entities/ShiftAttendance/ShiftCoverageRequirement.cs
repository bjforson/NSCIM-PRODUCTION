using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.ShiftAttendance
{
    /// <summary>
    /// Minimum staffing requirements per site/lane/shift
    /// </summary>
    [Table("ShiftCoverageRequirements")]
    public class ShiftCoverageRequirement
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid SiteId { get; set; }

        public Guid? LaneId { get; set; } // NULL = applies to entire site

        [Required]
        public Guid ShiftTemplateId { get; set; }

        public int? DayOfWeek { get; set; } // 0=Sunday, 1=Monday, ..., 6=Saturday. NULL = all days

        [StringLength(50)]
        public string? RequiredRole { get; set; } // e.g., 'SCANNER_OPERATOR', 'IMAGE_ANALYST'

        [Required]
        public int MinimumHeadcount { get; set; } = 1;

        public int? PreferredHeadcount { get; set; }

        public bool IsActive { get; set; } = true;

        [Required]
        public DateTime EffectiveFrom { get; set; }

        public DateTime? EffectiveTo { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [StringLength(100)]
        public string? UpdatedBy { get; set; }

        // Navigation properties
        [ForeignKey("SiteId")]
        public virtual HR.Site Site { get; set; } = null!;

        [ForeignKey("LaneId")]
        public virtual HR.Lane? Lane { get; set; }

        [ForeignKey("ShiftTemplateId")]
        public virtual ShiftTemplate ShiftTemplate { get; set; } = null!;
    }
}

