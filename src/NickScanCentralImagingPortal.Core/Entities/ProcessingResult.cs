using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    public class ProcessingResult
    {
        public int Id { get; set; }

        public int ContainerId { get; set; }

        [MaxLength(50)]
        public string ResultType { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Status { get; set; } = string.Empty;

        public string? ResultData { get; set; }

        public string? ErrorMessage { get; set; }

        public DateTime ProcessedAt { get; set; }

        public DateTime CreatedAt { get; set; }

        public virtual Container Container { get; set; } = null!;
    }
}
