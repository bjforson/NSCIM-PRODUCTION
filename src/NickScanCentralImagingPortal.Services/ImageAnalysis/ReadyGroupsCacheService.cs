using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    /// <summary>
    /// Shared cache service for ready groups queries.
    /// Uses ICacheService (Redis or IDistributedMemoryCache) for distributed caching across instances.
    /// Reduces duplicate database queries across AssignmentWorker, IntakeWorker, and HousekeepingWorker.
    /// Cache expiration: 30 seconds (balances freshness with query reduction).
    /// </summary>
    public class ReadyGroupsCacheService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ReadyGroupsCacheService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache? _memoryCache;
        private readonly int _cacheExpirationSeconds;
        private readonly int _maxReadyGroups;
        private readonly bool _cacheEmptyResults;
        private const string CacheKeyPrefix = "ReadyGroups";
        private const string MyAssignmentsCacheKeyPrefix = "my-assignments";

        public ReadyGroupsCacheService(
            IServiceScopeFactory scopeFactory,
            ILogger<ReadyGroupsCacheService> logger,
            IConfiguration configuration,
            IMemoryCache? memoryCache = null)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
            _memoryCache = memoryCache;
            _cacheExpirationSeconds = _configuration.GetValue<int>("ReadyGroupsCache:ExpirationSeconds", 30);
            _maxReadyGroups = _configuration.GetValue<int>("ReadyGroupsCache:MaxGroups", 200);
            _cacheEmptyResults = _configuration.GetValue<bool>("ReadyGroupsCache:CacheEmptyResults", false);
        }

        /// <summary>
        /// Get ready groups for a specific role with WorkflowStage filtering
        /// Caches results for 30 seconds to reduce duplicate queries
        /// Uses ICacheService (Redis/distributed) for multi-instance support
        /// </summary>
        public async Task<List<AnalysisGroup>> GetReadyGroupsForRoleAsync(
            string roleName,
            string eligibleStatus,
            CancellationToken cancellationToken = default)
        {
            // Create cache key based on role and status
            var cacheKey = $"{CacheKeyPrefix}:{roleName}:{eligibleStatus}";

            using var scope = _scopeFactory.CreateScope();
            var cache = scope.ServiceProvider.GetService<ICacheService>();

            // Try to get from distributed cache first (when ICacheService is available)
            if (cache != null)
            {
                var cachedGroups = await cache.GetAsync<List<AnalysisGroup>>(cacheKey, cancellationToken);
                if (cachedGroups != null)
                {
                    _logger.LogDebug("[CACHE-HIT] Ready groups for {Role} with status {Status}: {Count} groups",
                        roleName, eligibleStatus, cachedGroups.Count);
                    return cachedGroups;
                }
            }

            // Cache miss - query database
            _logger.LogDebug("[CACHE-MISS] Querying ready groups for {Role} with status {Status}",
                roleName, eligibleStatus);

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // ✅ MEMORY FIX: Limit to top 200 groups to prevent loading thousands into memory
            // Groups are ordered by Priority descending, so we get the most important ones first
            // This prevents memory bloat when there are many eligible groups
            var eligibleGroups = await db.AnalysisGroups
                .Where(g => g.Status == eligibleStatus)
                .OrderByDescending(g => g.Priority)
                .Take(_maxReadyGroups)
                .AsNoTracking()
                .ToListAsync(cancellationToken);

            var disabledGroups = eligibleGroups
                .Where(g => !IsAssignmentIntakeEnabled(g.ScannerType))
                .ToList();
            if (disabledGroups.Count > 0)
            {
                eligibleGroups = eligibleGroups
                    .Where(g => IsAssignmentIntakeEnabled(g.ScannerType))
                    .ToList();

                _logger.LogInformation(
                    "[CACHE-FILTER] Excluded {Count} {Role}/{Status} group(s) whose scanner workflow assignment intake is disabled: {ScannerTypes}",
                    disabledGroups.Count,
                    roleName,
                    eligibleStatus,
                    string.Join(", ", disabledGroups.Select(g => g.ScannerType ?? "Unknown").Distinct().Take(10)));
            }

            var compositeScanPairGroups = eligibleGroups
                .Where(g => IsCompositeContainerPairIdentifier(g.GroupIdentifier))
                .ToList();
            if (compositeScanPairGroups.Count > 0)
            {
                eligibleGroups = eligibleGroups
                    .Where(g => !IsCompositeContainerPairIdentifier(g.GroupIdentifier))
                    .ToList();

                _logger.LogWarning(
                    "[CACHE-FILTER] Excluded {Count} {Role}/{Status} group(s) whose identifiers are scan-pair container lists: {Groups}",
                    compositeScanPairGroups.Count,
                    roleName,
                    eligibleStatus,
                    string.Join(", ", compositeScanPairGroups.Select(g => g.GroupIdentifier).Take(10)));
            }

            if (!eligibleGroups.Any())
            {
                var emptyResult = new List<AnalysisGroup>();
                if (cache != null && _cacheEmptyResults)
                {
                    await cache.SetAsync(cacheKey, emptyResult, TimeSpan.FromSeconds(_cacheExpirationSeconds), cancellationToken);
                    _logger.LogInformation("[CACHE-SET] Cached empty ready groups for {Role} with status {Status} (expires in {Seconds}s)",
                        roleName, eligibleStatus, _cacheExpirationSeconds);
                }
                else if (cache != null)
                {
                    _logger.LogDebug("[CACHE-SKIP] Empty ready groups for {Role} with status {Status} not cached",
                        roleName, eligibleStatus);
                }
                return emptyResult;
            }

            // ── Pattern-A-aware join key resolution ─────────────────────────────
            // Pattern A: when an RCS-anchored AG has a ContainerGroupKey, the CCS row is
            // keyed by that CGK (the container number), not by the AG's own declaration.
            // For non-Pattern-A groups CGK is null and we fall back to the historical
            // NormalizedGroupIdentifier — preserving prior behaviour.
            var rcsIds = eligibleGroups
                .Where(g => g.RecordCompletenessStatusId.HasValue)
                .Select(g => g.RecordCompletenessStatusId!.Value)
                .Distinct()
                .ToList();
            var cgkByRcsId = rcsIds.Count > 0
                ? await db.RecordCompletenessStatuses
                    .AsNoTracking()
                    .Where(r => rcsIds.Contains(r.Id) && !string.IsNullOrEmpty(r.ContainerGroupKey))
                    .ToDictionaryAsync(r => r.Id, r => r.ContainerGroupKey!, cancellationToken)
                : new Dictionary<int, string>();

            string EffectiveJoinKey(AnalysisGroup g)
            {
                if (g.RecordCompletenessStatusId.HasValue
                    && cgkByRcsId.TryGetValue(g.RecordCompletenessStatusId.Value, out var cgk)
                    && !string.IsNullOrEmpty(cgk))
                {
                    return cgk;
                }
                return !string.IsNullOrEmpty(g.NormalizedGroupIdentifier)
                    ? g.NormalizedGroupIdentifier
                    : (GroupIdentifierHelper.GetNormalizedGroupIdentifier(g.GroupIdentifier) ?? g.GroupIdentifier ?? "");
            }

            // Load containers for these groups using the effective join key.
            var normalizedForCompleteness = eligibleGroups
                .Select(EffectiveJoinKey)
                .Where(g => !string.IsNullOrEmpty(g))
                .Distinct()
                .ToList();
            var containers = new List<ContainerCompletenessStatus>();
            const int batchSize = 500;

            if (normalizedForCompleteness.Count > 0)
            {
                for (int i = 0; i < normalizedForCompleteness.Count; i += batchSize)
                {
                    var batch = normalizedForCompleteness.Skip(i).Take(batchSize).ToList();
                    var parameters = new List<NpgsqlParameter>();
                    var paramNames = new List<string>();
                    for (int j = 0; j < batch.Count; j++)
                    {
                        var paramName = $"@g{j}";
                        paramNames.Add(paramName);
                        parameters.Add(new NpgsqlParameter(paramName, batch[j]));
                    }
                    var sql = $"SELECT * FROM ContainerCompletenessStatuses WHERE GroupIdentifier IN ({string.Join(",", paramNames)})";
                    var batchContainers = await db.ContainerCompletenessStatuses
                        .FromSqlRaw(sql, parameters.ToArray())
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);
                    containers.AddRange(batchContainers);
                }
            }

            // Group and aggregate in memory - join via EffectiveJoinKey (CGK for Pattern A,
            // NormalizedGroupIdentifier otherwise). Use a small mutable holder so the
            // second-chance pass below can replace stats in-place for orphan groups.
            var readyGroupsWithStats = eligibleGroups
                .GroupJoin(
                    containers,
                    g => EffectiveJoinKey(g),
                    c => c.GroupIdentifier ?? "",
                    (g, containerGroup) => new ReadyGroupStats
                    {
                        Group = g,
                        TotalContainers = containerGroup.Count(),
                        ImageAnalysisContainers = containerGroup.Count(c => c.WorkflowStage == "ImageAnalysis"),
                        PendingOrNullContainers = containerGroup.Count(c => string.IsNullOrEmpty(c.WorkflowStage) || c.WorkflowStage == "Pending"),
                        AuditContainers = containerGroup.Count(c => c.WorkflowStage == "Audit"),
                        CompletedContainers = containerGroup.Count(c => c.WorkflowStage == "Completed")
                    })
                .ToList();

            // ── Second-chance pass: rescue groups whose primary join missed ─────
            // For AGs whose key-based join produced zero containers, look up CCS rows
            // by analysisrecords.containernumber. This is the canonical relationship
            // and rescues groups whose CCS rows were keyed historically by a sibling
            // declaration rather than the canonical ContainerGroupKey.
            var orphanAgIds = readyGroupsWithStats
                .Where(w => w.TotalContainers == 0)
                .Select(w => w.Group.Id)
                .ToList();

            if (orphanAgIds.Count > 0)
            {
                var orphanRecords = await db.AnalysisRecords
                    .AsNoTracking()
                    .Where(r => orphanAgIds.Contains(r.GroupId))
                    .Select(r => new { r.GroupId, r.ContainerNumber })
                    .ToListAsync(cancellationToken);

                var distinctOrphanCns = orphanRecords
                    .Select(x => x.ContainerNumber)
                    .Where(cn => !string.IsNullOrEmpty(cn))
                    .Distinct()
                    .ToList();

                var orphanCcs = distinctOrphanCns.Count > 0
                    ? await db.ContainerCompletenessStatuses
                        .AsNoTracking()
                        .Where(c => distinctOrphanCns.Contains(c.ContainerNumber))
                        .ToListAsync(cancellationToken)
                    : new List<ContainerCompletenessStatus>();

                // For each container number, take the most-recently-updated CCS row.
                var ccsByCn = orphanCcs
                    .GroupBy(c => c.ContainerNumber)
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(c => c.UpdatedAt).First());

                int rescuedCount = 0;
                // Materialize the orphan list before mutating to avoid iterator
                // re-evaluation as TotalContainers transitions from 0.
                var orphans = readyGroupsWithStats.Where(x => x.TotalContainers == 0).ToList();
                foreach (var w in orphans)
                {
                    var matched = orphanRecords
                        .Where(x => x.GroupId == w.Group.Id)
                        .Select(x => ccsByCn.TryGetValue(x.ContainerNumber, out var c) ? c : null)
                        .Where(c => c != null)
                        .Cast<ContainerCompletenessStatus>()
                        .ToList();
                    if (matched.Count == 0) continue;

                    w.TotalContainers = matched.Count;
                    w.ImageAnalysisContainers = matched.Count(c => c.WorkflowStage == "ImageAnalysis");
                    w.PendingOrNullContainers = matched.Count(c => string.IsNullOrEmpty(c.WorkflowStage) || c.WorkflowStage == "Pending");
                    w.AuditContainers = matched.Count(c => c.WorkflowStage == "Audit");
                    w.CompletedContainers = matched.Count(c => c.WorkflowStage == "Completed");
                    rescuedCount++;
                }

                if (rescuedCount > 0)
                {
                    _logger.LogInformation(
                        "[CACHE-FILTER] Second-chance pass rescued {Rescued} of {Orphans} orphan groups via containernumber join",
                        rescuedCount, orphanAgIds.Count);
                }
            }

            // ✅ DIAGNOSTIC: Log WorkflowStage distribution for first few groups
            var sampleGroups = readyGroupsWithStats.Take(5).ToList();
            foreach (var sample in sampleGroups)
            {
                _logger.LogDebug(
                    "[CACHE-FILTER] Group {GroupIdentifier}: Total={Total}, ImageAnalysis={ImageAnalysis}, Pending/Null={Pending}, Audit={Audit}, Completed={Completed}",
                    sample.Group.GroupIdentifier, sample.TotalContainers, sample.ImageAnalysisContainers,
                    sample.PendingOrNullContainers, sample.AuditContainers, sample.CompletedContainers);
            }

            // Apply WorkflowStage filtering based on role
            if (roleName == "Analyst")
            {
                var beforeFilter = readyGroupsWithStats.Count;
                var excludedNoContainers = readyGroupsWithStats.Count(w => w.TotalContainers == 0);
                var excludedAllAudit = readyGroupsWithStats.Count(w => w.TotalContainers > 0 && w.AuditContainers == w.TotalContainers && w.PendingOrNullContainers == 0 && w.ImageAnalysisContainers == 0);
                var excludedAllCompleted = readyGroupsWithStats.Count(w => w.TotalContainers > 0 && w.CompletedContainers == w.TotalContainers);

                readyGroupsWithStats = readyGroupsWithStats
                    .Where(w => w.ImageAnalysisContainers > 0 ||
                                w.PendingOrNullContainers > 0 ||  // ✅ FIX: Explicitly include Pending/Null containers
                                (w.ImageAnalysisContainers == 0 &&
                                 w.PendingOrNullContainers == 0 &&
                                 w.AuditContainers < w.TotalContainers &&
                                 w.CompletedContainers < w.TotalContainers))
                    .ToList();
                var afterFilter = readyGroupsWithStats.Count;

                _logger.LogInformation(
                    "[CACHE-FILTER] Analyst role filter: {Before} → {After} groups (excluded {Excluded}: {NoContainers}=no-CCS-rows, {AllAudit}=all-Audit, {AllCompleted}=all-Completed)",
                    beforeFilter, afterFilter, beforeFilter - afterFilter,
                    excludedNoContainers, excludedAllAudit, excludedAllCompleted);
            }
            else if (roleName == "Audit")
            {
                // B2′-B (2026-05-09): previously this filter required AuditContainers > 0,
                // i.e. at least one CCS row with WorkflowStage='Audit'. That gated audit
                // assignment on the parallel-state-surface drift: AGs whose AG.Status had
                // legitimately transitioned to AnalystCompleted but whose CCS rows still
                // had WorkflowStage in {ImageAnalysis, PendingSubmission, null} were
                // silently excluded, even with auditors Ready. Sprint 5G2 / B1 (2026-05-07)
                // made AG.Status state-machine-authoritative; CCS.WorkflowStage is lagging
                // and aspirational. Drop the AuditContainers gate. Keep
                // CompletedContainers < TotalContainers (defence-in-depth — don't pull in
                // fully-completed AGs that should already be past audit-eligible status).
                // The orphan-AG guard below still rejects truly-empty AGs.
                var beforeFilter = readyGroupsWithStats.Count;
                var excludedNoContainers = readyGroupsWithStats.Count(w => w.TotalContainers == 0);
                var noAuditCcs = readyGroupsWithStats.Count(w => w.TotalContainers > 0 && w.AuditContainers == 0);
                var excludedAllCompleted = readyGroupsWithStats.Count(w => w.TotalContainers > 0 && w.CompletedContainers == w.TotalContainers);

                readyGroupsWithStats = readyGroupsWithStats
                    .Where(w => w.CompletedContainers < w.TotalContainers)
                    .ToList();
                var afterFilter = readyGroupsWithStats.Count;

                _logger.LogInformation(
                    "[CACHE-FILTER] Audit role filter: {Before} → {After} groups (excluded {Excluded}: {NoContainers}=no-CCS-rows, {AllCompleted}=all-Completed; drift-surfaced {NoAuditCcs}=Status-says-audit-but-CCS-WorkflowStage-disagrees)",
                    beforeFilter, afterFilter, beforeFilter - afterFilter,
                    excludedNoContainers, excludedAllCompleted, noAuditCcs);
            }

            // ── Orphan-AG guard ─────────────────────────────────────────────
            // 2026-05-04 (2.16.1): exclude AGs whose every container has NULL
            // BOEDocumentId AND zero active ContainerBOERelations. Those AGs
            // are FS6000 export-pending scans that are correctly held in
            // Export-Hold pending an ICUMS export-feed extension; the orchestrator
            // should not keep cycling them through analyst assignment. Without
            // this guard the lease sweeper re-issues an assignment every cycle,
            // surfacing a "phantom" 10-container assignment on the analyst's
            // workbench (declaration 70326214329 was the user-reported case).
            if (readyGroupsWithStats.Count > 0)
            {
                var candidateAgIds = readyGroupsWithStats.Select(w => w.Group.Id).ToList();
                var nonOrphanAgIds = new HashSet<Guid>();

                const int agBatchSize = 200;
                for (int i = 0; i < candidateAgIds.Count; i += agBatchSize)
                {
                    var batch = candidateAgIds.Skip(i).Take(agBatchSize).ToList();
                    // EF Core translates this to a single SQL with two NOT EXISTS subqueries —
                    // mirrors the predicate in tools/.../OrphanAgSweep manual cleanup.
                    var batchNonOrphans = await db.AnalysisGroups
                        .AsNoTracking()
                        .Where(g => batch.Contains(g.Id) && (
                            db.AnalysisRecords.Any(r => r.GroupId == g.Id &&
                                db.ContainerCompletenessStatuses.Any(c =>
                                    c.ContainerNumber == r.ContainerNumber && c.BOEDocumentId != null))
                            ||
                            db.AnalysisRecords.Any(r => r.GroupId == g.Id &&
                                db.ContainerBOERelations.Any(cbr =>
                                    cbr.ContainerNumber == r.ContainerNumber && cbr.IsActive))
                        ))
                        .Select(g => g.Id)
                        .ToListAsync(cancellationToken);

                    foreach (var id in batchNonOrphans) nonOrphanAgIds.Add(id);
                }

                var orphanCount = readyGroupsWithStats.Count - nonOrphanAgIds.Count;
                if (orphanCount > 0)
                {
                    readyGroupsWithStats = readyGroupsWithStats
                        .Where(w => nonOrphanAgIds.Contains(w.Group.Id))
                        .ToList();
                    _logger.LogInformation(
                        "[CACHE-FILTER] Orphan-AG guard for {Role}: excluded {Excluded} AGs (every container has no boedocumentid + no active CBR), {Remaining} AGs remain",
                        roleName, orphanCount, readyGroupsWithStats.Count);
                }
            }

            // Order and take top 50
            var readyGroups = readyGroupsWithStats
                .OrderByDescending(x => x.Group.Priority)
                .Take(50)
                .Select(x => x.Group)
                .ToList();

            // Cache the result (distributed cache when available). Empty queues are deliberately
            // uncached by default so a missed invalidation cannot hide newly-ready work.
            if (cache != null && (readyGroups.Count > 0 || _cacheEmptyResults))
            {
                await cache.SetAsync(cacheKey, readyGroups, TimeSpan.FromSeconds(_cacheExpirationSeconds), cancellationToken);
                _logger.LogInformation("[CACHE-SET] Cached {Count} ready groups for {Role} with status {Status} (expires in {Seconds}s)",
                    readyGroups.Count, roleName, eligibleStatus, _cacheExpirationSeconds);
            }
            else if (cache != null)
            {
                _logger.LogDebug("[CACHE-SKIP] Empty ready groups for {Role} with status {Status} not cached",
                    roleName, eligibleStatus);
            }

            return readyGroups;
        }

        /// <summary>
        /// Sync-compatible wrapper that schedules cache invalidation for a specific role and status.
        /// Prefer InvalidateCacheAsync from async code when the caller must wait for completion.
        /// </summary>
        public void InvalidateCache(string roleName, string status)
        {
            _ = InvalidateCacheBestEffortAsync(roleName, status);
        }

        /// <summary>
        /// Invalidate cache for a specific role and status (async)
        /// </summary>
        public async Task InvalidateCacheAsync(string roleName, string status, CancellationToken cancellationToken = default)
        {
            var cacheKey = $"{CacheKeyPrefix}:{roleName}:{status}";
            using var scope = _scopeFactory.CreateScope();
            var cache = scope.ServiceProvider.GetService<ICacheService>();
            if (cache != null)
            {
                await cache.RemoveAsync(cacheKey, cancellationToken);
                _logger.LogDebug("[CACHE-INVALIDATE] Invalidated cache for {Role} with status {Status}", roleName, status);
            }

            await PredictiveRoleInvalidationBestEffortAsync(roleName);
        }

        private async Task InvalidateCacheBestEffortAsync(string roleName, string status)
        {
            try
            {
                await InvalidateCacheAsync(roleName, status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CACHE-INVALIDATE] Background cache invalidation failed for {Role} with status {Status}", roleName, status);
            }
        }

        /// <summary>
        /// Sync-compatible wrapper that schedules invalidation of all ready groups caches.
        /// Prefer InvalidateAllCachesAsync from async code when the caller must wait for completion.
        /// </summary>
        public void InvalidateAllCaches()
        {
            _ = InvalidateAllCachesBestEffortAsync();
        }

        /// <summary>
        /// Invalidate all ready groups caches (async)
        /// </summary>
        public async Task InvalidateAllCachesAsync(CancellationToken cancellationToken = default)
        {
            using var scope = _scopeFactory.CreateScope();
            var cache = scope.ServiceProvider.GetService<ICacheService>();
            if (cache != null)
            {
                await cache.RemoveByPrefixAsync(CacheKeyPrefix, cancellationToken);
                _logger.LogDebug("[CACHE-INVALIDATE] Invalidated all ready groups caches");
            }

            var predictiveRoles = _configuration.GetSection("PredictivePreload:Roles").Get<string[]>()
                ?? new[] { "Analyst", "Audit" };
            foreach (var role in predictiveRoles.Where(r => !string.IsNullOrWhiteSpace(r)).Distinct(StringComparer.OrdinalIgnoreCase))
            {
                await PredictiveRoleInvalidationBestEffortAsync(role);
            }
        }

        private async Task InvalidateAllCachesBestEffortAsync()
        {
            try
            {
                await InvalidateAllCachesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[CACHE-INVALIDATE] Background cache invalidation failed for all ready groups caches");
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Materialized Assignment Queue — AnalysisQueueEntries table
        // Maintains a pre-computed row per active assignment so
        // GetMyAssignments reads a single table instead of 7-8 joins.
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Create or update a queue entry for an active assignment.
        /// Computes container counts, decision counts, and BOE consolidation.
        /// Idempotent — safe to call multiple times.
        /// </summary>
        public async Task UpsertQueueEntryAsync(ApplicationDbContext db, int assignmentId, CancellationToken ct = default)
        {
            try
            {
                var assignment = await db.AnalysisAssignments
                    .AsNoTracking()
                    .FirstOrDefaultAsync(a => a.Id == assignmentId, ct);
                if (assignment == null || assignment.State != "Active") return;

                var group = await db.AnalysisGroups
                    .AsNoTracking()
                    .FirstOrDefaultAsync(g => g.Id == assignment.GroupId, ct);
                if (group == null) return;

                if (IsCompositeContainerPairIdentifier(group.GroupIdentifier))
                {
                    _logger.LogWarning(
                        "[QUEUE] Skipping materialized queue entry for group {GroupId} ({GroupIdentifier}) because the identifier is a scan-pair container list, not a cargo/record key.",
                        group.Id,
                        group.GroupIdentifier);
                    return;
                }

                // Container list from AnalysisRecords
                var containers = await db.AnalysisRecords
                    .Where(r => r.GroupId == group.Id)
                    .Select(r => r.ContainerNumber)
                    .Distinct()
                    .ToListAsync(ct);

                // Container completeness
                var normalizedId = GroupIdentifierHelper.GetNormalizedGroupIdentifier(group.GroupIdentifier) ?? group.GroupIdentifier;
                int withImages = 0, withoutImages = 0;
                if (!string.IsNullOrEmpty(normalizedId))
                {
                    var completeness = await db.ContainerCompletenessStatuses
                        .Where(c => c.GroupIdentifier == normalizedId)
                        .Select(c => c.HasImageData)
                        .ToListAsync(ct);
                    withImages = completeness.Count(x => x);
                    withoutImages = completeness.Count(x => !x);
                }

                // Decision counts
                var decidedCount = await db.ImageAnalysisDecisions
                    .CountAsync(d => d.GroupIdentifier == group.GroupIdentifier
                        || d.GroupIdentifier == group.NormalizedGroupIdentifier, ct);

                // BOE consolidation (cross-DB, best-effort).
                // 2026-05-04 (2.16.3): require MasterBlNumber != NULL to consider a BOE truly
                // consolidated. Some BOEs are mis-tagged at ingest with `IsConsolidated=true`
                // despite `MasterBlNumber=NULL` — they don't actually have a master BL, so
                // they're not really consolidated. The frontend dialog assumes
                // `IsConsolidated => GroupIdentifier IS a container number` and routes
                // `GET /api/containerdetails/{scanner|icums|images}/{groupIdentifier}` accordingly.
                // For the 8+ mis-tagged declarations (e.g. 41225848361), GroupIdentifier was a
                // declaration number — the path lookup 404'd, blanking Scanner/ICUMS/Image tabs.
                bool isConsolidated = false;
                try
                {
                    using var icumScope = _scopeFactory.CreateScope();
                    var icumDb = icumScope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
                    icumDb.Database.SetCommandTimeout(10);
                    var boe = await icumDb.BOEDocuments
                        .AsNoTracking()
                        .Where(b => b.ContainerNumber == group.GroupIdentifier
                            || b.DeclarationNumber == group.GroupIdentifier)
                        .Select(b => new { b.IsConsolidated, b.MasterBlNumber })
                        .FirstOrDefaultAsync(ct);
                    isConsolidated = boe != null && boe.IsConsolidated && !string.IsNullOrEmpty(boe.MasterBlNumber);
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "[QUEUE] BOE consolidation check failed for {Group}, defaulting to false", group.GroupIdentifier);
                }

                var containersJson = System.Text.Json.JsonSerializer.Serialize(
                    containers.Where(c => !string.IsNullOrEmpty(c)).ToList());

                // Capture values into local typed variables so the FormattableString
                // binds them with the correct .NET type. EF Core 9's interpolation
                // handles nullable types correctly (unlike ExecuteSqlRawAsync with
                // positional object? parameters which requires typed NpgsqlParameter
                // for nulls). This is the safer pattern going forward.
                var leaseUntil = assignment.LeaseUntilUtc;
                var assignmentCreatedAt = assignment.CreatedAtUtc;
                var groupId = group.Id;
                var groupIdentifier = group.GroupIdentifier ?? "";
                var scannerType = group.ScannerType ?? "";
                var groupStatus = group.Status ?? "";
                var groupCreatedAt = group.CreatedAtUtc;
                var groupUpdatedAt = group.UpdatedAtUtc;
                var containerCount = containers.Count;
                var withImagesVal = withImages > 0 ? (int?)withImages : null;
                var withoutImagesVal = withoutImages > 0 ? (int?)withoutImages : null;
                var totalCount = group.TotalContainerCount;
                var submittedCount = group.SubmittedContainerCount;
                var pendingCount = group.PendingContainerCount;
                var partiallyCompletedDate = group.PartiallyCompletedDate;
                var queuedAt = DateTime.UtcNow;
                var refreshedAt = DateTime.UtcNow;

                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO analysisqueueentries (
                        assignmentid, assignedto, role, leaseuntilutc, assignmentcreatedatutc,
                        groupid, groupidentifier, scannertype, groupstatus, groupcreatedatutc, groupupdatedatutc,
                        containercount, containersjson, containerswithimages, containerswithoutimages,
                        decidedcount, totalcontainercount, submittedcontainercount, pendingcontainercount,
                        partiallycompleteddate, isconsolidated, queuedatutc, lastrefreshedatutc
                    ) VALUES (
                        {assignmentId}, {assignment.AssignedTo}, {assignment.Role}, {leaseUntil}, {assignmentCreatedAt},
                        {groupId}, {groupIdentifier}, {scannerType}, {groupStatus}, {groupCreatedAt}, {groupUpdatedAt},
                        {containerCount}, {containersJson}, {withImagesVal}, {withoutImagesVal},
                        {decidedCount}, {totalCount}, {submittedCount}, {pendingCount},
                        {partiallyCompletedDate}, {isConsolidated}, {queuedAt}, {refreshedAt}
                    )
                    ON CONFLICT (assignmentid) DO UPDATE SET
                        leaseuntilutc = EXCLUDED.leaseuntilutc,
                        groupstatus = EXCLUDED.groupstatus,
                        groupupdatedatutc = EXCLUDED.groupupdatedatutc,
                        scannertype = EXCLUDED.scannertype,
                        containercount = EXCLUDED.containercount,
                        containersjson = EXCLUDED.containersjson,
                        containerswithimages = EXCLUDED.containerswithimages,
                        containerswithoutimages = EXCLUDED.containerswithoutimages,
                        decidedcount = EXCLUDED.decidedcount,
                        totalcontainercount = EXCLUDED.totalcontainercount,
                        submittedcontainercount = EXCLUDED.submittedcontainercount,
                        pendingcontainercount = EXCLUDED.pendingcontainercount,
                        lastrefreshedatutc = EXCLUDED.lastrefreshedatutc");

                _logger.LogDebug("[QUEUE] Upserted entry for assignment {Id} group {Group}",
                    assignmentId, group.GroupIdentifier);
                InvalidateMyAssignmentsCache(assignment.AssignedTo, assignment.Role);
                QueuePredictiveAssignmentPreload(group.Id, assignment.Role, group.Status);
            }
            catch (Exception ex)
            {
                // ERROR level (not Warning) so it surfaces on the dashboard's
                // Recent Errors table and the AssignmentQueue health check will
                // detect the resulting divergence and trigger auto-repair within 2min.
                _logger.LogError(ex, "[QUEUE] UpsertQueueEntry failed for assignment {Id}", assignmentId);
            }
        }

        /// <summary>
        /// Remove queue entry when assignment is Released/Expired/Completed.
        /// </summary>
        public async Task RemoveQueueEntryAsync(ApplicationDbContext db, int assignmentId, CancellationToken ct = default)
        {
            try
            {
                var cacheTarget = await db.AnalysisAssignments
                    .AsNoTracking()
                    .Where(a => a.Id == assignmentId)
                    .Select(a => new { a.AssignedTo, a.Role, a.GroupId })
                    .FirstOrDefaultAsync(ct);

                if (cacheTarget == null)
                {
                    cacheTarget = await db.AnalysisQueueEntries
                        .AsNoTracking()
                        .Where(e => e.AssignmentId == assignmentId)
                        .Select(e => new { e.AssignedTo, e.Role, e.GroupId })
                        .FirstOrDefaultAsync(ct);
                }

                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM analysisqueueentries WHERE assignmentid = {0}", assignmentId);

                if (cacheTarget != null)
                {
                    InvalidateMyAssignmentsCache(cacheTarget.AssignedTo, cacheTarget.Role);
                    QueuePredictiveAssignmentInvalidation(cacheTarget.GroupId, cacheTarget.Role);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[QUEUE] RemoveQueueEntry failed for assignment {Id}", assignmentId);
            }
        }

        /// <summary>
        /// Remove all queue entries for a group (e.g. when group advances past audit).
        /// </summary>
        public async Task RemoveQueueEntriesForGroupAsync(ApplicationDbContext db, Guid groupId, CancellationToken ct = default)
        {
            try
            {
                var cacheTargets = await db.AnalysisQueueEntries
                    .AsNoTracking()
                    .Where(e => e.GroupId == groupId)
                    .Select(e => new { e.AssignedTo, e.Role })
                    .Distinct()
                    .ToListAsync(ct);

                if (cacheTargets.Count == 0)
                {
                    cacheTargets = await db.AnalysisAssignments
                        .AsNoTracking()
                        .Where(a => a.GroupId == groupId)
                        .Select(a => new { a.AssignedTo, a.Role })
                        .Distinct()
                        .ToListAsync(ct);
                }

                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM analysisqueueentries WHERE groupid = {0}", groupId);

                foreach (var cacheTarget in cacheTargets)
                {
                    InvalidateMyAssignmentsCache(cacheTarget.AssignedTo, cacheTarget.Role);
                    QueuePredictiveRoleInvalidation(cacheTarget.Role);
                }

                QueuePredictiveAssignmentInvalidation(groupId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[QUEUE] RemoveQueueEntriesForGroup failed for group {Id}", groupId);
            }
        }

        /// <summary>
        /// Safety-net reconciliation: syncs queue entries against live assignments.
        /// Called by housekeeping sweep. Adds missing entries, removes stale ones.
        /// </summary>
        public async Task ReconcileQueueAsync(ApplicationDbContext db, CancellationToken ct = default)
        {
            try
            {
                var now = DateTime.UtcNow;

                // All active assignments with valid leases
                var activeAssignments = await db.AnalysisAssignments
                    .AsNoTracking()
                    .Where(a => a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .Select(a => a.Id)
                    .ToListAsync(ct);

                var activeSet = new HashSet<int>(activeAssignments);

                // All queue entries
                var queueEntries = await db.AnalysisQueueEntries
                    .AsNoTracking()
                    .Select(e => new { e.AssignmentId, e.AssignedTo, e.Role, e.GroupId })
                    .ToListAsync(ct);

                var queueEntryIds = queueEntries.Select(e => e.AssignmentId).ToList();
                var queueSet = new HashSet<int>(queueEntryIds);

                // Missing: active assignment without queue entry → upsert
                var missing = activeAssignments.Where(id => !queueSet.Contains(id)).ToList();

                // Stale: queue entry without active assignment → delete
                var staleEntries = queueEntries.Where(e => !activeSet.Contains(e.AssignmentId)).ToList();
                var stale = staleEntries.Select(e => e.AssignmentId).ToList();

                if (missing.Count > 0)
                {
                    _logger.LogInformation("[QUEUE-RECONCILE] Found {Count} assignments without queue entries — upserting", missing.Count);
                    foreach (var id in missing.Take(50)) // Limit per cycle
                    {
                        await UpsertQueueEntryAsync(db, id, ct);
                    }
                }

                if (stale.Count > 0)
                {
                    _logger.LogInformation("[QUEUE-RECONCILE] Found {Count} stale queue entries — removing", stale.Count);
                    await db.AnalysisQueueEntries
                        .Where(e => stale.Contains(e.AssignmentId))
                        .ExecuteDeleteAsync(ct);

                    foreach (var staleEntry in staleEntries
                        .Select(e => new { e.AssignedTo, e.Role })
                        .Distinct())
                    {
                        InvalidateMyAssignmentsCache(staleEntry.AssignedTo, staleEntry.Role);
                        QueuePredictiveRoleInvalidation(staleEntry.Role);
                    }

                    foreach (var staleGroupId in staleEntries.Select(e => e.GroupId).Distinct())
                    {
                        QueuePredictiveAssignmentInvalidation(staleGroupId);
                    }
                }

                if (missing.Count == 0 && stale.Count == 0)
                {
                    _logger.LogDebug("[QUEUE-RECONCILE] Queue is in sync ({Count} entries)", queueEntryIds.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[QUEUE-RECONCILE] Reconciliation failed");
            }
        }

        /// <summary>
        /// Mutable holder for per-group container stats — replaces the prior anonymous
        /// type so the second-chance pass can update counters in-place when a group's
        /// primary key-based join misses (e.g. CCS row keyed by sibling declaration).
        /// </summary>
        private sealed class ReadyGroupStats
        {
            public AnalysisGroup Group { get; set; } = null!;
            public int TotalContainers { get; set; }
            public int ImageAnalysisContainers { get; set; }
            public int PendingOrNullContainers { get; set; }
            public int AuditContainers { get; set; }
            public int CompletedContainers { get; set; }
        }

        private static string GetMyAssignmentsCacheKey(string username, string role)
            => $"{MyAssignmentsCacheKeyPrefix}:{username}:{role}";

        private static bool IsCompositeContainerPairIdentifier(string? identifier)
        {
            var parts = (identifier ?? string.Empty)
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(part => !string.IsNullOrWhiteSpace(part))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            return parts.Count >= 2 && parts.All(IsLikelyIsoContainerNumber);
        }

        private static bool IsLikelyIsoContainerNumber(string value)
        {
            var token = value.Trim();
            return token.Length == 11
                && token.Take(4).All(char.IsLetter)
                && token.Skip(4).All(char.IsDigit);
        }

        private void InvalidateMyAssignmentsCache(string? username, string? role)
        {
            if (_memoryCache == null || string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(role))
                return;

            _memoryCache.Remove(GetMyAssignmentsCacheKey(username, role));
            _logger.LogDebug("[CACHE-INVALIDATE] Invalidated my-assignments cache for {User}/{Role}", username, role);
        }

        private void QueuePredictiveAssignmentPreload(Guid groupId, string? role, string? status)
        {
            if (string.IsNullOrWhiteSpace(role))
                return;

            _ = PredictiveAssignmentPreloadBestEffortAsync(groupId, role, status ?? string.Empty);
        }

        private async Task PredictiveAssignmentPreloadBestEffortAsync(Guid groupId, string role, string status)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var predictivePreload = scope.ServiceProvider.GetService<NickScanCentralImagingPortal.Services.Caching.IPredictivePreloadService>();
                if (predictivePreload == null)
                    return;

                await predictivePreload.PreloadAssignmentAsync(groupId, role, status, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[PREDICTIVE-PRELOAD] Best-effort assignment preload failed for group {GroupId}", groupId);
            }
        }

        private void QueuePredictiveAssignmentInvalidation(Guid groupId, string? role = null)
        {
            _ = PredictiveAssignmentInvalidationBestEffortAsync(groupId, role);
        }

        private async Task PredictiveAssignmentInvalidationBestEffortAsync(Guid groupId, string? role)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var predictivePreload = scope.ServiceProvider.GetService<NickScanCentralImagingPortal.Services.Caching.IPredictivePreloadService>();
                if (predictivePreload == null)
                    return;

                await predictivePreload.InvalidateAssignmentAsync(groupId, CancellationToken.None);
                if (!string.IsNullOrWhiteSpace(role))
                {
                    await predictivePreload.InvalidateRoleAssignmentsAsync(role, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[PREDICTIVE-PRELOAD] Best-effort assignment invalidation failed for group {GroupId}", groupId);
            }
        }

        private void QueuePredictiveRoleInvalidation(string? role)
        {
            if (string.IsNullOrWhiteSpace(role))
                return;

            _ = PredictiveRoleInvalidationBestEffortAsync(role);
        }

        private async Task PredictiveRoleInvalidationBestEffortAsync(string role)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var predictivePreload = scope.ServiceProvider.GetService<NickScanCentralImagingPortal.Services.Caching.IPredictivePreloadService>();
                if (predictivePreload == null)
                    return;

                await predictivePreload.InvalidateRoleAssignmentsAsync(role, CancellationToken.None);
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[PREDICTIVE-PRELOAD] Best-effort role invalidation failed for role {Role}", role);
            }
        }

        private bool IsAssignmentIntakeEnabled(string? scannerType)
        {
            var normalized = NormalizeScannerType(scannerType);
            if (string.IsNullOrEmpty(normalized))
            {
                return true;
            }

            if (normalized == "EAGLEA25")
            {
                return _configuration.GetValue<bool>("ScannerWorkflow:EagleA25:AssignmentIntakeEnabled", false);
            }

            var disabled = _configuration
                .GetSection("ScannerWorkflow:DisabledAssignmentIntakeScannerTypes")
                .Get<string[]>() ?? Array.Empty<string>();

            return !disabled.Any(s => NormalizeScannerType(s) == normalized);
        }

        private static string NormalizeScannerType(string? scannerType)
            => string.IsNullOrWhiteSpace(scannerType)
                ? string.Empty
                : scannerType.Trim()
                    .Replace("_", string.Empty, StringComparison.Ordinal)
                    .Replace("-", string.Empty, StringComparison.Ordinal)
                    .Replace(" ", string.Empty, StringComparison.Ordinal)
                    .ToUpperInvariant();
    }
}

