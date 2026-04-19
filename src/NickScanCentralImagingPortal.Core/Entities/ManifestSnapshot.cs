using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Frozen-in-time copy of the customs manifest as it stood at the moment an
    /// analyst made an inspection decision.
    ///
    /// Why this exists (AI training flywheel — Gap 0): NSCIM's analyst decisions
    /// are joined to manifest data via a foreign key into a separate ICUMS read
    /// database. ICUMS is mutable — manifests can be amended, re-downloaded, or
    /// purged after an analyst's decision is recorded. Without a snapshot, every
    /// decision row is a pointer to a moving target, and any future training
    /// pipeline that wants the (image, manifest, decision) triple cannot
    /// reproduce the manifest state at decision time.
    ///
    /// This snapshot fixes that. On every analyst SaveDecision, a row is written
    /// here with the manifest fields needed for AI training (description, HS
    /// codes, quantities, values, origin, parties) plus a RawManifestJson blob
    /// containing the complete ICUMS payload for forensic reproducibility. The
    /// FK to ImageAnalysisDecision makes the join trivial; the live BOEDocumentId
    /// is preserved for cross-reference but is not the load-bearing link.
    ///
    /// Both the security and revenue branches of the flywheel benefit, but
    /// revenue assurance training is the load-bearing case: a model that detects
    /// "declared as plastics, actually electronics" needs to know what the
    /// declaration said at the moment the analyst flagged the image, not what
    /// it says now.
    /// </summary>
    [Table("ManifestSnapshots")]
    public class ManifestSnapshot
    {
        [Key]
        public long Id { get; set; }

        /// <summary>
        /// FK to the analyst decision this snapshot was captured for. One snapshot
        /// per decision; cascade-aware index ensures fast lookup from the decision
        /// row when assembling training data.
        /// </summary>
        public int ImageAnalysisDecisionId { get; set; }

        /// <summary>
        /// The live link to the BOE document in the ICUMS read database. Kept for
        /// cross-reference / debugging only — the snapshot fields below are the
        /// authoritative manifest state for training.
        /// </summary>
        public int? BOEDocumentId { get; set; }

        /// <summary>
        /// UTC timestamp captured when the snapshot was written. Should be within
        /// milliseconds of the decision's ReviewedAt.
        /// </summary>
        public DateTime SnapshotTakenAtUtc { get; set; } = DateTime.UtcNow;

        // ── Container / consignment identifiers ─────────────────────────────────
        [StringLength(50)]
        public string? ContainerNumber { get; set; }

        [StringLength(20)]
        public string? ScannerType { get; set; }

        [StringLength(100)]
        public string? MasterBlNumber { get; set; }

        [StringLength(100)]
        public string? HouseBlNumber { get; set; }

        [StringLength(100)]
        public string? RotationNumber { get; set; }

        [StringLength(100)]
        public string? DeclarationNumber { get; set; }

        [StringLength(50)]
        public string? RegimeCode { get; set; }

        [StringLength(10)]
        public string? ClearanceType { get; set; }

        // ── Declared cargo (the load-bearing fields for revenue assurance) ─────
        public string? DeclaredGoodsDescription { get; set; }

        /// <summary>
        /// JSON array of HS codes drawn from the declared manifest line items.
        /// Stored as JSON rather than normalised so the snapshot is fully
        /// self-contained — a downstream training job can read everything it
        /// needs from this single row.
        /// </summary>
        public string? DeclaredHsCodesJson { get; set; }

        /// <summary>
        /// JSON array of quantity / unit pairs drawn from the declared manifest
        /// line items.
        /// </summary>
        public string? DeclaredQuantitiesJson { get; set; }

        /// <summary>
        /// JSON array of value / currency pairs drawn from the declared manifest
        /// line items. Used by undervaluation models.
        /// </summary>
        public string? DeclaredValuesJson { get; set; }

        public int? DeclaredLineItemCount { get; set; }

        public decimal? TotalDeclaredFob { get; set; }

        [StringLength(10)]
        public string? FobCurrency { get; set; }

        public decimal? TotalDeclaredDutyPaid { get; set; }

        public decimal? TotalDeclaredWeight { get; set; }

        // ── Routing & parties ───────────────────────────────────────────────────
        [StringLength(100)]
        public string? CountryOfOrigin { get; set; }

        [StringLength(200)]
        public string? DeliveryPlace { get; set; }

        [StringLength(500)]
        public string? ImporterName { get; set; }

        [StringLength(500)]
        public string? ImporterAddress { get; set; }

        [StringLength(500)]
        public string? ConsigneeName { get; set; }

        [StringLength(500)]
        public string? ConsigneeAddress { get; set; }

        [StringLength(500)]
        public string? ShipperName { get; set; }

        // ── Risk signals (preserved at decision time so future model training
        //    can correlate human decisions with the risk signals visible to them)
        [StringLength(20)]
        public string? CrmsLevel { get; set; }

        public bool? IsConsolidated { get; set; }

        // ── Forensic full payload ──────────────────────────────────────────────
        /// <summary>
        /// Complete JSON dump of the ICUMS manifest record at the moment of
        /// decision. Acts as a tier-2 backup for any field the snapshot schema
        /// doesn't yet capture explicitly. Sized for typical manifests; large
        /// payloads still fit comfortably in a Postgres text column.
        /// </summary>
        public string? RawManifestJson { get; set; }

        /// <summary>
        /// Source of the snapshot. "live_capture" for the normal SaveDecision
        /// path, "backfill" for the one-off historical sweep, "no_data" for rows
        /// where the ICUMS link existed but the manifest had been purged before
        /// the snapshot service could read it.
        /// </summary>
        [StringLength(20)]
        public string Source { get; set; } = "live_capture";

        // ── Navigation ─────────────────────────────────────────────────────────
        [ForeignKey(nameof(ImageAnalysisDecisionId))]
        public ImageAnalysisDecision? ImageAnalysisDecision { get; set; }
    }
}
