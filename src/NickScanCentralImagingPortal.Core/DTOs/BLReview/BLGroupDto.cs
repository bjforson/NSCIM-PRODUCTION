namespace NickScanCentralImagingPortal.Core.DTOs.BLReview
{
    /// <summary>
    /// DTO for BL group summary (list view)
    /// </summary>
    public class BLGroupDto
    {
        public string MasterBlNumber { get; set; } = string.Empty;
        public int TotalContainers { get; set; }
        public int CompleteContainers { get; set; }
        public string ReviewStatus { get; set; } = "Pending"; // Pending, InProgress, Completed
        public string FinalDecision { get; set; } = "Pending"; // Normal, Abnormal, Pending
        public DateTime? LastReviewedAt { get; set; }
        public string? ReviewedBy { get; set; }
        public int ReviewedContainers { get; set; }
        public int NormalContainers { get; set; }
        public int AbnormalContainers { get; set; }
        public List<string> ContainerNumbers { get; set; } = new();
    }
}

