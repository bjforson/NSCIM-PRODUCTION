using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Queue for ICUMS submissions - manages the workflow from scanner data to ICUMS submission
    /// </summary>
    public class ICUMSSubmissionQueue
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        [Required]
        public string ImagePaths { get; set; } = string.Empty; // JSON array of image paths

        [Required]
        public string ReportData { get; set; } = string.Empty; // JSON report data

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = ICUMSSubmissionQueueStatus.Pending;

        public int Priority { get; set; } = 1; // 1=Normal, 2=High, 3=Critical

        public DateTime? SubmittedAt { get; set; }

        [StringLength(100)]
        public string? ICUMSResponseId { get; set; }

        [StringLength(1000)]
        public string? ErrorMessage { get; set; }

        public int RetryCount { get; set; } = 0;

        public DateTime? NextRetryAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string? SubmittedBy { get; set; } = "System";

        public DateTime? CompletedAt { get; set; }
    }

    /// <summary>
    /// Queue status values consumed by ICUMSSubmissionService.
    /// </summary>
    public static class ICUMSSubmissionQueueStatus
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string Submitted = "Submitted";
        public const string Failed = "Failed";
        public const string Cancelled = "Cancelled";
    }
}
