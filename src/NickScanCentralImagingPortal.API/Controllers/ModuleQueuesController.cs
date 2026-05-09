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
