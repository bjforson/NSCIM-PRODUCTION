using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Review
{
    /// <summary>
    /// Represents a Bill of Lading (BL) review record
    /// Groups containers by MasterBlNumber for comprehensive review
    /// </summary>
    public class BLReviewRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string MasterBlNumber { get; set; } = string.Empty;

        public DateTime ReviewStartedAt { get; set; }

        public DateTime? ReviewCompletedAt { get; set; }

        [Required]
        [MaxLength(100)]
        public string ReviewedBy { get; set; } = string.Empty;

        /// <summary>
        /// Review status: Pending, InProgress, Completed
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string ReviewStatus { get; set; } = "Pending";

        /// <summary>
        /// Final decision: Normal, Abnormal, Pending
        /// Auto-calculated: If any container is Abnormal → BL is Abnormal
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string FinalDecision { get; set; } = "Pending";

        /// <summary>
        /// BL-level comments (overall assessment)
        /// </summary>
        [MaxLength(2000)]
        public string BLComments { get; set; } = string.Empty;

        /// <summary>
        /// Total number of containers in this BL
        /// </summary>
        public int TotalContainers { get; set; }

        /// <summary>
        /// Number of containers that have been reviewed
        /// </summary>
        public int ReviewedContainers { get; set; }

        /// <summary>
        /// Number of containers marked as Normal
        /// </summary>
        public int NormalContainers { get; set; }

        /// <summary>
        /// Number of containers marked as Abnormal
        /// </summary>
        public int AbnormalContainers { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation property
        public virtual ICollection<ContainerReviewDecision> ContainerDecisions { get; set; } = new List<ContainerReviewDecision>();
    }
}

