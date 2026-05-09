using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    /// <summary>
    /// Sole-writer facade for <c>AnalysisGroup.Status</c>. Wraps
    /// <see cref="AnalysisStatusValidator"/> (the existing transition
    /// table in <c>Core</c>) with mandatory enforcement, an append-only
    /// audit row (<see cref="AnalysisGroupStatusTransition"/>), and a
    /// single transactional write.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>Project placement.</b> This facade lives in
    /// <c>NickScanCentralImagingPortal.Infrastructure</c> rather than
    /// <c>Core</c> because it depends on EF Core
    /// (<c>DbContext</c>, <c>SaveChangesAsync</c>,
    /// <c>DbUpdateConcurrencyException</c>). <c>Core</c> stays EF-free
    /// per the existing v1 layering convention — it holds pure-domain
    /// helpers like <see cref="AnalysisStatusValidator"/> and the entity
    /// classes (annotated with <c>[Table]</c> / <c>[Column]</c> from
    /// <c>System.ComponentModel.DataAnnotations</c>, which is not EF
    /// itself).
    /// </para>
    /// <para>
    /// <b>Purpose.</b> Sprint 5G2 / Bridge B1 — see plan
    /// <c>i-need-an-analysis-abundant-pnueli.md</c> §B1. The v1
    /// codebase has 37 distinct write sites of
    /// <c>analysisgroups.status</c>, of which 34 bypass
    /// <see cref="AnalysisStatusValidator"/> entirely. The plan is to
    /// route ALL writes through this facade so:
    /// <list type="number">
    ///   <item>Illegal transitions throw, not silently corrupt state.</item>
    ///   <item>Every transition leaves an auditable row.</item>
    ///   <item>Future v2 cutover can replay the audit trail.</item>
    /// </list>
    /// This file lands the facade additively. The 37 call-site
    /// migrations land in a separate change set so each can be
    /// individually reviewed for its actor/reason context.
    /// </para>
    /// <para>
    /// <b>Tracking requirement.</b> The <paramref>group</paramref> must be tracked by the
    /// supplied DbContext — i.e. loaded via
    /// <c>db.AnalysisGroups.AsTracking().Where(...)</c> rather than the
    /// default <c>NoTracking</c> path. v1's <c>ApplicationDbContext</c>
    /// is NoTracking by default (memory:
    /// <c>feedback_application_dbcontext_notracking_default.md</c>); a
    /// caller that loads via the default and mutates would silently
    /// no-op the SaveChanges. The facade verifies the entity is
    /// tracked and throws <see cref="InvalidOperationException"/> with
    /// a clear message when it isn't.
    /// </para>
    /// <para>
    /// <b>Idempotent transitions.</b> If
    /// <paramref name="toStatus"/> equals the group's current status,
    /// the call is a no-op (returns
    /// <see cref="AnalysisGroupTransitionResult.Idempotent"/> and
    /// writes nothing). Matches the existing
    /// <see cref="AnalysisStatusValidator.IsValidTransition"/>
    /// behaviour.
    /// </para>
    /// <para>
    /// <b>Tenant scope.</b> Tenant id is read from the tracked group;
    /// if zero (legacy rows pre-tenancy), the audit row gets the DB
    /// default of <c>1</c>. Connection-level
    /// <c>app.tenant_id</c> (set by <c>TenantConnectionInterceptor</c>)
    /// is the authoritative scope for the audit row's RLS policy.
    /// </para>
    /// </remarks>
    public static class AnalysisGroupStateMachine
    {
        /// <summary>
        /// Atomically transition <paramref name="group"/> from its
        /// current status to <paramref name="toStatus"/>, validating via
        /// <see cref="AnalysisStatusValidator"/> and writing both the
        /// new status and an audit row in one
        /// <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>.
        /// </summary>
        /// <param name="db">
        /// The <see cref="DbContext"/> tracking <paramref name="group"/>.
        /// Must have <see cref="DbSet{TEntity}"/> registered for
        /// <see cref="AnalysisGroup"/> and
        /// <see cref="AnalysisGroupStatusTransition"/>.
        /// </param>
        /// <param name="group">
        /// The tracked <see cref="AnalysisGroup"/> to transition.
        /// Loading via <c>AsTracking()</c> (or the equivalent) is
        /// required.
        /// </param>
        /// <param name="toStatus">
        /// Target status — must match a constant in
        /// <see cref="Core.Models.AnalysisStatuses"/>.
        /// </param>
        /// <param name="triggerName">
        /// Convention-named cause of the transition (e.g.
        /// <c>"AnalystSubmittedFindings"</c>). Recorded in the audit
        /// row's <see cref="AnalysisGroupStatusTransition.TriggerName"/>.
        /// </param>
        /// <param name="actor">
        /// Who initiated the transition — user id for humans, service
        /// name for background workers (<c>"DECISION-AGENT"</c>,
        /// <c>"SYSTEM-HOUSEKEEPING"</c>, etc.). Required.
        /// </param>
        /// <param name="reason">
        /// Free-text justification (≤512 chars). Required — empty
        /// string throws <see cref="ArgumentException"/>.
        /// </param>
        /// <param name="correlationId">
        /// Optional correlation id propagated to the audit row.
        /// </param>
        /// <param name="ct">Cancellation token.</param>
        /// <returns>
        /// <see cref="AnalysisGroupTransitionResult.Applied"/> when the
        /// transition was committed,
        /// <see cref="AnalysisGroupTransitionResult.Idempotent"/> when
        /// the group was already in the target status (no row written).
        /// Illegal transitions throw <see cref="InvalidOperationException"/>
        /// — they never silently no-op.
        /// </returns>
        /// <exception cref="ArgumentNullException">
        /// <paramref name="db"/>, <paramref name="group"/>,
        /// <paramref name="toStatus"/>, <paramref name="triggerName"/>,
        /// <paramref name="actor"/>, or <paramref name="reason"/> is null.
        /// </exception>
        /// <exception cref="ArgumentException">
        /// <paramref name="actor"/>, <paramref name="reason"/>, or
        /// <paramref name="triggerName"/> is empty.
        /// </exception>
        /// <exception cref="InvalidOperationException">
        /// The transition from the group's current status to
        /// <paramref name="toStatus"/> is not in
        /// <see cref="AnalysisStatusValidator"/>'s legal table, OR the
        /// group is not tracked by <paramref name="db"/>.
        /// </exception>
        public static async Task<AnalysisGroupTransitionResult> TransitionAsync(
            DbContext db,
            AnalysisGroup group,
            string toStatus,
            string triggerName,
            string actor,
            string reason,
            string? correlationId = null,
            CancellationToken ct = default)
        {
            if (db is null) throw new ArgumentNullException(nameof(db));
            if (group is null) throw new ArgumentNullException(nameof(group));
            if (toStatus is null) throw new ArgumentNullException(nameof(toStatus));
            if (triggerName is null) throw new ArgumentNullException(nameof(triggerName));
            if (actor is null) throw new ArgumentNullException(nameof(actor));
            if (reason is null) throw new ArgumentNullException(nameof(reason));

            if (string.IsNullOrWhiteSpace(triggerName))
                throw new ArgumentException("Trigger name is required.", nameof(triggerName));
            if (string.IsNullOrWhiteSpace(actor))
                throw new ArgumentException(
                    "Actor is required (the audit log's value depends on this being populated).",
                    nameof(actor));
            if (string.IsNullOrWhiteSpace(reason))
                throw new ArgumentException(
                    "Reason is required (the audit log's value depends on this being populated).",
                    nameof(reason));

            // Verify the entity is being tracked. NoTracking returns
            // Detached for any entity loaded via the default path, and
            // a SaveChangesAsync wouldn't actually persist the Status
            // mutation — the canonical v1 footgun (memory:
            // feedback_application_dbcontext_notracking_default.md).
            var entry = db.Entry(group);
            if (entry.State == EntityState.Detached)
            {
                throw new InvalidOperationException(
                    $"AnalysisGroup {group.Id} is detached. Load with .AsTracking() (or call db.AnalysisGroups.Update(group) before TransitionAsync) — see memory feedback_application_dbcontext_notracking_default.md.");
            }

            var fromStatus = group.Status ?? string.Empty;

            // Idempotent — caller asks for the status the group's
            // already in. Match AnalysisStatusValidator.IsValidTransition's
            // behaviour by treating this as a no-op rather than
            // writing a degenerate audit row.
            if (string.Equals(fromStatus, toStatus, StringComparison.OrdinalIgnoreCase))
            {
                return AnalysisGroupTransitionResult.Idempotent;
            }

            // Validate. Throws InvalidOperationException with a clear
            // "valid targets are X, Y, Z" message if illegal.
            AnalysisStatusValidator.ValidateTransition(fromStatus, toStatus, group.Id.ToString());

            // Atomically: update the group + write the audit row + save.
            // Tenant id mirrors the group's; if zero (pre-tenancy
            // legacy), the DB default of 1 takes over on insert.
            group.Status = toStatus;
            group.UpdatedAtUtc = DateTime.UtcNow;

            var transition = new AnalysisGroupStatusTransition
            {
                TenantId = ResolveTenantId(group),
                GroupId = group.Id,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                TriggerName = triggerName,
                Actor = actor,
                Reason = TrimReason(reason),
                CorrelationId = correlationId,
                OccurredAtUtc = DateTime.UtcNow
            };
            db.Set<AnalysisGroupStatusTransition>().Add(transition);

            await db.SaveChangesAsync(ct).ConfigureAwait(false);

            // Phase B / B6 Live Pipeline (2026-05-09): broadcast the freshly-persisted
            // transition to any subscriber after SaveChanges returns. The DB row is the
            // source of truth — broadcast failures are best-effort. Outside the EF
            // transaction by construction (SaveChanges has returned). The subscriber is
            // expected to fan out to SignalR (IHubContext<ImageAnalysisDashboardHub>),
            // wired in API/Program.cs to avoid an Infrastructure→API dependency.
            var subscriber = Transitioned;
            if (subscriber is not null)
            {
                var groupIdentifier = group.GroupIdentifier ?? string.Empty;
                var payload = new AnalysisGroupTransitionEvent(
                    transition.Id,
                    transition.OccurredAtUtc,
                    transition.GroupId,
                    groupIdentifier,
                    transition.FromStatus,
                    transition.ToStatus,
                    transition.TriggerName,
                    transition.Actor,
                    transition.Reason,
                    transition.CorrelationId);

                try
                {
                    await subscriber(payload, ct).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    // Best-effort — never let a broadcast failure surface to the caller.
                    // The polling endpoints will eventually-consistently catch up.
                    TryGetLogger(db)?.LogWarning(
                        ex,
                        "AnalysisGroupStateMachine.Transitioned subscriber threw for group {GroupId} ({FromStatus}→{ToStatus}); broadcast skipped",
                        transition.GroupId, transition.FromStatus, transition.ToStatus);
                }
            }

            return AnalysisGroupTransitionResult.Applied;
        }

        /// <summary>
        /// Phase B / B6 Live Pipeline — single-subscriber hook fired after every
        /// successful <see cref="TransitionAsync"/> SaveChanges. Wired once at app
        /// startup in API/Program.cs to fan out the event over SignalR via
        /// <c>IHubContext&lt;ImageAnalysisDashboardHub&gt;</c>. Lives here (not in
        /// Core/API) because the DI container sits in API and the static facade is
        /// the only place every transition funnels through.
        /// </summary>
        /// <remarks>
        /// <para>Single-subscriber by design — assignment replaces the existing
        /// handler. Use <c>AnalysisGroupStateMachine.Transitioned += handler</c>
        /// only if a multi-cast is genuinely needed; in v1 we wire one fan-out to
        /// the dashboard hub and stop. The contract is "best-effort" — exceptions
        /// thrown by the subscriber are caught and logged at Warning, then
        /// swallowed so the caller's transition still returns Applied.</para>
        /// <para>Threading: invoked AFTER <see cref="DbContext.SaveChangesAsync(CancellationToken)"/>
        /// returns and OUTSIDE the EF transaction. Subscribers should perform their
        /// own fire-and-forget if they want to avoid blocking the caller.</para>
        /// </remarks>
        public static event Func<AnalysisGroupTransitionEvent, CancellationToken, Task>? Transitioned;

        private static ILogger? TryGetLogger(DbContext db)
        {
            try
            {
                var loggerFactory = db.GetService<ILoggerFactory>();
                return loggerFactory?.CreateLogger("AnalysisGroupStateMachine");
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Convenience overload for callers that don't have a tracked
        /// group handy — looks up by id, applies the transition,
        /// returns. Loads via <c>AsTracking()</c> so the
        /// SaveChangesAsync persists. Returns
        /// <see cref="AnalysisGroupTransitionResult.NotFound"/> when no
        /// group exists with the given id.
        /// </summary>
        public static async Task<AnalysisGroupTransitionResult> TransitionByIdAsync(
            DbContext db,
            Guid groupId,
            string toStatus,
            string triggerName,
            string actor,
            string reason,
            string? correlationId = null,
            CancellationToken ct = default)
        {
            if (db is null) throw new ArgumentNullException(nameof(db));

            var group = await db.Set<AnalysisGroup>()
                .AsTracking()
                .FirstOrDefaultAsync(g => g.Id == groupId, ct)
                .ConfigureAwait(false);

            if (group is null)
            {
                return AnalysisGroupTransitionResult.NotFound;
            }

            return await TransitionAsync(
                db, group, toStatus, triggerName, actor, reason, correlationId, ct)
                .ConfigureAwait(false);
        }

        private static long ResolveTenantId(AnalysisGroup group)
        {
            // AnalysisGroup doesn't currently have a TenantId property
            // (memory: phase-1 ITenantOwned adoption is a follow-up).
            // The DB-level DEFAULT 1 backfills new audit rows; we
            // explicitly stamp 1 here so EF doesn't send a NULL that
            // the column rejects.
            //
            // When the entity-side ITenantOwned adoption lands, replace
            // this with `group.TenantId` directly.
            _ = group;
            return 1L;
        }

        private static string TrimReason(string reason)
        {
            // Hard cap at the column's length — the validator could
            // throw, but truncation is friendlier for system actors
            // that may produce long error messages.
            const int max = 512;
            return reason.Length <= max ? reason : reason.Substring(0, max);
        }
    }

    /// <summary>
    /// Outcome of an <see cref="AnalysisGroupStateMachine.TransitionAsync"/>
    /// call. Returned by reference — illegal transitions throw rather
    /// than returning a sentinel, since the caller has a logic bug.
    /// </summary>
    public enum AnalysisGroupTransitionResult
    {
        /// <summary>The status was changed and an audit row was written.</summary>
        Applied = 0,

        /// <summary>The group was already in the target status; nothing was written.</summary>
        Idempotent = 1,

        /// <summary>
        /// Only returned by
        /// <see cref="AnalysisGroupStateMachine.TransitionByIdAsync"/>
        /// when no group exists with the supplied id.
        /// </summary>
        NotFound = 2
    }

    /// <summary>
    /// Phase B / B6 Live Pipeline (2026-05-09) — payload broadcast to
    /// <see cref="AnalysisGroupStateMachine.Transitioned"/> subscribers after every
    /// successful transition SaveChanges. Mirrors the <c>recent</c> endpoint row
    /// shape so the same DTO can flow over SignalR and HTTP without divergence
    /// (per the live-pipeline-api-contract.md §1.3).
    /// </summary>
    /// <param name="Id">Bigserial id of the audit row in <c>analysis_group_status_transitions</c>.</param>
    /// <param name="OccurredAtUtc">UTC moment the transition was committed.</param>
    /// <param name="GroupId">FK to the <see cref="AnalysisGroup"/> being transitioned.</param>
    /// <param name="GroupIdentifier">Display-form group identifier (BL/HouseBL/logical group).</param>
    /// <param name="FromStatus">Status the group left.</param>
    /// <param name="ToStatus">Status the group moved into.</param>
    /// <param name="TriggerName">Conventional trigger name (matches the audit row's <c>trigger_name</c>).</param>
    /// <param name="Actor">Who initiated the transition (matches the audit row's <c>actor</c>).</param>
    /// <param name="Reason">Free-text justification (≤512 chars).</param>
    /// <param name="CorrelationId">Optional request-correlation id, when present.</param>
    public sealed record AnalysisGroupTransitionEvent(
        long Id,
        DateTime OccurredAtUtc,
        Guid GroupId,
        string GroupIdentifier,
        string FromStatus,
        string ToStatus,
        string TriggerName,
        string Actor,
        string Reason,
        string? CorrelationId);
}
