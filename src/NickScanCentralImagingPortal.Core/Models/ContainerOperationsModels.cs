using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Models
{
    public class ContainerSearchCriteria
    {
        public string? SearchTerm { get; set; }
        public string? Status { get; set; }
        public string? ClearanceType { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "CreatedAt";
        public string SortOrder { get; set; } = "desc";
    }

    public class ContainerSearchResult
    {
        public IEnumerable<ContainerDetails> Containers { get; set; } = new List<ContainerDetails>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasNextPage => Page < TotalPages;
        public bool HasPreviousPage => Page > 1;
    }

    public class ContainerStatusUpdate
    {
        [Required]
        public string Status { get; set; } = string.Empty;

        public string? Comments { get; set; }

        public string? UpdatedBy { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }

    public class ContainerAssignment
    {
        [Required]
        public string OperatorId { get; set; } = string.Empty;

        [Required]
        public string OperatorName { get; set; } = string.Empty;

        public string? Comments { get; set; }

        public DateTime AssignedAt { get; set; } = DateTime.UtcNow;

        public string? AssignedBy { get; set; }

        public int Priority { get; set; } = 1; // 1=Low, 2=Medium, 3=High, 4=Critical
    }

    public class ProcessingStartRequest
    {
        [Required]
        public string OperatorId { get; set; } = string.Empty;

        [Required]
        public string OperatorName { get; set; } = string.Empty;

        public string? ScannerId { get; set; }

        public string? ProcessingType { get; set; } = "Standard"; // Standard, Priority, Express

        public string? Comments { get; set; }

        public DateTime StartedAt { get; set; } = DateTime.UtcNow;

        public Dictionary<string, object> ProcessingParameters { get; set; } = new();
    }

    public class ProcessingCompleteRequest
    {
        [Required]
        public string Status { get; set; } = string.Empty; // Completed, Failed, RequiresReview

        public string? Comments { get; set; }

        public string? QualityScore { get; set; } // A, B, C, D, F

        public int? ImagesProcessed { get; set; }

        public int? ErrorsEncountered { get; set; }

        public DateTime CompletedAt { get; set; } = DateTime.UtcNow;

        public Dictionary<string, object> ProcessingResults { get; set; } = new();

        public List<string> IssuesIdentified { get; set; } = new();
    }

    public class ContainerProcessingHistory
    {
        public int Id { get; set; }
        public int ContainerId { get; set; }
        public string Action { get; set; } = string.Empty; // Assigned, Started, Completed, Failed, Reassigned
        public string Status { get; set; } = string.Empty;
        public string OperatorId { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public string? ScannerId { get; set; }
        public string? Comments { get; set; }
        public DateTime Timestamp { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }

    public class ContainerStatistics
    {
        public int TotalContainers { get; set; }
        public int ContainersProcessed { get; set; }
        public int ContainersPending { get; set; }
        public int ContainersInProgress { get; set; }
        public int ContainersFailed { get; set; }
        public int ContainersCompleted { get; set; }
        public double AverageProcessingTime { get; set; } // in minutes
        public double ProcessingSuccessRate { get; set; } // percentage
        public Dictionary<string, int> StatusBreakdown { get; set; } = new();
        public Dictionary<string, int> ClearanceTypeBreakdown { get; set; } = new();
        public Dictionary<string, int> DailyProcessingCount { get; set; } = new();
        public DateTime StatisticsDate { get; set; } = DateTime.UtcNow;
    }

    public class BulkStatusUpdateRequest
    {
        [Required]
        public List<int> ContainerIds { get; set; } = new();

        [Required]
        public ContainerStatusUpdate StatusUpdate { get; set; } = new();

        public string? Reason { get; set; }

        public string? UpdatedBy { get; set; }
    }

    public class ContainerQueueStatus
    {
        public int TotalInQueue { get; set; }
        public int HighPriorityCount { get; set; }
        public int MediumPriorityCount { get; set; }
        public int LowPriorityCount { get; set; }
        public int AssignedCount { get; set; }
        public int UnassignedCount { get; set; }
        public int ProcessingCount { get; set; }
        public double AverageWaitTime { get; set; } // in minutes
        public List<ContainerQueueItem> QueueItems { get; set; } = new();
        public Dictionary<string, int> OperatorWorkload { get; set; } = new();
    }

    public class ContainerQueueItem
    {
        public int ContainerId { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public int Priority { get; set; }
        public string? AssignedOperator { get; set; }
        public DateTime QueuedAt { get; set; }
        public DateTime? AssignedAt { get; set; }
        public DateTime? StartedAt { get; set; }
        public TimeSpan WaitTime => DateTime.UtcNow - QueuedAt;
        public string ClearanceType { get; set; } = string.Empty;
        public string? Comments { get; set; }
    }

    public class ContainerWorkflowStep
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Order { get; set; }
        public bool IsRequired { get; set; }
        public string? RequiredRole { get; set; }
        public string? NextStep { get; set; }
        public string? PreviousStep { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class ContainerWorkflow
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ClearanceType { get; set; } = string.Empty;
        public List<ContainerWorkflowStep> Steps { get; set; } = new();
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ContainerProcessingMetrics
    {
        public int ContainerId { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime ProcessingStarted { get; set; }
        public DateTime? ProcessingCompleted { get; set; }
        public TimeSpan? TotalProcessingTime { get; set; }
        public int ImagesProcessed { get; set; }
        public int ErrorsEncountered { get; set; }
        public string QualityScore { get; set; } = string.Empty;
        public string OperatorId { get; set; } = string.Empty;
        public string OperatorName { get; set; } = string.Empty;
        public string? ScannerId { get; set; }
        public Dictionary<string, object> PerformanceMetrics { get; set; } = new();
    }
}
