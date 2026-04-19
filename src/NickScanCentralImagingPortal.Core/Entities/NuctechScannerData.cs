using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    public class NuctechScannerData
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ContainerId { get; set; } = string.Empty;

        [MaxLength(100)]
        public string ScannerId { get; set; } = string.Empty;

        public DateTime ScanDateTime { get; set; }

        public string? RawData { get; set; }

        public string? ImagePath { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? ProcessedAt { get; set; }

        [MaxLength(20)]
        public string ProcessingStatus { get; set; } = "Pending";
    }
}
