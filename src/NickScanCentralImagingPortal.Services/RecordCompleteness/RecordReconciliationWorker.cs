using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Helpers;
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
        private readonly bool _cmrCompositeProgressionEnabled;
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
            ILogger<RecordReconciliationWorker> logger,
            IConfiguration? configuration = null)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _cmrCompositeProgressionEnabled = configuration?.GetValue<bool>("CmrCompositeProgression:Enabled", false) ?? false;
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
            // IM/EX declarations keep the original declaration-number identity.
            // CMR composite-key records are reconciled in the gated pass below.
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

            if (_cmrCompositeProgressionEnabled)
            {
                var recordBuilder = scope.ServiceProvider.GetService<IRecordBuildingService>();
                var cmrStats = await ReconcileCmrCompositeRecordsAsync(
                    appDb,
                    icumDb,
                    recordBuilder,
                    watermark,
                    batchSize,
                    ct);

                stats.RecordsCreated += cmrStats.Created;
                stats.RecordsUpdated += cmrStats.Updated;
                if (cmrStats.NewWatermark.HasValue && cmrStats.NewWatermark.Value > newWatermark)
                {
                    newWatermark = cmrStats.NewWatermark.Value;
                }
            }
            else
            {
                _logger.LogDebug("{ServiceId} CMR composite progression disabled — skipping CMR reconciliation pass", SERVICE_ID);
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

        private async Task<CmrReconciliationStats> ReconcileCmrCompositeRecordsAsync(
            ApplicationDbContext appDb,
            IcumDownloadsDbContext icumDb,
            IRecordBuildingService? recordBuilder,
            DateTime watermark,
            int batchSize,
            CancellationToken ct)
        {
            var cmrRows = await icumDb.BOEDocuments
                .AsNoTracking()
                .Where(b => b.ClearanceType == "CMR"
                         && b.UpdatedAt > watermark
                         && b.RotationNumber != null
                         && b.RotationNumber != ""
                         && b.ContainerNumber != null
                         && b.ContainerNumber != ""
                         && b.BlNumber != null
                         && b.BlNumber != "")
                .OrderBy(b => b.UpdatedAt)
                .Take(batchSize * 20)
                .ToListAsync(ct);

            if (cmrRows.Count == 0)
            {
                return CmrReconciliationStats.Empty;
            }

            var newWatermark = cmrRows.Max(b => b.UpdatedAt);
            var validGroups = cmrRows
                .Select(row => new
                {
                    Row = row,
                    HasKey = CmrCompositeKeyHelper.TryCreate(row.RotationNumber, row.ContainerNumber, row.BlNumber, out var key),
                    Key = key
                })
                .Where(x => x.HasKey)
                .GroupBy(x => x.Key.OperationalKey)
                .Take(batchSize)
                .ToList();

            var skippedInvalid = cmrRows.Count - validGroups.Sum(g => g.Count());
            if (skippedInvalid > 0)
            {
                _logger.LogWarning(
                    "{ServiceId} Skipped {Count} CMR row(s) with incomplete composite key parts during reconciliation",
                    SERVICE_ID,
                    skippedInvalid);
            }

            var stats = new CmrReconciliationStats { NewWatermark = newWatermark };
            foreach (var group in validGroups)
            {
                if (ct.IsCancellationRequested) break;

                var key = group.First().Key;
                var rows = group.Select(x => x.Row).ToList();

                try
                {
                    var delegated = await TryCallCmrRecordBuilderAsync(recordBuilder, key, rows, ct);
                    if (delegated)
                    {
                        stats.Updated++;
                        continue;
                    }

                    var created = await UpsertCmrCompositeRecordAsync(appDb, key, rows, ct);
                    if (created) stats.Created++;
                    else stats.Updated++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(
                        ex,
                        "{ServiceId} Failed CMR composite reconciliation for {Key} ({Display})",
                        SERVICE_ID,
                        key.OperationalKey,
                        key.DisplayLabel);
                }
            }

            _logger.LogInformation(
                "{ServiceId} CMR reconciliation pass processed {Groups} composite group(s): created={Created} updated={Updated}",
                SERVICE_ID,
                validGroups.Count,
                stats.Created,
                stats.Updated);

            return stats;
        }

        private async Task<bool> UpsertCmrCompositeRecordAsync(
            ApplicationDbContext appDb,
            CmrCompositeKey key,
            List<BOEDocument> cmrRows,
            CancellationToken ct)
        {
            var existing = await appDb.RecordCompletenessStatuses
                .AsTracking()
                .Include(r => r.ExpectedContainers)
                .FirstOrDefaultAsync(r => r.DeclarationNumber == key.OperationalKey, ct);

            var nowUtc = DateTime.UtcNow;
            var representative = cmrRows
                .OrderByDescending(r => r.UpdatedAt)
                .First();

            if (existing == null)
            {
                var record = new RecordCompletenessStatus
                {
                    DeclarationNumber = key.OperationalKey,
                    ClearanceType = "CMR",
                    RegimeCode = representative.RegimeCode,
                    PrimaryBoeDocumentId = representative.Id,
                    RotationNumber = key.RotationNumber,
                    BlNumber = key.BlNumber,
                    ContainerGroupKey = key.OperationalKey,
                    ScannerType = null,
                    TotalExpectedContainers = 1,
                    ContainersAwaitingScan = 1,
                    Status = "Pending",
                    WorkflowStage = "Pending",
                    FirstSeenUtc = nowUtc,
                    LastNewContainerAtUtc = nowUtc,
                    CreatedAtUtc = nowUtc,
                    UpdatedAtUtc = nowUtc
                };

                appDb.RecordCompletenessStatuses.Add(record);
                await appDb.SaveChangesAsync(ct);

                appDb.RecordExpectedContainers.Add(new RecordExpectedContainer
                {
                    RecordId = record.Id,
                    ContainerNumber = key.ContainerNumber,
                    Status = "AwaitingScan",
                    BoeDocumentId = representative.Id,
                    HouseBl = representative.HouseBl,
                    ConsigneeName = representative.ConsigneeName,
                    FirstSeenUtc = nowUtc
                });
                await appDb.SaveChangesAsync(ct);
                return true;
            }

            existing.ClearanceType = "CMR";
            existing.RegimeCode = representative.RegimeCode ?? existing.RegimeCode;
            existing.PrimaryBoeDocumentId ??= representative.Id;
            existing.RotationNumber = key.RotationNumber;
            existing.BlNumber = key.BlNumber;
            existing.ContainerGroupKey = key.OperationalKey;
            existing.UpdatedAtUtc = nowUtc;

            var hasContainer = existing.ExpectedContainers
                .Any(c => string.Equals(c.ContainerNumber, key.ContainerNumber, StringComparison.OrdinalIgnoreCase));

            if (!hasContainer)
            {
                appDb.RecordExpectedContainers.Add(new RecordExpectedContainer
                {
                    RecordId = existing.Id,
                    ContainerNumber = key.ContainerNumber,
                    Status = "AwaitingScan",
                    BoeDocumentId = representative.Id,
                    HouseBl = representative.HouseBl,
                    ConsigneeName = representative.ConsigneeName,
                    FirstSeenUtc = nowUtc
                });
                existing.LastNewContainerAtUtc = nowUtc;
            }

            await appDb.SaveChangesAsync(ct);
            return false;
        }

        private async Task<bool> TryCallCmrRecordBuilderAsync(
            IRecordBuildingService? recordBuilder,
            CmrCompositeKey key,
            List<BOEDocument> rows,
            CancellationToken ct)
        {
            if (recordBuilder == null)
                return false;

            var method = recordBuilder.GetType()
                .GetMethods(BindingFlags.Instance | BindingFlags.Public)
                .Where(m => m.Name == "BuildOrUpdateCmrRecordAsync")
                .FirstOrDefault(m => TryBuildCmrBuilderArguments(m, key, rows, ct, out _));

            if (method == null)
                return false;

            TryBuildCmrBuilderArguments(method, key, rows, ct, out var args);
            var result = method.Invoke(recordBuilder, args);
            if (result is Task task)
            {
                await task.ConfigureAwait(false);
            }

            return true;
        }

        private static bool TryBuildCmrBuilderArguments(
            MethodInfo method,
            CmrCompositeKey key,
            List<BOEDocument> rows,
            CancellationToken ct,
            out object?[] args)
        {
            var parameters = method.GetParameters();
            args = new object?[parameters.Length];

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var parameterName = parameter.Name ?? string.Empty;
                var parameterType = parameter.ParameterType;

                if (parameterType == typeof(CancellationToken))
                {
                    args[i] = ct;
                }
                else if (parameterType == typeof(CmrCompositeKey))
                {
                    args[i] = key;
                }
                else if (parameterType.IsAssignableFrom(typeof(List<BOEDocument>)))
                {
                    args[i] = rows;
                }
                else if (parameterType == typeof(string))
                {
                    args[i] = ResolveCmrStringArgument(parameterName, key);
                    if (args[i] == null)
                    {
                        return false;
                    }
                }
                else if (parameterType == typeof(bool))
                {
                    args[i] = true;
                }
                else if (parameter.HasDefaultValue)
                {
                    args[i] = parameter.DefaultValue;
                }
                else
                {
                    return false;
                }
            }

            return true;
        }

        private static string? ResolveCmrStringArgument(string parameterName, CmrCompositeKey key)
        {
            if (parameterName.Contains("display", StringComparison.OrdinalIgnoreCase)
                || parameterName.Contains("label", StringComparison.OrdinalIgnoreCase))
                return key.DisplayLabel;

            if (parameterName.Contains("rotation", StringComparison.OrdinalIgnoreCase))
                return key.RotationNumber;

            if (parameterName.Contains("container", StringComparison.OrdinalIgnoreCase))
                return key.ContainerNumber;

            if (parameterName.Contains("bl", StringComparison.OrdinalIgnoreCase))
                return key.BlNumber;

            if (parameterName.Contains("key", StringComparison.OrdinalIgnoreCase)
                || parameterName.Contains("declaration", StringComparison.OrdinalIgnoreCase)
                || parameterName.Contains("record", StringComparison.OrdinalIgnoreCase))
                return key.OperationalKey;

            return null;
        }

        private async Task<int> PromoteAwaitingContainersAsync(ApplicationDbContext appDb, CancellationToken ct)
        {
            // Find containers in AwaitingScan status that have a matching scan event
            // anywhere in the NSCIM scanner tables. This is the reconciliation catch-up
            // that picks up containers scanned by any source (scanner, queue, direct).

            // Pull AwaitingScan candidates that actually have scanner/completeness
            // evidence. The old broad Take(5000) could starve later evidence-bearing
            // rows behind thousands of old ICUMS-only records, leaving real analyst
            // work stuck at RecordExpectedContainer.Status=AwaitingScan.
            var awaiting = await appDb.RecordExpectedContainers
                .AsTracking()
                .Where(c => c.Status == "AwaitingScan"
                    && appDb.ContainerCompletenessStatuses.Any(s => s.ContainerNumber == c.ContainerNumber))
                .OrderBy(c => c.FirstSeenUtc)
                .Take(5000)
                .ToListAsync(ct);

            int promoted = 0;
            var nowUtc = DateTime.UtcNow;
            var parentsToBump = new HashSet<int>();

            if (awaiting.Count > 0)
            {
                var awaitingNumbers = awaiting.Select(a => a.ContainerNumber).Distinct().ToList();

                // Check which ones have scanner evidence in ANY source
                var withCompleteness = await appDb.ContainerCompletenessStatuses
                    .Where(c => awaitingNumbers.Contains(c.ContainerNumber))
                    .Select(c => new { c.ContainerNumber, c.ScannerType, c.InspectionId, c.HasImageData })
                    .ToListAsync(ct);

                var evidenceByContainer = withCompleteness
                    .GroupBy(c => c.ContainerNumber, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(x => x.HasImageData).First(), StringComparer.OrdinalIgnoreCase);

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
            }

            if (promoted > 0)
            {
                await RecomputePromotedParentsAsync(appDb, parentsToBump, nowUtc, ct);

                _logger.LogInformation("{ServiceId} Promoted {Count} containers from AwaitingScan (affected {Parents} parent records)",
                    SERVICE_ID, promoted, parentsToBump.Count);
            }

            // Also promote Pending → Ready for rows that now have images. Do this even
            // when no AwaitingScan rows were promoted this tick; the previous early
            // return skipped Pending catch-up entirely on quiet AwaitingScan cycles.
            var pending = await appDb.RecordExpectedContainers
                .AsTracking()
                .Where(c => c.Status == "Pending"
                    && appDb.ContainerCompletenessStatuses.Any(s => s.ContainerNumber == c.ContainerNumber && s.HasImageData))
                .OrderBy(c => c.ScannedAtUtc ?? c.FirstSeenUtc)
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
                var pendingParentsToBump = new HashSet<int>();
                foreach (var row in pending)
                {
                    if (!readyNowSet.Contains(row.ContainerNumber)) continue;
                    row.Status = "Ready";
                    row.BecameReadyUtc = nowUtc;
                    pendingPromoted++;
                    pendingParentsToBump.Add(row.RecordId);
                }

                if (pendingPromoted > 0)
                {
                    await RecomputePromotedParentsAsync(appDb, pendingParentsToBump, nowUtc, ct);
                    promoted += pendingPromoted;
                    _logger.LogInformation("{ServiceId} Promoted {Count} containers from Pending → Ready", SERVICE_ID, pendingPromoted);
                }
            }

            return promoted;
        }

        private static async Task RecomputePromotedParentsAsync(
            ApplicationDbContext appDb,
            HashSet<int> parentIds,
            DateTime nowUtc,
            CancellationToken ct)
        {
            if (parentIds.Count == 0) return;

            var parents = await appDb.RecordCompletenessStatuses
                .AsTracking()
                .Include(r => r.ExpectedContainers)
                .Where(r => parentIds.Contains(r.Id))
                .ToListAsync(ct);

            foreach (var parent in parents)
            {
                parent.LastNewContainerAtUtc = nowUtc;
                RecordCompletenessBuilder.Recompute(parent, parent.ExpectedContainers.ToList());
            }

            await appDb.SaveChangesAsync(ct);
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

        private sealed class CmrReconciliationStats
        {
            public static CmrReconciliationStats Empty { get; } = new();

            public int Created { get; set; }
            public int Updated { get; set; }
            public DateTime? NewWatermark { get; set; }
        }
    }
}
