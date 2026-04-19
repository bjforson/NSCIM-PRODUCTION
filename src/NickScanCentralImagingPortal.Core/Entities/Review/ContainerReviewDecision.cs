using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Review
{
    /// <summary>
    /// Represents a review decision for an individual container within a BL
    /// </summary>
    public class ContainerReviewDecision
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [ForeignKey(nameof(BLReviewRecord))]
        public int BLReviewRecordId { get; set; }

        [Required]
        [MaxLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        /// <summary>
        /// Decision: Normal, Abnormal, Pending
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Decision { get; set; } = "Pending";

        /// <summary>
        /// Container-specific comments
        /// </summary>
        [MaxLength(1000)]
        public string Comments { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string ReviewedBy { get; set; } = string.Empty;

        public DateTime? ReviewedAt { get; set; }

        /// <summary>
        /// Data availability flags for this container
        /// </summary>
        public bool HasScanner { get; set; }

        public bool HasICUMS { get; set; }

        public bool HasImages { get; set; }

        /// <summary>
        /// Scanner type: FS6000, ASE, HeimannSmith, etc.
        /// </summary>
        [MaxLength(50)]
        public string ScannerType { get; set; } = string.Empty;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation property
        public virtual BLReviewRecord BLReviewRecord { get; set; } = null!;
    }
}

