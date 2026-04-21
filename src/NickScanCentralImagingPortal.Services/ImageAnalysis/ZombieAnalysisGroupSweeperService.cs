using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    /// <summary>
    /// Periodically sweeps AnalysisGroups that are stuck in <c>AnalystCompleted</c> but have no
    /// ContainerCompletenessStatus rows to support them (so the audit-assignment
    /// pipeline can never pick them up), and archives them.
    ///
    /// Context: the 2026-04-19 operator session found group <c>40326195252</c> stuck
    /// in AnalystCompleted for 12 days with zero CCS rows. The audit-assignment
    /// code's "all containers must be WorkflowStage='Audit'" gate evaluates vacuously
    /// true against an empty collection, so the group passes the gate but has nothing
    /// real to assign — it quietly inflates the AnalystCompleted backlog and the
    /// SLA banner.
    ///
    /// Logic: every <see cref="ProcessIntervalMinutes"/> (default 60 min), find groups
    /// where <c>Status='AnalystCompleted'</c> that (a) have been in that state longer
    /// than <see cref="GraceHours"/> (default 24 h), and (b) have zero matching rows
    /// in <c>ContainerCompletenessStatuses</c> by <c>GroupIdentifier</c>. Flip them
    /// to <c>Archived</c>, stamp <c>UpdatedAtUtc</c>, and log each archive line with
    /// enough detail to audit retrospectively.
    ///
    /// Safety:
    /// - Only touches AnalystCompleted (not Ready — Ready groups can legitimately
    ///   exist briefly without CCS during ingest race).
    /// - Grace period protects against transient CCS absence during ingestion.
    /// - Archives in-place (reversible by hand if needed); does not delete.
    /// - No effect on analysisassignments rows — historical terminal-state
    ///   assignments are left in place.
    /// </summary>
    public class ZombieAnalysisGroupSweeperService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ZombieAnalysisGroupSweeperService> _logger;
        private readonly TimeSpan _processingInterval;
        private readonly TimeSpan _graceWindow;
        private readonly bool _enabled;

        public ZombieAnalysisGroupSweeperService(
            IServiceProvider serviceProvider,
            ILogger<ZombieAnalysisGroupSweeperService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;

            var intervalMinutes = configuration.GetValue<int>(
                "BackgroundServices:ZombieAnalysisGroupSweeper:ProcessIntervalMinutes", 60);
            var graceHours = configuration.GetValue<int>(
                "BackgroundServices:ZombieAnalysisGroupSweeper:GraceHours", 24);
            _enabled = configuration.GetValue<bool>(
                "BackgroundServices:ZombieAnalysisGroupSweeper:Enabled", true);

            _processingInterval = TimeSpan.FromMinutes(Math.Max(1, intervalMinutes));
            _graceWindow = TimeSpan.FromHours(Math.Max(1, graceHours));
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation(
                    "[ZOMBIE-SWEEPER] Disabled via configuration (BackgroundServices:ZombieAnalysisGroupSweeper:Enabled=false). Service will not run.");
                return;
            }

            _logger.LogInformation(
                "[ZOMBIE-SWEEPER] Started. Interval: {Interval}, Grace window: {Grace}",
                _processingInterval, _graceWindow);

            // Small initial delay to let the rest of the app warm up.
            try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
            catch (TaskCanceledException) { return; }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SweepOnceAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[ZOMBIE-SWEEPER] Error during sweep iteration");
                }

                try { await Task.Delay(_processingInterval, stoppingToken); }
                catch (TaskCanceledException) { return; }
            }
        }

        private async Task SweepOnceAsync(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var cutoff = DateTime.UtcNow - _graceWindow;

            // Pull candidates cheaply: AnalystCompleted, old enough to be past the grace window.
            // The "no CCS rows" check is a second step so we only pay the per-row JOIN cost
            // on candidates that cleared the time gate. Expected candidate count is tiny
            // in steady state (single digits), so .ToList() is fine.
            var candidates = await db.AnalysisGroups
                .AsNoTracking()
                .Where(g => g.Status == "AnalystCompleted")
                .Where(g => (g.UpdatedAtUtc ?? g.CreatedAtUtc) < cutoff)
                .Select(g => new { g.Id, g.GroupIdentifier, g.CreatedAtUtc, g.UpdatedAtUtc })
                .ToListAsync(ct);

            if (candidates.Count == 0)
            {
                _logger.LogDebug("[ZOMBIE-SWEEPER] No AnalystCompleted candidates past grace window.");
                return;
            }

            var archivedCount = 0;
            foreach (var c in candidates)
            {
                ct.ThrowIfCancellationRequested();

                var hasCcs = await db.ContainerCompletenessStatuses
                    .AsNoTracking()
                    .AnyAsync(s => s.GroupIdentifier == c.GroupIdentifier, ct);

                if (hasCcs)
                {
                    // Real group, not a zombie — skip.
                    continue;
                }

                // Transactional flip: guard on Status in the WHERE clause so we don't
                // stomp on a concurrent upgrade that advanced the group legitimately.
                var rowsAffected = await db.Database.ExecuteSqlInterpolatedAsync($@"
                    UPDATE analysisgroups
                       SET status = 'Archived',
                           updatedatutc = now()
                     WHERE id = {c.Id}
                       AND status = 'AnalystCompleted'", ct);

                if (rowsAffected > 0)
                {
                    archivedCount++;
                    var ageHours = (DateTime.UtcNow - (c.UpdatedAtUtc ?? c.CreatedAtUtc)).TotalHours;
                    _logger.LogWarning(
                        "[ZOMBIE-SWEEPER] Archived zombie AnalysisGroup Id={GroupId} GroupIdentifier={GroupIdentifier} (was AnalystCompleted for {AgeHours:F1} h with zero CCS rows)",
                        c.Id, c.GroupIdentifier, ageHours);
                }
            }

            if (archivedCount > 0)
            {
                _logger.LogInformation(
                    "[ZOMBIE-SWEEPER] Sweep complete: archived {Count} zombie AnalystCompleted group(s) out of {Candidates} candidate(s).",
                    archivedCount, candidates.Count);
            }
            else
            {
                _logger.LogDebug(
                    "[ZOMBIE-SWEEPER] Sweep complete: checked {Candidates} candidate(s), none qualified as zombies.",
                    candidates.Count);
            }
        }
    }
}
