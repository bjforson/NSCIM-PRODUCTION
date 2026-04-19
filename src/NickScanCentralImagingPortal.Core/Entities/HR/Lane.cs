using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.HR
{
    /// <summary>
    /// Represents a scanning lane at a site
    /// </summary>
    [Table("Lanes")]
    public class Lane
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

        public Guid? ScannerAssetId { get; set; }

        [StringLength(20)]
        public string Direction { get; set; } = "BOTH"; // INBOUND, OUTBOUND, BOTH

        public int? MaxThroughputPerHour { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "ACTIVE";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("SiteId")]
        public virtual Site Site { get; set; } = null!;

        [ForeignKey("ScannerAssetId")]
        public virtual ScannerAsset? ScannerAsset { get; set; }
    }
}

