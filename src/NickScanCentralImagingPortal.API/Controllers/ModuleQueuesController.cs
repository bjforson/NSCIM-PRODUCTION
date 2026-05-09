using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers;

// TODO Sprint 5G2 / B1+B3: once the AnalysisGroupStateMachine refactor lands across the 37
// call sites, expose a transitions/min rate from analysis_group_status_transitions here too.
// Don't implement now — the table is empty until Bridge B1 ships.

/// <summary>
/// Sprint 5G2 / Bridge B3 — observability for the v1 in-flight pipeline. Surfaces
/// AnalysisGroup queue depth grouped by status, plus the ICUMS Outbox file count,
/// in a single round-trip. Backs the future dashboard cards.
///
/// `[Authorize]` is intentional — anonymous endpoints in v1 return 401 since the
/// 2026-04 security rollout. Pair the manifest controller (`api/_module/manifest`)
/// shape — same prefix, different resource.
/// </summary>
[ApiController]
[Authorize]
[Route("api/_module/queues")]
public sealed class ModuleQueuesController : ControllerBase
{
    private readonly ApplicationDbContext _db;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ModuleQueuesController> _logger;

    public ModuleQueuesController(
        ApplicationDbContext db,
        IConfiguration configuration,
        ILogger<ModuleQueuesController> logger)
    {
        _db = db;
        _configuration = configuration;
        _logger = logger;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ModuleQueuesResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ModuleQueuesResponse>> GetQueueDepths(CancellationToken ct)
    {
        var asOfUtc = DateTime.UtcNow;

        // 1. One round-trip: GROUP BY status + (count, min CreatedAtUtc).
        var rows = await _db.AnalysisGroups
            .GroupBy(g => g.Status)
            .Select(g => new StatusGroup(g.Key, g.LongCount(), g.Min(x => x.CreatedAtUtc)))
            .ToListAsync(ct);

        var byStatus = rows.ToDictionary(r => r.Status, r => r);

        // 2. Map known statuses to queue names. Skip terminal + transient states
        //    (AgentProcessing is DA-claimed for seconds; Completed/Cancelled/Archived/Submitted/PartiallyCompleted are terminal-ish).
        var queues = new List<ModuleQueueRow>(capacity: 6)
        {
            BuildQueueRow(byStatus, AnalysisStatuses.Ready, "image_analysis_ready", asOfUtc),
            BuildQueueRow(byStatus, AnalysisStatuses.AnalystAssigned, "analyst_assigned", asOfUtc),
            BuildQueueRow(byStatus, AnalysisStatuses.AnalystCompleted, "audit_assignment", asOfUtc),
            BuildQueueRow(byStatus, AnalysisStatuses.AuditAssigned, "audit_review", asOfUtc),
            BuildQueueRow(byStatus, AnalysisStatuses.AuditCompleted, "submission", asOfUtc),
        };

        // 3. ICUMS Outbox depth — file count + oldest mtime in Data\ICUMS\Outbox\ICUMS\ICUMS_*.json.
        queues.Add(GetIcumsOutboxRow(asOfUtc));

        // 4. Phase B / B3 (2026-05-09): per-role userreadiness pool snapshot. The B2′-A
        //    investigation found that "audit queue empty" is an operational dead-mans-switch:
        //    when no userreadiness row has IsReady=true with a heartbeat within 60 min,
        //    AutoAssignByRoleAsync(Audit) returns silently and AGs in AnalystCompleted
        //    pile up. Surface this directly so dashboards can flag it without re-running
        //    the diagnostic SQL probe.
        var maxIdleMinutes = _configuration.GetValue<int>("ImageAnalysis:MaxIdleMinutesForReadiness", 60);
        var readinessCutoff = asOfUtc.AddMinutes(-maxIdleMinutes);
        var readinessRows = await _db.UserReadiness
            .GroupBy(r => r.Role)
            .Select(g => new RoleReadinessRow(
                g.Key,
                g.LongCount(),
                g.LongCount(r => r.IsReady),
                g.LongCount(r => r.IsReady && r.LastHeartbeat >= readinessCutoff),
                g.Where(r => r.IsReady).Max(r => (DateTime?)r.LastHeartbeat)))
            .ToListAsync(ct);

        // 5. Drift detection: AGs in audit-stage where the underlying CCS rows' WorkflowStage
        //    disagrees with the AG status. Catches the parallel-state-surface drift documented
        //    in the IAS-Design plan. Cheap O(N) — N is small in steady state.
        var driftCount = await _db.AnalysisGroups
            .Where(g => g.Status == AnalysisStatuses.AnalystCompleted
                     || g.Status == AnalysisStatuses.AuditAssigned)
            .Where(g => _db.AnalysisRecords
                .Where(r => r.GroupId == g.Id)
                .SelectMany(r => _db.ContainerCompletenessStatuses
                    .Where(c => c.ContainerNumber == r.ContainerNumber))
                .Any(c => c.WorkflowStage != "Audit"
                       && c.WorkflowStage != "PendingSubmission"
                       && c.WorkflowStage != "Submitted"
                       && c.WorkflowStage != "Completed"))
            .LongCountAsync(ct);

        return Ok(new ModuleQueuesResponse(queues, asOfUtc, driftCount, readinessRows));
    }

    private static ModuleQueueRow BuildQueueRow(
        IReadOnlyDictionary<string, StatusGroup> byStatus,
        string status,
        string queueName,
        DateTime asOfUtc)
    {
        if (!byStatus.TryGetValue(status, out var row))
        {
            return new ModuleQueueRow(queueName, 0, 0);
        }

        var ageSeconds = Math.Max(0d, (asOfUtc - row.OldestUtc).TotalSeconds);
        return new ModuleQueueRow(queueName, row.Depth, ageSeconds);
    }

    /// <summary>EF-translatable projection for the GROUP BY query.</summary>
    private sealed record StatusGroup(string Status, long Depth, DateTime OldestUtc);

    /// <summary>
    /// Phase B / B6 Live Pipeline (2026-05-09) — throughput aggregation over
    /// <c>analysis_group_status_transitions</c> for the last <paramref name="minutes"/> minutes.
    /// Powers the LivePipelinePanel sparkline + actor/transition tile counts.
    ///
    /// Three aggregations in one round-trip:
    ///   • byActor — GROUP BY actor (count desc)
    ///   • byTransition — GROUP BY (from_status, to_status) (count desc)
    ///   • byMinute — date_trunc('minute', occurred_at_utc), zero-filled across the full window
    ///
    /// Tenant scope is enforced by the <c>TenantConnectionInterceptor</c> setting
    /// <c>app.tenant_id</c> per connection — no extra WHERE needed here.
    /// </summary>
    [HttpGet("throughput")]
    [ProducesResponseType(typeof(ThroughputResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<ThroughputResponse>> GetThroughput(
        [FromQuery] int minutes = 15,
        CancellationToken ct = default)
    {
        // Clamp to [1, 120] — the contract caps the window so that one client can't
        // ask for "since the dawn of time" and starve the DB.
        if (minutes < 1) minutes = 1;
        if (minutes > 120) minutes = 120;

        var asOfUtc = DateTime.UtcNow;
        // Truncate to the minute so the zero-filled buckets line up with date_trunc().
        var nowMinute = new DateTime(asOfUtc.Year, asOfUtc.Month, asOfUtc.Day, asOfUtc.Hour, asOfUtc.Minute, 0, DateTimeKind.Utc);
        var windowStart = nowMinute.AddMinutes(-(minutes - 1));

        // Pull just the columns we aggregate over. Volume is bounded by the
        // (<=120-minute) window, which is sparse in steady state. Bucketing
        // client-side avoids relying on Npgsql's date_trunc translation and
        // keeps the SQL trivially shaped (single index scan on occurred_at_utc).
        // Postgres `timestamp` columns come back as Kind=Unspecified — re-stamp
        // to Utc so JSON serialisation includes the `Z` suffix the contract shows.
        var rawRows = await _db.Set<AnalysisGroupStatusTransition>()
            .AsNoTracking()
            .Where(t => t.OccurredAtUtc >= windowStart)
            .Select(t => new { t.OccurredAtUtc, t.Actor, t.FromStatus, t.ToStatus })
            .ToListAsync(ct);

        var rows = rawRows
            .Select(r => new TransitionAggregationRow(
                DateTime.SpecifyKind(r.OccurredAtUtc, DateTimeKind.Utc),
                r.Actor,
                r.FromStatus,
                r.ToStatus))
            .ToList();

        var byActor = rows
            .GroupBy(r => r.Actor)
            .Select(g => new ActorCount(g.Key, g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.Actor)
            .ToList();

        var byTransition = rows
            .GroupBy(r => new { r.FromStatus, r.ToStatus })
            .Select(g => new TransitionCount(g.Key.FromStatus, g.Key.ToStatus, g.Count()))
            .OrderByDescending(x => x.Count)
            .ThenBy(x => x.FromStatus)
            .ThenBy(x => x.ToStatus)
            .ToList();

        // Zero-fill every minute in the window so the sparkline doesn't gap.
        // Bucket = floor(occurred_at_utc to minute). Window is inclusive of both
        // endpoints — `minutes` slots total, oldest first.
        var bucketCounts = rows
            .GroupBy(r => new DateTime(r.OccurredAtUtc.Year, r.OccurredAtUtc.Month, r.OccurredAtUtc.Day,
                                       r.OccurredAtUtc.Hour, r.OccurredAtUtc.Minute, 0, DateTimeKind.Utc))
            .ToDictionary(g => g.Key, g => g.Count());

        var byMinute = new List<MinuteCount>(minutes);
        for (int i = 0; i < minutes; i++)
        {
            var bucket = windowStart.AddMinutes(i);
            bucketCounts.TryGetValue(bucket, out var count);
            byMinute.Add(new MinuteCount(bucket, count));
        }

        return Ok(new ThroughputResponse(minutes, asOfUtc, byActor, byTransition, byMinute));
    }

    /// <summary>
    /// Phase B / B6 Live Pipeline (2026-05-09) — last <paramref name="limit"/> AnalysisGroup
    /// transitions, newest first, joined to <c>analysisgroups</c> for the display
    /// <c>groupidentifier</c>. Backs the activity feed in LivePipelinePanel.razor.
    ///
    /// Tenant scope enforced by the <c>TenantConnectionInterceptor</c>.
    /// </summary>
    [HttpGet("recent")]
    [ProducesResponseType(typeof(RecentTransitionsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<RecentTransitionsResponse>> GetRecent(
        [FromQuery] int limit = 50,
        CancellationToken ct = default)
    {
        // Clamp to [1, 200] — the contract caps the page so a misbehaving client
        // can't ask for the entire transition log.
        if (limit < 1) limit = 1;
        if (limit > 200) limit = 200;

        var asOfUtc = DateTime.UtcNow;

        // Left-join semantics: an audit row may reference a group that was deleted
        // (cancellation paths could remove the AG, though current code retains
        // them) — fall back to empty string so the row still surfaces in the feed.
        // Postgres `timestamp` columns come back Kind=Unspecified; re-stamp Utc on
        // the way out to give the JSON output the `Z` suffix the contract shows.
        var rawTransitions = await (
            from t in _db.Set<AnalysisGroupStatusTransition>().AsNoTracking()
            orderby t.OccurredAtUtc descending, t.Id descending
            join g in _db.AnalysisGroups.AsNoTracking() on t.GroupId equals g.Id into groupJoin
            from g in groupJoin.DefaultIfEmpty()
            select new
            {
                t.Id,
                t.OccurredAtUtc,
                t.GroupId,
                GroupIdentifier = g != null ? g.GroupIdentifier : string.Empty,
                t.FromStatus,
                t.ToStatus,
                t.TriggerName,
                t.Actor,
                t.Reason,
                t.CorrelationId
            })
            .Take(limit)
            .ToListAsync(ct);

        var transitions = rawTransitions
            .Select(r => new TransitionEventDto(
                r.Id,
                DateTime.SpecifyKind(r.OccurredAtUtc, DateTimeKind.Utc),
                r.GroupId,
                r.GroupIdentifier ?? string.Empty,
                r.FromStatus,
                r.ToStatus,
                r.TriggerName,
                r.Actor,
                r.Reason,
                r.CorrelationId))
            .ToList();

        return Ok(new RecentTransitionsResponse(asOfUtc, transitions));
    }

    /// <summary>EF-translatable projection over the audit row.</summary>
    private sealed record TransitionAggregationRow(
        DateTime OccurredAtUtc,
        string Actor,
        string FromStatus,
        string ToStatus);

    private ModuleQueueRow GetIcumsOutboxRow(DateTime asOfUtc)
    {
        // Configured root is `C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox`; the actual
        // submission JSONs land in the `ICUMS` subdirectory and are named `ICUMS_*.json`.
        // TODO: factor this out to a shared OutboxPathProvider once a second consumer appears.
        var outboxRoot = _configuration["ICUMS:Submission:OutputFolder"]
            ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox";
        var outboxDir = Path.Combine(outboxRoot, "ICUMS");

        try
        {
            if (!Directory.Exists(outboxDir))
            {
                return new ModuleQueueRow("icums_outbox", 0, 0);
            }

            var files = Directory.EnumerateFiles(outboxDir, "ICUMS_*.json", SearchOption.TopDirectoryOnly).ToList();
            if (files.Count == 0)
            {
                return new ModuleQueueRow("icums_outbox", 0, 0);
            }

            var oldestMtime = files.Min(f => System.IO.File.GetLastWriteTimeUtc(f));
            var ageSeconds = Math.Max(0d, (asOfUtc - oldestMtime).TotalSeconds);
            return new ModuleQueueRow("icums_outbox", files.Count, ageSeconds);
        }
        catch (Exception ex)
        {
            // Don't fail the whole endpoint over a filesystem hiccup — return depth 0
            // and log; the AG queue depths are still useful on their own.
            _logger.LogWarning(ex, "Failed to enumerate ICUMS outbox at {OutboxDir}", outboxDir);
            return new ModuleQueueRow("icums_outbox", 0, 0);
        }
    }
}

public sealed record ModuleQueueRow(string Name, long Depth, double OldestAgeSeconds);

public sealed record RoleReadinessRow(
    string Role,
    long TotalUsers,
    long ReadyUsers,
    long ReadyRecent,
    DateTime? LatestReadyHeartbeat);

public sealed record ModuleQueuesResponse(
    IReadOnlyList<ModuleQueueRow> Queues,
    DateTime AsOfUtc,
    long DriftCount,
    IReadOnlyList<RoleReadinessRow> Readiness);

// ─────────────────────────────────────────────────────────────────────────────
// Phase B / B6 Live Pipeline (2026-05-09) — DTO contract per
// C:\Users\Administrator\.claude\plans\live-pipeline-api-contract.md §1.
//
// Agent 2 mirrors these inline in the Razor component, so casing/order/types
// MUST stay in sync. JSON serialiser handles PascalCase→camelCase.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record ActorCount(string Actor, int Count);

public sealed record TransitionCount(string FromStatus, string ToStatus, int Count);

public sealed record MinuteCount(DateTime MinuteUtc, int Count);

public sealed record ThroughputResponse(
    int WindowMinutes,
    DateTime AsOfUtc,
    IReadOnlyList<ActorCount> ByActor,
    IReadOnlyList<TransitionCount> ByTransition,
    IReadOnlyList<MinuteCount> ByMinute);

public sealed record TransitionEventDto(
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

public sealed record RecentTransitionsResponse(
    DateTime AsOfUtc,
    IReadOnlyList<TransitionEventDto> Transitions);
