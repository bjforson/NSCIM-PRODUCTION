using System;
using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Represents a queued request to download ICUMS data for a container
    /// </summary>
    public class ICUMSDownloadQueue
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string ContainerNumber { get; set; } = string.Empty;

        /// <summary>
        /// Queue status: Pending, Processing, Completed, Failed
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string Status { get; set; } = "Pending";

        /// <summary>
        /// Priority: 0=Normal, 1=High, 2=Urgent
        /// </summary>
        public int Priority { get; set; } = 0;

        /// <summary>
        /// When the item was added to the queue
        /// </summary>
        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the first download attempt was made
        /// </summary>
        public DateTime? FirstAttemptAt { get; set; }

        /// <summary>
        /// When the most recent download attempt was made
        /// </summary>
        public DateTime? LastAttemptAt { get; set; }

        /// <summary>
        /// When the download was completed successfully
        /// </summary>
        public DateTime? CompletedAt { get; set; }

        /// <summary>
        /// Number of failed download attempts.
        /// </summary>
        public int RetryCount { get; set; } = 0;

        /// <summary>
        /// Maximum number of retries before marking as failed
        /// </summary>
        public int MaxRetries { get; set; } = 5;

        /// <summary>
        /// Error message from last failed attempt
        /// </summary>
        [MaxLength(1000)]
        public string? LastErrorMessage { get; set; }

        /// <summary>
        /// Error code from last failed attempt
        /// </summary>
        [MaxLength(50)]
        public string? LastErrorCode { get; set; }

        /// <summary>
        /// Who/what requested this download
        /// </summary>
        [MaxLength(100)]
        public string? RequestedBy { get; set; }

        /// <summary>
        /// Source of the request: Validation, Background, Manual, Ingestion
        /// </summary>
        [MaxLength(50)]
        public string? RequestSource { get; set; }

        /// <summary>
        /// Additional metadata as JSON
        /// </summary>
        [MaxLength(2000)]
        public string? Metadata { get; set; }
    }

    /// <summary>
    /// Queue priority levels
    /// </summary>
    public enum QueuePriority
    {
        Low = 0,
        Normal = 1,
        High = 2,
        Urgent = 3
    }

    /// <summary>
    /// Queue status values
    /// </summary>
    public static class QueueStatus
    {
        public const string Pending = "Pending";
        public const string Processing = "Processing";
        public const string Completed = "Completed";
        public const string Failed = "Failed";
    }

    /// <summary>
    /// Request source values
    /// </summary>
    public static class RequestSource
    {
        public const string Validation = "Validation";
        public const string Background = "Background";
        public const string Manual = "Manual";
        public const string Ingestion = "Ingestion";
        public const string ContainerCompleteness = "ContainerCompleteness";
    }
}
