using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    public class Container
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ContainerId { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string ScannerId { get; set; } = string.Empty;

        public DateTime ScanDateTime { get; set; }

        public DateTime CreatedAt { get; set; }

        public DateTime? UpdatedAt { get; set; }

        [MaxLength(20)]
        public string ProcessingStatus { get; set; } = "Pending";

        public virtual ICollection<ContainerImage> Images { get; set; } = new List<ContainerImage>();

        public virtual ICollection<ProcessingResult> ProcessingResults { get; set; } = new List<ProcessingResult>();
    }
}
