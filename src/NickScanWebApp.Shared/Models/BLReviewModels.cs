namespace NickScanWebApp.Shared.Models
{
    public class BLGroupDto
    {
        public string MasterBlNumber { get; set; } = string.Empty;
        public int TotalContainers { get; set; }
        public int CompleteContainers { get; set; }
        public string ReviewStatus { get; set; } = "Pending";
        public string FinalDecision { get; set; } = "Pending";
        public DateTime? LastReviewedAt { get; set; }
        public string? ReviewedBy { get; set; }
        public int ReviewedContainers { get; set; }
        public int NormalContainers { get; set; }
        public int AbnormalContainers { get; set; }
        public List<string> ContainerNumbers { get; set; } = new();
    }

    public class BLDetailsDto
    {
        public string MasterBlNumber { get; set; } = string.Empty;
        public List<ContainerInBLDto> Containers { get; set; } = new();
        public BLReviewSummary? CurrentReview { get; set; }
        public List<BLReviewSummary> ReviewHistory { get; set; } = new();
    }

    public class ContainerInBLDto
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public bool HasScanner { get; set; }
        public bool HasICUMS { get; set; }
        public bool HasImages { get; set; }
        public int ImageCount { get; set; }
        public int ScannerRecordCount { get; set; }
        public int ICUMSRecordCount { get; set; }
        public DateTime? ScanDate { get; set; }
        public string? CurrentDecision { get; set; }
        public string? CurrentComments { get; set; }
    }

    public class BLReviewSummary
    {
        public int Id { get; set; }
        public string ReviewStatus { get; set; } = string.Empty;
        public string FinalDecision { get; set; } = string.Empty;
        public string BLComments { get; set; } = string.Empty;
        public string ReviewedBy { get; set; } = string.Empty;
        public DateTime ReviewStartedAt { get; set; }
        public DateTime? ReviewCompletedAt { get; set; }
        public int TotalContainers { get; set; }
        public int ReviewedContainers { get; set; }
        public int NormalContainers { get; set; }
        public int AbnormalContainers { get; set; }
    }

    public class BLReviewSubmission
    {
        public int? Id { get; set; }
        public string MasterBlNumber { get; set; } = string.Empty;
        public List<ContainerDecisionSubmission> ContainerDecisions { get; set; } = new();
        public string BLComments { get; set; } = string.Empty;
        public string ReviewedBy { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
    }

    public class ContainerDecisionSubmission
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string Decision { get; set; } = "Pending";
        public string Comments { get; set; } = string.Empty;
    }

    public class BLReviewSaveResult
    {
        public int Id { get; set; }
        public string MasterBlNumber { get; set; } = string.Empty;
        public string ReviewStatus { get; set; } = string.Empty;
        public string FinalDecision { get; set; } = string.Empty;
        public int ReviewedContainers { get; set; }
        public int TotalContainers { get; set; }
        public string Message { get; set; } = string.Empty;
    }

    public class BLReviewHistoryItem
    {
        public int Id { get; set; }
        public string ReviewStatus { get; set; } = string.Empty;
        public string FinalDecision { get; set; } = string.Empty;
        public string BLComments { get; set; } = string.Empty;
        public string ReviewedBy { get; set; } = string.Empty;
        public DateTime ReviewStartedAt { get; set; }
        public DateTime? ReviewCompletedAt { get; set; }
    }

    public class BLReviewStatistics
    {
        public int TotalBLs { get; set; }
        public int PendingBLs { get; set; }
        public int InProgressBLs { get; set; }
        public int CompletedBLs { get; set; }
        public int TotalContainers { get; set; }
        public int CompleteContainers { get; set; }
    }

    public class ContainerCompletenessResult
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public bool IsComplete { get; set; }
    }
}

