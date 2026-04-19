namespace NickScanCentralImagingPortal.Core.DTOs.BLReview
{
    /// <summary>
    /// DTO for submitting/saving a BL review
    /// </summary>
    public class BLReviewSubmission
    {
        public int? Id { get; set; } // Null for new, populated for updates
        public string MasterBlNumber { get; set; } = string.Empty;
        public List<ContainerDecisionSubmission> ContainerDecisions { get; set; } = new();
        public string BLComments { get; set; } = string.Empty;
        public string ReviewedBy { get; set; } = string.Empty;
        public bool IsComplete { get; set; } // True if all containers reviewed
    }

    public class ContainerDecisionSubmission
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string Decision { get; set; } = "Pending"; // Normal, Abnormal, Pending
        public string Comments { get; set; } = string.Empty;
    }
}

