using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.HR
{
    /// <summary>
    /// Represents a department or unit within an organization
    /// </summary>
    [Table("OrgUnits")]
    public class OrgUnit
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid OrganizationId { get; set; }

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [StringLength(20)]
        public string Type { get; set; } = string.Empty; // DIRECTORATE, DEPARTMENT, UNIT, TEAM

        public Guid? ParentOrgUnitId { get; set; }

        public Guid? SiteId { get; set; }

        [StringLength(50)]
        public string? CostCenterCode { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "ACTIVE";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("OrganizationId")]
        public virtual Organization Organization { get; set; } = null!;

        [ForeignKey("ParentOrgUnitId")]
        public virtual OrgUnit? ParentOrgUnit { get; set; }

        [ForeignKey("SiteId")]
        public virtual Site? Site { get; set; }

        public virtual ICollection<OrgUnit> ChildOrgUnits { get; set; } = new List<OrgUnit>();
        public virtual ICollection<Position> Positions { get; set; } = new List<Position>();
    }
}

