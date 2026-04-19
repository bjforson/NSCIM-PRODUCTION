using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.HR
{
    /// <summary>
    /// Links employees to positions (supports multiple assignments)
    /// </summary>
    [Table("EmployeePositions")]
    public class EmployeePosition
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid EmployeeId { get; set; }

        [Required]
        public Guid PositionId { get; set; }

        public bool Primary { get; set; } = false;

        public DateTime EffectiveFrom { get; set; }

        public DateTime? EffectiveTo { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "ACTIVE"; // ACTIVE, ENDED

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [StringLength(100)]
        public string? UpdatedBy { get; set; }

        // Navigation properties
        [ForeignKey("EmployeeId")]
        public virtual Employee Employee { get; set; } = null!;

        [ForeignKey("PositionId")]
        public virtual Position Position { get; set; } = null!;
    }
}

