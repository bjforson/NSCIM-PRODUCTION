using Npgsql;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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
        private readonly int _cacheExpirationSeconds;
        private readonly int _maxReadyGroups;
        private const string CacheKeyPrefix = "ReadyGroups";

        public ReadyGroupsCacheService(
            IServiceScopeFactory scopeFactory,
            ILogger<ReadyGroupsCacheService> logger,
            IConfiguration configuration)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _configuration = configuration;
            _cacheExpirationSeconds = _configuration.GetValue<int>("ReadyGroupsCache:ExpirationSeconds", 30);
            _maxReadyGroups = _configuration.GetValue<int>("ReadyGroupsCache:MaxGroups", 200);
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

            if (!eligibleGroups.Any())
            {
                // Cache empty result too (prevents repeated queries for empty results)
                var emptyResult = new List<AnalysisGroup>();
                if (cache != null)
                {
                    await cache.SetAsync(cacheKey, emptyResult, TimeSpan.FromSeconds(_cacheExpirationSeconds), cancellationToken);
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
                var beforeFilter = readyGroupsWithStats.Count;
                var excludedNoContainers = readyGroupsWithStats.Count(w => w.TotalContainers == 0);
                var excludedNoAudit = readyGroupsWithStats.Count(w => w.TotalContainers > 0 && w.AuditContainers == 0);
                var excludedAllCompleted = readyGroupsWithStats.Count(w => w.TotalContainers > 0 && w.CompletedContainers == w.TotalContainers);

                readyGroupsWithStats = readyGroupsWithStats
                    .Where(w => w.AuditContainers > 0 && w.CompletedContainers < w.TotalContainers)
                    .ToList();
                var afterFilter = readyGroupsWithStats.Count;

                _logger.LogInformation(
                    "[CACHE-FILTER] Audit role filter: {Before} → {After} groups (excluded {Excluded}: {NoContainers}=no-CCS-rows, {NoAudit}=no-Audit-containers, {AllCompleted}=all-Completed)",
                    beforeFilter, afterFilter, beforeFilter - afterFilter,
                    excludedNoContainers, excludedNoAudit, excludedAllCompleted);
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

            // Cache the result (distributed cache when available)
            if (cache != null)
            {
                await cache.SetAsync(cacheKey, readyGroups, TimeSpan.FromSeconds(_cacheExpirationSeconds), cancellationToken);
                _logger.LogInformation("[CACHE-SET] Cached {Count} ready groups for {Role} with status {Status} (expires in {Seconds}s)",
                    readyGroups.Count, roleName, eligibleStatus, _cacheExpirationSeconds);
            }

            return readyGroups;
        }

        /// <summary>
        /// Invalidate cache for a specific role and status
        /// Call this when groups are updated to ensure fresh data
        /// </summary>
        public void InvalidateCache(string roleName, string status)
        {
            InvalidateCacheAsync(roleName, status).GetAwaiter().GetResult();
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
        }

        /// <summary>
        /// Invalidate all ready groups caches
        /// Uses RemoveByPrefixAsync when supported (Redis); otherwise cache entries expire naturally
        /// </summary>
        public void InvalidateAllCaches()
        {
            InvalidateAllCachesAsync().GetAwaiter().GetResult();
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

                // BOE consolidation (cross-DB, best-effort)
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
                        .Select(b => b.IsConsolidated)
                        .FirstOrDefaultAsync(ct);
                    isConsolidated = boe;
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
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM analysisqueueentries WHERE assignmentid = {0}", assignmentId);
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
                await db.Database.ExecuteSqlRawAsync(
                    "DELETE FROM analysisqueueentries WHERE groupid = {0}", groupId);
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
                var queueEntryIds = await db.AnalysisQueueEntries
                    .AsNoTracking()
                    .Select(e => e.AssignmentId)
                    .ToListAsync(ct);

                var queueSet = new HashSet<int>(queueEntryIds);

                // Missing: active assignment without queue entry → upsert
                var missing = activeAssignments.Where(id => !queueSet.Contains(id)).ToList();

                // Stale: queue entry without active assignment → delete
                var stale = queueEntryIds.Where(id => !activeSet.Contains(id)).ToList();

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
                    var staleIds = string.Join(",", stale);
                    await db.Database.ExecuteSqlRawAsync(
                        $"DELETE FROM analysisqueueentries WHERE assignmentid IN ({staleIds})");
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
    }
}

