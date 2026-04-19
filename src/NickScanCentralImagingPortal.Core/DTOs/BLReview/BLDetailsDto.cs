namespace NickScanCentralImagingPortal.Core.DTOs.BLReview
{
    /// <summary>
    /// DTO for detailed BL information (review dialog)
    /// </summary>
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
}

