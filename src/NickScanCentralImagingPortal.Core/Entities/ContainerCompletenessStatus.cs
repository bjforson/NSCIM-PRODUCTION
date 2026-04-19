using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Tracks the completeness status of container data between scanners and ICUMS
    /// </summary>
    public class ContainerCompletenessStatus
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        /// <summary>
        /// Unique inspection/scan ID from the scanner system
        /// Each scan creates a new InspectionId, allowing same container to be tracked multiple times
        /// Stored as string to accommodate both FS6000 (Guid) and ASE (int)
        /// </summary>
        [StringLength(50)]
        public string? InspectionId { get; set; }

        public DateTime ScanDate { get; set; }

        public bool HasICUMSData { get; set; } = false;

        public DateTime? ICUMSDataDate { get; set; }

        /// <summary>
        /// The actual BOE Document ID from ICUMS_Downloads database
        /// Stores the primary BOE document ID when ICUMS data is found
        /// Replaces the need for ContainerBOERelations mapper
        /// </summary>
        public int? BOEDocumentId { get; set; }

        /// <summary>
        /// Pre-computed ClearanceType from BOE document (CMR, IMEX, or null)
        /// Stored here for fast API access without needing to lookup BOE document
        /// </summary>
        [StringLength(10)]
        public string? ClearanceType { get; set; } // 'CMR', 'IMEX', or null

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Missing"; // 'Complete', 'Missing', 'Requested', 'Failed'

        // ✅ NEW: Pre-computed completeness scores for fast API access
        public int ScannerDataCompleteness { get; set; } = 0; // 0-100%
        public int ICUMSDataCompleteness { get; set; } = 0; // 0-100%
        public int ImageDataCompleteness { get; set; } = 0; // 0-100%
        public int OverallCompleteness { get; set; } = 0; // 0-100% (average of above)

        // ✅ NEW: Data availability flags
        public bool HasScannerData { get; set; } = false;
        public bool HasImageData { get; set; } = false;

        // ✅ NEW: Consolidated cargo fields
        public bool IsConsolidated { get; set; } = false;
        public int? TotalHouseBLs { get; set; }
        public int? CompleteHouseBLs { get; set; }

        public string? ConsolidationDetails { get; set; } // JSON for consolidated House BL details, or "N container(s)" for non-consolidated

        [StringLength(100)]
        public string? GroupIdentifier { get; set; } // Container# (consolidated) or BOE# (non-consolidated)

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(1000)]
        public string? ErrorMessage { get; set; }

        public int RetryCount { get; set; } = 0;

        public DateTime? LastCheckedAt { get; set; }

        /// <summary>
        /// Workflow stage for stage-based workflow progression
        /// Values: 'Pending', 'ImageAnalysis', 'Audit', 'PendingSubmission', 'Submitted'
        /// </summary>
        [StringLength(50)]
        public string WorkflowStage { get; set; } = "Pending";
    }
}
