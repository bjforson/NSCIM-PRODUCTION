using System;
using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities.FS6000
{
    public class FS6000FileProcessing
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(500)]
        public string FilePath { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string FileName { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        public string FileType { get; set; } = string.Empty; // 'XML', 'JPEG'

        [Required]
        [MaxLength(20)]
        public string ProcessingStatus { get; set; } = "Pending"; // 'Pending', 'Processing', 'Completed', 'Failed'

        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }

        public DateTime? ProcessedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
