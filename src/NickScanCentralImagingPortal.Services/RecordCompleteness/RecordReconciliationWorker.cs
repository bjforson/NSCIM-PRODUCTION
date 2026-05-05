using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.Services.RecordCompleteness
{
    /// <summary>
    /// 1.14.0 — The record-completeness reconciliation loop.
    ///
    /// Runs every N minutes (default 30, configurable via AnalysisSettings.
    /// RecordReconciliationIntervalMinutes). On each tick:
    ///
    ///   1. Pull new/updated BOE rows from nickscan_downloads since the last watermark
    ///   2. For each declaration, upsert a RecordCompletenessStatus + its expected-container children
    ///   3. Scan for newly-arrived scanner events (containerscanqueues, fs6000scans, asescans)
    ///      and flip matching AwaitingScan rows to Pending
    ///   4. Re-check image availability for Pending rows and flip to Ready
    ///   5. Recompute parent rollup counts + derive Status / WorkflowStage
    ///   6. Apply the 30-day archive rule to stale records
    ///   7. Persist the new watermark + reconciliation state counters
    ///
    /// STRICTLY ADDITIVE: this worker does not modify any existing entities
    /// (ContainerCompletenessStatus, AnalysisGroup, WavePendingContainer, etc.).
    /// It only writes to the new record-level tables introduced in 1.14.0.
    /// </summary>
    public class RecordReconciliationWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecordReconciliationWorker> _logger;
        private const string SERVICE_ID = "[RECORD-RECON]";
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(60);

        // Audit 8.13 (Sprint 5G2 follow-up): heartbeat state. RunTickAsync
        // writes these and ExecuteAsync reads them for the per-iteration
        // summary line.
        private int _cycleCount = 0;
        private int _lastTickProcessed = 0;
        private int _lastTickSkipped = 0;

        public RecordReconciliationWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<RecordReconciliationWorker> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{ServiceId} Starting with initial delay of {Delay}s", SERVICE_ID, StartupDelay.TotalSeconds);

            try
            {
                await Task.Delay(StartupDelay, stoppingToken);
            }
            catch (TaskCanceledException)
            {
                return;
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                // Audit 8.10 (Sprint 5G2 follow-up): mint per-cycle CorrelationId
                // so every log line emitted during this iteration carries the
                // same key.
                using var _cycleScope = _logger.BeginCycle(nameof(RecordReconciliationWorker));
                // Audit 8.13 (Sprint 5G2 follow-up): track elapsed for heartbeat.
                var _cycleStartedAt = DateTime.UtcNow;
                _cycleCount++;
                _lastTickProcessed = 0;
                _lastTickSkipped = 0;
                int _failedThisCycle = 0;

                var intervalMinutes = 30;
                try
                {
                    intervalMinutes = await RunTickAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _failedThisCycle = 1;
                    _logger.LogError(ex, "{ServiceId} Tick failed — continuing after interval", SERVICE_ID);
                }

                // Audit 8.13 (Sprint 5G2 follow-up): per-iteration heartbeat.
                // processed = records created+updated+containers promoted this
                // tick; skipped = records archived (deliberate non-progress).
                _logger.LogIterationSummary(
                    "RECORD-RECON",
                    _cycleCount,
                    DateTime.UtcNow - _cycleStartedAt,
                    itemsProcessed: _lastTickProcessed,
                    itemsSkipped: _lastTickSkipped,
                    itemsFailed: _failedThisCycle);

                try
                {
                    await Task.Delay(TimeSpan.FromMinutes(intervalMinutes), stoppingToken);
                }
                catch (TaskCanceledException)
                {
                    return;
                }
            }
        }

        private async Task<int> RunTickAsync(CancellationToken ct)
        {
            using var scope = _scopeFactory.CreateScope();
            var appDb = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icumDb = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

            // Load the settings tunables
            var settings = await appDb.AnalysisSettings.AsNoTracking().FirstOrDefaultAsync(ct);
            // Demoted to safety-net: default interval increased from 30 to 240 minutes.
            // Event-driven record building (RecordBuildingService) handles the primary path.
            var intervalMinutes = settings?.RecordReconciliationIntervalMinutes ?? 240;
            var enabled = settings?.RecordReconciliationEnabled ?? true;
            var archiveAfterDays = settings?.RecordArchiveAfterDays ?? 30;
            var batchSize = settings?.RecordReconciliationBatchSize ?? 100;

            if (!enabled)
            {
                _logger.LogDebug("{ServiceId} Disabled in settings — skipping tick", SERVICE_ID);
                return intervalMinutes;
            }

            var sw = Stopwatch.StartNew();
            var stats = new TickStats();

            // Load the persistent state (watermark etc.)
            var state = await appDb.RecordReconciliationStates.AsTracking().FirstOrDefaultAsync(s => s.Id == 1, ct);
            if (state == null)
            {
                state = new RecordReconciliationState { Id = 1 };
                appDb.RecordReconciliationStates.Add(state);
                await appDb.SaveChangesAsync(ct);
            }

            var watermark = state.LastWatermarkUtc ?? DateTime.UtcNow.AddYears(-10);

            _logger.LogInformation("{ServiceId} Tick start — watermark={Watermark:o}, batch={Batch}", SERVICE_ID, watermark, batchSize);

            // ── Step 1: Pull new / updated BOE rows since the watermark ─────────
            //
            // We want IM and EX declarations (CMR is handled by the 1.13.0 implicit
            // upgrade path and doesn't become a record until it upgrades to IM/EX).
            var newBoeRows = await icumDb.BOEDocuments
                .AsNoTracking()
                .Where(b => (b.ClearanceType == "IM" || b.ClearanceType == "EX")
                         && b.UpdatedAt > watermark
                         && b.DeclarationNumber != null
                         && b.DeclarationNumber != "")
                .OrderBy(b => b.UpdatedAt)
                .Take(batchSize * 20) // up to 20 containers per declaration
                .ToListAsync(ct);

            DateTime newWatermark = watermark;
            if (newBoeRows.Count > 0)
            {
                newWatermark = newBoeRows.Max(b => b.UpdatedAt);
            }

            _logger.LogInformation("{ServiceId} Pulled {Count} new/updated BOE rows (new watermark {Watermark:o})",
                SERVICE_ID, newBoeRows.Count, newWatermark);

            // ── Step 2: Upsert RecordCompletenessStatus + children per declaration ──
            if (newBoeRows.Count > 0)
            {
                var byDeclaration = newBoeRows
                    .GroupBy(r => r.DeclarationNumber!.Trim())
                    .Take(batchSize) // cap per tick
                    .ToList();

                foreach (var group in byDeclaration)
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var created = await UpsertRecordAsync(appDb, icumDb, group.Key, group.ToList(), ct);
                        if (created) stats.RecordsCreated++;
                        else stats.RecordsUpdated++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{ServiceId} Failed to upsert record for declaration {Decl}", SERVICE_ID, group.Key);
                    }
                }
            }

            // ── Step 3 + 4: Promote AwaitingScan → Pending → Ready for EXISTING rows ──
            stats.ContainersPromoted = await PromoteAwaitingContainersAsync(appDb, ct);

            // ── Step 5: Recompute rollups on records whose children changed this tick ──
            // Simple approach: any record touched today gets recomputed. Over-inclusive but
            // cheap given ~12k rows max and indexed queries.
            await RecomputeRecentRollupsAsync(appDb, ct);

            // ── Step 6: Apply 30-day archive rule ──
            stats.RecordsArchived = await ApplyArchiveRuleAsync(appDb, archiveAfterDays, ct);

            // ── Step 7: Persist state ──
            state.LastWatermarkUtc = newWatermark;
            state.LastTickAtUtc = DateTime.UtcNow;
            state.LastTickDurationMs = (int)sw.ElapsedMilliseconds;
            state.RecordsCreatedTotal += stats.RecordsCreated;
            state.RecordsUpdatedTotal += stats.RecordsUpdated;
            state.ContainersPromotedTotal += stats.ContainersPromoted;
            state.RecordsArchivedTotal += stats.RecordsArchived;
            await appDb.SaveChangesAsync(ct);

            _logger.LogInformation(
                "{ServiceId} Tick done in {Ms}ms — created={Created} updated={Updated} promoted={Promoted} archived={Archived}",
                SERVICE_ID, sw.ElapsedMilliseconds, stats.RecordsCreated, stats.RecordsUpdated, stats.ContainersPromoted, stats.RecordsArchived);

            // Audit 8.13 (Sprint 5G2 follow-up): publish per-tick counts to
            // ExecuteAsync's heartbeat emitter.
            _lastTickProcessed = stats.RecordsCreated + stats.RecordsUpdated + stats.ContainersPromoted;
            _lastTickSkipped = stats.RecordsArchived;

            return intervalMinutes;
        }

        private async Task<bool> UpsertRecordAsync(
            ApplicationDbContext appDb,
            IcumDownloadsDbContext icumDb,
            string declarationNumber,
            List<BOEDocument> declarationRows,
            CancellationToken ct)
        {
            var existing = await appDb.RecordCompletenessStatuses
                .AsTracking()
                .Include(r => r.ExpectedContainers)
                .FirstOrDefaultAsync(r => r.DeclarationNumber == declarationNumber, ct);

            // Detect Pattern A: pull sibling declarations that share any container in this declaration
            var containerNumbers = declarationRows
                .Select(r => r.ContainerNumber?.Trim().ToUpperInvariant())
                .Where(c => !string.IsNullOrEmpty(c))
                .Distinct()
                .ToList();

            List<BOEDocument> siblings = new();
            if (containerNumbers.Count == 1)
            {
                siblings = await icumDb.BOEDocuments
                    .AsNoTracking()
                    .Where(b => (b.ClearanceType == "IM" || b.ClearanceType == "EX")
                             && b.ContainerNumber == containerNumbers[0]
                             && b.DeclarationNumber != null
                             && b.DeclarationNumber != declarationNumber)
                    .Take(50)
                    .ToListAsync(ct);
            }

            var built = RecordCompletenessBuilder.Build(declarationRows, siblings, DateTime.UtcNow);

            if (existing == null)
            {
                appDb.RecordCompletenessStatuses.Add(built.Record);
                await appDb.SaveChangesAsync(ct);

                foreach (var child in built.Children)
                {
                    child.RecordId = built.Record.Id;
                }
                appDb.RecordExpectedContainers.AddRange(built.Children);
                await appDb.SaveChangesAsync(ct);
                return true;
            }

            // Existing — amend the container set if ICUMS added new containers
            var existingContainerNumbers = new HashSet<string>(
                existing.ExpectedContainers.Select(c => c.ContainerNumber),
                StringComparer.OrdinalIgnoreCase);

            var newChildren = built.Children
                .Where(c => !existingContainerNumbers.Contains(c.ContainerNumber))
                .ToList();

            if (newChildren.Count > 0)
            {
                foreach (var child in newChildren)
                {
                    child.RecordId = existing.Id;
                }
                appDb.RecordExpectedContainers.AddRange(newChildren);
                existing.LastNewContainerAtUtc = DateTime.UtcNow;
                existing.UpdatedAtUtc = DateTime.UtcNow;
                await appDb.SaveChangesAsync(ct);
                _logger.LogInformation("{ServiceId} Amended declaration {Decl}: +{NewCount} containers", SERVICE_ID, declarationNumber, newChildren.Count);
            }

            return false;
        }

        private async Task<int> PromoteAwaitingContainersAsync(ApplicationDbContext appDb, CancellationToken ct)
        {
            // Find containers in AwaitingScan status that have a matching scan event
            // anywhere in the NSCIM scanner tables. This is the reconciliation catch-up
            // that picks up containers scanned by any source (scanner, queue, direct).

            // Pull the AwaitingScan candidates
            var awaiting = await appDb.RecordExpectedContainers
                .AsTracking()
                .Where(c => c.Status == "AwaitingScan")
                .Take(5000)
                .ToListAsync(ct);

            if (awaiting.Count == 0) return 0;

            var awaitingNumbers = awaiting.Select(a => a.ContainerNumber).Distinct().ToList();

            // Check which ones have scanner evidence in ANY source
            var withCompleteness = await appDb.ContainerCompletenessStatuses
                .Where(c => awaitingNumbers.Contains(c.ContainerNumber))
                .Select(c => new { c.ContainerNumber, c.ScannerType, c.InspectionId, c.HasImageData })
                .ToListAsync(ct);

            var evidenceByContainer = withCompleteness
                .GroupBy(c => c.ContainerNumber, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.HasImageData).First(), StringComparer.OrdinalIgnoreCase);

            int promoted = 0;
            var nowUtc = DateTime.UtcNow;
            var parentsToBump = new HashSet<int>();

            foreach (var row in awaiting)
            {
                if (!evidenceByContainer.TryGetValue(row.ContainerNumber, out var evidence)) continue;

                row.ScannedAtUtc = nowUtc;
                row.InspectionId = evidence.InspectionId;
                row.ScannerType = evidence.ScannerType;
                row.Status = evidence.HasImageData ? "Ready" : "Pending";
                if (evidence.HasImageData)
                {
                    row.BecameReadyUtc = nowUtc;
                }
                promoted++;
                parentsToBump.Add(row.RecordId);
            }

            if (promoted > 0)
            {
                // Bump LastNewContainerAtUtc on parents whose children just transitioned
                var parents = await appDb.RecordCompletenessStatuses
                    .AsTracking()
                    .Where(r => parentsToBump.Contains(r.Id))
                    .ToListAsync(ct);
                foreach (var p in parents)
                {
                    p.LastNewContainerAtUtc = nowUtc;
                    p.UpdatedAtUtc = nowUtc;
                }
                await appDb.SaveChangesAsync(ct);

                _logger.LogInformation("{ServiceId} Promoted {Count} containers from AwaitingScan (affected {Parents} parent records)",
                    SERVICE_ID, promoted, parentsToBump.Count);
            }

            // Also promote Pending → Ready for rows that now have images
            var pending = await appDb.RecordExpectedContainers
                .AsTracking()
                .Where(c => c.Status == "Pending")
                .Take(5000)
                .ToListAsync(ct);

            if (pending.Count > 0)
            {
                var pendingNumbers = pending.Select(p => p.ContainerNumber).Distinct().ToList();
                var withImages = await appDb.ContainerCompletenessStatuses
                    .Where(c => pendingNumbers.Contains(c.ContainerNumber) && c.HasImageData)
                    .Select(c => c.ContainerNumber)
                    .Distinct()
                    .ToListAsync(ct);

                var readyNowSet = new HashSet<string>(withImages, StringComparer.OrdinalIgnoreCase);
                int pendingPromoted = 0;
                foreach (var row in pending)
                {
                    if (!readyNowSet.Contains(row.ContainerNumber)) continue;
                    row.Status = "Ready";
                    row.BecameReadyUtc = nowUtc;
                    pendingPromoted++;
                }

                if (pendingPromoted > 0)
                {
                    await appDb.SaveChangesAsync(ct);
                    promoted += pendingPromoted;
                    _logger.LogInformation("{ServiceId} Promoted {Count} containers from Pending → Ready", SERVICE_ID, pendingPromoted);
                }
            }

            return promoted;
        }

        private async Task RecomputeRecentRollupsAsync(ApplicationDbContext appDb, CancellationToken ct)
        {
            // Recompute every record that has children touched in the last tick window.
            // For simplicity we recompute all non-terminal records. This is cheap on
            // ~12k rows and ensures rollups are always consistent with child state.
            var records = await appDb.RecordCompletenessStatuses
                .AsTracking()
                .Include(r => r.ExpectedContainers)
                .Where(r => r.Status != "Archived" && r.Status != "Failed" && r.Status != "Completed")
                .Take(5000)
                .ToListAsync(ct);

            foreach (var r in records)
            {
                var children = r.ExpectedContainers.ToList();
                RecordCompletenessBuilder.Recompute(r, children);
            }

            await appDb.SaveChangesAsync(ct);
        }

        private async Task<int> ApplyArchiveRuleAsync(ApplicationDbContext appDb, int archiveAfterDays, CancellationToken ct)
        {
            var cutoff = DateTime.UtcNow.AddDays(-archiveAfterDays);
            var stale = await appDb.RecordCompletenessStatuses
                .AsTracking()
                .Where(r => (r.Status == "Pending" || r.Status == "PartiallyReady")
                         && r.LastNewContainerAtUtc != null
                         && r.LastNewContainerAtUtc < cutoff
                         && r.ArchivedAtUtc == null)
                .Take(1000)
                .ToListAsync(ct);

            if (stale.Count == 0) return 0;

            var nowUtc = DateTime.UtcNow;
            foreach (var r in stale)
            {
                r.Status = "Archived";
                r.ArchivedAtUtc = nowUtc;
                r.ArchivalReason = "StaleNoNewContainers";
                r.UpdatedAtUtc = nowUtc;
            }

            // Flip any remaining AwaitingScan/Pending children to NoScanReceived
            var staleIds = stale.Select(r => r.Id).ToList();
            var orphanChildren = await appDb.RecordExpectedContainers
                .AsTracking()
                .Where(c => staleIds.Contains(c.RecordId)
                         && (c.Status == "AwaitingScan" || c.Status == "Pending"))
                .ToListAsync(ct);
            foreach (var c in orphanChildren)
            {
                c.Status = "NoScanReceived";
            }

            await appDb.SaveChangesAsync(ct);
            _logger.LogWarning("{ServiceId} Archived {Count} stale records (> {Days}d with no new containers)",
                SERVICE_ID, stale.Count, archiveAfterDays);
            return stale.Count;
        }

        private sealed class TickStats
        {
            public int RecordsCreated { get; set; }
            public int RecordsUpdated { get; set; }
            public int ContainersPromoted { get; set; }
            public int RecordsArchived { get; set; }
        }
    }
}
