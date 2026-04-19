namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Result of ICUMS submission
    /// </summary>
    public class SubmissionResult
    {
        public bool IsSuccess { get; set; }
        public string? ICUMSResponseId { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
        public Dictionary<string, object> ResponseData { get; set; } = new();
    }

    /// <summary>
    /// Submission statistics
    /// </summary>
    public class SubmissionStatistics
    {
        public int TotalSubmissions { get; set; }
        public int PendingSubmissions { get; set; }
        public int ProcessingSubmissions { get; set; }
        public int CompletedSubmissions { get; set; }
        public int FailedSubmissions { get; set; }
        public int RetryCount { get; set; }
        public DateTime LastSubmissionAt { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
    }
}
