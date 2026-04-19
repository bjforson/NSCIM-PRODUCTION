using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IICUMSDownloadQueueRepository
    {
        /// <summary>
        /// Add a container to the download queue
        /// </summary>
        Task<int> AddToQueueAsync(ICUMSDownloadQueue queueItem);

        /// <summary>
        /// Get next batch of items to process (ordered by priority and queue time)
        /// </summary>
        Task<List<ICUMSDownloadQueue>> GetNextBatchAsync(int batchSize = 50);

        /// <summary>
        /// Get all queue items (all statuses) for UI display
        /// </summary>
        Task<List<ICUMSDownloadQueue>> GetAllQueueItemsAsync(int limit = 100);

        /// <summary>
        /// Mark an item as processing
        /// </summary>
        Task MarkAsProcessingAsync(int id);

        /// <summary>
        /// Mark an item as completed
        /// </summary>
        Task MarkAsCompletedAsync(int id);

        /// <summary>
        /// Mark an item as failed
        /// </summary>
        Task MarkAsFailedAsync(int id, string errorMessage, string? errorCode = null);

        /// <summary>
        /// Update retry count and last attempt time
        /// </summary>
        Task UpdateRetryInfoAsync(int id, string errorMessage, string? errorCode = null);

        /// <summary>
        /// Check if container is already in queue
        /// </summary>
        Task<bool> IsInQueueAsync(string containerNumber);

        /// <summary>
        /// Get queue item by container number
        /// </summary>
        Task<ICUMSDownloadQueue?> GetByContainerNumberAsync(string containerNumber);

        /// <summary>
        /// Get queue statistics
        /// </summary>
        Task<QueueStatistics> GetQueueStatisticsAsync();

        /// <summary>
        /// Remove old completed/failed items (cleanup)
        /// </summary>
        Task<int> CleanupOldItemsAsync(int daysToKeep = 7);

        /// <summary>
        /// Update priority for a container
        /// </summary>
        Task UpdatePriorityAsync(string containerNumber, int priority);

        /// <summary>
        /// Recover items stuck in Processing status (likely from service crash/restart)
        /// Resets them to Pending if they've been processing for more than the specified timeout
        /// </summary>
        Task<int> RecoverStuckProcessingItemsAsync(int timeoutMinutes = 30);

        /// <summary>
        /// Save changes to the database
        /// </summary>
        Task SaveChangesAsync();
    }

    public class QueueStatistics
    {
        public int TotalPending { get; set; }
        public int TotalProcessing { get; set; }
        public int TotalCompleted { get; set; }
        public int TotalFailed { get; set; }
        public int HighPriority { get; set; }
        public int NormalPriority { get; set; }
        public int LowPriority { get; set; }
        public double AverageWaitTimeMinutes { get; set; }
        public double SuccessRate { get; set; }
        public DateTime? OldestPendingQueuedAt { get; set; }
    }
}
