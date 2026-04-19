namespace NickScanWebApp.New.Models
{
    /// <summary>
    /// Detailed queue item response from the API
    /// </summary>
    public class QueueItemDetailResponse
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public string? InspectionId { get; set; }
        public DateTime ScanDate { get; set; }
        public string Status { get; set; } = string.Empty;
        public int Priority { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public string? Metadata { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Paged response for queue items
    /// </summary>
    public class PagedQueueItemsResponse
    {
        public List<QueueItemDetailResponse> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
    }

    /// <summary>
    /// Queue statistics response wrapper
    /// </summary>
    public class QueueStatisticsResponse
    {
        public DateTime Timestamp { get; set; }
        public QueueStatistics Statistics { get; set; } = new QueueStatistics();
    }

    /// <summary>
    /// Queue statistics data
    /// </summary>
    public class QueueStatistics
    {
        public int TotalPending { get; set; }
        public int TotalProcessing { get; set; }
        public int TotalCompleted { get; set; }
        public int TotalFailed { get; set; }
        public int ByScannerTypeFS6000 { get; set; }
        public int ByScannerTypeASE { get; set; }
        public int ItemsProcessedLastHour { get; set; }
        public int? AverageProcessingTimeSeconds { get; set; }
        public int? AverageWaitTimeMinutes { get; set; }
        public double SuccessRate { get; set; }
        public DateTime? OldestPendingQueuedAt { get; set; }
    }
}

