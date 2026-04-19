using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.FS6000
{
    public class FS6000Image
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid ScanId { get; set; }

        [Required]
        [MaxLength(20)]
        public string ImageType { get; set; } = string.Empty; // 'Main', 'Icon', 'CCR', 'LPR', 'Manifest'

        [Required]
        [MaxLength(200)]
        public string FileName { get; set; } = string.Empty;

        public byte[]? ImageData { get; set; } // Base64 as binary

        public int? FileSizeBytes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [ForeignKey("ScanId")]
        public virtual FS6000Scan Scan { get; set; } = null!;
    }
}
