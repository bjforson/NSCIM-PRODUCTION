using System;

namespace NickScanWebApp.Mobile.Models.ICUMS
{
    public class SubmissionQueueItem
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ValidationResult { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Priority { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime? SubmittedAt { get; set; }
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; } = 3;
        public string? LastErrorMessage { get; set; }
        public string? SubmissionType { get; set; }

        // Backward compatibility properties
        public string? ErrorMessage
        {
            get => LastErrorMessage;
            set => LastErrorMessage = value;
        }

        public string? ResponseCode { get; set; }
    }
}

