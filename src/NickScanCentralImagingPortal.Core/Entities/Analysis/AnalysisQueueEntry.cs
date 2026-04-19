using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    /// <summary>
    /// Pre-computed queue entry for each active assignment. Maintained by
    /// <see cref="ReadyGroupsCacheService"/> so that GetMyAssignments can
    /// read a single table instead of joining 7-8 tables on every call.
    ///
    /// One row per active AnalysisAssignment. Created on assignment,
    /// deleted on release/expiry/completion. Reconciled by housekeeping.
    /// </summary>
    [Table("analysisqueueentries")]
    public class AnalysisQueueEntry
    {
        /// <summary>FK to AnalysisAssignments.Id (1-to-1, natural key)</summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int AssignmentId { get; set; }

        // ── Assignment fields ──
        [Required][StringLength(100)]
        public string AssignedTo { get; set; } = string.Empty;
        [Required][StringLength(20)]
        public string Role { get; set; } = string.Empty;
        public DateTime? LeaseUntilUtc { get; set; }
        public DateTime AssignmentCreatedAtUtc { get; set; }

        // ── Group fields (denormalized) ──
        public Guid GroupId { get; set; }
        [Required][StringLength(150)]
        public string GroupIdentifier { get; set; } = string.Empty;
        [StringLength(20)]
        public string? ScannerType { get; set; }
        [Required][StringLength(30)]
        public string GroupStatus { get; set; } = string.Empty;
        public DateTime GroupCreatedAtUtc { get; set; }
        public DateTime? GroupUpdatedAtUtc { get; set; }

        // ── Container counts (denormalized from AnalysisRecords) ──
        public int ContainerCount { get; set; }
        public string ContainersJson { get; set; } = "[]";

        // ── Container completeness (denormalized) ──
        public int? ContainersWithImages { get; set; }
        public int? ContainersWithoutImages { get; set; }

        // ── Decision counts ──
        public int DecidedCount { get; set; }

        // ── Partial completion ──
        public int? TotalContainerCount { get; set; }
        public int? SubmittedContainerCount { get; set; }
        public int? PendingContainerCount { get; set; }
        public DateTime? PartiallyCompletedDate { get; set; }

        // ── BOE consolidation (computed once at creation) ──
        public bool IsConsolidated { get; set; }

        // ── Queue metadata ──
        public DateTime QueuedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime LastRefreshedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
