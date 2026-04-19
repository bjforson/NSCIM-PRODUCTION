using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.FS6000
{
    using NickScanCentralImagingPortal.Core.Entities;
    public class FS6000Scan
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        public DateTime ScanTime { get; set; }

        [Required]
        [MaxLength(100)]
        public string PicNumber { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? FycoPresent { get; set; }

        [MaxLength(100)]
        public string? VesselName { get; set; }

        [MaxLength(100)]
        public string? TruckPlate { get; set; }

        [MaxLength(50)]
        public string? OperatorId { get; set; }

        [MaxLength(50)]
        public string? ScanResult { get; set; }

        [MaxLength(500)]
        public string? GoodsDescription { get; set; }

        [MaxLength(200)]
        public string? ShippingCompany { get; set; }

        [MaxLength(200)]
        public string? Consignee { get; set; }

        [MaxLength(500)]
        public string? FilePath { get; set; }

        [MaxLength(20)]
        public string SyncStatus { get; set; } = "Pending";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ProcessedAt { get; set; }

        // Image completeness tracking
        public bool HasImage { get; set; } = false;

        public int ImageCount { get; set; } = 0;

        public DateTime? ImageIngestedAt { get; set; }

        [MaxLength(500)]
        public string? ImageValidationError { get; set; }

        public int? OriginalScanRecordId { get; set; }

        // Navigation properties
        public virtual ICollection<FS6000Image> Images { get; set; } = new List<FS6000Image>();
        public virtual OriginalScanRecord? OriginalScanRecord { get; set; }
    }
}
