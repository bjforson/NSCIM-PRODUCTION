using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.HR
{
    /// <summary>
    /// Represents a physical location (port, border post, airport, scanning center)
    /// </summary>
    [Table("Sites")]
    public class Site
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
        public string Type { get; set; } = string.Empty; // PORT, LAND_BORDER, AIRPORT, ICD, SCANNING_CENTER

        [Required]
        public Guid OrganizationId { get; set; }

        [StringLength(100)]
        public string? Country { get; set; }

        [StringLength(100)]
        public string? City { get; set; }

        [StringLength(200)]
        public string? Address { get; set; }

        [StringLength(50)]
        public string? Timezone { get; set; }

        [StringLength(500)]
        public string? OperationalHours { get; set; } // JSON or text description

        [StringLength(20)]
        public string Status { get; set; } = "ACTIVE";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("OrganizationId")]
        public virtual Organization Organization { get; set; } = null!;

        public virtual ICollection<Lane> Lanes { get; set; } = new List<Lane>();
        public virtual ICollection<Position> Positions { get; set; } = new List<Position>();
    }
}

