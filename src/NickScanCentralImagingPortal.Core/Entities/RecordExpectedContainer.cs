using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// 1.14.0 — One row per container that a <see cref="RecordCompletenessStatus"/> expects
    /// to receive, per what ICUMS has declared for that record.
    ///
    /// Pre-populated at record creation time with <see cref="Status"/> = <c>AwaitingScan</c>.
    /// The <see cref="Services.RecordCompleteness.RecordReconciliationWorker"/> transitions
    /// rows through the state machine as scanner events arrive:
    ///
    ///     AwaitingScan  ─── scan event arrives ──► Pending
    ///     Pending       ─── images linked      ──► Ready
    ///     Ready         ─── analyst decides    ──► Decided
    ///     Decided       ─── submitted to ICUMS ──► Submitted
    ///
    ///     Pending       ─── 72h timeout        ──► NoImageAvailable
    ///     AwaitingScan  ─── parent archived    ──► NoScanReceived
    /// </summary>
    [Table("RecordExpectedContainers")]
    public class RecordExpectedContainer
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int RecordId { get; set; }

        [ForeignKey(nameof(RecordId))]
        public virtual RecordCompletenessStatus? Record { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        /// <summary>
        /// AwaitingScan | Pending | Ready | Decided | Submitted | NoImageAvailable | NoScanReceived
        /// </summary>
        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "AwaitingScan";

        /// <summary>Logical FK to nickscan_downloads.boedocuments.id for this container's row.</summary>
        public int? BoeDocumentId { get; set; }

        [StringLength(100)]
        public string? HouseBl { get; set; }

        [StringLength(500)]
        public string? ConsigneeName { get; set; }

        /// <summary>Inspection id from the scanner event, set when AwaitingScan → Pending fires.</summary>
        [StringLength(50)]
        public string? InspectionId { get; set; }

        /// <summary>Which scanner reported the scan event. Set when AwaitingScan → Pending fires.</summary>
        [StringLength(20)]
        public string? ScannerType { get; set; }

        /// <summary>
        /// Canonical source image identity bound when scanner evidence arrives.
        /// </summary>
        public Guid? ScanImageAssetId { get; set; }

        public int? OriginalScanRecordId { get; set; }

        [StringLength(500)]
        public string? SourceContainerLabel { get; set; }

        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;
        public DateTime? ScannedAtUtc { get; set; }
        public DateTime? BecameReadyUtc { get; set; }
        public DateTime? DecidedAtUtc { get; set; }
    }
}
