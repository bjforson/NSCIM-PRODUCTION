using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for managing re-download queue for problematic CMR records
    /// </summary>
    public interface ICMRRedownloadService
    {
        /// <summary>
        /// Queues a CMR record for re-download
        /// </summary>
        /// <param name="containerNumber">Container number to re-download</param>
        /// <param name="reason">Reason for re-download</param>
        /// <returns>Queue result</returns>
        Task<CMRRedownloadResult> QueueForRedownloadAsync(string containerNumber, string reason);

        /// <summary>
        /// Queues multiple CMR records for re-download
        /// </summary>
        /// <param name="containerNumbers">List of container numbers to re-download</param>
        /// <param name="reason">Reason for re-download</param>
        /// <returns>Batch queue result</returns>
        Task<CMRBatchRedownloadResult> QueueBatchForRedownloadAsync(List<string> containerNumbers, string reason);

        /// <summary>
        /// Processes the re-download queue
        /// </summary>
        /// <returns>Processing result</returns>
        Task<CMRQueueProcessingResult> ProcessRedownloadQueueAsync();

        /// <summary>
        /// Gets the current re-download queue status
        /// </summary>
        /// <returns>Queue status</returns>
        Task<CMRRedownloadQueueStatus> GetQueueStatusAsync();

        /// <summary>
        /// Clears completed items from the queue
        /// </summary>
        /// <returns>Number of items cleared</returns>
        Task<int> ClearCompletedItemsAsync();

        /// <summary>
        /// Gets queue statistics
        /// </summary>
        /// <returns>Queue statistics</returns>
        Task<CMRRedownloadStatistics> GetQueueStatisticsAsync();
    }

    /// <summary>
    /// Result of queuing a CMR record for re-download
    /// </summary>
    public class CMRRedownloadResult
    {
        public bool Success { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
        public string QueueId { get; set; } = string.Empty;
    }

    /// <summary>
    /// Result of batch queuing CMR records for re-download
    /// </summary>
    public class CMRBatchRedownloadResult
    {
        public int TotalRequested { get; set; }
        public int SuccessfullyQueued { get; set; }
        public int Failed { get; set; }
        public List<CMRRedownloadResult> Results { get; set; } = new();
        public List<string> Errors { get; set; } = new();
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Result of processing the re-download queue
    /// </summary>
    public class CMRQueueProcessingResult
    {
        public int TotalProcessed { get; set; }
        public int Successful { get; set; }
        public int Failed { get; set; }
        public int Skipped { get; set; }
        public List<string> Errors { get; set; } = new();
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public TimeSpan ProcessingTime { get; set; }
    }

    /// <summary>
    /// Status of the re-download queue
    /// </summary>
    public class CMRRedownloadQueueStatus
    {
        public int PendingItems { get; set; }
        public int ProcessingItems { get; set; }
        public int CompletedItems { get; set; }
        public int FailedItems { get; set; }
        public DateTime LastProcessed { get; set; }
        public bool IsProcessing { get; set; }
        public List<CMRRedownloadQueueItem> RecentItems { get; set; } = new();
    }

    /// <summary>
    /// Individual queue item
    /// </summary>
    public class CMRRedownloadQueueItem
    {
        public string Id { get; set; } = string.Empty;
        public string ContainerNumber { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; } = 3;
    }

    /// <summary>
    /// Statistics for the re-download queue
    /// </summary>
    public class CMRRedownloadStatistics
    {
        public int TotalQueued { get; set; }
        public int TotalProcessed { get; set; }
        public int TotalSuccessful { get; set; }
        public int TotalFailed { get; set; }
        public double SuccessRate { get; set; }
        public double AverageProcessingTimeMinutes { get; set; }
        public DateTime LastProcessed { get; set; }
        public int CurrentlyProcessing { get; set; }
        public int PendingProcessing { get; set; }
    }
}
