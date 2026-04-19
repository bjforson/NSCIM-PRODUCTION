using Npgsql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    // 2026-04-19: added class-level [Authorize] after a security audit found the
    // per-action [AllowAnonymous] markers were serving as the only auth gate on
    // admin/operational endpoints (agent settings, audit log, assignments, etc.).
    // Removing [AllowAnonymous] alone was insufficient because the class had no
    // default auth requirement — requests fell through to anonymous-allowed.
    [Authorize]
    [ApiController]
    [Route("api/image-analysis-management")]
    public class ImageAnalysisManagementController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IcumDownloadsDbContext _icumDb;
        private readonly ILogger<ImageAnalysisManagementController> _logger;
        private readonly IMemoryCache _cache;
        private const string ReadyGroupsCacheKey = "ready-groups";
        private static readonly TimeSpan ReadyGroupsCacheExpiration = TimeSpan.FromMinutes(2); // ✅ FIX: Increased to 2 minutes to reduce database load

        // ✅ Helper class for BOE lookup results (only the 3 columns we need)
        private class BoeLookupResult
        {
            public string? ContainerNumber { get; set; }
            public string? DeclarationNumber { get; set; }
            public bool IsConsolidated { get; set; }
        }

        public ImageAnalysisManagementController(
            ApplicationDbContext db,
            IcumDownloadsDbContext icumDb,
            ILogger<ImageAnalysisManagementController> logger,
            IMemoryCache cache)
        {
            _db = db;
            _icumDb = icumDb;
            _logger = logger;
            _cache = cache;
        }

        // GET /api/image-analysis/service-state
        // 2026-04-19: removed [AllowAnonymous] + fake-defaults fallback.
        [HttpGet("service-state")]
        public async Task<ActionResult<AnalysisSettings>> GetServiceState()
        {
            var settings = await _db.AnalysisSettings.AsNoTracking().FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new AnalysisSettings();
                _db.AnalysisSettings.Add(settings);
                await _db.SaveChangesAsync();
            }
            return Ok(settings);
        }

        // POST /api/image-analysis/service-state
        [HttpPost("service-state")]
        [HasPermission(Permissions.ControllersImageAnalysisManagementSettings)]
        public async Task<ActionResult> UpdateServiceState([FromBody] UpdateServiceStateRequest request)
        {
            // ✅ CRITICAL: Log at the very start to confirm endpoint is being called
            _logger.LogInformation("🔵🔵🔵 UpdateServiceState ENDPOINT CALLED 🔵🔵🔵");
            _logger.LogInformation("User: {User}, Authenticated: {IsAuthenticated}",
                User.Identity?.Name ?? "null", User.Identity?.IsAuthenticated ?? false);

            // ✅ CRITICAL: Log the raw request to see what was actually received (after deserialization)
            var requestJson = System.Text.Json.JsonSerializer.Serialize(request);
            _logger.LogInformation("UpdateServiceState called by {User}. Raw Request JSON: {RequestJson}",
                User.Identity?.Name, requestJson);
            _logger.LogInformation("UpdateServiceState called by {User}. Request: Enabled={Enabled}, AssignmentMode={AssignmentMode}, LeaseMinutes={LeaseMinutes}, MaxConcurrent={MaxConcurrent}",
                User.Identity?.Name, request?.enabled, request?.assignmentMode, request?.leaseMinutes, request?.maxConcurrent);

            // ✅ CRITICAL: Log if assignmentMode is null or empty
            if (string.IsNullOrEmpty(request?.assignmentMode))
            {
                _logger.LogWarning("⚠️ WARNING: request.assignmentMode is null or empty! Will default to 'Manual'");
            }
            else
            {
                _logger.LogInformation("✅ request.assignmentMode value: '{AssignmentMode}' (Length: {Length})",
                    request.assignmentMode, request.assignmentMode.Length);
                _logger.LogInformation("   Raw value bytes: {Bytes}",
                    string.Join(", ", System.Text.Encoding.UTF8.GetBytes(request.assignmentMode).Select(b => b.ToString("X2"))));
            }

            if (request == null)
            {
                _logger.LogError("UpdateServiceState called with null request");
                return BadRequest(new { error = "Request body is required" });
            }

            // ⚠️ ApplicationDbContext defaults to NoTracking queries. We must explicitly enable tracking
            // here because we intend to modify and persist this entity within the same DbContext scope.
            var settings = await _db.AnalysisSettings.AsTracking().FirstOrDefaultAsync();
            if (settings == null)
            {
                _logger.LogInformation("Creating new AnalysisSettings record");
                settings = new AnalysisSettings();
                _db.AnalysisSettings.Add(settings);
            }

            var oldMode = settings.AssignmentMode;
            var oldEnabled = settings.Enabled;

            // ✅ CRITICAL: Trim and validate assignmentMode before saving
            var assignmentModeToSave = request.assignmentMode?.Trim() ?? "Manual";

            // ✅ Validate that it's one of the allowed values
            var allowedModes = new[] { "Auto", "Manual", "UserClaim" };
            if (!allowedModes.Contains(assignmentModeToSave, StringComparer.OrdinalIgnoreCase))
            {
                _logger.LogWarning("⚠️ WARNING: Invalid assignmentMode value: '{AssignmentMode}'. Allowed values: {Allowed}. Defaulting to 'Manual'",
                    assignmentModeToSave, string.Join(", ", allowedModes));
                assignmentModeToSave = "Manual";
            }
            else
            {
                // ✅ Normalize to exact case (Auto, Manual, UserClaim)
                assignmentModeToSave = allowedModes.First(m => string.Equals(m, assignmentModeToSave, StringComparison.OrdinalIgnoreCase));
                _logger.LogInformation("✅ Normalized assignmentMode: '{AssignmentMode}'", assignmentModeToSave);
            }

            settings.Enabled = request.enabled;
            settings.AssignmentMode = assignmentModeToSave;
            settings.AutoAssignStrategy = request.autoAssignStrategy ?? "RoundRobin";
#pragma warning disable CS0618 // 'AnalysisSettings.AutoAssign' is obsolete: 'Use AssignmentMode instead'
            settings.AutoAssign = request.autoAssign; // Keep for backward compatibility
#pragma warning restore CS0618
            settings.LeaseMinutes = request.leaseMinutes;
            // ✅ CRITICAL: Use the value from request, don't apply defaults (0 is valid)
            settings.MaxConcurrentPerUser = request.maxConcurrent;

            // Wave Processing settings (only update if provided)
            if (request.enableWaveProcessing.HasValue)
                settings.EnableWaveProcessing = request.enableWaveProcessing.Value;
            if (request.waveMinBatchSize.HasValue)
                settings.WaveMinBatchSize = Math.Clamp(request.waveMinBatchSize.Value, 1, 50);
            if (request.waveTimeoutHours.HasValue)
                settings.WaveTimeoutHours = Math.Clamp(request.waveTimeoutHours.Value, 1, 168);
            if (request.waveAutoCloseDays.HasValue)
                settings.WaveAutoCloseDays = Math.Clamp(request.waveAutoCloseDays.Value, 1, 90);

            // ✅ CRITICAL: Log what we're about to save
            _logger.LogInformation("💾 About to set settings: AssignmentMode='{AssignmentMode}', MaxConcurrent={MaxConcurrent}, LeaseMinutes={LeaseMinutes}",
                assignmentModeToSave, request.maxConcurrent, request.leaseMinutes);
            settings.UpdatedAtUtc = DateTime.UtcNow;

            // ✅ CRITICAL: Log BEFORE saving to database
            _logger.LogInformation("💾 About to save to database: AssignmentMode='{AssignmentMode}' (was '{OldMode}')",
                settings.AssignmentMode, oldMode);

            try
            {
                await _db.SaveChangesAsync();
                _logger.LogInformation("✅ Database save successful!");
            }
            catch (Exception dbEx)
            {
                _logger.LogError(dbEx, "❌ Database save failed! Exception: {Message}", dbEx.Message);
                throw;
            }

            // ✅ CRITICAL: Verify what was actually saved by reading it back
            await _db.Entry(settings).ReloadAsync();
            var savedMode = settings.AssignmentMode;
            _logger.LogInformation("🔍 Verified saved value from database: AssignmentMode='{AssignmentMode}'", savedMode);

            if (savedMode != assignmentModeToSave)
            {
                _logger.LogError("❌❌❌ CRITICAL: Database value mismatch! Expected '{Expected}', but database has '{Actual}'",
                    assignmentModeToSave, savedMode);
            }

            _logger.LogInformation("✅ Settings updated by {User}: Enabled={Enabled} (was {OldEnabled}), AssignmentMode={AssignmentMode} (was {OldMode}), LeaseMinutes={LeaseMinutes}, MaxConcurrent={MaxConcurrent}",
                User.Identity?.Name, settings.Enabled, oldEnabled, settings.AssignmentMode, oldMode, settings.LeaseMinutes, settings.MaxConcurrentPerUser);

            // ✅ CRITICAL: Use the verified saved value, not the entity property (in case of reload issues)
            var finalAssignmentMode = savedMode ?? assignmentModeToSave;
            _logger.LogInformation("📤 Returning response: AssignmentMode='{AssignmentMode}' (from database: '{SavedMode}', expected: '{Expected}')",
                finalAssignmentMode, savedMode, assignmentModeToSave);

            // ✅ Return the updated settings so frontend can verify
            return Ok(new
            {
                success = true,
                message = "Settings updated successfully",
                settings = new
                {
                    enabled = settings.Enabled,
                    assignmentMode = finalAssignmentMode, // ✅ Use verified saved value
                    autoAssignStrategy = settings.AutoAssignStrategy,
#pragma warning disable CS0618 // 'AnalysisSettings.AutoAssign' is obsolete: 'Use AssignmentMode instead'
                    autoAssign = settings.AutoAssign,
#pragma warning restore CS0618
                    leaseMinutes = settings.LeaseMinutes,
                    maxConcurrent = settings.MaxConcurrentPerUser
                }
            });
        }

        // POST /api/image-analysis/sync-stages
        [HttpPost("sync-stages")]
        public async Task<ActionResult> SyncStages()
        {
            // Placeholder: light no-op to satisfy UI; real implementation will reconcile lifecycle states
            _logger.LogInformation("[IMAGE-ANALYSIS] SyncStages invoked by {User}", User?.Identity?.Name ?? "unknown");
            await Task.CompletedTask;
            return Ok();
        }

        // POST /api/image-analysis/rebuild-intake
        [HttpPost("rebuild-intake")]
        public async Task<ActionResult> RebuildIntake()
        {
            // Placeholder: no-op; real implementation will rescan Completeness and repopulate Ready groups
            _logger.LogInformation("[IMAGE-ANALYSIS] RebuildIntake invoked by {User}", User?.Identity?.Name ?? "unknown");
            await Task.CompletedTask;
            return Ok();
        }

        // POST /api/image-analysis-management/fix-stuck-groups
        [HttpPost("fix-stuck-groups")]
        [HasPermission(Permissions.ControllersImageAnalysisManagementSettings)]
        public async Task<ActionResult> FixStuckAnalystGroups()
        {
            _logger.LogInformation("🔧 FixStuckAnalystGroups invoked by {User}", User?.Identity?.Name ?? "unknown");

            try
            {
                var now = DateTime.UtcNow;
                var fixedCount = 0;
                var assignmentReleasedCount = 0;
                var decisionsFixedCount = 0;
                var groupIdentifiersToUpdateWorkflow = new List<string>();

                // Only process AnalystAssigned groups (not Ready, which has too many).
                // Ready groups haven't been assigned yet so they can't be "stuck" with completed decisions.
                var stuckGroups = await _db.AnalysisGroups
                    .Where(g => g.Status == AnalysisStatuses.AnalystAssigned)
                    .AsTracking()
                    .ToListAsync();

                _logger.LogInformation("🔍 FixStuckAnalystGroups: Found {Count} AnalystAssigned groups to check", stuckGroups.Count);

                foreach (var group in stuckGroups)
                {
                    var groupContainers = await _db.AnalysisRecords
                        .Where(r => r.GroupId == group.Id)
                        .Select(r => r.ContainerNumber)
                        .Distinct()
                        .ToListAsync();

                    if (!groupContainers.Any()) continue;

                    var decidedContainers = await _db.ImageAnalysisDecisions
                        .Where(d => d.GroupIdentifier == group.GroupIdentifier &&
                                   (d.Decision == "Normal" || d.Decision == "Abnormal"))
                        .Select(d => d.ContainerNumber)
                        .Distinct()
                        .ToListAsync();

                    if (decidedContainers.Count >= groupContainers.Count && groupContainers.Count > 0)
                    {
                        _logger.LogInformation("✅ Fixing stuck group {GroupId} ({GroupIdentifier}): {Decided}/{Total} containers decided",
                            group.Id, group.GroupIdentifier, decidedContainers.Count, groupContainers.Count);

                        group.Status = AnalysisStatuses.AnalystCompleted;
                        group.UpdatedAtUtc = now;
                        fixedCount++;

                        var analystAssignments = await _db.AnalysisAssignments
                            .Where(a => a.GroupId == group.Id && a.Role == "Analyst" && a.State == "Active")
                            .AsTracking()
                            .ToListAsync();

                        foreach (var assignment in analystAssignments)
                        {
                            assignment.State = "Released";
                            assignment.UpdatedAtUtc = now;
                            assignmentReleasedCount++;
                        }

                        await _db.SaveChangesAsync();

                        if (!string.IsNullOrEmpty(group.GroupIdentifier))
                            groupIdentifiersToUpdateWorkflow.Add(group.GroupIdentifier);
                    }
                }

                // Update WorkflowStage via raw SQL after all EF changes are flushed
                foreach (var groupId in groupIdentifiersToUpdateWorkflow)
                {
                    await _db.Database.ExecuteSqlRawAsync(
                        "UPDATE ContainerCompletenessStatuses SET WorkflowStage = @p0, UpdatedAt = now() AT TIME ZONE 'UTC' WHERE GroupIdentifier = @p1 AND WorkflowStage <> @p0 AND WorkflowStage <> 'Completed'",
                        new NpgsqlParameter("@p0", "Audit"),
                        new NpgsqlParameter("@p1", groupId));
                }

                _logger.LogInformation("✅ FixStuckAnalystGroups completed: {Fixed} groups fixed, {Released} assignments released, {Decisions} decisions fixed",
                    fixedCount, assignmentReleasedCount, decisionsFixedCount);

                return Ok(new
                {
                    success = true,
                    groupsFixed = fixedCount,
                    assignmentsReleased = assignmentReleasedCount,
                    decisionsFixed = decisionsFixedCount,
                    message = $"Fixed {fixedCount} stuck groups, released {assignmentReleasedCount} assignments, fixed {decisionsFixedCount} decisions"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error fixing stuck groups");
                return StatusCode(500, new { success = false, error = ex.Message });
            }
        }

        // GET /api/image-analysis/groups/ready
        [HttpGet("groups/ready")]
        public async Task<ActionResult<IEnumerable<ReadyGroupResponse>>> GetReadyGroups()
        {
            // ✅ PERFORMANCE: Check cache first to avoid expensive query
            if (_cache.TryGetValue(ReadyGroupsCacheKey, out List<ReadyGroupResponse>? cachedResult))
            {
                _logger.LogDebug("✅ [CACHE HIT] Returning cached ready groups ({Count} groups)", cachedResult?.Count ?? 0);
                return Ok(cachedResult);
            }

            _logger.LogDebug("⏳ [CACHE MISS] Loading ready groups from database...");
            var readyGroups = await _db.AnalysisGroups
                .AsNoTracking()
                .Where(g => g.Status == AnalysisStatuses.Ready)
                .ToListAsync();

            // ✅ PERFORMANCE FIX: Batch query container counts instead of per-group queries
            // ✅ FIX: Load records first, then group in memory to avoid CTE generation
            var groupIds = readyGroups.Select(g => g.Id).ToList();
            var allAnalysisRecords = new List<(Guid GroupId, int Count)>();
            const int groupBatchSize = 1000;

            if (groupIds.Count > 0)
            {
                for (int i = 0; i < groupIds.Count; i += groupBatchSize)
                {
                    var batch = groupIds.Skip(i).Take(groupBatchSize).ToList();
                    // ✅ FIX: Use FromSqlRaw to avoid CTE generation for Contains()
                    // Safe: batch contains only GUIDs from database, no user input
                    // Convert GUIDs to strings for SQL IN clause
                    var placeholders = string.Join(",", batch.Select(g => $"'{g}'"));
#pragma warning disable EF1002 // Method 'FromSqlRaw' inserts interpolated strings directly into the SQL
                    var batchRecords = await _db.AnalysisRecords
                        .FromSqlRaw($"SELECT * FROM AnalysisRecords WHERE GroupId IN ({placeholders})")
#pragma warning restore EF1002
                        .AsNoTracking()
                        .ToListAsync();

                    // Group and count in memory
                    var batchCounts = batchRecords
                        .GroupBy(r => r.GroupId)
                        .Select(g => new { GroupId = g.Key, Count = g.Count() })
                        .ToList();
                    allAnalysisRecords.AddRange(batchCounts.Select(x => (x.GroupId, x.Count)));
                }
            }

            var containerCounts = allAnalysisRecords.ToDictionary(x => x.GroupId, x => x.Count);

            // ✅ PERFORMANCE FIX: Optimize BOE document queries - use SELECT with only needed columns instead of SELECT *
            // This dramatically reduces data transfer and query time (225K+ records in BOEDocuments)
            var groupIdentifiers = readyGroups.Select(g => g.GroupIdentifier).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();

            // ✅ FIX: Use optimized queries that only select the 3 columns we need (ContainerNumber, DeclarationNumber, IsConsolidated)
            // This is much faster than SELECT * which loads all columns
            var consolidatedContainers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nonConsolidatedDeclarations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var allContainerNumbers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            const int boeBatchSize = 2000; // ✅ FIX: Increased batch size since we're only selecting 3 columns

            if (groupIdentifiers.Count > 0)
            {
                _logger.LogDebug("⏳ Querying BOEDocuments for {Count} group identifiers...", groupIdentifiers.Count);

                for (int i = 0; i < groupIdentifiers.Count; i += boeBatchSize)
                {
                    var batch = groupIdentifiers.Skip(i).Take(boeBatchSize).ToList();
                    var placeholders = string.Join(",", batch.Select(g => $"'{g.Replace("'", "''")}'"));

                    // ✅ PERFORMANCE FIX: Use SELECT with only needed columns - much faster than SELECT *
                    // Query by ContainerNumber
                    var containerQuery = $"SELECT DISTINCT ContainerNumber, DeclarationNumber, IsConsolidated FROM BOEDocuments WHERE ContainerNumber IN ({placeholders})";
                    var containerResults = await _icumDb.Database
                        .SqlQueryRaw<BoeLookupResult>(containerQuery)
                        .ToListAsync();

                    // Query by DeclarationNumber
                    var declarationQuery = $"SELECT DISTINCT ContainerNumber, DeclarationNumber, IsConsolidated FROM BOEDocuments WHERE DeclarationNumber IN ({placeholders})";
                    var declarationResults = await _icumDb.Database
                        .SqlQueryRaw<BoeLookupResult>(declarationQuery)
                        .ToListAsync();

                    // Build lookup sets from results
                    foreach (var boeResult in containerResults.Concat(declarationResults))
                    {
                        if (!string.IsNullOrEmpty(boeResult.ContainerNumber))
                        {
                            allContainerNumbers.Add(boeResult.ContainerNumber);
                            if (boeResult.IsConsolidated)
                            {
                                consolidatedContainers.Add(boeResult.ContainerNumber);
                            }
                        }
                        if (!string.IsNullOrEmpty(boeResult.DeclarationNumber) && !boeResult.IsConsolidated)
                        {
                            nonConsolidatedDeclarations.Add(boeResult.DeclarationNumber);
                        }
                    }
                }

                _logger.LogDebug("✅ BOEDocuments query complete - Found {Consolidated} consolidated containers, {NonConsolidated} non-consolidated declarations",
                    consolidatedContainers.Count, nonConsolidatedDeclarations.Count);
            }

            var result = new List<ReadyGroupResponse>();

            foreach (var group in readyGroups)
            {
                // ✅ PERFORMANCE FIX: Use dictionary lookup instead of database query
                var containerCount = containerCounts.GetValueOrDefault(group.Id, 0);

                // ✅ PERFORMANCE FIX: Use in-memory lookups instead of database queries
                var isConsolidatedAsContainer = consolidatedContainers.Contains(group.GroupIdentifier);
                var isNonConsolidatedAsDeclaration = nonConsolidatedDeclarations.Contains(group.GroupIdentifier);

                bool isConsolidated = isConsolidatedAsContainer;
                if (!isConsolidatedAsContainer && !isNonConsolidatedAsDeclaration)
                {
                    // Fallback: if GroupIdentifier matches any container number, treat as consolidated
                    isConsolidated = allContainerNumbers.Contains(group.GroupIdentifier);
                }

                result.Add(new ReadyGroupResponse
                {
                    GroupIdentifier = group.GroupIdentifier,
                    ScannerType = group.ScannerType,
                    ContainerCount = containerCount,
                    IsConsolidated = isConsolidated
                });
            }

            // ✅ PERFORMANCE: Cache the result for 45 seconds to reduce database load
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = ReadyGroupsCacheExpiration,
                Size = 1, // Each cache entry counts as 1 unit toward the 1000 limit
                Priority = CacheItemPriority.Normal
            };
            _cache.Set(ReadyGroupsCacheKey, result, cacheOptions);

            _logger.LogDebug("✅ [CACHE SET] Cached ready groups ({Count} groups) for {Seconds} seconds",
                result.Count, ReadyGroupsCacheExpiration.TotalSeconds);

            return Ok(result);
        }

        // GET /api/image-analysis/assignments
        [HttpGet("assignments")]
        public async Task<ActionResult<IEnumerable<ActiveAssignmentResponse>>> GetActiveAssignments()
        {
            try
            {
                var now = DateTime.UtcNow;
                // ✅ FIX: Load assignments and groups separately, then join in memory to avoid CTE generation
                var assignmentsList = await _db.AnalysisAssignments
                    .AsNoTracking()
                    .Where(a => a.State == "Active" && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .ToListAsync();

                if (!assignmentsList.Any())
                {
                    return Ok(new List<ActiveAssignmentResponse>());
                }

                var groupIds = assignmentsList.Select(a => a.GroupId).Distinct().ToList();

                // ✅ FIX: Batch Contains() to avoid CTE generation
                var groups = new List<AnalysisGroup>();
                const int groupBatchSize3 = 1000;

                if (groupIds.Count > 0)
                {
                    for (int i = 0; i < groupIds.Count; i += groupBatchSize3)
                    {
                        var batch = groupIds.Skip(i).Take(groupBatchSize3).ToList();
                        // ✅ FIX: Use FromSqlRaw with semicolon to avoid CTE generation (SQL Server "WITH" syntax error)
                        var placeholders = string.Join(",", batch.Select(g => $"'{g}'"));
#pragma warning disable EF1002 // Safe: batch contains only GUIDs from our database
                        var batchGroups = await _db.AnalysisGroups
                            .FromSqlRaw($";SELECT * FROM AnalysisGroups WHERE Id IN ({placeholders})")
#pragma warning restore EF1002
                            .AsNoTracking()
                            .ToListAsync();
                        groups.AddRange(batchGroups);
                    }
                }

                var groupsDict = groups.ToDictionary(g => g.Id);
                var assignments = assignmentsList
                    .Where(a => groupsDict.ContainsKey(a.GroupId))
                    .Select(a => new { Assignment = a, Group = groupsDict[a.GroupId] })
                    .ToList();

                if (!assignments.Any())
                {
                    return Ok(new List<ActiveAssignmentResponse>());
                }

                // ✅ PERFORMANCE FIX: Single query to get all BOE document data needed
                var groupIdentifiers = assignments
                    .Select(a => a.Group.GroupIdentifier)
                    .Where(g => !string.IsNullOrEmpty(g))
                    .Distinct()
                    .ToList();

                // ✅ FIX: Use typed anonymous class instead of dynamic to avoid runtime errors
                var boeData = new List<BoeLookupResult>();
                const int boeBatchSize2 = 1000;

                if (groupIdentifiers.Count > 0)
                {
                    try
                    {
                        for (int i = 0; i < groupIdentifiers.Count; i += boeBatchSize2)
                        {
                            var batch = groupIdentifiers.Skip(i).Take(boeBatchSize2).Where(g => g != null).ToList();
                            if (!batch.Any()) continue;
                            // ✅ FIX: Use raw SQL to avoid CTE generation from Contains() (SQL Server "WITH" syntax error)
                            var placeholders = string.Join(",", batch.Select(g => $"'{g?.Replace("'", "''")}'"));
                            var containerQuery = $"SELECT DISTINCT ContainerNumber, DeclarationNumber, IsConsolidated FROM BOEDocuments WHERE ContainerNumber IN ({placeholders})";
                            var declarationQuery = $"SELECT DISTINCT ContainerNumber, DeclarationNumber, IsConsolidated FROM BOEDocuments WHERE DeclarationNumber IN ({placeholders})";
                            var containerResults = await _icumDb.Database.SqlQueryRaw<BoeLookupResult>(containerQuery).ToListAsync();
                            var declarationResults = await _icumDb.Database.SqlQueryRaw<BoeLookupResult>(declarationQuery).ToListAsync();
                            boeData.AddRange(containerResults.Concat(declarationResults));
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[ASSIGNMENTS] Error querying BOEDocuments, continuing without consolidation data");
                        // Continue without BOE data - assignments will still work, just without IsConsolidated flag
                    }
                }

                // Build lookup sets from single query result
                var consolidatedContainers = boeData
                    .Where(b => b.IsConsolidated && !string.IsNullOrEmpty(b.ContainerNumber))
                    .Select(b => b.ContainerNumber!)
                    .Distinct()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var nonConsolidatedDeclarations = boeData
                    .Where(b => !b.IsConsolidated && !string.IsNullOrEmpty(b.DeclarationNumber))
                    .Select(b => b.DeclarationNumber!)
                    .Distinct()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var allContainerNumbers = boeData
                    .Where(b => !string.IsNullOrEmpty(b.ContainerNumber))
                    .Select(b => b.ContainerNumber!)
                    .Distinct()
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var result = new List<ActiveAssignmentResponse>();

                foreach (var item in assignments)
                {
                    if (string.IsNullOrEmpty(item.Group.GroupIdentifier))
                        continue;

                    // ✅ PERFORMANCE FIX: Use in-memory lookups instead of database queries
                    var isConsolidatedAsContainer = consolidatedContainers.Contains(item.Group.GroupIdentifier);
                    var isNonConsolidatedAsDeclaration = nonConsolidatedDeclarations.Contains(item.Group.GroupIdentifier);

                    bool isConsolidated = isConsolidatedAsContainer;
                    if (!isConsolidatedAsContainer && !isNonConsolidatedAsDeclaration)
                    {
                        // Fallback: if GroupIdentifier matches any container number, treat as consolidated
                        isConsolidated = allContainerNumbers.Contains(item.Group.GroupIdentifier);
                    }

                    result.Add(new ActiveAssignmentResponse
                    {
                        GroupIdentifier = item.Group.GroupIdentifier ?? string.Empty,
                        AssignedTo = item.Assignment.AssignedTo ?? string.Empty,
                        Role = item.Assignment.Role ?? "Analyst", // ✅ Include Role field
                        LeaseUntilUtc = item.Assignment.LeaseUntilUtc,
                        IsConsolidated = isConsolidated
                    });
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ASSIGNMENTS] Error in GetActiveAssignments endpoint");
                return StatusCode(500, new { error = "An error occurred while retrieving assignments", message = ex.Message });
            }
        }

        // GET /api/image-analysis/stats
        [HttpGet("stats")]
        public async Task<ActionResult<ManagementStatsResponse>> GetStats()
        {
            // ✅ PERFORMANCE FIX: Single query instead of 3 separate queries
            var allGroups = await _db.AnalysisGroups
                .AsNoTracking()
                .Select(g => g.Status)
                .ToListAsync();

            var stats = new ManagementStatsResponse
            {
                ImageAnalysis = allGroups.Count(g =>
                    g == AnalysisStatuses.Ready ||
                    g == AnalysisStatuses.AnalystAssigned ||
                    g == "Assigned"),
                Audit = allGroups.Count(g =>
                    g == AnalysisStatuses.AnalystCompleted ||
                    g == AnalysisStatuses.AuditAssigned ||
                    g == "AuditAssigned" ||
                    g == "AuditPending"),
                Completed = allGroups.Count(g =>
                    g == AnalysisStatuses.AuditCompleted ||
                    g == AnalysisStatuses.Submitted ||
                    g == AnalysisStatuses.Completed ||
                    g == "Completed")
            };
            return Ok(stats);
        }

        // GET /api/image-analysis/analysts
        [HttpGet("analysts")]
        public async Task<ActionResult<IEnumerable<string>>> GetAnalysts()
        {
            // Basic: return all active users; refine to Role=Analyst/Audit if role relationships are available
            var names = await _db.Users
                .AsNoTracking()
                .Where(u => u.IsActive)
                .OrderBy(u => u.Username)
                .Select(u => u.Username)
                .ToListAsync();
            return Ok(names);
        }

        // GET /api/image-analysis-management/wave-monitor
        //
        // Wave #3 (1.11.0): read-only monitor for wave processing health.
        // Surfaces the data points the deep wave audit identified as invisible
        // to operators today: active parent groups, pending container backlog
        // broken down by status, the oldest stuck container (in case auto-close
        // is disabled or misconfigured), and the 20 most recent parent groups
        // so operators can spot stalled ones at a glance.
        //
        // Single endpoint for the whole panel — the caller hits this once per
        // refresh and renders everything. All three data sets are small (wave
        // tables don't accumulate many rows in practice), so no pagination
        // is needed at this layer.
        [HttpGet("wave-monitor")]
        public async Task<ActionResult<WaveMonitorResponse>> GetWaveMonitor()
        {
            var now = DateTime.UtcNow;

            // Active parent group counts by status.
            var parentStatusCounts = await _db.AnalysisParentGroups
                .AsNoTracking()
                .GroupBy(p => p.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            // Pending container counts by status.
            var pendingStatusCounts = await _db.WavePendingContainers
                .AsNoTracking()
                .GroupBy(w => w.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();

            // Oldest pending-without-images container — the one most likely to
            // be stuck if the pending-no-image timeout isn't firing (Wave #4).
            var oldestPending = await _db.WavePendingContainers
                .AsNoTracking()
                .Where(w => w.Status == "Pending")
                .OrderBy(w => w.FirstSeenUtc)
                .Select(w => new
                {
                    w.ContainerNumber,
                    w.ScannerType,
                    w.FirstSeenUtc,
                    w.ParentGroupId,
                })
                .FirstOrDefaultAsync();

            // Oldest Ready container — if this is large, the wave-timeout logic
            // is waiting too long to form a wave (likely WaveMinBatchSize too
            // high for current traffic).
            var oldestReady = await _db.WavePendingContainers
                .AsNoTracking()
                .Where(w => w.Status == "Ready" && w.BecameReadyUtc != null)
                .OrderBy(w => w.BecameReadyUtc)
                .Select(w => new
                {
                    w.ContainerNumber,
                    w.ScannerType,
                    w.BecameReadyUtc,
                    w.ParentGroupId,
                })
                .FirstOrDefaultAsync();

            // 20 most recent parent groups — lets an operator eyeball the
            // wave history ordered by creation and spot any that have been
            // "Active" for much longer than their peers.
            var recentParents = await _db.AnalysisParentGroups
                .AsNoTracking()
                .OrderByDescending(p => p.CreatedAtUtc)
                .Take(20)
                .Select(p => new WaveMonitorParentDto
                {
                    Id = p.Id,
                    GroupIdentifier = p.GroupIdentifier,
                    ScannerType = p.ScannerType,
                    Status = p.Status,
                    TotalExpectedContainers = p.TotalExpectedContainers,
                    CompletedWaveCount = p.CompletedWaveCount,
                    CreatedAtUtc = p.CreatedAtUtc,
                    UpdatedAtUtc = p.UpdatedAtUtc,
                    AgeHours = (now - p.CreatedAtUtc).TotalHours,
                })
                .ToListAsync();

            // Pending containers older than 24 h in "Pending" (no images ever
            // arrived) are the most common stuck-state symptom. Surface the
            // count prominently.
            var stuckThreshold = now.AddHours(-24);
            var stuckPendingCount = await _db.WavePendingContainers
                .AsNoTracking()
                .CountAsync(w => w.Status == "Pending" && w.FirstSeenUtc < stuckThreshold);

            // Wave settings — just the bits the operator might want to verify
            // without navigating to the Settings panel.
            var settings = await _db.AnalysisSettings
                .AsNoTracking()
                .Select(s => new
                {
                    s.EnableWaveProcessing,
                    s.WaveMinBatchSize,
                    s.WaveTimeoutHours,
                    s.WaveAutoCloseDays,
                })
                .FirstOrDefaultAsync();

            // 1.15.0: enrich with record-level rollup so the PANEL 3.5 monitor shows both legacy wave parents and new records.
            var recordStatusCounts = await _db.RecordCompletenessStatuses
                .GroupBy(r => r.Status)
                .Select(g => new { Status = g.Key, Count = g.Count() })
                .ToListAsync();
            var recordIntegrityGap = await _db.RecordCompletenessStatuses
                .Where(r => r.TotalExpectedContainers > 1 && r.Status != "Archived")
                .SumAsync(r => (int?)r.ContainersAwaitingScan) ?? 0;
            var recordReconState = await _db.RecordReconciliationStates.AsNoTracking().FirstOrDefaultAsync(s => s.Id == 1);

            var response = new WaveMonitorResponse
            {
                GeneratedAtUtc = now,
                WaveProcessingEnabled = settings?.EnableWaveProcessing ?? false,
                WaveMinBatchSize = settings?.WaveMinBatchSize ?? 0,
                WaveTimeoutHours = settings?.WaveTimeoutHours ?? 0,
                WaveAutoCloseDays = settings?.WaveAutoCloseDays ?? 0,

                ParentGroupCounts = parentStatusCounts.ToDictionary(x => x.Status, x => x.Count),
                PendingContainerCounts = pendingStatusCounts.ToDictionary(x => x.Status, x => x.Count),

                RecordCounts = recordStatusCounts.ToDictionary(x => x.Status, x => x.Count),
                RecordIntegrityGapContainers = recordIntegrityGap,
                RecordReconciliationLastTickAtUtc = recordReconState?.LastTickAtUtc,
                RecordReconciliationWatermarkUtc = recordReconState?.LastWatermarkUtc,
                RecordReconciliationContainersPromotedTotal = recordReconState?.ContainersPromotedTotal ?? 0,

                StuckPendingOver24hCount = stuckPendingCount,

                OldestPending = oldestPending == null ? null : new WaveMonitorStuckContainerDto
                {
                    ContainerNumber = oldestPending.ContainerNumber,
                    ScannerType = oldestPending.ScannerType,
                    AgeHours = (now - oldestPending.FirstSeenUtc).TotalHours,
                    ParentGroupId = oldestPending.ParentGroupId,
                },

                OldestReady = oldestReady == null || oldestReady.BecameReadyUtc == null ? null : new WaveMonitorStuckContainerDto
                {
                    ContainerNumber = oldestReady.ContainerNumber,
                    ScannerType = oldestReady.ScannerType,
                    AgeHours = (now - oldestReady.BecameReadyUtc.Value).TotalHours,
                    ParentGroupId = oldestReady.ParentGroupId,
                },

                RecentParents = recentParents,
            };

            return Ok(response);
        }

        public class WaveMonitorResponse
        {
            public DateTime GeneratedAtUtc { get; set; }
            public bool WaveProcessingEnabled { get; set; }
            public int WaveMinBatchSize { get; set; }
            public int WaveTimeoutHours { get; set; }
            public int WaveAutoCloseDays { get; set; }

            public Dictionary<string, int> ParentGroupCounts { get; set; } = new();
            public Dictionary<string, int> PendingContainerCounts { get; set; } = new();

            // 1.15.0 — record-level rollups from the new RecordCompletenessStatus table
            public Dictionary<string, int> RecordCounts { get; set; } = new();
            public int RecordIntegrityGapContainers { get; set; }
            public DateTime? RecordReconciliationLastTickAtUtc { get; set; }
            public DateTime? RecordReconciliationWatermarkUtc { get; set; }
            public long RecordReconciliationContainersPromotedTotal { get; set; }

            public int StuckPendingOver24hCount { get; set; }

            public WaveMonitorStuckContainerDto? OldestPending { get; set; }
            public WaveMonitorStuckContainerDto? OldestReady { get; set; }

            public List<WaveMonitorParentDto> RecentParents { get; set; } = new();
        }

        public class WaveMonitorStuckContainerDto
        {
            public string ContainerNumber { get; set; } = string.Empty;
            public string? ScannerType { get; set; }
            public double AgeHours { get; set; }
            public Guid ParentGroupId { get; set; }
        }

        public class WaveMonitorParentDto
        {
            public Guid Id { get; set; }
            public string GroupIdentifier { get; set; } = string.Empty;
            public string? ScannerType { get; set; }
            public string Status { get; set; } = string.Empty;
            public int TotalExpectedContainers { get; set; }
            public int CompletedWaveCount { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public DateTime? UpdatedAtUtc { get; set; }
            public double AgeHours { get; set; }
        }

        // POST /api/image-analysis/{groupId}/assign
        [HttpPost("{groupId:guid}/assign")]
        [HasPermission(Permissions.ControllersImageAnalysisAssign)]
        public async Task<ActionResult> AssignGroup([FromRoute] Guid groupId, [FromBody] ManagementAssignRequest? request)
        {
            var group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.Id == groupId);
            if (group == null) return NotFound();

            var settings = await _db.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();
            var username = string.IsNullOrWhiteSpace(request?.user) ? (User?.Identity?.Name ?? "unknown") : request!.user!;

            var leaseUntil = DateTime.UtcNow.AddMinutes(Math.Max(1, settings.LeaseMinutes));
            var assignment = new AnalysisAssignment
            {
                GroupId = groupId,
                AssignedTo = username,
                Role = "Analyst",
                LeaseUntilUtc = leaseUntil,
                State = "Active",
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.AnalysisAssignments.Add(assignment);

            // move group to Assigned if it was Ready
            if (group.Status == AnalysisStatuses.Ready)
            {
                group.Status = AnalysisStatuses.AnalystAssigned;
                group.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return Ok();
        }

        // POST /api/image-analysis/{groupId}/reassign
        [HttpPost("{groupId:guid}/reassign")]
        [HasPermission(Permissions.ControllersImageAnalysisAssign)]
        public async Task<ActionResult> ReassignGroup([FromRoute] Guid groupId, [FromBody] ManagementAssignRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.user)) return BadRequest("user required");

            var active = await _db.AnalysisAssignments.Where(a => a.GroupId == groupId && a.State == "Active").AsTracking().ToListAsync();
            foreach (var a in active)
            {
                a.State = "Released";
                a.UpdatedAtUtc = DateTime.UtcNow;
            }

            var settings = await _db.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();
            var leaseUntil = DateTime.UtcNow.AddMinutes(Math.Max(1, settings.LeaseMinutes));

            var assignment = new AnalysisAssignment
            {
                GroupId = groupId,
                AssignedTo = request.user!,
                Role = "Analyst",
                LeaseUntilUtc = leaseUntil,
                State = "Active",
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.AnalysisAssignments.Add(assignment);
            await _db.SaveChangesAsync();
            return Ok();
        }

        // POST /api/image-analysis/{groupId}/release
        [HttpPost("{groupId:guid}/release")]
        [HasPermission(Permissions.ControllersImageAnalysisAssign)]
        public async Task<ActionResult> ReleaseGroup([FromRoute] Guid groupId)
        {
            var active = await _db.AnalysisAssignments.Where(a => a.GroupId == groupId && a.State == "Active").AsTracking().ToListAsync();
            foreach (var a in active)
            {
                a.State = "Released";
                a.UpdatedAtUtc = DateTime.UtcNow;

                // ✅ PHASE 3: Event logging for assignment release
                _logger.LogInformation("[ASSIGNMENT-EVENT] Released | AssignmentId={AssignmentId} | GroupId={GroupId} | User={User} | Role={Role} | Reason=ManualRelease",
                    a.Id, a.GroupId, a.AssignedTo, a.Role);
            }

            var group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.Id == groupId);
            if (group != null)
            {
                group.Status = group.Status == AnalysisStatuses.AuditAssigned
                    ? AnalysisStatuses.AnalystCompleted
                    : AnalysisStatuses.Ready;
                group.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            return Ok();
        }

        // ⚠️ BACKWARD COMPATIBILITY: String-based endpoints for legacy support
        // These endpoints accept string groupId (GroupIdentifier) instead of GUID
        // They convert GroupIdentifier to GUID and use the new AnalysisAssignments model

        // POST /api/image-analysis/{groupId}/assign (string groupId for backward compatibility)
        // ✅ FIX: Exclude from Swagger to avoid route conflict with Guid version
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("{groupId}/assign")]
        [HasPermission(Permissions.ControllersImageAnalysisAssign)]
        public async Task<ActionResult> AssignGroupByString([FromRoute] string groupId, [FromBody] ManagementAssignRequest? request)
        {
            // Find group by GroupIdentifier
            var group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.GroupIdentifier == groupId);
            if (group == null)
            {
                return NotFound(new { error = $"Group with identifier '{groupId}' not found" });
            }

            var settings = await _db.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();
            var username = string.IsNullOrWhiteSpace(request?.user) ? (User?.Identity?.Name ?? "unknown") : request!.user!;

            // Check max concurrent assignments
            var maxConcurrent = settings.MaxConcurrentPerUser;
            var activeAssignments = await _db.AnalysisAssignments
                .Where(a => a.AssignedTo == username && a.State == "Active" &&
                    (a.LeaseUntilUtc == null || a.LeaseUntilUtc > DateTime.UtcNow))
                .CountAsync();

            if (activeAssignments >= maxConcurrent)
            {
                return Conflict(new { error = $"User '{username}' has reached maximum concurrent assignments ({maxConcurrent})" });
            }

            // Check if already assigned to someone else
            var existingAssignment = await _db.AnalysisAssignments
                .Where(a => a.GroupId == group.Id && a.State == "Active" &&
                    (a.LeaseUntilUtc == null || a.LeaseUntilUtc > DateTime.UtcNow))
                .AsTracking()
                .FirstOrDefaultAsync();

            if (existingAssignment != null && existingAssignment.AssignedTo != username)
            {
                return Conflict(new { error = $"Group '{groupId}' is already assigned to '{existingAssignment.AssignedTo}'" });
            }

            var leaseUntil = DateTime.UtcNow.AddMinutes(Math.Max(1, settings.LeaseMinutes));

            // If exists, update; otherwise create new
            if (existingAssignment != null)
            {
                existingAssignment.AssignedTo = username;
                existingAssignment.LeaseUntilUtc = leaseUntil;
                existingAssignment.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                var assignment = new AnalysisAssignment
                {
                    GroupId = group.Id,
                    AssignedTo = username,
                    Role = "Analyst",
                    LeaseUntilUtc = leaseUntil,
                    State = "Active",
                    CreatedAtUtc = DateTime.UtcNow
                };
                _db.AnalysisAssignments.Add(assignment);
            }

            // Move group to Assigned if it was Ready
            if (group.Status == AnalysisStatuses.Ready)
            {
                group.Status = AnalysisStatuses.AnalystAssigned;
                group.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Assigned group {GroupIdentifier} to {User} (backward compatibility endpoint)", groupId, username);
            return Ok(new { success = true });
        }

        // POST /api/image-analysis/{groupId}/release (string groupId for backward compatibility)
        // ✅ FIX: Exclude from Swagger to avoid route conflict with Guid version
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("{groupId}/release")]
        [HasPermission(Permissions.ControllersImageAnalysisAssign)]
        public async Task<ActionResult> ReleaseGroupByString([FromRoute] string groupId)
        {
            // Find group by GroupIdentifier
            var group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.GroupIdentifier == groupId);
            if (group == null)
            {
                return NotFound(new { error = $"Group with identifier '{groupId}' not found" });
            }

            var active = await _db.AnalysisAssignments.Where(a => a.GroupId == group.Id && a.State == "Active").AsTracking().ToListAsync();
            foreach (var a in active)
            {
                a.State = "Released";
                a.UpdatedAtUtc = DateTime.UtcNow;
            }

            if (group != null)
            {
                group.Status = group.Status == AnalysisStatuses.AuditAssigned
                    ? AnalysisStatuses.AnalystCompleted
                    : AnalysisStatuses.Ready;
                group.UpdatedAtUtc = DateTime.UtcNow;
            }

            await _db.SaveChangesAsync();
            _logger.LogInformation("Released group {GroupIdentifier} (backward compatibility endpoint)", groupId);
            return Ok(new { success = true });
        }

        // POST /api/image-analysis/{groupId}/reassign (string groupId for backward compatibility)
        // ✅ FIX: Exclude from Swagger to avoid route conflict with Guid version
        [ApiExplorerSettings(IgnoreApi = true)]
        [HttpPost("{groupId}/reassign")]
        [HasPermission(Permissions.ControllersImageAnalysisAssign)]
        public async Task<ActionResult> ReassignGroupByString([FromRoute] string groupId, [FromBody] ManagementAssignRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.user))
            {
                return BadRequest(new { error = "user is required" });
            }

            // Find group by GroupIdentifier
            var group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.GroupIdentifier == groupId);
            if (group == null)
            {
                return NotFound(new { error = $"Group with identifier '{groupId}' not found" });
            }

            var settings = await _db.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();
            var username = request.user!;

            // Check max concurrent assignments
            var maxConcurrent = settings.MaxConcurrentPerUser;
            var activeAssignments = await _db.AnalysisAssignments
                .Where(a => a.AssignedTo == username && a.State == "Active" &&
                    (a.LeaseUntilUtc == null || a.LeaseUntilUtc > DateTime.UtcNow))
                .CountAsync();

            if (activeAssignments >= maxConcurrent)
            {
                return Conflict(new { error = $"User '{username}' has reached maximum concurrent assignments ({maxConcurrent})" });
            }

            // Release existing assignments
            var active = await _db.AnalysisAssignments.Where(a => a.GroupId == group.Id && a.State == "Active").AsTracking().ToListAsync();
            foreach (var a in active)
            {
                a.State = "Released";
                a.UpdatedAtUtc = DateTime.UtcNow;
            }

            // Create new assignment
            var leaseUntil = DateTime.UtcNow.AddMinutes(Math.Max(1, settings.LeaseMinutes));
            var assignment = new AnalysisAssignment
            {
                GroupId = group.Id,
                AssignedTo = username,
                Role = "Analyst",
                LeaseUntilUtc = leaseUntil,
                State = "Active",
                CreatedAtUtc = DateTime.UtcNow
            };
            _db.AnalysisAssignments.Add(assignment);

            await _db.SaveChangesAsync();
            _logger.LogInformation("Reassigned group {GroupIdentifier} to {User} (backward compatibility endpoint)", groupId, username);
            return Ok(new { success = true });
        }

        public sealed class UpdateServiceStateRequest
        {
            public bool enabled { get; set; }
            public string? assignmentMode { get; set; } // Auto | Manual | UserClaim
            public string? autoAssignStrategy { get; set; } // RoundRobin | LeastLoaded
            public bool autoAssign { get; set; } // Keep for backward compatibility
            public int leaseMinutes { get; set; }
            public int maxConcurrent { get; set; }
            // Wave Processing settings
            public bool? enableWaveProcessing { get; set; }
            public int? waveMinBatchSize { get; set; }
            public int? waveTimeoutHours { get; set; }
            public int? waveAutoCloseDays { get; set; }
        }

        public sealed class ReadyGroupResponse
        {
            public string GroupIdentifier { get; set; } = string.Empty;
            public string? ScannerType { get; set; }
            public int ContainerCount { get; set; }
            public bool IsConsolidated { get; set; }
        }

        public sealed class ActiveAssignmentResponse
        {
            public string GroupIdentifier { get; set; } = string.Empty;
            public string AssignedTo { get; set; } = string.Empty;
            public string Role { get; set; } = "Analyst"; // Analyst | Audit
            public DateTime? LeaseUntilUtc { get; set; }
            public bool IsConsolidated { get; set; }
        }

        public sealed class ManagementStatsResponse
        {
            public int ImageAnalysis { get; set; }
            public int Audit { get; set; }
            public int Completed { get; set; }
        }

        public sealed class ManagementAssignRequest
        {
            public string? user { get; set; }
        }

        // ═══════════════════════════════════════════════════════════════
        // DECISION AGENT ENDPOINTS
        // ═══════════════════════════════════════════════════════════════

        [HttpGet("agent/settings")]
        public async Task<IActionResult> GetAgentSettings()
        {
            var settings = await _db.DecisionAgentSettings.AsNoTracking().FirstOrDefaultAsync()
                           ?? new DecisionAgentSettings();
            var conditions = await _db.DecisionAgentConditions.AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ToListAsync();

            return Ok(new { settings, conditions });
        }

        [HttpPost("agent/settings")]
        [HasPermission(Permissions.ControllersImageAnalysisManagementSettings)]
        public async Task<IActionResult> UpdateAgentSettings([FromBody] AgentSettingsRequest request)
        {
            // Validate thresholds
            if (request.normalThreshold >= request.abnormalThreshold)
                return BadRequest("Normal threshold must be less than abnormal threshold.");
            if (request.normalThreshold < 0 || request.normalThreshold > 1 || request.abnormalThreshold < 0 || request.abnormalThreshold > 1)
                return BadRequest("Thresholds must be between 0.0 and 1.0.");

            var settings = await _db.DecisionAgentSettings.AsTracking().FirstOrDefaultAsync();
            if (settings == null)
            {
                settings = new DecisionAgentSettings();
                _db.DecisionAgentSettings.Add(settings);
            }

            settings.Enabled = request.enabled;
            settings.ShadowMode = request.shadowMode;
            settings.AllowNormalDecisions = request.allowNormalDecisions;
            settings.AllowAbnormalDecisions = request.allowAbnormalDecisions;
            settings.NormalThreshold = request.normalThreshold;
            settings.AbnormalThreshold = request.abnormalThreshold;
            settings.ProcessingDepthDecision = true; // always true
            settings.ProcessingDepthAudit = request.processingDepthAudit;
            settings.ProcessingDepthSubmission = request.processingDepthSubmission;
            settings.MaxGroupsPerCycle = Math.Clamp(request.maxGroupsPerCycle, 1, 500);
            settings.UpdatedAtUtc = DateTime.UtcNow;

            await _db.SaveChangesAsync();
            _logger.LogInformation("[DECISION-AGENT-API] Settings updated: Enabled={Enabled}, Shadow={Shadow}, Normal<={NormalT}, Abnormal>={AbnormalT}",
                settings.Enabled, settings.ShadowMode, settings.NormalThreshold, settings.AbnormalThreshold);

            return Ok(settings);
        }

        [HttpGet("agent/conditions")]
        public async Task<IActionResult> GetAgentConditions()
        {
            var conditions = await _db.DecisionAgentConditions.AsNoTracking()
                .OrderBy(c => c.SortOrder)
                .ToListAsync();
            return Ok(conditions);
        }

        [HttpPost("agent/conditions")]
        [HasPermission(Permissions.ControllersImageAnalysisManagementSettings)]
        public async Task<IActionResult> UpsertAgentCondition([FromBody] AgentConditionRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.name) || string.IsNullOrWhiteSpace(request.conditionKey))
                return BadRequest("Name and condition key are required.");
            if (request.weight < 0 || request.weight > 1)
                return BadRequest("Weight must be between 0.0 and 1.0.");

            DecisionAgentCondition condition;

            if (request.id.HasValue && request.id.Value > 0)
            {
                condition = await _db.DecisionAgentConditions.AsTracking().FirstOrDefaultAsync(c => c.Id == request.id.Value);
                if (condition == null) return NotFound($"Condition {request.id} not found");

                // Built-in conditions: only allow weight, enabled, dynamicValue, description changes
                if (condition.EvaluatorType == "BuiltIn")
                {
                    condition.Weight = request.weight;
                    condition.Enabled = request.enabled;
                    condition.DynamicValue = request.dynamicValue;
                    condition.Description = request.description;
                }
                else
                {
                    condition.Name = request.name;
                    condition.ConditionKey = request.conditionKey;
                    condition.Weight = request.weight;
                    condition.Enabled = request.enabled;
                    condition.DynamicFieldPath = request.dynamicFieldPath;
                    condition.DynamicOperator = request.dynamicOperator;
                    condition.DynamicValue = request.dynamicValue;
                    condition.Description = request.description;
                }
                condition.UpdatedAtUtc = DateTime.UtcNow;
            }
            else
            {
                // New dynamic condition
                var maxSort = await _db.DecisionAgentConditions.MaxAsync(c => (int?)c.SortOrder) ?? 0;
                condition = new DecisionAgentCondition
                {
                    Name = request.name,
                    ConditionKey = request.conditionKey,
                    EvaluatorType = "Dynamic",
                    Weight = request.weight,
                    Enabled = request.enabled,
                    SortOrder = maxSort + 1,
                    DynamicFieldPath = request.dynamicFieldPath,
                    DynamicOperator = request.dynamicOperator,
                    DynamicValue = request.dynamicValue,
                    Description = request.description,
                    CreatedAtUtc = DateTime.UtcNow
                };
                _db.DecisionAgentConditions.Add(condition);
            }

            await _db.SaveChangesAsync();
            return Ok(condition);
        }

        [HttpDelete("agent/conditions/{id}")]
        [HasPermission(Permissions.ControllersImageAnalysisManagementSettings)]
        public async Task<IActionResult> DeleteAgentCondition(int id)
        {
            var condition = await _db.DecisionAgentConditions.AsTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (condition == null) return NotFound();
            if (condition.EvaluatorType == "BuiltIn")
                return BadRequest("Cannot delete built-in conditions. Disable them instead.");

            _db.DecisionAgentConditions.Remove(condition);
            await _db.SaveChangesAsync();
            return Ok();
        }

        [HttpGet("agent/audit-log")]
        public async Task<IActionResult> GetAgentAuditLog([FromQuery] int page = 1, [FromQuery] int pageSize = 20)
        {
            pageSize = Math.Clamp(pageSize, 1, 100);
            var query = _db.DecisionAgentAuditLogs.AsNoTracking()
                .OrderByDescending(a => a.CreatedAtUtc);

            var total = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return Ok(new { total, page, pageSize, items });
        }

        [HttpPost("agent/reverse/{auditLogId}")]
        [HasPermission(Permissions.ControllersImageAnalysisManagementSettings)]
        public async Task<IActionResult> ReverseAgentDecision(long auditLogId, [FromBody] AgentReversalRequest? request)
        {
            var auditLog = await _db.DecisionAgentAuditLogs.AsTracking().FirstOrDefaultAsync(a => a.Id == auditLogId);
            if (auditLog == null) return NotFound("Audit log entry not found");
            if (auditLog.ReversedAtUtc != null) return BadRequest("Already reversed");
            if (auditLog.IsShadowMode) return BadRequest("Cannot reverse shadow-mode evaluations");
            if (auditLog.Decision == "Skipped") return BadRequest("Cannot reverse skipped evaluations");

            var group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.Id == auditLog.GroupId);
            if (group == null) return NotFound("Analysis group not found");

            // Delete agent-created decisions
            if (!string.IsNullOrWhiteSpace(auditLog.DecisionIds))
            {
                var decisionIds = System.Text.Json.JsonSerializer.Deserialize<List<int>>(auditLog.DecisionIds) ?? new();
                var decisions = await _db.ImageAnalysisDecisions
                    .Where(d => decisionIds.Contains(d.Id))
                    .ToListAsync();
                _db.ImageAnalysisDecisions.RemoveRange(decisions);

                // Delete any audit decisions linked to these
                var auditDecisions = await _db.AuditDecisions
                    .Where(ad => decisionIds.Contains(ad.ImageAnalysisDecisionId))
                    .ToListAsync();
                _db.AuditDecisions.RemoveRange(auditDecisions);
            }

            // Revert group to Ready
            group.Status = "Ready";
            await _db.SaveChangesAsync();

            // Revert workflow stages
            if (!string.IsNullOrWhiteSpace(auditLog.ContainerNumbers))
            {
                var containers = System.Text.Json.JsonSerializer.Deserialize<List<string>>(auditLog.ContainerNumbers) ?? new();
                foreach (var cn in containers)
                {
                    var completeness = await _db.ContainerCompletenessStatuses.AsTracking()
                        .FirstOrDefaultAsync(c => c.ContainerNumber == cn);
                    if (completeness != null)
                    {
                        completeness.WorkflowStage = "ImageAnalysis";
                        completeness.UpdatedAt = DateTime.UtcNow;
                    }
                }
            }

            // Mark reversal
            auditLog.ReversedAtUtc = DateTime.UtcNow;
            auditLog.ReversedBy = User.Identity?.Name ?? "Unknown";
            auditLog.ReversalReason = request?.reason ?? "Manual reversal";
            await _db.SaveChangesAsync();

            _logger.LogInformation("[DECISION-AGENT-API] Reversed decision for group {GroupId} by {User}",
                auditLog.GroupIdentifier, auditLog.ReversedBy);
            return Ok(new { message = "Decision reversed. Group returned to Ready for human review." });
        }

        [HttpGet("agent/stats")]
        public async Task<IActionResult> GetAgentStats()
        {
            var logs = _db.DecisionAgentAuditLogs.AsNoTracking();
            var total = await logs.CountAsync();

            if (total == 0)
                return Ok(new AgentStatsResponse());

            var normalCount = await logs.CountAsync(a => a.Decision == "Normal" && !a.IsShadowMode);
            var abnormalCount = await logs.CountAsync(a => a.Decision == "Abnormal" && !a.IsShadowMode);
            var skippedCount = await logs.CountAsync(a => a.Decision == "Skipped");
            var shadowCount = await logs.CountAsync(a => a.IsShadowMode);
            var reversedCount = await logs.CountAsync(a => a.ReversedAtUtc != null);
            var avgScore = await logs.AverageAsync(a => a.TotalScore);
            var lastRun = await logs.MaxAsync(a => (DateTime?)a.CreatedAtUtc);

            return Ok(new AgentStatsResponse
            {
                totalProcessed = total,
                normalDecisions = normalCount,
                abnormalDecisions = abnormalCount,
                skippedForHuman = skippedCount,
                shadowModeOnly = shadowCount,
                reversed = reversedCount,
                averageScore = Math.Round(avgScore, 4),
                lastRunUtc = lastRun
            });
        }

        // --- Decision Agent DTOs ---

        public sealed class AgentSettingsRequest
        {
            public bool enabled { get; set; }
            public bool shadowMode { get; set; }
            public bool allowNormalDecisions { get; set; }
            public bool allowAbnormalDecisions { get; set; }
            public double normalThreshold { get; set; }
            public double abnormalThreshold { get; set; }
            public bool processingDepthAudit { get; set; }
            public bool processingDepthSubmission { get; set; }
            public int maxGroupsPerCycle { get; set; }
        }

        public sealed class AgentConditionRequest
        {
            public int? id { get; set; }
            public string name { get; set; } = "";
            public string conditionKey { get; set; } = "";
            public double weight { get; set; }
            public bool enabled { get; set; }
            public string? dynamicFieldPath { get; set; }
            public string? dynamicOperator { get; set; }
            public string? dynamicValue { get; set; }
            public string? description { get; set; }
        }

        public sealed class AgentReversalRequest
        {
            public string? reason { get; set; }
        }

        public sealed class AgentStatsResponse
        {
            public int totalProcessed { get; set; }
            public int normalDecisions { get; set; }
            public int abnormalDecisions { get; set; }
            public int skippedForHuman { get; set; }
            public int shadowModeOnly { get; set; }
            public int reversed { get; set; }
            public double averageScore { get; set; }
            public DateTime? lastRunUtc { get; set; }
        }
    }
}


