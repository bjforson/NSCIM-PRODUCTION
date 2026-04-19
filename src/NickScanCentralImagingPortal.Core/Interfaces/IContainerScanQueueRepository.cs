using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Repository interface for managing ContainerScanQueue
    /// Handles queue operations for container completeness processing
    /// </summary>
    public interface IContainerScanQueueRepository
    {
        /// <summary>
        /// Add a container scan to the queue
        /// </summary>
        /// <param name="queueItem">Queue item to add</param>
        /// <returns>ID of the created queue item, or existing item ID if duplicate</returns>
        Task<int> AddToQueueAsync(ContainerScanQueue queueItem);

        /// <summary>
        /// Add multiple container scans to the queue in batch
        /// </summary>
        /// <param name="queueItems">List of queue items to add</param>
        /// <returns>Number of items successfully added</returns>
        Task<int> AddBatchToQueueAsync(List<ContainerScanQueue> queueItems);

        /// <summary>
        /// Get next batch of items to process (ordered by priority and queue time)
        /// </summary>
        /// <param name="batchSize">Number of items to retrieve</param>
        /// <returns>List of queue items ready for processing</returns>
        Task<List<ContainerScanQueue>> GetNextBatchAsync(int batchSize = 100);

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
        Task MarkAsFailedAsync(int id, string errorMessage);

        /// <summary>
        /// Update retry count and error message
        /// </summary>
        Task UpdateRetryInfoAsync(int id, string errorMessage);

        /// <summary>
        /// Check if a container scan is already in queue (by ContainerNumber, ScannerType, and InspectionId)
        /// </summary>
        Task<bool> IsInQueueAsync(string containerNumber, string scannerType, string? inspectionId);

        /// <summary>
        /// Get queue item by container number, scanner type, and inspection ID
        /// </summary>
        Task<ContainerScanQueue?> GetByKeyAsync(string containerNumber, string scannerType, string? inspectionId);

        /// <summary>
        /// Get queue statistics
        /// </summary>
        Task<ContainerScanQueueStatistics> GetQueueStatisticsAsync();

        /// <summary>
        /// Remove old completed/failed items (cleanup)
        /// </summary>
        /// <param name="daysToKeep">Number of days to keep completed items</param>
        /// <returns>Number of items removed</returns>
        Task<int> CleanupOldItemsAsync(int daysToKeep = 7);

        /// <summary>
        /// Recover items stuck in Processing status (likely from service crash/restart)
        /// Resets them to Pending if they've been processing for more than the specified timeout
        /// </summary>
        /// <param name="timeoutMinutes">Minutes after which an item is considered stuck</param>
        /// <returns>Number of items recovered</returns>
        Task<int> RecoverStuckProcessingItemsAsync(int timeoutMinutes = 30);

        /// <summary>
        /// Save changes to the database
        /// </summary>
        Task SaveChangesAsync();
    }

    /// <summary>
    /// Queue statistics for ContainerScanQueue
    /// </summary>
    public class ContainerScanQueueStatistics
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
        public int ByScannerTypeFS6000 { get; set; }
        public int ByScannerTypeASE { get; set; }
        public int ByScannerTypeHeimannSmith { get; set; }
    }
}

