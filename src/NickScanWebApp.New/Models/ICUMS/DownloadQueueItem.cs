using System;

namespace NickScanWebApp.New.Models.ICUMS
{
    public class DownloadQueueItem
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string BOENumber { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime QueuedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; } = 3;
        public string? LastErrorMessage { get; set; }
        public string? Source { get; set; }
        public string? RequestSource { get; set; }

        // Backward compatibility
        public DateTime RequestedAt
        {
            get => QueuedAt;
            set => QueuedAt = value;
        }

        public string? ErrorMessage
        {
            get => LastErrorMessage;
            set => LastErrorMessage = value;
        }
    }
}

