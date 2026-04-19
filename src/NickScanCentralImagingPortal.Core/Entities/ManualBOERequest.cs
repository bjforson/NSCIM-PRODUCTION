using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Tracks manual BOE selectivity requests to ICUMS API
    /// </summary>
    public class ManualBOERequest
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        public DateTime RequestDate { get; set; } = DateTime.UtcNow;

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // 'Pending', 'Processing', 'Completed', 'Failed'

        [StringLength(100)]
        public string? ICUMSResponseId { get; set; }

        [StringLength(1000)]
        public string? ErrorMessage { get; set; }

        public int RetryCount { get; set; } = 0;

        public DateTime? CompletedAt { get; set; }

        public DateTime? NextRetryAt { get; set; }

        [StringLength(50)]
        public string? RequestedBy { get; set; } = "System";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
