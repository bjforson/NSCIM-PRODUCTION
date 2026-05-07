using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    /// <summary>
    /// Append-only audit row written by <c>AnalysisGroupStateMachine</c>
    /// (in <c>NickScanCentralImagingPortal.Infrastructure.Data</c> —
    /// can't <c>see cref</c> across the project boundary since
    /// <c>Core</c> doesn't reference Infrastructure)
    /// on every successful <c>analysisgroups.status</c> transition.
    /// One row per transition; rows are never updated or deleted.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sprint 5G2 / Bridge B1 — see plan
    /// <c>i-need-an-analysis-abundant-pnueli.md</c> §B1. Backs the v1
    /// hardening that promotes <c>AnalysisStatusValidator</c> from
    /// advisory to mandatory by routing every <c>g.Status =</c> write
    /// through a single facade. Until that refactor lands across all
    /// 37 call sites, this table records only the transitions the
    /// facade has been wired into.
    /// </para>
    /// <para>
    /// Append-only by role design — the migration grants
    /// <c>nscim_app</c> only <c>SELECT + INSERT</c>. The table is RLS-
    /// enforced + FORCE ROW LEVEL SECURITY (matches the 2026-04-25
    /// hardening posture).
    /// </para>
    /// <para>
    /// Per-tenant indexes back the per-group timeline view + the global
    /// "recent transitions" feed; a partial index on system actors
    /// (DECISION-AGENT, SYSTEM-HOUSEKEEPING, etc.) backs forensic
    /// queries when triaging an automated-actor incident.
    /// </para>
    /// </remarks>
    [Table("analysis_group_status_transitions")]
    public class AnalysisGroupStatusTransition
    {
        /// <summary>Stable primary key — bigserial.</summary>
        [Key]
        [Column("id")]
        public long Id { get; set; }

        /// <summary>
        /// Owning tenant. Defaulted to 1 by the DB (matching every
        /// other phase-1 table). EF inserts may set it explicitly when
        /// the entity is touched outside a connection that has
        /// <c>app.tenant_id</c> resolved; the RLS policy keeps things
        /// honest.
        /// </summary>
        [Column("tenant_id")]
        public long TenantId { get; set; }

        /// <summary>FK to <see cref="AnalysisGroup.Id"/> — the AG this transition belongs to.</summary>
        [Required]
        [Column("group_id")]
        public Guid GroupId { get; set; }

        /// <summary>
        /// The status the group left. Empty string for the synthetic
        /// "creation" transition the facade writes on first save (so
        /// the audit trail captures intake without a special case).
        /// </summary>
        [Required]
        [StringLength(40)]
        [Column("from_status")]
        public string FromStatus { get; set; } = string.Empty;

        /// <summary>The status the group moved into.</summary>
        [Required]
        [StringLength(40)]
        [Column("to_status")]
        public string ToStatus { get; set; } = string.Empty;

        /// <summary>
        /// Conventional name of what triggered the transition — useful
        /// for forensic queries that group by cause. Examples:
        /// <c>"AnalystSubmittedFindings"</c>,
        /// <c>"DecisionAgentAutoApproved"</c>,
        /// <c>"JanitorReleasedExpiredLease"</c>,
        /// <c>"SubmissionWorkflowCompleted"</c>.
        /// </summary>
        [Required]
        [StringLength(64)]
        [Column("trigger_name")]
        public string TriggerName { get; set; } = string.Empty;

        /// <summary>
        /// Who initiated the transition. User id (Guid as string) for
        /// human actors; service name for system actors
        /// (<c>"DECISION-AGENT"</c>, <c>"SYSTEM-HOUSEKEEPING"</c>,
        /// <c>"QueueJanitor"</c>, <c>"ZombieAnalysisGroupSweeper"</c>).
        /// Required.
        /// </summary>
        [Required]
        [StringLength(128)]
        [Column("actor")]
        public string Actor { get; set; } = string.Empty;

        /// <summary>
        /// Free-text justification (≤512 chars). Required — the audit
        /// log's usefulness depends on this being populated. System
        /// actors pass tags like <c>"lease-expired"</c>; human actors
        /// pass whatever the UI captured.
        /// </summary>
        [Required]
        [StringLength(512)]
        [Column("reason")]
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Optional correlation id propagated from the originating
        /// request (the <c>X-Correlation-Id</c> header / Serilog
        /// <c>correlation-id</c> property). Lets ops trace a transition
        /// back to the originating HTTP request, scanner event, or
        /// background-job invocation.
        /// </summary>
        [StringLength(128)]
        [Column("correlation_id")]
        public string? CorrelationId { get; set; }

        /// <summary>UTC moment the transition occurred. DB default <c>now()</c>.</summary>
        [Column("occurred_at_utc")]
        public DateTime OccurredAtUtc { get; set; } = DateTime.UtcNow;
    }
}
