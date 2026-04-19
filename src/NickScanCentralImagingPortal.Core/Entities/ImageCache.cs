using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    public class ImageCache
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "bytea")]
        public byte[] ImageData { get; set; } = Array.Empty<byte>();

        [StringLength(50)]
        public string MimeType { get; set; } = "image/jpeg";

        public int Width { get; set; }
        public int Height { get; set; }
        public long FileSizeBytes { get; set; }

        public DateTime ScanTime { get; set; }
        public DateTime CachedAt { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string ProcessingPipeline { get; set; } = string.Empty;

        [StringLength(50)]
        public string Quality { get; set; } = "High";
    }
}
