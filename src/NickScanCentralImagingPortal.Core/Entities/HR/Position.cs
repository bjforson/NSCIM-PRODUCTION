using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.HR
{
    /// <summary>
    /// Represents a position/post in the organizational structure
    /// </summary>
    [Table("Positions")]
    public class Position
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Title { get; set; } = string.Empty;

        [Required]
        public Guid OrgUnitId { get; set; }

        public Guid? SiteId { get; set; }

        [StringLength(20)]
        public string? Grade { get; set; }

        [StringLength(20)]
        public string PositionType { get; set; } = "PERMANENT"; // PERMANENT, CONTRACT, CONSULTANT, SECONDMENT

        public int Headcount { get; set; } = 1;

        public bool IsCritical { get; set; } = false;

        [StringLength(20)]
        public string Status { get; set; } = "ACTIVE";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [StringLength(100)]
        public string? UpdatedBy { get; set; }

        // Navigation properties
        [ForeignKey("OrgUnitId")]
        public virtual OrgUnit OrgUnit { get; set; } = null!;

        [ForeignKey("SiteId")]
        public virtual Site? Site { get; set; }

        public virtual ICollection<EmployeePosition> EmployeePositions { get; set; } = new List<EmployeePosition>();
    }
}

