using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Models.Gateway
{
    /// <summary>
    /// Batch operation request
    /// </summary>
    public class BatchOperationRequest
    {
        public string OperationType { get; set; } = string.Empty; // "Validate", "Process", "Download", "Export"
        public List<string> ContainerNumbers { get; set; } = new();
        public Dictionary<string, object> Parameters { get; set; } = new();
        public bool ProcessAsync { get; set; } = true; // Process in background
        public string? CallbackUrl { get; set; } // Webhook for completion
    }

    /// <summary>
    /// Batch operation response
    /// </summary>
    public class BatchOperationResponse
    {
        public string BatchId { get; set; } = Guid.NewGuid().ToString();
        public string OperationType { get; set; } = string.Empty;
        public string Status { get; set; } = "Queued"; // "Queued", "Processing", "Completed", "Failed"
        public DateTime QueuedAt { get; set; } = DateTime.UtcNow;
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public int TotalItems { get; set; }
        public int ProcessedItems { get; set; }
        public int SuccessfulItems { get; set; }
        public int FailedItems { get; set; }
        public double ProgressPercentage => TotalItems > 0 ? (ProcessedItems * 100.0 / TotalItems) : 0;

        public List<BatchItemResult> Results { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Individual batch item result
    /// </summary>
    public class BatchItemResult
    {
        public string ItemId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // "Success", "Failed", "Pending"
        public string? Message { get; set; }
        public Dictionary<string, object> Data { get; set; } = new();
    }
}

