using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers;

/// <summary>
/// Phase B / B5 (2026-05-09) — operator self-service diagnostic surface. Wraps
/// the SQL probes the dev team had to run by hand from <c>C:\temp\nscim-probe</c>
/// during the 2026-05-08/09 audit-queue investigation, exposing them as JSON the
/// monitoring UI can poll on a 60 s timer.
///
/// Three GET endpoints, each one round-trip per backing query:
///   * /completeness  — RecordCompletenessStatus + ContainerCompletenessStatus snapshot
///   * /da-posture    — DecisionAgentSettings singleton + recent DA transitions
///   * /drift-counts  — three drift signals: CCS orphans, fyco↔regime mismatches,
///                      audit-stage AGs without an active assignment row.
///
/// `[Authorize]` is intentional — anonymous endpoints in v1 return 401 since the
/// 2026-04 security rollout. Mirrors the route style of <c>ModuleQueuesController</c>.
/// </summary>
[ApiController]
[Authorize]
[Route("api/_module/diagnostics")]
public sealed class ModuleDiagnosticsController : ControllerBase
{
    // The /drift-counts endpoint compares fyco direction against BOE regime
    // direction. Mirror the canonical import set kept in RegimeDirectionMap so
    // the diagnostic agrees with the live validation rule. Source-of-truth
    // lives in ContainerScanQueue.cs (RegimeDirectionMap.IsImport); we
    // duplicate the membership set here to keep the endpoint's two-DB join
    // self-contained.
    private static readonly HashSet<string> ImportRegimes = new(StringComparer.OrdinalIgnoreCase)
    {
        "40", "50", "61", "62", "70", "90"
    };

    // /da-posture surfaces the most recent DA transitions so operators can see
    // what the agent has actually been doing. Bound to a small window so the
    // payload stays compact.
    private const int RecentDaTransitionLimit = 25;

    private readonly ApplicationDbContext _db;
    private readonly IcumDownloadsDbContext _icumsDb;
    private readonly ILogger<ModuleDiagnosticsController> _logger;

    public ModuleDiagnosticsController(
        ApplicationDbContext db,
        IcumDownloadsDbContext icumsDb,
        ILogger<ModuleDiagnosticsController> logger)
    {
        _db = db;
        _icumsDb = icumsDb;
        _logger = logger;
    }

    /// <summary>
    /// Wraps <c>C:\temp\nscim-probe\B7CompletenessSnap.cs</c>. Counts of
    /// <see cref="RecordCompletenessStatus"/> by status and
    /// <see cref="ContainerCompletenessStatus"/> by workflow stage, plus the
    /// "incomplete" tally (rows whose individual completeness scores are
    /// below 100).
    /// </summary>
    [HttpGet("completeness")]
    [ProducesResponseType(typeof(CompletenessSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<CompletenessSnapshot>> GetCompleteness(CancellationToken ct)
    {
        var asOfUtc = DateTime.UtcNow;

        // RecordCompletenessStatus — group by status, count, oldest+newest UpdatedAtUtc.
        var recordsByStatus = await _db.RecordCompletenessStatuses
            .AsNoTracking()
            .GroupBy(r => r.Status)
            .Select(g => new RecordStatusRow(
                g.Key,
                g.LongCount(),
                g.Min(x => (DateTime?)x.UpdatedAtUtc),
                g.Max(x => (DateTime?)x.UpdatedAtUtc)))
            .OrderByDescending(r => r.Count)
            .ToListAsync(ct);

        // ContainerCompletenessStatus — group by workflow stage.
        var containersByStage = await _db.ContainerCompletenessStatuses
            .AsNoTracking()
            .GroupBy(c => c.WorkflowStage)
            .Select(g => new ContainerStageRow(
                g.Key,
                g.LongCount(),
                g.Min(x => (DateTime?)x.UpdatedAt),
                g.Max(x => (DateTime?)x.UpdatedAt)))
            .OrderByDescending(c => c.Count)
            .ToListAsync(ct);

        // Incomplete tallies — single round-trip via conditional aggregations.
        // Counts rows whose individual completeness score is < 100, plus a
        // total so the UI can render percentages.
        var incomplete = await _db.ContainerCompletenessStatuses
            .AsNoTracking()
            .GroupBy(_ => 1)
            .Select(g => new IncompleteTotals(
                g.LongCount(c => c.ScannerDataCompleteness < 100),
                g.LongCount(c => c.ICUMSDataCompleteness < 100),
                g.LongCount(c => c.ImageDataCompleteness < 100),
                g.LongCount(c => c.OverallCompleteness < 100),
                g.LongCount()))
            .FirstOrDefaultAsync(ct)
            ?? new IncompleteTotals(0, 0, 0, 0, 0);

        return Ok(new CompletenessSnapshot(asOfUtc, recordsByStatus, containersByStage, incomplete));
    }

    /// <summary>
    /// Wraps <c>C:\temp\nscim-probe\B2VerifyDeploy.cs</c> §6 (DA-actor recent
    /// transitions) plus the singleton <see cref="DecisionAgentSettings"/>
    /// snapshot. Operators use this to confirm whether DA is in shadow mode
    /// (no auto-advancement) and to spot-check what it's been doing.
    /// </summary>
    [HttpGet("da-posture")]
    [ProducesResponseType(typeof(DaPostureSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<DaPostureSnapshot>> GetDaPosture(CancellationToken ct)
    {
        var asOfUtc = DateTime.UtcNow;

        // The settings table is enforced as a singleton (id=1) by the
        // configuration controller; if for some reason no row exists we
        // surface that as null so the UI can flag the misconfig.
        var settings = await _db.DecisionAgentSettings
            .AsNoTracking()
            .OrderBy(s => s.Id)
            .Select(s => new DaSettingsRow(
                s.Id,
                s.Enabled,
                s.ShadowMode,
                s.AllowNormalDecisions,
                s.AllowAbnormalDecisions,
                s.NormalThreshold,
                s.AbnormalThreshold,
                s.ProcessingDepthDecision,
                s.ProcessingDepthAudit,
                s.ProcessingDepthSubmission,
                s.MaxGroupsPerCycle,
                s.CreatedAtUtc,
                s.UpdatedAtUtc))
            .FirstOrDefaultAsync(ct);

        // Most-recent DA transitions, newest first. Keep this short (25 rows)
        // — the operator wants to see "is DA active and what is it touching?"
        // not a forensic feed (the LivePipelinePanel covers that).
        var recentRows = await _db.AnalysisGroupStatusTransitions
            .AsNoTracking()
            .Where(t => t.Actor == "DECISION-AGENT")
            .OrderByDescending(t => t.OccurredAtUtc)
            .ThenByDescending(t => t.Id)
            .Take(RecentDaTransitionLimit)
            .Select(t => new
            {
                t.Id,
                t.OccurredAtUtc,
                t.GroupId,
                t.FromStatus,
                t.ToStatus,
                t.TriggerName,
                t.Reason
            })
            .ToListAsync(ct);

        // Postgres `timestamp` columns come back Kind=Unspecified — re-stamp
        // to Utc so JSON serialisation includes the `Z` suffix.
        var recentDaTransitions = recentRows
            .Select(r => new DaTransitionRow(
                r.Id,
                DateTime.SpecifyKind(r.OccurredAtUtc, DateTimeKind.Utc),
                r.GroupId,
                r.FromStatus,
                r.ToStatus,
                r.TriggerName,
                r.Reason))
            .ToList();

        return Ok(new DaPostureSnapshot(asOfUtc, settings, recentDaTransitions));
    }

    /// <summary>
    /// Wraps <c>C:\temp\nscim-probe\B7FycoBlastRadius.cs</c> + two adjacent
    /// drift signals. Surfaces three counts that should all read 0 in a
    /// healthy steady state:
    ///   * CCS rows missing both <c>GroupIdentifier</c> and
    ///     <c>BOEDocumentId</c> — the post-Sprint-2C orphan shape.
    ///   * Active Primary BOE relations whose latest FS6000 fyco indicates
    ///     EXPORT but the linked BOE is in an import regime (40/50/61/62/70/90).
    ///   * AGs in audit-stage with no active <c>AnalysisAssignment</c> row —
    ///     the dead-mans-switch the queue page warns about.
    /// </summary>
    [HttpGet("drift-counts")]
    [ProducesResponseType(typeof(DriftSnapshot), StatusCodes.Status200OK)]
    public async Task<ActionResult<DriftSnapshot>> GetDriftCounts(CancellationToken ct)
    {
        var asOfUtc = DateTime.UtcNow;

        // 1. CCS orphans — rows that fell out of the workflow because both
        //    grouping keys are null. Cheap predicate; single index scan.
        var ccsOrphans = await _db.ContainerCompletenessStatuses
            .AsNoTracking()
            .CountAsync(c => c.GroupIdentifier == null && c.BOEDocumentId == null, ct);

        // 2. Audit-stage AGs without an active assignment — when this is > 0
        //    plus the audit pool is empty, AGs sit indefinitely (the v1 audit-
        //    queue dead-mans-switch). The Module Queues panel surfaces the
        //    pool side; this surfaces the orphan AG side.
        var auditStageOrphans = await _db.AnalysisGroups
            .AsNoTracking()
            .Where(g => g.Status == AnalysisStatuses.AnalystCompleted
                     || g.Status == AnalysisStatuses.AuditAssigned)
            .Where(g => !_db.AnalysisAssignments
                .Any(a => a.GroupId == g.Id && a.State == "Active"))
            .CountAsync(ct);

        // 3. Fyco-vs-regime mismatch — two-DB problem. Materialise the
        //    fyco=EXPORT primary relations from nickscan_production, then
        //    intersect with the import-regime BOEs from nickscan_downloads.
        //    The B7 probe walks every relation; we do the same here but
        //    tighten the SQL-side filter so the in-memory count loop is
        //    bounded by the number of fyco-tagged active relations (small
        //    in steady state — current snapshot ~371 rows).
        var fycoMismatches = await CountFycoMismatchesAsync(ct);

        return Ok(new DriftSnapshot(asOfUtc, ccsOrphans, fycoMismatches, auditStageOrphans));
    }

    /// <summary>
    /// Two-DB walk: pull active Primary CBR rows + their latest FS6000
    /// fyco from <c>nickscan_production</c>, classify with the canonical
    /// <see cref="FycoClassifier.IsExport"/>, then intersect with import-
    /// regime BOE ids from <c>nickscan_downloads</c>. Returns the count of
    /// (fyco=EXPORT) ∩ (regime ∈ ImportRegimes).
    ///
    /// Avoids dblink / FDW; the join is in-memory which is fine because the
    /// fyco-tagged active-relation set is bounded (~371 rows post-flip).
    /// </summary>
    private async Task<long> CountFycoMismatchesAsync(CancellationToken ct)
    {
        try
        {
            // Pull the (boe-id, fyco) candidates for fyco-non-empty active
            // primary relations. EF can't translate FycoClassifier so we
            // narrow to "fyco is non-empty" in SQL and classify in-memory.
            var fycoCandidates = await (
                from cbr in _db.ContainerBOERelations.AsNoTracking()
                where cbr.IsActive && cbr.RelationType == "Primary"
                let latestFyco = _db.FS6000Scans
                    .Where(f => f.ContainerNumber == cbr.ContainerNumber)
                    .OrderByDescending(f => f.ScanTime)
                    .Select(f => f.FycoPresent)
                    .FirstOrDefault()
                where latestFyco != null && latestFyco != string.Empty
                select new { BoeId = cbr.ICUMSBOEId, Fyco = latestFyco })
                .ToListAsync(ct);

            // Classify with the single source of truth — keep behaviour in
            // lockstep with the live ContainerValidationService rule.
            var fycoExportBoeIds = fycoCandidates
                .Where(c => FycoClassifier.IsExport(c.Fyco))
                .Select(c => c.BoeId)
                .Distinct()
                .ToArray();

            if (fycoExportBoeIds.Length == 0) return 0L;

            // Cross-DB lookup. EF's `Contains` on an array is translated to
            // an `= ANY()` against an int[] parameter, so this stays a single
            // round-trip with a single index seek per matched id.
            var importBoeCount = await _icumsDb.BOEDocuments
                .AsNoTracking()
                .Where(b => fycoExportBoeIds.Contains(b.Id))
                .Where(b => b.RegimeCode != null && ImportRegimes.Contains(b.RegimeCode))
                .CountAsync(ct);

            return importBoeCount;
        }
        catch (Exception ex)
        {
            // Keep the endpoint useful when one DB is degraded — surface 0
            // for the mismatch count and log so ops can correlate.
            _logger.LogWarning(ex, "Failed to compute fyco/regime mismatch count");
            return 0L;
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// DTO contract — Razor component mirrors these inline. JSON serialiser handles
// PascalCase → camelCase. Order/types must stay in sync with the consumer.
// ─────────────────────────────────────────────────────────────────────────────

public sealed record RecordStatusRow(
    string Status,
    long Count,
    DateTime? OldestUtc,
    DateTime? NewestUtc);

public sealed record ContainerStageRow(
    string WorkflowStage,
    long Count,
    DateTime? OldestUtc,
    DateTime? NewestUtc);

public sealed record IncompleteTotals(
    long ScannerIncomplete,
    long IcumsIncomplete,
    long ImageIncomplete,
    long OverallIncomplete,
    long Total);

public sealed record CompletenessSnapshot(
    DateTime AsOfUtc,
    IReadOnlyList<RecordStatusRow> RecordsByStatus,
    IReadOnlyList<ContainerStageRow> ContainersByStage,
    IncompleteTotals IncompleteTotals);

public sealed record DaSettingsRow(
    int Id,
    bool Enabled,
    bool ShadowMode,
    bool AllowNormalDecisions,
    bool AllowAbnormalDecisions,
    double NormalThreshold,
    double AbnormalThreshold,
    bool ProcessingDepthDecision,
    bool ProcessingDepthAudit,
    bool ProcessingDepthSubmission,
    int MaxGroupsPerCycle,
    DateTime CreatedAtUtc,
    DateTime? UpdatedAtUtc);

public sealed record DaTransitionRow(
    long Id,
    DateTime OccurredAtUtc,
    Guid GroupId,
    string FromStatus,
    string ToStatus,
    string TriggerName,
    string Reason);

public sealed record DaPostureSnapshot(
    DateTime AsOfUtc,
    DaSettingsRow? Settings,
    IReadOnlyList<DaTransitionRow> RecentDaTransitions);

public sealed record DriftSnapshot(
    DateTime AsOfUtc,
    long CcsOrphans,
    long FycoMismatches,
    long AuditStageOrphans);
