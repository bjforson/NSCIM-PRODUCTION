using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.HR
{
    /// <summary>
    /// Represents an organization (e.g., Customs, Port Authority, Terminal Operator)
    /// </summary>
    [Table("Organizations")]
    public class Organization
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [StringLength(20)]
        public string Type { get; set; } = string.Empty; // CUSTOMS, PORT_AUTHORITY, TERMINAL_OPERATOR, SECURITY_AGENCY, VENDOR

        public Guid? ParentOrganizationId { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "ACTIVE"; // ACTIVE, INACTIVE

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [StringLength(100)]
        public string? UpdatedBy { get; set; }

        // Navigation properties
        [ForeignKey("ParentOrganizationId")]
        public virtual Organization? ParentOrganization { get; set; }

        public virtual ICollection<Organization> ChildOrganizations { get; set; } = new List<Organization>();
        public virtual ICollection<OrgUnit> OrgUnits { get; set; } = new List<OrgUnit>();
        public virtual ICollection<Site> Sites { get; set; } = new List<Site>();
    }
}

