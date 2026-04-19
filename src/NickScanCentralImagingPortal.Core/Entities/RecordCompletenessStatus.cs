using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// 1.14.0 — The canonical record-level operational state.
    ///
    /// A "record" in NSCIM is a customs declaration (BOE) from ICUMS and its full
    /// expected container set. This entity is proactively ingested from the ICUMS
    /// downloads database by RecordReconciliationWorker, so that the operator view
    /// of a shipment reflects what ICUMS actually says exists — not just what has
    /// happened to be scanned so far.
    ///
    /// Keyed by (DeclarationNumber, ScannerType). DeclarationNumber is the
    /// globally-unique customs filing identifier and is always used as the primary
    /// operational identifier — master BL is NOT used because multiple unrelated
    /// customers can share a shipping contract.
    ///
    /// For the "used cars in one container" case (Pattern A), multiple declarations
    /// may share a single physical container. Those records carry the same
    /// <see cref="ContainerGroupKey"/> (the container number) so the UI can collapse
    /// them into a single image-level decision while keeping each declaration's
    /// manifest snapshot and audit trail separate.
    /// </summary>
    [Table("RecordCompletenessStatuses")]
    public class RecordCompletenessStatus
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// The customs declaration (BOE) number. Primary operational identifier.
        /// Globally unique per ICUMS filing event; monotonic; more stable than master BL.
        /// </summary>
        [Required]
        [StringLength(100)]
        public string DeclarationNumber { get; set; } = string.Empty;

        /// <summary>IM | EX. (CMR pre-declaration manifests are not records yet — they're upgraded when the declaration arrives.)</summary>
        [StringLength(20)]
        public string? ClearanceType { get; set; }

        /// <summary>WCO regime code (40 = home use, 70 = warehousing, 80 = inward processing, 90 = other, 10/21 = export).</summary>
        [StringLength(20)]
        public string? RegimeCode { get; set; }

        /// <summary>Logical FK to nickscan_downloads.boedocuments.id. First BOE row for this declaration.</summary>
        public int? PrimaryBoeDocumentId { get; set; }

        /// <summary>Vessel rotation number from the ICUMS BOE. Useful for display, not a uniqueness key.</summary>
        [StringLength(100)]
        public string? RotationNumber { get; set; }

        /// <summary>
        /// Master BL / shipping contract number. Recorded for display and cross-reference only.
        /// NOT used as an identifier — see class documentation.
        /// </summary>
        [StringLength(100)]
        public string? BlNumber { get; set; }

        /// <summary>
        /// Pattern A grouping key. NULL for most records. For "used cars in one container"
        /// cases where multiple declarations share a single physical container, set to the
        /// container number so the UI can collapse sibling declarations into a single
        /// image-level decision.
        /// </summary>
        [StringLength(150)]
        public string? ContainerGroupKey { get; set; }

        /// <summary>
        /// Scanner that this record is associated with, if known. NULL means "any/unknown"
        /// — the reconciliation worker will bind a scanner type the first time a scan event
        /// arrives for any of the expected containers.
        /// </summary>
        [StringLength(20)]
        public string? ScannerType { get; set; }

        // ── Rollup counts (derived; recomputed by the reconciliation worker) ──

        public int TotalExpectedContainers { get; set; }
        public int ContainersAwaitingScan { get; set; }
        public int ContainersScanned { get; set; }
        public int ContainersReady { get; set; }
        public int ContainersDecided { get; set; }
        public int ContainersSubmitted { get; set; }
        public int ContainersNoImage { get; set; }
        public int ContainersNoScan { get; set; }

        /// <summary>
        /// Pending | PartiallyReady | Ready | InAnalysis | InAudit | PendingSubmission
        /// | Submitted | Completed | Archived | Failed
        /// </summary>
        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Pending";

        [Required]
        [StringLength(50)]
        public string WorkflowStage { get; set; } = "Pending";

        // ── Timestamps ────────────────────────────────────────────────────────

        /// <summary>When the record was first ingested into NSCIM operational state.</summary>
        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When a container was most recently added to this record (either at initial
        /// creation or via an ICUMS amendment). This is the clock that drives the
        /// 30-day archive rule.
        /// </summary>
        public DateTime? LastNewContainerAtUtc { get; set; }

        public DateTime? FirstReadyAtUtc { get; set; }
        public DateTime? ArchivedAtUtc { get; set; }

        /// <summary>StaleNoNewContainers | Bypassed | Merged | ManualClose</summary>
        [StringLength(50)]
        public string? ArchivalReason { get; set; }

        public DateTime? LastCheckedAtUtc { get; set; }

        /// <summary>
        /// JSON metadata for Pattern A records: list of sibling declarations sharing the
        /// same physical container. Shape: [{"declarationNumber":"...","consignee":"...","houseBl":"..."}]
        /// NULL for all other records.
        /// </summary>
        [Column(TypeName = "jsonb")]
        public string? DeclarationsJson { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        // Navigation — EF will populate via the child FK
        public virtual ICollection<RecordExpectedContainer> ExpectedContainers { get; set; } = new List<RecordExpectedContainer>();
    }
}
