using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.HR
{
    /// <summary>
    /// Represents a scanner asset (fixed or mobile)
    /// </summary>
    [Table("ScannerAssets")]
    public class ScannerAsset
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public Guid SiteId { get; set; }

        [StringLength(100)]
        public string? Manufacturer { get; set; }

        [StringLength(100)]
        public string? Model { get; set; }

        [StringLength(100)]
        public string? SerialNumber { get; set; }

        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty; // FIXED, MOBILE

        [StringLength(50)]
        public string? EnergyType { get; set; }

        public DateTime? CommissionedOn { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "ACTIVE";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("SiteId")]
        public virtual Site Site { get; set; } = null!;

        public virtual ICollection<Lane> Lanes { get; set; } = new List<Lane>();
    }
}

