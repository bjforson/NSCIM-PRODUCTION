using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Entity representing a CMR record queued for re-download
    /// </summary>
    [Table("CMRRedownloadQueue")]
    public class CMRRedownloadQueue
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string Reason { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Processing, Completed, Failed

        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ProcessedAt { get; set; }

        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }

        public int RetryCount { get; set; } = 0;

        public int MaxRetries { get; set; } = 3;

        [MaxLength(100)]
        public string? ProcessedBy { get; set; }

        [MaxLength(50)]
        public string? Priority { get; set; } = "Normal"; // Low, Normal, High, Critical

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        [MaxLength(50)]
        public string? OriginalDeclarationNumber { get; set; }

        [MaxLength(20)]
        public string? OriginalClearanceType { get; set; }
    }
}
