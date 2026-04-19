using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Persistent record of a flagged container-to-BOE matching anomaly.
    ///
    /// Replaces the historical "log a warning and move on" approach in
    /// ContainerCompletenessService with a queryable, resolvable record. Each row
    /// is one flag against one container; a single container may carry multiple
    /// open flags simultaneously (e.g. both NullDeliveryPlace and DuplicateImage).
    ///
    /// Created by the matching pipeline when an anomaly is detected, and resolved
    /// either by the admin Match Correction Tool (manual unmatch / rematch /
    /// confirm) or implicitly by a downstream re-evaluation that no longer
    /// triggers the flag.
    ///
    /// Use cases:
    ///   - Driver list for the admin /validation/match-corrections page (shows
    ///     all unresolved flags ordered by severity)
    ///   - Audit trail for who confirmed/unmatched/rematched what and why
    ///   - Operational metric: count of open flags by type, time-to-resolution
    /// </summary>
    [Table("MatchQualityFlags")]
    public class MatchQualityFlag
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        /// <summary>
        /// Scanner that produced the scan being flagged. Helps the admin filter
        /// flags by location (Tema vs Takoradi) without joining on scan tables.
        /// </summary>
        [StringLength(20)]
        public string? ScannerType { get; set; }

        /// <summary>
        /// Live link to the BOE document that was matched (or attempted-matched)
        /// at the moment the flag was raised. Nullable because some flags fire
        /// before any BOE is selected (e.g. when no BOE is found at all).
        /// </summary>
        public int? BOEDocumentId { get; set; }

        /// <summary>
        /// Anomaly category. Stable string code, not enum, so future flag types
        /// can be added without a migration. Current set:
        ///   - "NullDeliveryPlace"  — BOE has no DeliveryPlace; location gate cannot verify
        ///   - "FycoMismatch"       — scan FycoPresent contradicts BOE ClearanceType
        ///   - "DuplicateImage"     — multiple containers share the same FS6000Image filename
        ///   - "ManualFlag"         — admin manually flagged a match for review
        /// </summary>
        [Required]
        [StringLength(64)]
        public string FlagType { get; set; } = string.Empty;

        /// <summary>
        /// "Warning" or "Critical". Critical flags block the match outright;
        /// warnings allow the match through but surface it for human review.
        /// </summary>
        [Required]
        [StringLength(20)]
        public string Severity { get; set; } = "Warning";

        /// <summary>
        /// Free-text explanation captured at flag time. Includes the specific
        /// values that triggered the flag (FycoPresent value, BOE clearance type,
        /// duplicate filename, etc.) so the admin doesn't have to dig.
        /// </summary>
        [StringLength(1000)]
        public string? Description { get; set; }

        /// <summary>
        /// True once an admin (or an automatic re-evaluation) has dispositioned
        /// the flag. Resolved flags are kept for audit, not deleted.
        /// </summary>
        public bool IsResolved { get; set; }

        [StringLength(100)]
        public string? ResolvedBy { get; set; }

        public DateTime? ResolvedAt { get; set; }

        /// <summary>
        /// What the admin chose. Stable string code, not enum:
        ///   - "Confirmed"  — match was correct despite the anomaly
        ///   - "Unmatched"  — ContainerBOERelation removed, completeness reset
        ///   - "Rematched"  — old relation removed, new relation created against a different BOE
        ///   - "AutoCleared" — re-evaluation no longer detects the anomaly
        /// </summary>
        [StringLength(20)]
        public string? Resolution { get; set; }

        /// <summary>
        /// Free-text rationale the admin typed when resolving. Pairs with
        /// Resolution to make the audit trail useful months later.
        /// </summary>
        [StringLength(1000)]
        public string? ResolutionNotes { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
