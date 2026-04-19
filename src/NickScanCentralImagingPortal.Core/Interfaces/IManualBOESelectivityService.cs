using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service interface for managing manual BOE selectivity requests to ICUMS
    /// </summary>
    public interface IManualBOESelectivityService
    {
        /// <summary>
        /// Processes pending manual BOE requests
        /// </summary>
        /// <returns>Number of requests processed</returns>
        Task<int> ProcessPendingBOERequestsAsync();

        /// <summary>
        /// Creates a new manual BOE request for a specific container
        /// </summary>
        /// <param name="containerNumber">Container number to request BOE data for</param>
        /// <param name="requestedBy">User or system requesting the BOE data</param>
        /// <returns>Created BOE request</returns>
        Task<ManualBOERequest> CreateManualBOERequestAsync(string containerNumber, string requestedBy = "System");

        /// <summary>
        /// Processes a single BOE request by calling ICUMS API
        /// </summary>
        /// <param name="request">BOE request to process</param>
        /// <returns>Updated BOE request with results</returns>
        Task<ManualBOERequest> ProcessBOERequestAsync(ManualBOERequest request);

        /// <summary>
        /// Gets all pending BOE requests that are ready for processing
        /// </summary>
        /// <param name="limit">Maximum number of requests to return</param>
        /// <returns>List of pending BOE requests</returns>
        Task<List<ManualBOERequest>> GetPendingBOERequestsAsync(int limit = 50);

        /// <summary>
        /// Updates the status of a BOE request
        /// </summary>
        /// <param name="requestId">ID of the BOE request</param>
        /// <param name="status">New status</param>
        /// <param name="errorMessage">Error message if any</param>
        /// <param name="icuMSResponseId">ICUMS response ID if successful</param>
        /// <returns>Updated BOE request</returns>
        Task<ManualBOERequest> UpdateBOERequestStatusAsync(
            int requestId,
            string status,
            string? errorMessage = null,
            string? icuMSResponseId = null);

        /// <summary>
        /// Gets BOE requests that have failed and need retry
        /// </summary>
        /// <param name="maxRetryCount">Maximum retry count threshold</param>
        /// <returns>List of BOE requests ready for retry</returns>
        Task<List<ManualBOERequest>> GetFailedBOERequestsForRetryAsync(int maxRetryCount = 3);

        /// <summary>
        /// Gets statistics about BOE requests
        /// </summary>
        /// <returns>BOE request statistics</returns>
        Task<BOERequestStatistics> GetBOERequestStatisticsAsync();

        /// <summary>
        /// Auto-discovers containers with missing ICUMS data and queues them for download
        /// </summary>
        /// <returns>Number of containers queued</returns>
        Task<int> AutoQueueMissingICUMSContainersAsync();
    }

    /// <summary>
    /// Statistics for BOE requests
    /// </summary>
    public class BOERequestStatistics
    {
        public int TotalRequests { get; set; }
        public int PendingRequests { get; set; }
        public int ProcessingRequests { get; set; }
        public int CompletedRequests { get; set; }
        public int FailedRequests { get; set; }
        public int RetryRequests { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    }
}
