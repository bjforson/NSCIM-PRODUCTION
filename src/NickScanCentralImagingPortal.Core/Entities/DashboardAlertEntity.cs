using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Persistent record of a dashboard alert raised by the in-process detection
    /// rules in <c>ImageAnalysisDashboardBroadcastService</c> and its peers.
    ///
    /// Sprint 5G3 / audit finding 8.25 — prior to 2026-05-05 the dashboard
    /// constructed in-memory <c>DashboardAlert</c> objects (Bottleneck,
    /// Performance, DataIntegrity) and broadcast them to connected SignalR
    /// clients only. If no operator had the dashboard open the alerts were lost,
    /// no email was sent, and no historical record was kept.
    ///
    /// This entity is the persistence target. <c>IDashboardAlertService.RaiseAsync</c>
    /// dedupes (by Type+Title within a 30-minute window), persists, and (for
    /// <c>Severity == "Critical"</c>) emails on-call via NickComms.Gateway.
    ///
    /// Tenancy: <c>TenantId</c> follows the phase-1 pattern — DB-side default
    /// <c>current_setting('app.tenant_id')::bigint</c> + a
    /// <c>tenant_isolation_dashboardalerts</c> RLS policy. The C# property is
    /// not required for writes (the DB default fills it) but is exposed for
    /// readability when debugging.
    /// </summary>
    [Table("dashboardalerts")]
    public class DashboardAlertEntity
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Stable string code naming the detection family. Current set:
        ///   - "Bottleneck"     — queue depth alerts (Ready / Audit / etc.)
        ///   - "Performance"    — stale assignments, lease cycle issues
        ///   - "DataIntegrity"  — denormalisation drift, NULL groupId, missing BOEDocumentId
        /// </summary>
        [Required]
        [StringLength(64)]
        [Column("type")]
        public string Type { get; set; } = string.Empty;

        /// <summary>
        /// "Info" / "Warning" / "Critical". Critical alerts trigger an
        /// outbound email via <c>INickCommsClient</c> on first creation
        /// (touch-only updates from the dedupe path do not re-send email).
        /// </summary>
        [Required]
        [StringLength(20)]
        [Column("severity")]
        public string Severity { get; set; } = "Warning";

        [Required]
        [StringLength(200)]
        [Column("title")]
        public string Title { get; set; } = string.Empty;

        [StringLength(2000)]
        [Column("description")]
        public string? Description { get; set; }

        /// <summary>
        /// Which BackgroundService raised it — e.g.
        /// "ImageAnalysisDashboardBroadcastService". Helps trace the rule
        /// back to source when a noisy alert needs tuning.
        /// </summary>
        [StringLength(200)]
        [Column("source")]
        public string? Source { get; set; }

        [Column("raisedatutc")]
        public DateTime RaisedAtUtc { get; set; } = DateTime.UtcNow;

        [Column("acknowledgedatutc")]
        public DateTime? AcknowledgedAtUtc { get; set; }

        [StringLength(100)]
        [Column("acknowledgedby")]
        public string? AcknowledgedBy { get; set; }

        /// <summary>
        /// Set when the email-on-Critical path completed successfully. Null
        /// when the alert is non-Critical, or when the email send failed
        /// (the failure is logged; the row is still persisted for the UI).
        /// </summary>
        [Column("emailsentatutc")]
        public DateTime? EmailSentAtUtc { get; set; }

        /// <summary>
        /// Phase-1 tenancy column. DB has
        /// <c>NOT NULL DEFAULT current_setting('app.tenant_id')::bigint</c>
        /// so EF inserts can omit this and the database fills it. RLS policy
        /// <c>tenant_isolation_dashboardalerts</c> enforces row visibility.
        ///
        /// <c>DatabaseGeneratedOption.Computed</c> tells EF to omit this column
        /// from INSERTs and read it back, so the DB DEFAULT actually fires
        /// instead of EF sending a literal <c>0</c> from the CLR field.
        /// </summary>
        [Column("tenant_id")]
        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public long TenantId { get; set; }
    }
}
