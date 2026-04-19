using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    public class ContainerImage
    {
        public int Id { get; set; }

        public int ContainerId { get; set; }

        [Required]
        [MaxLength(255)]
        public string ImagePath { get; set; } = string.Empty;

        [MaxLength(50)]
        public string ImageType { get; set; } = string.Empty;

        public long FileSizeBytes { get; set; }

        [MaxLength(100)]
        public string OriginalFileName { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; }

        public DateTime? ProcessedAt { get; set; }

        [MaxLength(20)]
        public string ProcessingStatus { get; set; } = "Pending";

        public virtual Container Container { get; set; } = null!;
    }
}
