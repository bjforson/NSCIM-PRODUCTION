using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    [Table("AnalysisGroups")]
    public class AnalysisGroup
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(150)]
        public string GroupIdentifier { get; set; } = string.Empty; // e.g., BL/HouseBL/Logical Group (display; may have date suffix)

        /// <summary>
        /// Normalized GroupIdentifier for cross-table joins (no date suffix).
        /// Matches ContainerCompletenessStatus.GroupIdentifier format.
        /// </summary>
        [StringLength(150)]
        public string? NormalizedGroupIdentifier { get; set; }

        [StringLength(50)]
        public string? GroupType { get; set; } // e.g., BL, Container, VIN

        [StringLength(20)]
        public string? ScannerType { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Ready";

        public int Priority { get; set; } = 0;

        // ✅ NEW: Fields for tracking partially completed records
        public DateTime? PartiallyCompletedDate { get; set; } // When record was marked as partially completed
        public int? TotalContainerCount { get; set; } // Total containers in group
        public int? SubmittedContainerCount { get; set; } // Containers submitted to ICUMS
        public int? PendingContainerCount { get; set; } // Containers waiting for images

        // Wave Processing fields
        /// <summary>
        /// LEGACY (pre-1.15.0): FK to the old AnalysisParentGroups table. Kept for backward
        /// compat and still dual-written by wave processing. Use
        /// <see cref="RecordCompletenessStatusId"/> as the canonical parent going forward.
        /// Scheduled for removal in a future release once all consumers migrate.
        /// </summary>
        [Obsolete("1.17.0: use RecordCompletenessStatusId as the canonical parent. ParentGroupId will be dropped in a future release.")]
        public Guid? ParentGroupId { get; set; }
        public int? WaveNumber { get; set; }

        /// <summary>
        /// 1.15.0 — FK to the new RecordCompletenessStatus table (nickscan_production).
        /// Nullable for backward compat: groups created before 1.15.0 may not be linked.
        /// When populated, this is the canonical parent; ParentGroupId (AnalysisParentGroup)
        /// is the legacy fallback. 1.17.0 will drop the legacy column.
        /// </summary>
        public int? RecordCompletenessStatusId { get; set; }

        [StringLength(50)]
        public string? WaveCreatedReason { get; set; } // InitialBatch, NewImages, Timeout, AutoClose

        /// <summary>AI-generated cargo summary text (persisted for reuse across the app)</summary>
        [StringLength(2000)]
        public string? AiCargoSummary { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }
    }
}


