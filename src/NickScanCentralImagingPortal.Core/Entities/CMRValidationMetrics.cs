using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Entity for tracking CMR validation metrics over time
    /// </summary>
    [Table("CMRValidationMetrics")]
    public class CMRValidationMetrics
    {
        [Key]
        public int Id { get; set; }

        public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

        public int TotalCMRRecords { get; set; }

        public int ValidCMRRecords { get; set; }

        public int InvalidCMRRecords { get; set; }

        public double ValidationSuccessRate { get; set; }

        public int MissingBlNumber { get; set; }

        public int MissingRotationNumber { get; set; }

        public int MissingBothFields { get; set; }

        // Daily change metrics
        public int NewRecordsToday { get; set; }

        public int FixedRecordsToday { get; set; }

        public int NewIssuesDetectedToday { get; set; }

        // Queue metrics
        public int QueuePendingCount { get; set; }

        public int QueueProcessingCount { get; set; }

        public int QueueCompletedCount { get; set; }

        public int QueueFailedCount { get; set; }

        // Performance metrics
        public double AverageRedownloadTimeMinutes { get; set; }

        public double QueueSuccessRate { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}

