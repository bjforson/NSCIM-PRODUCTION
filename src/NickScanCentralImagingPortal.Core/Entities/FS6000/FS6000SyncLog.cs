using System;
using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities.FS6000
{
    public class FS6000SyncLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(500)]
        public string SourcePath { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string DestinationPath { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string SyncStatus { get; set; } = "Pending"; // 'Pending', 'Processing', 'Completed', 'Failed'

        [MaxLength(1000)]
        public string? ErrorMessage { get; set; }

        public int RetryCount { get; set; } = 0;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastRetryAt { get; set; }

        public DateTime? CompletedAt { get; set; }
    }
}
