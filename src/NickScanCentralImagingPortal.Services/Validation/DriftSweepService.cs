using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Validation
{
    /// <summary>
    /// Item 7 (2026-05-09) — periodic drift sweep that surfaces three classes of
    /// silent integrity issue surfaced during the B2'-A/B/C analyst-audit
    /// triage. The sweep is OBSERVATION ONLY: it counts, logs, and (when a
    /// threshold is exceeded) raises a single dashboardalerts row. It does not
    /// fix anything — that's the job of the actual repair tools.
    ///
    /// Three counts emitted per cycle:
    ///
    ///   1. Orphan audit-stage AGs — <c>AnalysisGroup.Status</c> in
    ///      <c>AnalystCompleted</c> or <c>AuditAssigned</c> with no active
    ///      <c>AnalysisAssignments</c> row of <c>Role='Audit'</c>. This is the
    ///      same probe that found <c>MSBU3196923</c> on 2026-05-09 — when
    ///      <c>UserReadiness</c> has no auditor with <c>IsReady=true</c> +
    ///      recent heartbeat the orchestrator skips audit assignment, leaving
    ///      AGs in <c>AnalystCompleted</c> indefinitely (the "audit-queue
    ///      empty = userreadiness dead-mans-switch" pattern).
    ///
    ///   2. CCS denormalisation drift — <c>ContainerCompletenessStatus</c>
    ///      rows with <c>GroupIdentifier IS NULL OR BOEDocumentId IS NULL</c>
    ///      where the container has an active <c>ContainerBOERelations</c>
    ///      row. These are the rows the upstream ICUMS pipeline left
    ///      half-populated; they cause "audit queue empty when AG is in
    ///      AnalystCompleted" because the audit fallback chain reads CCS by
    ///      GroupIdentifier (see AuditReviewController.cs:229-244 — the three
    ///      CCS-GroupIdentifier filters from Phase B / B2'-C).
    ///
    ///   3. Long-stale audit queue — AGs with <c>Status='AnalystCompleted'</c>
    ///      for more than 24h. This is a strict superset of count #1 (an AG
    ///      can be long-stale without orphan audit assignments) but the
    ///      separate count surfaces the velocity/age of the audit queue
    ///      independent of the assignment topology.
    ///
    /// Output mode:
    ///   - Always: a Serilog Warning line with all three counts. The line is
    ///     keyed <c>[DRIFT-SWEEP]</c> for easy log filtering.
    ///   - Conditional: when ANY count exceeds <c>DriftSweepHighCountThreshold</c>
    ///     (default 5), raise a single <c>dashboardalerts</c> row of
    ///     <c>type='DriftSweepHighCounts'</c>, severity <c>Warning</c>. Idempotent
    ///     on a content hash of the (orphan, denorm, stale) tuple — re-firing
    ///     for the same shape collapses onto the existing
    ///     <c>RaiseAsync</c> dedupe path (Type+Title within 30 min) so we
    ///     don't spam ops.
    ///
    /// Disable via <c>Validation:DriftSweepEnabled=false</c>. Default true.
    /// Cadence: <c>Validation:DriftSweepIntervalHours</c>, default 24.
    /// Threshold: <c>Validation:DriftSweepHighCountThreshold</c>, default 5.
    ///
    /// Lifecycle: registered as a hosted service via
    /// <c>AddHostedService&lt;DriftSweepService&gt;()</c>. Pulls a fresh scope
    /// per cycle. Cancellation-token-aware (stoppingToken on every awaited
    /// call). Cycle-level exceptions are caught, logged, and swallowed so a
    /// transient DB blip doesn't kill the loop — same pattern as
    /// <see cref="BackfillValidationService"/>.
    /// </summary>
    public class DriftSweepService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<DriftSweepService> _logger;
        private readonly IConfiguration _configuration;

        // First sweep runs after a startup delay to avoid piling onto the
        // orchestrator's bootstrap window. Mirrors BackfillValidationService.
        private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

        // "AnalystCompleted older than this is interesting." 24h matches the
        // operational SLO for audit turnaround on a pilot site.
        private static readonly TimeSpan StaleAgeForAnalystCompleted = TimeSpan.FromHours(24);

        public DriftSweepService(
            IServiceScopeFactory scopeFactory,
            ILogger<DriftSweepService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var enabled = _configuration.GetValue<bool>("Validation:DriftSweepEnabled", true);
            if (!enabled)
            {
                _logger.LogInformation("[DRIFT-SWEEP] disabled via Validation:DriftSweepEnabled=false; service exiting.");
                return;
            }

            var intervalHours = _configuration.GetValue<int>("Validation:DriftSweepIntervalHours", 24);
            if (intervalHours < 1) intervalHours = 1;
            var interval = TimeSpan.FromHours(intervalHours);

            _logger.LogInformation(
                "[DRIFT-SWEEP] starting; first cycle in {StartupDelay}, then every {Interval}",
                StartupDelay, interval);

            try { await Task.Delay(StartupDelay, stoppingToken); }
            catch (OperationCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunOnceAsync(stoppingToken);
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    // Cycle-level error must not kill the loop — survive transient
                    // DB blips. Pattern matches BackfillValidationService /
                    // CMRRedownloadBackgroundService.
                    _logger.LogError(ex, "[DRIFT-SWEEP] error during cycle; continuing at next interval");
                }

                try { await Task.Delay(interval, stoppingToken); }
                catch (OperationCanceledException) { break; }
            }

            _logger.LogInformation("[DRIFT-SWEEP] stopped");
        }

        /// <summary>
        /// One sweep — public so admin tooling can invoke it on demand without
        /// spinning up the BackgroundService loop.
        /// </summary>
        public async Task RunOnceAsync(CancellationToken stoppingToken)
        {
            var threshold = _configuration.GetValue<int>("Validation:DriftSweepHighCountThreshold", 5);
            if (threshold < 1) threshold = 1;

            var startedAt = DateTime.UtcNow;
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var alertService = scope.ServiceProvider.GetRequiredService<IDashboardAlertService>();

            // ── Count 1: orphan audit-stage AGs ─────────────────────────────
            // AGs whose Status routes them to the audit queue but no active
            // assignment row exists. Active = State='Active' (the same column
            // ZombieAnalysisGroupSweeperService and AuditReviewController
            // honour for "is this assignment live").
            var orphanAuditStageCount = await db.AnalysisGroups
                .AsNoTracking()
                .Where(g => g.Status == "AnalystCompleted" || g.Status == "AuditAssigned")
                .Where(g => !db.AnalysisAssignments.Any(a =>
                    a.GroupId == g.Id
                    && a.Role == "Audit"
                    && a.State == "Active"))
                .CountAsync(stoppingToken);

            // ── Count 2: CCS denormalisation drift ──────────────────────────
            // Containers with an active ContainerBOERelations row but the
            // matching CCS row has a NULL groupidentifier or NULL BOEDocumentId.
            // We only count the CCS row if there's a corresponding active
            // relation — bare-NULL CCS rows for containers we never linked are
            // outside this drift class.
            var activeRelationContainers = db.ContainerBOERelations
                .AsNoTracking()
                .Where(r => r.IsActive)
                .Select(r => r.ContainerNumber);

            var ccsDenormDriftCount = await db.ContainerCompletenessStatuses
                .AsNoTracking()
                .Where(s => s.GroupIdentifier == null || s.BOEDocumentId == null)
                .Where(s => activeRelationContainers.Contains(s.ContainerNumber))
                .CountAsync(stoppingToken);

            // ── Count 3: long-stale audit queue ─────────────────────────────
            // AGs that have been in AnalystCompleted longer than the SLO
            // window. UpdatedAtUtc is NULL on freshly-created groups; fall
            // back to CreatedAtUtc so the comparison stays defined.
            var staleCutoff = DateTime.UtcNow - StaleAgeForAnalystCompleted;
            var longStaleAuditQueueCount = await db.AnalysisGroups
                .AsNoTracking()
                .Where(g => g.Status == "AnalystCompleted")
                .Where(g => (g.UpdatedAtUtc ?? g.CreatedAtUtc) < staleCutoff)
                .CountAsync(stoppingToken);

            var elapsed = DateTime.UtcNow - startedAt;

            // Always emit a Warning summary so a daily log scan picks up the
            // shape of drift growth even when nothing is over threshold.
            _logger.LogWarning(
                "[DRIFT-SWEEP] cycle done in {Elapsed}: orphanAuditStage={Orphan}, ccsDenormDrift={Denorm}, longStaleAuditQueue={Stale}, threshold={Threshold}",
                elapsed,
                orphanAuditStageCount,
                ccsDenormDriftCount,
                longStaleAuditQueueCount,
                threshold);

            // Conditional alert — any count over threshold tips us into
            // dashboard-visible territory. Description carries all three
            // counts so the operator sees the full shape on hover.
            var anyOverThreshold =
                orphanAuditStageCount > threshold ||
                ccsDenormDriftCount > threshold ||
                longStaleAuditQueueCount > threshold;

            if (anyOverThreshold)
            {
                // Idempotency: dedupe key includes a coarse hash of the
                // count tuple so two cycles with the same shape collapse onto
                // the existing alert (RaiseAsync's 30-min dedupe window does
                // most of the work; the hash narrows "same shape, same week").
                var hash = ComputeDriftHash(orphanAuditStageCount, ccsDenormDriftCount, longStaleAuditQueueCount);
                var title = $"Drift sweep — high counts (hash={hash})";
                var description =
                    $"Drift sweep observed counts above threshold ({threshold}): " +
                    $"orphanAuditStage={orphanAuditStageCount}, " +
                    $"ccsDenormDrift={ccsDenormDriftCount}, " +
                    $"longStaleAuditQueue={longStaleAuditQueueCount}. " +
                    "This sweep does not fix; it surfaces drift growth before it bites. " +
                    "Triage starting points: " +
                    "orphanAuditStage → check userreadiness for an auditor with IsReady=true + recent heartbeat (audit-queue dead-mans-switch); " +
                    "ccsDenormDrift → re-run CCS denormalisation backfill; " +
                    "longStaleAuditQueue → check ZombieAnalysisGroupSweeperService cadence + auditor staffing.";

                await alertService.RaiseAsync(
                    type: "DriftSweepHighCounts",
                    severity: "Warning",
                    title: title,
                    description: description,
                    source: nameof(DriftSweepService),
                    ct: stoppingToken);
            }
        }

        /// <summary>
        /// Stable 8-character hex hash of the count tuple. Used as a dedupe
        /// key on the alert title so identical shapes collapse onto one row
        /// inside the dashboardalerts dedupe window. Non-cryptographic; we
        /// only need stability + low collision rate over the count space.
        /// </summary>
        private static string ComputeDriftHash(int orphan, int denorm, int stale)
        {
            var payload = $"{orphan}|{denorm}|{stale}";
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
            // First 4 bytes → 8 hex chars. Plenty for a dedupe key.
            return Convert.ToHexString(bytes, 0, 4);
        }
    }
}
