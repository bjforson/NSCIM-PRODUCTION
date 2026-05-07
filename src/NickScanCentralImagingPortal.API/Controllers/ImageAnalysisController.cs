using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Core.DTOs.ImageProcessing;
using NickScanCentralImagingPortal.Infrastructure.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/image-analysis")]
    [Authorize]
    public class ImageAnalysisController : ControllerBase
    {
        private readonly ApplicationDbContext _db;
        private readonly IcumDownloadsDbContext _icumDb;
        private readonly ILogger<ImageAnalysisController> _logger;
        private readonly IConfiguration _configuration;
        private readonly IMemoryCache _memoryCache;
        private readonly IImageAnalysisFacade _imageAnalysis;
        private readonly IServiceScopeFactory _scopeFactory;
        private static readonly Regex _safeKeyRegex = new Regex("[^A-Za-z0-9_-]+", RegexOptions.Compiled);
        private static readonly TimeSpan MyAssignmentsCacheDuration = TimeSpan.FromSeconds(15);

        public ImageAnalysisController(
            ApplicationDbContext db,
            IcumDownloadsDbContext icumDb,
            ILogger<ImageAnalysisController> logger,
            IConfiguration configuration,
            IMemoryCache memoryCache,
            IImageAnalysisFacade imageAnalysis,
            IServiceScopeFactory scopeFactory)
        {
            _db = db;
            _icumDb = icumDb;
            _logger = logger;
            _configuration = configuration;
            _memoryCache = memoryCache;
            _imageAnalysis = imageAnalysis;
            _scopeFactory = scopeFactory;
        }

        // DTOs
        private class BoeLookupResult
        {
            public string? ContainerNumber { get; set; }
            public string? DeclarationNumber { get; set; }
            public bool IsConsolidated { get; set; }
        }

        public sealed class MyAssignmentResponse
        {
            public Guid GroupId { get; set; }
            public string GroupIdentifier { get; set; } = string.Empty;
            public string ScannerType { get; set; } = string.Empty;
            public string Status { get; set; } = string.Empty;
            public int ContainerCount { get; set; }
            public List<string> Containers { get; set; } = new();
            public DateTime? LeaseUntilUtc { get; set; }
            public DateTime CreatedAtUtc { get; set; }
            public bool IsConsolidated { get; set; }

            // ✅ NEW: Enhanced fields for better UI display
            public int? ContainersWithImages { get; set; }
            public int? ContainersWithoutImages { get; set; }
            public int DecidedCount { get; set; }
            public int? TotalContainerCount { get; set; } // For PartiallyCompleted records
            public int? SubmittedContainerCount { get; set; } // For PartiallyCompleted records
            public int? PendingContainerCount { get; set; } // For PartiallyCompleted records
            public DateTime? PartiallyCompletedDate { get; set; } // For PartiallyCompleted records
            public DateTime? UpdatedAtUtc { get; set; }
        }

        public sealed class GroupResponse
        {
            public AnalysisGroup Group { get; set; } = new AnalysisGroup();
            public List<AnalysisRecord> Records { get; set; } = new List<AnalysisRecord>();
            public AnalysisAssignment? ActiveAssignment { get; set; }
            public DateTime? LeaseUntilUtc => ActiveAssignment?.LeaseUntilUtc;
        }

        public sealed class AnalystDecisionRequest
        {
            public string Decision { get; set; } = string.Empty; // Normal|Abnormal|NotClear
            public string? Notes { get; set; }
            public object? Tags { get; set; } // arbitrary JSON (polygons/boxes)
        }

        public sealed class AuditDecisionRequest
        {
            public string Decision { get; set; } = string.Empty; // Approve|Reject
            public string? Notes { get; set; }
        }

        public sealed class IntakeRequest
        {
            public List<string> GroupIdentifiers { get; set; } = new List<string>();
            public string? GroupType { get; set; }
            public string? ScannerType { get; set; }
        }

        public sealed class AssignRequest
        {
            public string? AssignedTo { get; set; } // Username or null for unassigned
            public string? Role { get; set; } // Analyst or Audit
            public DateTime? LeaseUntilUtc { get; set; }
        }

        public sealed class UserInfoResponse
        {
            public string Username { get; set; } = string.Empty;
            public string Email { get; set; } = string.Empty;
            public string FirstName { get; set; } = string.Empty;
            public string LastName { get; set; } = string.Empty;
            public int? RoleId { get; set; }
            public string RoleName { get; set; } = string.Empty;
        }

        /// <summary>
        /// Get service state (backward compatibility endpoint - delegates to management controller)
        /// </summary>
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

        /// <summary>
        /// Get assigned groups for current user (filtered by role: Analyst sees Analyst assignments, Audit sees Audit assignments)
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-empty fallback.
        [HttpGet("my-assignments")]
        public async Task<ActionResult<List<MyAssignmentResponse>>> GetMyAssignments([FromQuery] string? role = null)
        {
            var username = User.Identity?.Name ?? "unknown";
            var now = DateTime.UtcNow;

            // Determine user role from claims or query parameter
            var userRole = role ?? "Analyst"; // Default to Analyst
            if (string.IsNullOrEmpty(role))
            {
                if (User.IsInRole("Audit"))
                    userRole = "Audit";
                else if (User.IsInRole("Analyst"))
                    userRole = "Analyst";
            }

            // ✅ PERFORMANCE: Per-user cache (15s TTL) to reduce repeated heavy queries while staying responsive
            var cacheKey = $"my-assignments:{username}:{userRole}";
            if (_memoryCache.TryGetValue(cacheKey, out List<MyAssignmentResponse>? cachedResult) && cachedResult != null)
            {
                _logger.LogDebug("[GetMyAssignments] Cache hit for user {Username}, role {Role}", username, userRole);
                _ = Task.Run(async () => await UpdateLastAccessedForCachedAssignments(username, userRole, cachedResult.Select(a => a.GroupId).ToList()));
                return Ok(cachedResult);
            }

            _logger.LogInformation("Getting assignments for user: {Username}, Role: {Role}", username, userRole);
            var startTime = DateTime.UtcNow;

            // ═══ FAST PATH: Read from materialized queue table (single query) ═══
            var queueEntries = await _db.AnalysisQueueEntries
                .Where(e => e.AssignedTo == username && e.Role == userRole)
                .AsNoTracking()
                .ToListAsync();

            if (queueEntries.Any())
            {
                var fastResult = queueEntries.Select(e => new MyAssignmentResponse
                {
                    GroupId = e.GroupId,
                    GroupIdentifier = e.GroupIdentifier,
                    ScannerType = e.ScannerType ?? string.Empty,
                    Status = e.GroupStatus,
                    ContainerCount = e.ContainerCount,
                    Containers = System.Text.Json.JsonSerializer.Deserialize<List<string>>(e.ContainersJson ?? "[]") ?? new(),
                    LeaseUntilUtc = e.LeaseUntilUtc,
                    CreatedAtUtc = e.AssignmentCreatedAtUtc,
                    IsConsolidated = e.IsConsolidated,
                    ContainersWithImages = e.ContainersWithImages > 0 ? e.ContainersWithImages : null,
                    ContainersWithoutImages = e.ContainersWithoutImages > 0 ? e.ContainersWithoutImages : null,
                    DecidedCount = e.DecidedCount,
                    TotalContainerCount = e.TotalContainerCount,
                    SubmittedContainerCount = e.SubmittedContainerCount,
                    PendingContainerCount = e.PendingContainerCount,
                    PartiallyCompletedDate = e.PartiallyCompletedDate,
                    UpdatedAtUtc = e.GroupUpdatedAtUtc
                }).ToList();

                // Fire-and-forget LastAccessedAtUtc update
                _ = Task.Run(async () => await UpdateLastAccessedForCachedAssignments(username, userRole, fastResult.Select(a => a.GroupId).ToList()));

                _memoryCache.Set(cacheKey, fastResult, new MemoryCacheEntryOptions()
                    .SetAbsoluteExpiration(MyAssignmentsCacheDuration)
                    .SetSize(1));

                var fastDuration = (DateTime.UtcNow - startTime).TotalSeconds;
                _logger.LogInformation("[GetMyAssignments] FAST PATH: {Count} entries from queue table in {Duration:F3}s for {Username}",
                    fastResult.Count, fastDuration, username);

                return Ok(fastResult);
            }

            // ═══ SLOW FALLBACK: Legacy multi-query path (used when queue table is empty) ═══
            _logger.LogInformation("[GetMyAssignments] Queue table empty for {Username}/{Role} — falling back to legacy path", username, userRole);

            // Get active assignments for this user with matching role
            var assignments = await _db.AnalysisAssignments
                .Where(a => a.AssignedTo == username
                    && a.Role == userRole
                    && a.State == "Active"
                    && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                .ToListAsync();

            // ✅ DIAGNOSTIC: Log raw assignments found
            _logger.LogInformation("[GetMyAssignments] Found {RawCount} raw active assignments for user {Username}, role {Role}",
                assignments.Count, username, userRole);

            // Join with groups and records
            var groupIds = assignments.Select(a => a.GroupId).Distinct().ToList();

            if (!groupIds.Any())
            {
                return Ok(new List<MyAssignmentResponse>());
            }

            // ✅ PERFORMANCE FIX: Use batched queries with IN clauses to filter in database instead of loading everything
            // Load groups in batches to avoid CTE generation while still filtering efficiently in the database
            var groups = new List<AnalysisGroup>();
            const int groupBatchSize = 500;

            for (int i = 0; i < groupIds.Count; i += groupBatchSize)
            {
                var batch = groupIds.Skip(i).Take(groupBatchSize).ToList();
                // ✅ FIX: Use FromSqlRaw to avoid CTE generation from Contains()
                // Format GUIDs as strings for SQL IN clause
                var batchPlaceholders = string.Join(",", batch.Select(g => $"'{g}'"));
                var batchQuery = $"SELECT * FROM AnalysisGroups WHERE Id IN ({batchPlaceholders})";

                var batchGroups = await _db.AnalysisGroups
                    .FromSqlRaw(batchQuery)
                    .AsNoTracking()
                    .ToListAsync();

                groups.AddRange(batchGroups);
            }

            // ✅ Filter by status in memory (small dataset after database filtering)
            groups = groups
                .Where(g => userRole != "Audit" || (g.Status != AnalysisStatuses.AuditCompleted && g.Status != AnalysisStatuses.Completed))
                .Where(g => userRole != "Analyst" || (g.Status != AnalysisStatuses.AnalystCompleted && g.Status != AnalysisStatuses.AuditCompleted && g.Status != AnalysisStatuses.Completed))
                .ToList();

            // ✅ DIAGNOSTIC: Log groups after status filtering
            var filteredGroupIds = groups.Select(g => g.Id).Distinct().ToList();
            var validGroupIdsSet = new HashSet<Guid>(filteredGroupIds);
            var orphanedAssignments = assignments.Where(a => !validGroupIdsSet.Contains(a.GroupId)).ToList();

            if (orphanedAssignments.Any())
            {
                _logger.LogWarning("[GetMyAssignments] Found {Count} orphaned assignments for user {Username}, role {Role} — releasing to free capacity",
                    orphanedAssignments.Count, username, userRole);

                try
                {
                    var orphanedIds = string.Join(",", orphanedAssignments.Select(a => $"'{a.Id}'"));
                    var expireSql = $"UPDATE AnalysisAssignments SET State = 'Expired', UpdatedAtUtc = now() AT TIME ZONE 'UTC' WHERE Id IN ({orphanedIds})";
                    var released = await _db.Database.ExecuteSqlRawAsync(expireSql);
                    _logger.LogInformation("[GetMyAssignments] Auto-released {Count} orphaned assignments for user {Username} — capacity freed",
                        released, username);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[GetMyAssignments] Failed to auto-release orphaned assignments for user {Username}", username);
                }
            }

            _logger.LogInformation("[GetMyAssignments] After group status filtering: {Count} groups remain for user {Username} (orphaned: {Orphaned})",
                groups.Count, username, orphanedAssignments.Count);

            // ✅ RELIABILITY FIX: Update LastAccessedAtUtc for all assignments (tracks active usage to prevent expiration during work)
            var assignmentsToUpdate = assignments.Where(a => validGroupIdsSet.Contains(a.GroupId)).ToList();

            if (assignmentsToUpdate.Any())
            {
                // ✅ PERFORMANCE FIX: Use parameterized SQL with table-valued parameter instead of string concatenation
                // This is safer and potentially faster for large ID lists
                var assignmentIds = assignmentsToUpdate.Select(a => a.Id).ToList();
                const int updateBatchSize = 500;

                // Batch the updates to avoid very long SQL statements
                for (int i = 0; i < assignmentIds.Count; i += updateBatchSize)
                {
                    var batch = assignmentIds.Skip(i).Take(updateBatchSize);
                    var batchIds = string.Join(",", batch);
                    var updateSql = $"UPDATE AnalysisAssignments SET LastAccessedAtUtc = now() AT TIME ZONE 'UTC' WHERE Id IN ({batchIds})";
                    await _db.Database.ExecuteSqlRawAsync(updateSql);
                }

                _logger.LogDebug("[GetMyAssignments] Updated LastAccessedAtUtc for {Count} assignments for user {Username}",
                    assignmentsToUpdate.Count, username);
            }

            // ✅ PERFORMANCE FIX: Query AnalysisRecords only for filtered groups using batched database queries
            // This ensures records are only fetched for groups that should be displayed, filtering in database not memory

            var records = new List<AnalysisRecord>();
            const int recordBatchSize = 500;

            if (filteredGroupIds.Any())
            {
                for (int i = 0; i < filteredGroupIds.Count; i += recordBatchSize)
                {
                    var batch = filteredGroupIds.Skip(i).Take(recordBatchSize).ToList();
                    var batchPlaceholders = string.Join(",", batch.Select(g => $"'{g}'"));
                    var batchQuery = $"SELECT * FROM AnalysisRecords WHERE GroupId IN ({batchPlaceholders})";

                    var batchRecords = await _db.AnalysisRecords
                        .FromSqlRaw(batchQuery)
                        .AsNoTracking()
                        .ToListAsync();

                    records.AddRange(batchRecords);
                }
            }

            // ✅ PERFORMANCE FIX: Determine consolidation status by querying BOE documents
            // ✅ OPTIMIZED: Use timeout protection and simplified query structure
            var groupIdentifiers = groups.Select(g => g.GroupIdentifier).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();

            var consolidatedContainers = new List<string>();
            var nonConsolidatedDeclarations = new List<string>();
            var allContainerNumbers = new List<string>();

            // ✅ PERFORMANCE FIX: Only query BOE if we have identifiers, and use timeout protection
            if (groupIdentifiers.Count > 0)
            {
                try
                {
                    // ✅ PERFORMANCE FIX: Use smaller batches and add timeout protection
                    const int boeBatchSize = 500; // Reduced batch size for faster queries

                    // ✅ PERFORMANCE FIX: Set command timeout to prevent long waits
                    var originalTimeout = _icumDb.Database.GetCommandTimeout();
                    _icumDb.Database.SetCommandTimeout(10); // 10 second timeout for BOE queries

                    try
                    {
                        for (int i = 0; i < groupIdentifiers.Count; i += boeBatchSize)
                        {
                            var batch = groupIdentifiers.Skip(i).Take(boeBatchSize).Where(g => g != null).ToList();
                            if (!batch.Any()) continue;

                            // ✅ FIX: Escape single quotes in identifiers to prevent SQL injection
                            var placeholders = string.Join(",", batch.Select(g => $"'{g!.Replace("'", "''")}'"));

                            var combinedQuery = $@"
                                SELECT DISTINCT containernumber, declarationnumber, isconsolidated FROM (
                                    (SELECT containernumber, declarationnumber, isconsolidated 
                                     FROM boedocuments
                                     WHERE containernumber IN ({placeholders})
                                     LIMIT 10000)
                                    UNION ALL
                                    (SELECT containernumber, declarationnumber, isconsolidated 
                                     FROM boedocuments
                                     WHERE declarationnumber IN ({placeholders})
                                     LIMIT 10000)
                                ) AS combined";

                            var boeResults = await _icumDb.Database
                                .SqlQueryRaw<BoeLookupResult>(combinedQuery)
                                .ToListAsync();

                            // Build lookup sets from results
                            foreach (var boeResult in boeResults)
                            {
                                if (!string.IsNullOrEmpty(boeResult.ContainerNumber) && boeResult.IsConsolidated)
                                {
                                    consolidatedContainers.Add(boeResult.ContainerNumber);
                                }
                                if (!string.IsNullOrEmpty(boeResult.DeclarationNumber) && !boeResult.IsConsolidated)
                                {
                                    nonConsolidatedDeclarations.Add(boeResult.DeclarationNumber);
                                }
                                if (!string.IsNullOrEmpty(boeResult.ContainerNumber))
                                {
                                    allContainerNumbers.Add(boeResult.ContainerNumber);
                                }
                            }
                        }
                    }
                    finally
                    {
                        // Restore original timeout
                        _icumDb.Database.SetCommandTimeout(originalTimeout);
                    }
                }
                catch (Exception ex)
                {
                    // ✅ RESILIENCE FIX: If BOE query fails or times out, continue without consolidation data
                    // This prevents the entire endpoint from failing if BOE database is slow
                    _logger.LogWarning(ex, "[GetMyAssignments] BOE query failed or timed out for user {Username}, continuing without consolidation data", username);
                }
            }

            consolidatedContainers = consolidatedContainers.Distinct().ToList();
            nonConsolidatedDeclarations = nonConsolidatedDeclarations.Distinct().ToList();
            allContainerNumbers = allContainerNumbers.Distinct().ToList();

            var result = assignments
                .Select(a =>
                {
                    var group = groups.FirstOrDefault(g => g.Id == a.GroupId);
                    var groupRecords = records.Where(r => r.GroupId == a.GroupId).ToList();

                    // ✅ FIX: Use actual BOE data to determine consolidation status
                    var groupIdentifier = group?.GroupIdentifier ?? string.Empty;
                    var isConsolidatedAsContainer = consolidatedContainers.Contains(groupIdentifier);
                    var isNonConsolidatedAsDeclaration = nonConsolidatedDeclarations.Contains(groupIdentifier);

                    bool isConsolidated = isConsolidatedAsContainer;
                    if (!isConsolidatedAsContainer && !isNonConsolidatedAsDeclaration)
                    {
                        // Fallback: if GroupIdentifier matches any container number, treat as consolidated
                        isConsolidated = allContainerNumbers.Contains(groupIdentifier);
                    }

                    return new
                    {
                        Assignment = a,
                        Group = group,
                        GroupRecords = groupRecords,
                        GroupIdentifier = groupIdentifier,
                        IsConsolidated = isConsolidated
                    };
                })
                // ✅ FIX: Filter out assignments where group was filtered out (completed groups for Audit role)
                .Where(x => x.Group != null)
                .ToList(); // Materialize to avoid multiple enumerations

            // ✅ NEW: Get container completeness data and decision counts for enhanced display
            var groupIdentifiersForQuery = result.Select(x => x.GroupIdentifier).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();
            var containerCompletenessData = new Dictionary<string, (int WithImages, int WithoutImages)>();
            var decisionCounts = new Dictionary<string, int>();

            if (groupIdentifiersForQuery.Any())
            {
                try
                {
                    // Get container completeness status for these groups
                    const int batchSize = 500;
                    for (int i = 0; i < groupIdentifiersForQuery.Count; i += batchSize)
                    {
                        var batch = groupIdentifiersForQuery.Skip(i).Take(batchSize).ToList();
                        if (!batch.Any()) continue;

                        // Round-1 audit C-2: previously this used FromSql with a manually
                        // single-quote-escaped IN list — fragile if any future change ever
                        // routes user input into batch. LINQ Contains() translates to
                        // = ANY(@p) under Npgsql so the values are bound parameters.
                        var batchKeys = batch.Where(g => !string.IsNullOrEmpty(g)).ToList();
                        var batchCompleteness = await _db.ContainerCompletenessStatuses
                            .Where(c => c.GroupIdentifier != null && batchKeys.Contains(c.GroupIdentifier))
                            .AsNoTracking()
                            .ToListAsync();

                        var grouped = batchCompleteness
                            .GroupBy(c => c.GroupIdentifier)
                            .ToList();

                        foreach (var group in grouped)
                        {
                            var withImages = group.Count(c => c.HasImageData == true);
                            var withoutImages = group.Count(c => c.HasImageData != true);
                            containerCompletenessData[group.Key ?? string.Empty] = (withImages, withoutImages);
                        }
                    }

                    // Get decision counts for these groups
                    // ✅ FIX: For Audit role, count DISTINCT containers with AuditDecisions
                    //         For Analyst role, count ImageAnalysisDecisions (original logic)
                    if (groupIdentifiersForQuery.Any())
                    {
                        const int decisionBatchSize = 500;
                        for (int i = 0; i < groupIdentifiersForQuery.Count; i += decisionBatchSize)
                        {
                            var batch = groupIdentifiersForQuery.Skip(i).Take(decisionBatchSize).ToList();
                            if (!batch.Any()) continue;

                            var placeholders = string.Join(",", batch.Select(g => $"'{g!.Replace("'", "''")}'"));

                            if (userRole == "Audit")
                            {
                                // ✅ AUDIT ROLE: Count distinct containers with audit decisions per group
                                var batchAuditDecisions = await _db.AuditDecisions
                                    .FromSql($"SELECT * FROM AuditDecisions WHERE GroupIdentifier IN ({placeholders})")
                                    .AsNoTracking()
                                    .ToListAsync();

                                var groupedAuditDecisions = batchAuditDecisions
                                    .GroupBy(d => d.GroupIdentifier)
                                    .ToList();

                                foreach (var decisionGroup in groupedAuditDecisions)
                                {
                                    // Count DISTINCT containers audited (not individual decision rows)
                                    decisionCounts[decisionGroup.Key] = decisionGroup
                                        .Select(d => d.ContainerNumber)
                                        .Distinct(StringComparer.OrdinalIgnoreCase)
                                        .Count();
                                }
                            }
                            else
                            {
                                // ANALYST ROLE: Count ImageAnalysisDecisions (original logic)
                                var batchDecisions = await _db.ImageAnalysisDecisions
                                    .FromSql($"SELECT * FROM ImageAnalysisDecisions WHERE GroupIdentifier IN ({placeholders}) AND Decision IN ('Normal', 'Abnormal')")
                                    .AsNoTracking()
                                    .ToListAsync();

                                // ✅ SCANNER TYPE FIX: Match decisions to groups by GroupIdentifier AND normalized ScannerType
                                var groupScannerTypes = groups
                                    .Where(g => batch.Contains(g.GroupIdentifier))
                                    .ToDictionary(g => g.GroupIdentifier ?? string.Empty, g => g.ScannerType ?? string.Empty);

                                var groupedDecisions = batchDecisions
                                    .GroupBy(d => d.GroupIdentifier ?? string.Empty)
                                    .ToList();

                                foreach (var decisionGroup in groupedDecisions)
                                {
                                    var groupIdentifier = decisionGroup.Key;
                                    if (!groupScannerTypes.TryGetValue(groupIdentifier, out var groupScannerType))
                                    {
                                        decisionCounts[groupIdentifier] = decisionGroup.Count();
                                        continue;
                                    }

                                    var baseGroupScannerType = groupScannerType.Split('-')[0];
                                    var matchingDecisions = decisionGroup.Where(d =>
                                        d.ScannerType == groupScannerType ||
                                        d.ScannerType.StartsWith(baseGroupScannerType + "-") ||
                                        d.ScannerType == baseGroupScannerType);

                                    decisionCounts[groupIdentifier] = matchingDecisions.Count();
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[GetMyAssignments] Error loading container completeness or decision counts, continuing without enhanced data");
                }
            }

            // ✅ NEW: Build enhanced response with container breakdown and decision counts
            var enhancedResult = result.Select(x =>
            {
                var completeness = containerCompletenessData.GetValueOrDefault(x.GroupIdentifier, (0, 0));
                var decidedCount = decisionCounts.GetValueOrDefault(x.GroupIdentifier, 0);

                return new MyAssignmentResponse
                {
                    GroupId = x.Assignment.GroupId,
                    GroupIdentifier = x.GroupIdentifier,
                    ScannerType = x.Group?.ScannerType ?? string.Empty,
                    Status = x.Group?.Status ?? "Unknown",
                    ContainerCount = x.GroupRecords.Count(),
                    Containers = x.GroupRecords.Select(r => r.ContainerNumber).Distinct().ToList(),
                    LeaseUntilUtc = x.Assignment.LeaseUntilUtc,
                    CreatedAtUtc = x.Group?.CreatedAtUtc ?? x.Assignment.CreatedAtUtc,
                    IsConsolidated = x.IsConsolidated,
                    // ✅ NEW: Enhanced fields - tuple access: Item1 = withImages, Item2 = withoutImages
                    ContainersWithImages = completeness.Item1 > 0 ? completeness.Item1 : null,
                    ContainersWithoutImages = completeness.Item2 > 0 ? completeness.Item2 : null,
                    DecidedCount = decidedCount,
                    TotalContainerCount = x.Group?.TotalContainerCount,
                    SubmittedContainerCount = x.Group?.SubmittedContainerCount,
                    PendingContainerCount = x.Group?.PendingContainerCount,
                    PartiallyCompletedDate = x.Group?.PartiallyCompletedDate,
                    UpdatedAtUtc = x.Group?.UpdatedAtUtc
                };
            })
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToList();

            // ✅ SELF-HEALING: For Audit role, detect groups that have all containers audited
            // but are still stuck in AuditAssigned (missed completion trigger). Auto-complete them.
            if (userRole == "Audit")
            {
                var stuckGroups = enhancedResult
                    .Where(a => a.Status == AnalysisStatuses.AuditAssigned
                             && a.ContainerCount > 0
                             && a.DecidedCount >= a.ContainerCount)
                    .ToList();

                if (stuckGroups.Any())
                {
                    _logger.LogWarning(
                        "[GetMyAssignments] SELF-HEALING: Found {Count} stuck audit records (all containers audited but status still AuditAssigned): {Groups}",
                        stuckGroups.Count,
                        string.Join(", ", stuckGroups.Select(g => g.GroupIdentifier)));

                    // Auto-complete in background to avoid slowing down the response.
                    // Use the singleton IServiceScopeFactory (NOT HttpContext.RequestServices) — once
                    // we return from this action, the request scope is disposed, so a scope created
                    // from HttpContext.RequestServices after the response would throw
                    // ObjectDisposedException. The scope factory survives request lifetime.
                    var stuckGroupIdentifiers = stuckGroups.Select(g => g.GroupIdentifier).ToList();
                    var scopeFactory = _scopeFactory;
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            using var scope = scopeFactory.CreateScope();
                            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                            foreach (var groupIdentifier in stuckGroupIdentifiers)
                            {
                                // Sprint 5G2 / B1: load AsTracking so the state-machine facade can persist the
                                // status flip (default ApplicationDbContext is NoTracking — see memory
                                // feedback_application_dbcontext_notracking_default.md).
                                var grp = await db.AnalysisGroups
                                    .AsTracking()
                                    .FirstOrDefaultAsync(g => g.GroupIdentifier == groupIdentifier
                                        && g.Status == AnalysisStatuses.AuditAssigned);
                                if (grp == null) continue;

                                await AnalysisGroupStateMachine.TransitionAsync(
                                    db, grp, AnalysisStatuses.AuditCompleted,
                                    triggerName: "AuditSelfHealStuckAssignment",
                                    actor: "SYSTEM-SELFHEAL",
                                    reason: $"All containers for group {groupIdentifier} were audited but status was still AuditAssigned (missed completion trigger).",
                                    correlationId: null);
                                grp.UpdatedAtUtc = DateTime.UtcNow;

                                // Release active audit assignments for this group
                                await db.Database.ExecuteSqlRawAsync(
                                    "UPDATE AnalysisAssignments SET State = 'Released', UpdatedAtUtc = now() AT TIME ZONE 'UTC' WHERE GroupId = {0} AND Role = 'Audit' AND State = 'Active'",
                                    grp.Id);

                                // Mark audit decisions as completed
                                await db.Database.ExecuteSqlRawAsync(
                                    "UPDATE AuditDecisions SET IsCompleted = true, CompletedAt = now() AT TIME ZONE 'UTC', UpdatedAt = now() AT TIME ZONE 'UTC' WHERE GroupIdentifier = {0} AND IsCompleted = false",
                                    groupIdentifier);

                                _logger.LogInformation(
                                    "[GetMyAssignments] SELF-HEALING: Auto-completed stuck audit group {GroupIdentifier} → AuditCompleted",
                                    groupIdentifier);
                            }

                            await db.SaveChangesAsync();

                            // Invalidate cache so next request shows updated state
                            _memoryCache.Remove(cacheKey);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "[GetMyAssignments] SELF-HEALING: Failed to auto-complete stuck audit groups");
                        }
                    }).ContinueWith(t =>
                    {
                        // Defense in depth: the inner try/catch should always handle, but if a
                        // synchronous throw happens before the try (e.g., scope creation), surface it
                        // instead of letting it become an unobserved task exception.
                        if (t.IsFaulted && t.Exception is { } aex)
                        {
                            _logger.LogError(aex, "[GetMyAssignments] SELF-HEALING: Background task faulted (unobserved)");
                        }
                    }, TaskScheduler.Default);

                    // Remove stuck groups from the response (they're being completed)
                    var stuckSet = new HashSet<string>(stuckGroupIdentifiers);
                    enhancedResult = enhancedResult.Where(a => !stuckSet.Contains(a.GroupIdentifier)).ToList();
                }
            }

            // ✅ DIAGNOSTIC: Log final result count and performance
            var duration = (DateTime.UtcNow - startTime).TotalSeconds;
            _logger.LogInformation(
                "[GetMyAssignments] Returning {FinalCount} assignments to user {Username}, role {Role} (from {RawCount} raw assignments) in {Duration:F2}s",
                enhancedResult.Count,
                username,
                userRole,
                assignments.Count,
                duration);

            if (duration > 5)
            {
                _logger.LogWarning(
                    "[GetMyAssignments] SLOW: GetMyAssignments took {Duration:F2}s for user {Username} - consider optimizing queries",
                    duration,
                    username);
            }

            // ✅ PERFORMANCE: Cache result for 15s to reduce repeated heavy queries
            // SetSize(1) required when MemoryCache.SizeLimit is configured (prevents unbounded growth)
            _memoryCache.Set(cacheKey, enhancedResult, new MemoryCacheEntryOptions()
                .SetAbsoluteExpiration(MyAssignmentsCacheDuration)
                .SetSize(1));

            return Ok(enhancedResult);
        }

        /// <summary>
        /// Updates LastAccessedAtUtc for cached assignments (fire-and-forget) to keep lease alive during cache TTL.
        /// IMPORTANT: Uses its own scoped DbContext — do NOT use _db here. This method runs
        /// in a fire-and-forget Task.Run after the HTTP request completes. Using the request-scoped
        /// _db would leak the connection (it stays "idle" in the pool forever because the request
        /// scope never disposes it from the Task.Run context). Over ~20h this exhausts the pool
        /// and blocks all DB access including login. Fixed 2026-04-16.
        /// </summary>
        private async Task UpdateLastAccessedForCachedAssignments(string username, string userRole, List<Guid> groupIds)
        {
            if (groupIds.Count == 0) return;
            try
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                const int batchSize = 500;
                for (int i = 0; i < groupIds.Count; i += batchSize)
                {
                    var batch = groupIds.Skip(i).Take(batchSize).ToList();
                    var placeholders = string.Join(",", batch.Select(g => $"'{g}'"));
#pragma warning disable EF1002 // FromSqlRaw/ExecuteSqlRaw - username/role from User.Identity, GUIDs from our data
                    var updateSql = $"UPDATE AnalysisAssignments SET LastAccessedAtUtc = now() AT TIME ZONE 'UTC' WHERE AssignedTo = '{username.Replace("'", "''")}' AND Role = '{userRole.Replace("'", "''")}' AND State = 'Active' AND GroupId IN ({placeholders})";
                    await db.Database.ExecuteSqlRawAsync(updateSql);
#pragma warning restore EF1002
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "[GetMyAssignments] Background LastAccessedAtUtc update failed for {Username}", username);
            }
        }

        /// <summary>
        /// Get available groups for UserClaim mode (eligible groups only - Ready for Analyst, AnalystCompleted for Audit)
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-empty fallback.
        [HttpGet("available")]
        public async Task<ActionResult<List<MyAssignmentResponse>>> GetAvailableGroups([FromQuery] string? role = null)
        {
            var username = User.Identity?.Name ?? "unknown";
            var now = DateTime.UtcNow;

            // Determine user role from claims or query parameter
            var userRole = role ?? "Analyst";
            if (string.IsNullOrEmpty(role))
            {
                if (User.IsInRole("Audit"))
                    userRole = "Audit";
                else if (User.IsInRole("Analyst"))
                    userRole = "Analyst";
            }

            // Get settings to check mode and user's current assignments
            var settings = await _db.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();

            if (settings.AssignmentMode != "UserClaim")
            {
                return BadRequest(new { error = "Available groups only shown in UserClaim mode" });
            }

            // Check user's current assignment count
            var userAssignmentCount = await _db.AnalysisAssignments
                .Where(a => a.AssignedTo == username
                    && a.Role == userRole
                    && a.State == "Active"
                    && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                .CountAsync();

            if (userAssignmentCount >= settings.MaxConcurrentPerUser)
            {
                return Ok(new List<MyAssignmentResponse>()); // Already at max
            }

            // Get eligible groups based on role
            var eligibleStatus = userRole == "Audit" ? AnalysisStatuses.AnalystCompleted : AnalysisStatuses.Ready;

            var eligibleGroups = await _db.AnalysisGroups
                .Where(g => g.Status == eligibleStatus)
                .OrderByDescending(g => g.Priority)
                .ThenByDescending(g => g.CreatedAtUtc)
                .Take(100)
                .ToListAsync();

            // Filter out groups with active assignments
            var groupIds = eligibleGroups.Select(g => g.Id).ToList();
            var activeAssignments = await _db.AnalysisAssignments
                .Where(a => groupIds.Contains(a.GroupId)
                    && a.State == "Active"
                    && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                .Select(a => a.GroupId)
                .ToListAsync();

            var availableGroups = eligibleGroups
                .Where(g => !activeAssignments.Contains(g.Id))
                .ToList();

            // Get records for available groups
            var availableGroupIds = availableGroups.Select(g => g.Id).ToList();
            var records = await _db.AnalysisRecords
                .Where(r => availableGroupIds.Contains(r.GroupId))
                .ToListAsync();

            // ✅ FIX: Determine consolidation status by querying BOE documents (same logic as above)
            var groupIdentifiers = availableGroups.Select(g => g.GroupIdentifier).Where(g => !string.IsNullOrEmpty(g)).Distinct().ToList();

            // ✅ FIX: Batch Contains() to avoid EF Core CTE generation with large lists
            var consolidatedContainers = new List<string>();
            var nonConsolidatedDeclarations = new List<string>();
            var allContainerNumbers = new List<string>();
            const int boeBatchSize2 = 1000;

            if (groupIdentifiers.Count > 0)
            {
                for (int i = 0; i < groupIdentifiers.Count; i += boeBatchSize2)
                {
                    var batch = groupIdentifiers.Skip(i).Take(boeBatchSize2).Where(g => g != null).ToList();

                    var batchConsolidated = await _icumDb.BOEDocuments
                        .AsNoTracking()
                        .Where(b => b.ContainerNumber != null && batch.Contains(b.ContainerNumber) && b.IsConsolidated)
                        .Select(b => b.ContainerNumber!)
                        .Distinct()
                        .ToListAsync();
                    consolidatedContainers.AddRange(batchConsolidated);

                    var batchNonConsolidated = await _icumDb.BOEDocuments
                        .AsNoTracking()
                        .Where(b => b.DeclarationNumber != null && batch.Contains(b.DeclarationNumber) && !b.IsConsolidated)
                        .Select(b => b.DeclarationNumber!)
                        .Distinct()
                        .ToListAsync();
                    nonConsolidatedDeclarations.AddRange(batchNonConsolidated);

                    var batchAllContainers = await _icumDb.BOEDocuments
                        .AsNoTracking()
                        .Where(b => b.ContainerNumber != null && batch.Contains(b.ContainerNumber))
                        .Select(b => b.ContainerNumber!)
                        .Distinct()
                        .ToListAsync();
                    allContainerNumbers.AddRange(batchAllContainers);
                }
            }

            consolidatedContainers = consolidatedContainers.Distinct().ToList();
            nonConsolidatedDeclarations = nonConsolidatedDeclarations.Distinct().ToList();
            allContainerNumbers = allContainerNumbers.Distinct().ToList();

            var result = availableGroups.Select(g =>
            {
                var groupRecords = records.Where(r => r.GroupId == g.Id).ToList();

                // ✅ FIX: Use actual BOE data to determine consolidation status
                var groupIdentifier = g.GroupIdentifier ?? string.Empty;
                var isConsolidatedAsContainer = consolidatedContainers.Contains(groupIdentifier);
                var isNonConsolidatedAsDeclaration = nonConsolidatedDeclarations.Contains(groupIdentifier);

                bool isConsolidated = isConsolidatedAsContainer;
                if (!isConsolidatedAsContainer && !isNonConsolidatedAsDeclaration)
                {
                    // Fallback: if GroupIdentifier matches any container number, treat as consolidated
                    isConsolidated = allContainerNumbers.Contains(groupIdentifier);
                }

                return new MyAssignmentResponse
                {
                    GroupId = g.Id,
                    GroupIdentifier = groupIdentifier,
                    ScannerType = g.ScannerType ?? string.Empty,
                    Status = g.Status,
                    ContainerCount = groupRecords.Count(),
                    Containers = groupRecords.Select(r => r.ContainerNumber).Distinct().ToList(),
                    LeaseUntilUtc = null, // Not assigned yet
                    CreatedAtUtc = g.CreatedAtUtc,
                    IsConsolidated = isConsolidated
                };
            }).ToList();

            _logger.LogInformation("Found {Count} available groups for user {Username} (role: {Role})", result.Count(), username, userRole);
            return Ok(result);
        }

        /// <summary>
        /// Claim a group (UserClaim mode - one at a time, must be eligible and under MaxConcurrent)
        /// </summary>
        [HttpPost("groups/{groupId}/claim")]
        [HasPermission(Permissions.ControllersImageAnalysisClaim)]
        public async Task<ActionResult> ClaimGroup(string groupId)
        {
            // Support both GUID and GroupIdentifier
            Guid? gid = null;
            string? groupIdentifier = null;

            if (Guid.TryParse(groupId, out var parsedGuid))
            {
                gid = parsedGuid;
            }
            else
            {
                groupIdentifier = groupId;
            }

            var username = User.Identity?.Name ?? "unknown";
            var now = DateTime.UtcNow;

            // Determine user role
            var userRole = "Analyst";
            if (User.IsInRole("Audit"))
                userRole = "Audit";
            else if (User.IsInRole("Analyst"))
                userRole = "Analyst";

            // Check settings
            var settings = await _db.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();

            if (settings.AssignmentMode != "UserClaim")
            {
                return BadRequest(new { error = "Claim only available in UserClaim mode" });
            }

            // Find group
            AnalysisGroup? group;
            if (gid.HasValue)
            {
                group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.Id == gid.Value);
            }
            else
            {
                group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.GroupIdentifier == groupIdentifier);
            }

            if (group == null) return NotFound(new { error = "Group not found" });

            // Check if group is eligible for this role
            var eligibleStatus = userRole == "Audit" ? AnalysisStatuses.AnalystCompleted : AnalysisStatuses.Ready;
            if (group.Status != eligibleStatus)
            {
                return BadRequest(new { error = $"Group status '{group.Status}' not eligible for {userRole} role" });
            }

            // Check if group already has active assignment
            var hasActive = await _db.AnalysisAssignments
                .AnyAsync(a => a.GroupId == group.Id
                    && a.State == "Active"
                    && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now));

            if (hasActive)
            {
                return Conflict(new { error = "Group already has active assignment" });
            }

            // Check user's current assignment count
            var userAssignmentCount = await _db.AnalysisAssignments
                .Where(a => a.AssignedTo == username
                    && a.Role == userRole
                    && a.State == "Active"
                    && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                .CountAsync();

            if (userAssignmentCount >= settings.MaxConcurrentPerUser)
            {
                return BadRequest(new { error = $"Maximum concurrent assignments ({settings.MaxConcurrentPerUser}) reached" });
            }

            // Create assignment
            var assignment = new AnalysisAssignment
            {
                GroupId = group.Id,
                AssignedTo = username,
                Role = userRole,
                LeaseUntilUtc = now.AddMinutes(Math.Max(1, settings.LeaseMinutes)),
                State = "Active",
                CreatedAtUtc = now
            };
            _db.AnalysisAssignments.Add(assignment);

            // Sprint 5G2 / B1: routed through facade. The facade's SaveChangesAsync also commits
            // the AnalysisAssignment.Add above. If the transition is illegal (e.g. the group was
            // already claimed by someone else and is no longer in a claimable state), return
            // 409 Conflict — the AnalysisAssignment is not persisted (transaction rolls back via
            // the absent SaveChanges) and the client should re-fetch + retry.
            var targetStatus = userRole == "Audit" ? AnalysisStatuses.AuditAssigned : AnalysisStatuses.AnalystAssigned;
            try
            {
                await AnalysisGroupStateMachine.TransitionAsync(
                    _db, group, targetStatus,
                    triggerName: "UserClaimedGroup",
                    actor: username,
                    reason: $"User {username} claimed group {groupId} (role={userRole}, lease={assignment.LeaseUntilUtc:O}).",
                    correlationId: HttpContext?.TraceIdentifier,
                    ct: HttpContext?.RequestAborted ?? CancellationToken.None);
            }
            catch (InvalidOperationException ex)
            {
                _logger.LogInformation(
                    "Claim conflict for group {GroupId} by {Username}: {Error}",
                    groupId, username, ex.Message);
                return Conflict(new { error = "Group is no longer in a claimable state. Refresh and try again." });
            }

            await WriteAudit("ImageAnalysis", "Claim", $"User {username} claimed group {groupId}", nameof(AnalysisAssignment), groupId);

            return Ok(new { success = true, leaseUntilUtc = assignment.LeaseUntilUtc });
        }

        /// <summary>
        /// Assign a group to a user (Admin/Lead only - explicit assignment)
        /// </summary>
        [HttpPost("groups/{groupId}/assign")]
        [HasPermission(Permissions.ControllersImageAnalysisAssign)]
        public async Task<ActionResult> AssignGroup(string groupId, [FromBody] AssignRequest request)
        {
            // Support both GUID and GroupIdentifier
            Guid? gid = null;
            string? groupIdentifier = null;

            if (Guid.TryParse(groupId, out var parsedGuid))
            {
                gid = parsedGuid;
            }
            else
            {
                groupIdentifier = groupId;
            }

            var username = User.Identity?.Name ?? "system";
            var now = DateTime.UtcNow;

            // Find group
            AnalysisGroup? group;
            if (gid.HasValue)
            {
                group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.Id == gid.Value);
            }
            else
            {
                group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.GroupIdentifier == groupIdentifier);
            }

            if (group == null) return NotFound(new { error = "Group not found" });

            // Determine role
            var assignRole = request.Role ?? "Analyst";
            if (assignRole != "Analyst" && assignRole != "Audit")
            {
                return BadRequest(new { error = "Role must be 'Analyst' or 'Audit'" });
            }

            // Check if group is eligible for this role
            var eligibleStatus = assignRole == "Audit" ? AnalysisStatuses.AnalystCompleted : AnalysisStatuses.Ready;
            if (group.Status != eligibleStatus && group.Status != AnalysisStatuses.Ready && group.Status != AnalysisStatuses.AnalystCompleted)
            {
                return BadRequest(new { error = $"Group status '{group.Status}' not eligible for {assignRole} role" });
            }

            // Expire any existing active assignments for this group
            var existingActive = await _db.AnalysisAssignments
                .Where(a => a.GroupId == group.Id && a.State == "Active")
                .AsTracking()
                .ToListAsync();

            foreach (var existing in existingActive)
            {
                existing.State = "Expired";
                existing.UpdatedAtUtc = now;
            }

            // Check user's assignment count if specified
            if (!string.IsNullOrEmpty(request.AssignedTo))
            {
                var settings = await _db.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();
                var userAssignmentCount = await _db.AnalysisAssignments
                    .Where(a => a.AssignedTo == request.AssignedTo
                        && a.Role == assignRole
                        && a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .CountAsync();

                if (userAssignmentCount >= settings.MaxConcurrentPerUser)
                {
                    return BadRequest(new { error = $"User {request.AssignedTo} has reached maximum concurrent assignments ({settings.MaxConcurrentPerUser})" });
                }
            }

            // Create new assignment
            var assignment = new AnalysisAssignment
            {
                GroupId = group.Id,
                AssignedTo = request.AssignedTo ?? "unassigned",
                Role = assignRole,
                LeaseUntilUtc = request.LeaseUntilUtc ?? now.AddMinutes(15),
                State = "Active",
                CreatedAtUtc = now
            };
            _db.AnalysisAssignments.Add(assignment);

            // Sprint 5G2 / B1: routed through facade. The existing guard limits fromStatus to
            // {Ready, AnalystCompleted}; the facade's validator covers all four resulting edges
            // (Ready→AnalystAssigned, Ready→AuditAssigned, AnalystCompleted→AnalystAssigned,
            // AnalystCompleted→AuditAssigned).
            if (group.Status == AnalysisStatuses.Ready || group.Status == AnalysisStatuses.AnalystCompleted)
            {
                var targetStatus = assignRole == "Audit" ? AnalysisStatuses.AuditAssigned : AnalysisStatuses.AnalystAssigned;
                try
                {
                    await AnalysisGroupStateMachine.TransitionAsync(
                        _db, group, targetStatus,
                        triggerName: "AdminAssignedGroup",
                        actor: username,
                        reason: $"Admin {username} assigned group {groupId} to {request.AssignedTo ?? "unassigned"} (role={assignRole}, lease={assignment.LeaseUntilUtc:O}).",
                        correlationId: HttpContext?.TraceIdentifier,
                        ct: HttpContext?.RequestAborted ?? CancellationToken.None);
                }
                catch (InvalidOperationException ex)
                {
                    _logger.LogInformation(
                        "Admin assign conflict for group {GroupId}: {Error}",
                        groupId, ex.Message);
                    return Conflict(new { error = "Group state changed during assignment. Refresh and try again." });
                }
            }
            else
            {
                // Group is in a state where status doesn't change (e.g. already AnalystAssigned);
                // persist the assignment record on its own.
                await _db.SaveChangesAsync();
            }
            await WriteAudit("ImageAnalysis", "Assign", $"Admin {username} assigned group {groupId} to {request.AssignedTo ?? "unassigned"} ({assignRole})", nameof(AnalysisAssignment), groupId);

            return Ok(new { success = true, assignment = new { assignment.AssignedTo, assignment.Role, assignment.LeaseUntilUtc } });
        }

        /// <summary>
        /// Get users by role (for Management page)
        /// </summary>
        [HttpGet("users")]
        [HasPermission(Permissions.ControllersImageAnalysisUsers)]
        public async Task<ActionResult<List<UserInfoResponse>>> GetUsersByRole([FromQuery] string? role = null)
        {
            if (string.IsNullOrEmpty(role))
            {
                // Return all users with Analyst or Audit roles
                var analystRole = await _db.Roles
                    .Where(r => r.Name == "Analyst" && r.IsActive)
                    .FirstOrDefaultAsync();

                var auditRole = await _db.Roles
                    .Where(r => r.Name == "Audit" && r.IsActive)
                    .FirstOrDefaultAsync();

                var roleIds = new List<int?>();
                if (analystRole != null) roleIds.Add(analystRole.Id);
                if (auditRole != null) roleIds.Add(auditRole.Id);

                var users = await _db.Users
                    .Where(u => u.IsActive && (roleIds.Contains(u.RoleId)))
                    .OrderBy(u => u.Username)
                    .Select(u => new UserInfoResponse
                    {
                        Username = u.Username,
                        Email = u.Email,
                        FirstName = u.FirstName,
                        LastName = u.LastName,
                        RoleId = u.RoleId,
                        RoleName = u.AssignedRole != null ? u.AssignedRole.Name : "Unknown"
                    })
                    .ToListAsync();

                return Ok(users);
            }
            else
            {
                // Return users with specific role
                var roleEntity = await _db.Roles
                    .Where(r => r.Name == role && r.IsActive)
                    .FirstOrDefaultAsync();

                if (roleEntity == null)
                {
                    return NotFound(new { error = $"Role '{role}' not found" });
                }

                var users = await _db.Users
                    .Where(u => u.IsActive && u.RoleId == roleEntity.Id)
                    .OrderBy(u => u.Username)
                    .Select(u => new UserInfoResponse
                    {
                        Username = u.Username,
                        Email = u.Email,
                        FirstName = u.FirstName,
                        LastName = u.LastName,
                        RoleId = u.RoleId,
                        RoleName = u.AssignedRole != null ? u.AssignedRole.Name : "Unknown"
                    })
                    .ToListAsync();

                return Ok(users);
            }
        }

        [HttpGet("groups/{groupId}")]
        public async Task<ActionResult<GroupResponse>> GetGroup(string groupId)
        {
            if (!Guid.TryParse(groupId, out var gid)) return BadRequest("Invalid groupId");

            var group = await _db.AnalysisGroups.FirstOrDefaultAsync(g => g.Id == gid);
            if (group == null) return NotFound();

            var records = await _db.AnalysisRecords.Where(r => r.GroupId == gid).OrderBy(r => r.CreatedAtUtc).ToListAsync();

            var activeAssignment = await _db.AnalysisAssignments
                .Where(a => a.GroupId == gid && a.State == "Active" && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > DateTime.UtcNow))
                .OrderByDescending(a => a.UpdatedAtUtc ?? a.CreatedAtUtc)
                .FirstOrDefaultAsync();

            return Ok(new GroupResponse { Group = group, Records = records, ActiveAssignment = activeAssignment });
        }

        [HttpPost("groups/{groupId}/lease/renew")]
        [HasPermission(Permissions.ControllersImageAnalysisLeaseRenew)]
        public async Task<ActionResult> RenewLease(string groupId)
        {
            // Support both GUID and GroupIdentifier
            Guid? gid = null;
            string? groupIdentifier = null;

            if (Guid.TryParse(groupId, out var parsedGuid))
            {
                gid = parsedGuid;
            }
            else
            {
                groupIdentifier = groupId;
            }

            var user = User.Identity?.Name ?? "unknown";
            var settings = await _db.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();
            var leaseMinutes = Math.Max(1, settings.LeaseMinutes);

            AnalysisAssignment? active;

            if (gid.HasValue)
            {
                active = await _db.AnalysisAssignments
                    .Where(a => a.GroupId == gid.Value && a.State == "Active" && a.AssignedTo == user)
                    .OrderByDescending(a => a.UpdatedAtUtc ?? a.CreatedAtUtc)
                    .AsTracking()
                    .FirstOrDefaultAsync();
            }
            else
            {
                // Find group by GroupIdentifier first
                var group = await _db.AnalysisGroups
                    .Where(g => g.GroupIdentifier == groupIdentifier)
                    .FirstOrDefaultAsync();

                if (group == null) return NotFound(new { error = "Group not found" });

                active = await _db.AnalysisAssignments
                    .Where(a => a.GroupId == group.Id && a.State == "Active" && a.AssignedTo == user)
                    .OrderByDescending(a => a.UpdatedAtUtc ?? a.CreatedAtUtc)
                    .AsTracking()
                    .FirstOrDefaultAsync();
            }

            if (active == null) return NotFound(new { error = "No active assignment for user" });

            active.LeaseUntilUtc = DateTime.UtcNow.AddMinutes(leaseMinutes);
            active.UpdatedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync();
            await WriteAudit("ImageAnalysis", "LeaseRenew", $"Lease renewed for group {groupId}", nameof(AnalysisAssignment), groupId);
            return Ok(new { success = true, leaseUntilUtc = active.LeaseUntilUtc });
        }

        [HttpPost("groups/{groupId}/decision/analyst")]
        [HasPermission(Permissions.ControllersImageAnalysisDecisionAnalyst)]
        public async Task<ActionResult> SaveAnalystDecision(string groupId, [FromBody] AnalystDecisionRequest req)
        {
            if (!Guid.TryParse(groupId, out var gid)) return BadRequest("Invalid groupId");
            var group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.Id == gid);
            if (group == null) return NotFound(new { error = "Group not found" });

            var username = User.Identity?.Name ?? "system";

            // Save one ImageAnalysisDecision per record in group (simple mapping)
            var records = await _db.AnalysisRecords.Where(r => r.GroupId == gid).ToListAsync();
            var now = DateTime.UtcNow;

            foreach (var r in records)
            {
                var existing = await _db.ImageAnalysisDecisions.AsTracking().FirstOrDefaultAsync(d => d.ContainerNumber == r.ContainerNumber && d.ScannerType == (r.ScannerType ?? group.ScannerType ?? string.Empty));
                if (existing == null)
                {
                    existing = new ImageAnalysisDecision
                    {
                        ContainerNumber = r.ContainerNumber,
                        ScannerType = (r.ScannerType ?? group.ScannerType ?? string.Empty),
                        Decision = string.IsNullOrWhiteSpace(req.Decision) ? "Pending" : req.Decision,
                        ReviewedBy = username,
                        ReviewedAt = now,
                        CreatedAt = now,
                        GroupIdentifier = group.GroupIdentifier,
                        IsConsolidated = string.Equals(group.GroupType, "BL", StringComparison.OrdinalIgnoreCase)
                    };
                    _db.ImageAnalysisDecisions.Add(existing);
                }
                else
                {
                    existing.Decision = string.IsNullOrWhiteSpace(req.Decision) ? (existing.Decision ?? "Pending") : req.Decision;
                    existing.ReviewedBy = username;
                    existing.ReviewedAt = now;
                    existing.UpdatedAt = now;
                }

                if (req.Tags != null)
                {
                    try { existing.Tags = JsonSerializer.Serialize(req.Tags); } catch (Exception ex) { _logger.LogWarning(ex, "Failed to serialize tags for image analysis decision"); }
                }
                if (!string.IsNullOrWhiteSpace(req.Notes)) existing.Comments = req.Notes;
            }

            // Mark group state
            // Sprint 5G2 / B1: route through the state-machine facade. Replaces the prior
            // advisory IsValidTransition() check with mandatory enforcement. Ready/AnalystAssigned →
            // AnalystCompleted are in the legal table.
            await AnalysisGroupStateMachine.TransitionAsync(
                _db, group, AnalysisStatuses.AnalystCompleted,
                triggerName: "AnalystSubmittedFindingsBulk",
                actor: username,
                reason: $"Analyst saved bulk decision for group {groupId} (deprecated bulk endpoint).",
                correlationId: HttpContext?.TraceIdentifier);
            group.UpdatedAtUtc = now;

            // Centralized side effects for all containers in this group
            var sideEffects = new NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionSideEffectsService(_logger);
            await sideEffects.ApplyForGroupAsync(_db, group.GroupIdentifier, group.ScannerType);

            await WriteAudit("ImageAnalysis", "AnalystDecision", $"Analyst decision saved for group {groupId}", nameof(ImageAnalysisDecision), groupId);
            return Ok(new { success = true });
        }

        /// <remarks>
        /// DEPRECATED: The frontend uses /api/AuditReview/submit exclusively.
        /// This endpoint is retained for backwards compatibility but may be removed in a future release.
        /// Prefer /api/AuditReview/submit which has full auto-progression and per-container decision support.
        /// </remarks>
        [HttpPost("groups/{groupId}/decision/audit")]
        [HasPermission(Permissions.ControllersImageAnalysisDecisionAudit)]
        public async Task<ActionResult> SaveAuditDecision(string groupId, [FromBody] AuditDecisionRequest req)
        {
            _logger.LogWarning("[DEPRECATED] SaveAuditDecision called for group {GroupId} — prefer /api/AuditReview/submit", groupId);

            if (!Guid.TryParse(groupId, out var gid)) return BadRequest("Invalid groupId");
            var group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.Id == gid);
            if (group == null) return NotFound(new { error = "Group not found" });

            var username = User.Identity?.Name ?? "system";
            var now = DateTime.UtcNow;

            var records = await _db.AnalysisRecords.Where(r => r.GroupId == gid).ToListAsync();
            foreach (var r in records)
            {
                var iad = await _db.ImageAnalysisDecisions.FirstOrDefaultAsync(d => d.ContainerNumber == r.ContainerNumber && d.GroupIdentifier == group.GroupIdentifier);
                var aud = await _db.AuditDecisions.AsTracking().FirstOrDefaultAsync(a => a.ContainerNumber == r.ContainerNumber && a.GroupIdentifier == (group.GroupIdentifier ?? string.Empty));
                if (aud == null)
                {
                    aud = new AuditDecision
                    {
                        ContainerNumber = r.ContainerNumber,
                        GroupIdentifier = group.GroupIdentifier ?? string.Empty,
                        ScannerType = (r.ScannerType ?? group.ScannerType ?? string.Empty),
                        ImageAnalysisDecisionId = iad?.Id ?? 0,
                        Decision = req.Decision,
                        AuditNotes = req.Notes,
                        AuditedBy = username,
                        AuditedAt = now,
                        OverallGroupDecision = null,
                        IsCompleted = false,
                        CreatedAt = now
                    };
                    _db.AuditDecisions.Add(aud);
                }
                else
                {
                    aud.Decision = req.Decision;
                    aud.AuditNotes = req.Notes;
                    aud.AuditedBy = username;
                    aud.AuditedAt = now;
                    aud.UpdatedAt = now;
                }
            }

            // Sprint 5G2 / B1: route through the state-machine facade. Replaces the prior
            // advisory IsValidTransition() check with mandatory enforcement. AnalystCompleted/AuditAssigned →
            // AuditCompleted are in the legal table.
            await AnalysisGroupStateMachine.TransitionAsync(
                _db, group, AnalysisStatuses.AuditCompleted,
                triggerName: "AuditDecisionSavedBulk",
                actor: username,
                reason: $"Audit saved bulk decision={req.Decision} for group {groupId} (deprecated bulk endpoint).",
                correlationId: HttpContext?.TraceIdentifier);
            group.UpdatedAtUtc = now;

            var auditAssignments = await _db.AnalysisAssignments
                .Where(a => a.GroupId == gid
                    && a.Role == "Audit"
                    && a.State == "Active")
                .AsTracking()
                .ToListAsync();

            var auditorUsernames = auditAssignments.Select(a => a.AssignedTo).Distinct().ToList();

            foreach (var assignment in auditAssignments)
            {
                assignment.State = "Released";
                assignment.UpdatedAtUtc = now;
                _logger.LogInformation("Released Audit assignment {AssignmentId} for group {Group} - audit completed",
                    assignment.Id, groupId);
                _logger.LogInformation("[ASSIGNMENT-EVENT] Released | AssignmentId={AssignmentId} | GroupId={GroupId} | User={User} | Role={Role} | Reason=AuditCompleted",
                    assignment.Id, assignment.GroupId, assignment.AssignedTo, assignment.Role);
            }

            // FIX D: Update CCS WorkflowStage (was missing — could leave containers stuck in 'Audit')
            var normalizedId = GroupIdentifierHelper.GetNormalizedGroupIdentifier(group.GroupIdentifier) ?? group.GroupIdentifier;
            var ccsGroupIds = new[] { group.GroupIdentifier, normalizedId }
                .Where(x => !string.IsNullOrEmpty(x))
                .Select(x => x!)
                .Distinct()
                .ToList();
            foreach (var ccsGid in ccsGroupIds)
            {
                var ccsUpdated = await _db.Database.ExecuteSqlRawAsync(
                    "UPDATE ContainerCompletenessStatuses SET WorkflowStage = 'PendingSubmission', UpdatedAt = now() AT TIME ZONE 'UTC' WHERE GroupIdentifier = {0} AND WorkflowStage NOT IN ('PendingSubmission','Submitted')",
                    ccsGid);
                if (ccsUpdated > 0)
                    _logger.LogInformation("[SaveAuditDecision] Updated {Count} CCS record(s) to WorkflowStage=PendingSubmission for GroupIdentifier={GroupId}", ccsUpdated, ccsGid);
            }

            await _db.SaveChangesAsync();
            await WriteAudit("ImageAnalysis", "AuditDecision", $"Audit decision saved for group {groupId}", nameof(AuditDecision), groupId);

            // ✅ NEW: Trigger immediate assignment check for auditor if they completed all work
            if (auditorUsernames.Any())
            {
                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Triggering immediate assignment check for {Count} auditor(s): {Auditors}",
                    auditorUsernames.Count, string.Join(", ", auditorUsernames));

                // Use Task.Run to avoid blocking the response
                _ = Task.Run(async () =>
                {
                    try
                    {
                        using var scope = HttpContext.RequestServices.CreateScope();
                        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                        foreach (var auditorUsername in auditorUsernames)
                        {
                            await CheckAndTriggerImmediateAuditAssignmentAsync(db, _logger, auditorUsername);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in background immediate audit assignment trigger");
                    }
                });
            }

            return Ok(new { success = true });
        }

        /// <summary>
        /// Check if auditor has completed ALL their assignments and trigger immediate assignment if in Auto mode
        /// </summary>
        private async Task CheckAndTriggerImmediateAuditAssignmentAsync(ApplicationDbContext db, ILogger logger, string auditorUsername)
        {
            try
            {
                logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Checking immediate assignment eligibility for auditor {Username}", auditorUsername);
                var now = DateTime.UtcNow;

                // Check if auditor has any remaining active assignments
                var remainingActiveAssignments = await db.AnalysisAssignments
                    .Where(a => a.AssignedTo == auditorUsername
                        && a.Role == "Audit"
                        && a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .CountAsync();

                if (remainingActiveAssignments > 0)
                {
                    logger.LogInformation("⚠️ [IMMEDIATE-AUDIT-ASSIGNMENT] Auditor {Username} still has {Count} active assignments - not ready",
                        auditorUsername, remainingActiveAssignments);
                    return;
                }

                // Check if Auto mode is enabled
                var settings = await db.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();

                if (!settings.Enabled || string.IsNullOrEmpty(settings.AssignmentMode) || settings.AssignmentMode != "Auto")
                {
                    logger.LogDebug("[IMMEDIATE-AUDIT-ASSIGNMENT] Assignment mode is not Auto - skipping");
                    return;
                }

                // Double-check active count
                var activeCount = await db.AnalysisAssignments
                    .Where(a => a.AssignedTo == auditorUsername
                        && a.Role == "Audit"
                        && a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .CountAsync();

                if (activeCount >= settings.MaxConcurrentPerUser)
                {
                    return;
                }

                // ✅ FIX: Load groups and containers separately, then join in memory to avoid CTE generation
                var analystCompletedGroups = await db.AnalysisGroups
                    .Where(g => g.Status == AnalysisStatuses.AnalystCompleted && !string.IsNullOrEmpty(g.GroupIdentifier))
                    .AsTracking()
                    .ToListAsync();

                var groupIdentifiers = analystCompletedGroups.Select(g => g.GroupIdentifier).Distinct().ToList();

                // ✅ FIX: Batch Contains() to avoid CTE generation
                var containers = new List<ContainerCompletenessStatus>();
                const int containerBatchSize = 1000;

                if (groupIdentifiers.Count > 0)
                {
                    for (int i = 0; i < groupIdentifiers.Count; i += containerBatchSize)
                    {
                        var batch = groupIdentifiers.Skip(i).Take(containerBatchSize).Where(g => g != null).ToList();
                        var batchContainers = await db.ContainerCompletenessStatuses
                            .Where(c => c.GroupIdentifier != null && batch.Contains(c.GroupIdentifier))
                            .ToListAsync();
                        containers.AddRange(batchContainers);
                    }
                }

                // ✅ Join and group in memory
                var allAnalystCompletedGroups = analystCompletedGroups
                    .GroupJoin(
                        containers,
                        g => g.GroupIdentifier,
                        c => c.GroupIdentifier,
                        (g, containerGroup) => new
                        {
                            Group = g,
                            TotalContainers = containerGroup.Count(),
                            AuditContainers = containerGroup.Count(c => c.WorkflowStage == "Audit"),
                            CompletedContainers = containerGroup.Count(c => c.WorkflowStage == "PendingSubmission" || c.WorkflowStage == "Submitted" || c.WorkflowStage == "Completed")
                        })
                    .Where(w => w.AuditContainers > 0 && w.CompletedContainers < w.TotalContainers)
                    .Select(x => x.Group)
                    .OrderByDescending(g => g.Priority)
                    .ToList();

                // Get group IDs with active assignments
                var groupIdsWithActiveAssignments = await db.AnalysisAssignments
                    .Where(a => a.State == "Active" && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .Select(a => a.GroupId)
                    .Distinct()
                    .ToListAsync();

                // Filter and take up to max
                var readyGroups = allAnalystCompletedGroups
                    .Where(g => !groupIdsWithActiveAssignments.Contains(g.Id))
                    .Take(settings.MaxConcurrentPerUser - activeCount)
                    .ToList();

                if (!readyGroups.Any())
                {
                    return;
                }

                // Assign groups immediately
                var assignedCount = 0;
                foreach (var group in readyGroups)
                {
                    var leaseUntil = now.AddMinutes(Math.Max(1, settings.LeaseMinutes));
                    db.AnalysisAssignments.Add(new AnalysisAssignment
                    {
                        GroupId = group.Id,
                        AssignedTo = auditorUsername,
                        Role = "Audit",
                        LeaseUntilUtc = leaseUntil,
                        State = "Active",
                        CreatedAtUtc = now
                    });

                    // Sprint 5G2 / B1: route through the state-machine facade. AnalystCompleted → AuditAssigned
                    // is already in the legal table.
                    await AnalysisGroupStateMachine.TransitionAsync(
                        db, group, AnalysisStatuses.AuditAssigned,
                        triggerName: "ImmediateAuditAssignment",
                        actor: "SYSTEM-IMMEDIATE-AUDIT-ASSIGNMENT",
                        reason: $"Auto-assignment to auditor {auditorUsername} after they completed all prior assignments.",
                        correlationId: null);
                    group.UpdatedAtUtc = now;
                    assignedCount++;
                }

                await db.SaveChangesAsync();

                logger.LogInformation("✅ [IMMEDIATE-AUDIT-ASSIGNMENT] Immediately assigned {Count} new groups to auditor {Username}",
                    assignedCount, auditorUsername);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error checking/triggering immediate assignment for auditor {Username}", auditorUsername);
            }
        }

        [HttpPost("groups/{groupId}/submit-icums")]
        [HasPermission(Permissions.ControllersImageAnalysisSubmit)] // submission in test mode
        public async Task<ActionResult> SubmitIcumTest(string groupId)
        {
            if (!Guid.TryParse(groupId, out var gid)) return BadRequest("Invalid groupId");
            var group = await _db.AnalysisGroups.AsTracking().FirstOrDefaultAsync(g => g.Id == gid);
            if (group == null) return NotFound(new { error = "Group not found" });

            var analyst = await _db.ImageAnalysisDecisions.Where(d => d.GroupIdentifier == group.GroupIdentifier).ToListAsync();
            var audits = await _db.AuditDecisions.Where(a => a.GroupIdentifier == group.GroupIdentifier).ToListAsync();

            var idempotencyKey = $"{group.Id}-{analyst.OrderBy(a => a.Id).FirstOrDefault()?.Id}-{audits.OrderBy(a => a.Id).FirstOrDefault()?.Id}";
            var safeKey = _safeKeyRegex.Replace(group.GroupIdentifier ?? "group", "_");
            var now = DateTime.UtcNow;

            var payload = new
            {
                idempotencyKey,
                group = new { group.Id, group.GroupIdentifier, group.GroupType, group.ScannerType },
                analystDecisions = analyst.Select(a => new { a.ContainerNumber, a.ScannerType, a.Decision, a.Tags, a.Comments, a.ReviewedBy, a.ReviewedAt }),
                auditDecisions = audits.Select(a => new { a.ContainerNumber, a.ScannerType, a.Decision, a.AuditNotes, a.AuditedBy, a.AuditedAt })
            };

            var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });

            var settings = await _db.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();
            var outputFolder = _configuration["ICUMS:Submission:OutputFolder"]
                ?? Environment.GetEnvironmentVariable("ICUMS_Submission_OutputFolder")
                ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox";
            Directory.CreateDirectory(outputFolder);
            var fileName = $"{group.Id}_{safeKey}_{now:yyyyMMdd_HHmmssfff}_{idempotencyKey}.json";
            var fullPath = Path.Combine(outputFolder, fileName);
            await System.IO.File.WriteAllTextAsync(fullPath, json, Encoding.UTF8);

            var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(json)));

            var submission = new AnalysisSubmission
            {
                GroupId = group.Id,
                PayloadPath = fullPath,
                PayloadHash = hash,
                Status = "TestSaved",
                CreatedAtUtc = now,
                SubmittedAtUtc = now
            };
            _db.AnalysisSubmissions.Add(submission);

            // Sprint 5G2 / B1: route through the state-machine facade. AuditCompleted → Submitted
            // is in the legal table.
            await AnalysisGroupStateMachine.TransitionAsync(
                _db, group, AnalysisStatuses.Submitted,
                triggerName: "SubmitICUMS_Test",
                actor: User?.Identity?.Name ?? "anonymous",
                reason: $"Test ICUMS payload saved for group {groupId}.",
                correlationId: HttpContext?.TraceIdentifier);
            group.UpdatedAtUtc = now;

            await WriteAudit("ImageAnalysis", "SubmitICUMS_Test", $"Test payload saved for group {groupId}", nameof(AnalysisSubmission), groupId);
            return Ok(new { success = true, path = fullPath });
        }

        [HttpPost("intake/complete-records")]
        [HasPermission(Permissions.ControllersImageAnalysisIntake)]
        public async Task<ActionResult> IntakeCompleteRecords([FromBody] IntakeRequest req)
        {
            // Idempotent upsert: create AnalysisGroup per GroupIdentifier, and AnalysisRecord per container
            var identifiers = req.GroupIdentifiers?.Where(s => !string.IsNullOrWhiteSpace(s)).Distinct().ToList() ?? new List<string>();
            if (!identifiers.Any()) return BadRequest(new { error = "No group identifiers provided" });

            var now = DateTime.UtcNow;
            // ✅ FIX: Batch Contains() to avoid EF Core CTE generation with large lists
            var existingGroups = new List<AnalysisGroup>();
            const int identifierBatchSize2 = 1000;

            if (identifiers.Count > 0)
            {
                for (int i = 0; i < identifiers.Count; i += identifierBatchSize2)
                {
                    var batch = identifiers.Skip(i).Take(identifierBatchSize2).ToList();
                    var batchGroups = await _db.AnalysisGroups
                        .Where(g => batch.Contains(g.GroupIdentifier))
                        .ToListAsync();
                    existingGroups.AddRange(batchGroups);
                }
            }

            var existingByKey = existingGroups.ToDictionary(g => g.GroupIdentifier ?? string.Empty, g => g);

            foreach (var id in identifiers)
            {
                if (!existingByKey.TryGetValue(id, out var group))
                {
                    // Sprint 5G2 / B1 lock-the-door: redundant Status="Ready" removed (default value).
                    group = new AnalysisGroup
                    {
                        GroupIdentifier = id,
                        GroupType = req.GroupType,
                        ScannerType = req.ScannerType,
                        CreatedAtUtc = now
                    };
                    _db.AnalysisGroups.Add(group);
                    await _db.SaveChangesAsync(); // ensure Id for FK below
                    existingByKey[id] = group;
                }

                // Pull containers for identifier from Completeness
                var containers = await _db.ContainerCompletenessStatuses
                    .Where(s => s.GroupIdentifier == id)
                    .Select(s => new { s.ContainerNumber, s.ScannerType })
                    .Distinct()
                    .ToListAsync();

                // existing records in group
                var currentRecords = await _db.AnalysisRecords.Where(r => r.GroupId == group.Id).ToListAsync();
                var currentSet = currentRecords.Select(r => r.ContainerNumber).ToHashSet(StringComparer.OrdinalIgnoreCase);

                foreach (var c in containers)
                {
                    if (currentSet.Contains(c.ContainerNumber)) continue;
                    _db.AnalysisRecords.Add(new AnalysisRecord
                    {
                        GroupId = group.Id,
                        ContainerNumber = c.ContainerNumber,
                        ScannerType = c.ScannerType,
                        Status = "Ready",
                        CreatedAtUtc = now
                    });
                }
            }

            await _db.SaveChangesAsync();
            await WriteAudit("ImageAnalysis", "Intake", $"Intake completed for {identifiers.Count} group(s)", nameof(AnalysisGroup), string.Join(",", identifiers));
            return Ok(new { success = true });
        }

        private async Task WriteAudit(string eventType, string action, string description, string entityType, string? entityId)
        {
            try
            {
                var log = new AuditLog
                {
                    Timestamp = DateTime.UtcNow,
                    Username = User?.Identity?.Name,
                    EventType = eventType,
                    Action = action,
                    Description = description,
                    EntityType = entityType,
                    EntityId = entityId,
                    Severity = "Info",
                    Success = true
                };
                _db.AuditLogs.Add(log);
                await _db.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                // Log audit event failure - don't fail the main operation
                _logger.LogWarning(ex, "Failed to log audit event for image analysis decision");
            }
        }

        /// <summary>
        /// Get enhanced image for container (Phase 1: Image Enhancement)
        /// Applies automatic enhancement: brightness, contrast, noise reduction, sharpening, histogram equalization
        /// </summary>
        [HttpGet("{containerNumber}/enhanced")]
        [ProducesResponseType(200, Type = typeof(FileContentResult))]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetEnhancedImage(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Getting enhanced image for container: {ContainerNumber}", containerNumber);

                var enhancedImageBytes = await _imageAnalysis.GetEnhancedImageAsync(containerNumber);

                if (enhancedImageBytes == null || enhancedImageBytes.Length == 0)
                {
                    _logger.LogWarning("No enhanced image available for container: {ContainerNumber}", containerNumber);
                    return NotFound(new { error = "Enhanced image not available for this container" });
                }

                _logger.LogInformation("Successfully retrieved enhanced image for container: {ContainerNumber}, Size: {Size} bytes",
                    containerNumber, enhancedImageBytes.Length);

                return File(enhancedImageBytes, "image/jpeg", $"{containerNumber}_enhanced.jpg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting enhanced image for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, new { error = "Failed to process enhanced image", message = ex.Message });
            }
        }

        /// <summary>
        /// Get OCR results for container (Phase 2: OCR Integration)
        /// Extracts container number from image and validates against expected
        /// </summary>
        [HttpGet("{containerNumber}/ocr")]
        [ProducesResponseType(200, Type = typeof(OcrResult))]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<OcrResult>> GetOcrResult(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Getting OCR results for container: {ContainerNumber}", containerNumber);

                var ocrResult = await _imageAnalysis.ExtractContainerNumberAsync(containerNumber);

                if (!ocrResult.Success)
                {
                    // ✅ FIX: Check if it's a missing tessdata file error - return 503 (Service Unavailable) instead of 500
                    var isMissingTessdata = ocrResult.ErrorMessage?.Contains("Tesseract language data file") == true;
                    var statusCode = isMissingTessdata ? 503 : 500;

                    _logger.LogWarning("OCR processing failed for container: {ContainerNumber}, Error: {Error}",
                        containerNumber, ocrResult.ErrorMessage);
                    return StatusCode(statusCode, ocrResult);
                }

                _logger.LogInformation("OCR completed for container: {ContainerNumber}. Detected: {Detected}, Matches: {Matches}",
                    containerNumber, ocrResult.ContainerNumber ?? "None", ocrResult.MatchesExpected);

                return Ok(ocrResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting OCR results for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, new { error = "Failed to process OCR", message = ex.Message });
            }
        }

        /// <summary>
        /// Get object detection results for container (Phase 3: Object Detection)
        /// Detects containers, vehicles, and anomalies in scan images
        /// </summary>
        [HttpGet("{containerNumber}/detect")]
        [ProducesResponseType(200, Type = typeof(ObjectDetectionResult))]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<ObjectDetectionResult>> GetObjectDetection(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Getting object detection results for container: {ContainerNumber}", containerNumber);

                var detectionResult = await _imageAnalysis.DetectObjectsAsync(containerNumber);

                if (!detectionResult.Success)
                {
                    _logger.LogWarning("Object detection failed for container: {ContainerNumber}, Error: {Error}",
                        containerNumber, detectionResult.ErrorMessage);
                    return StatusCode(500, detectionResult);
                }

                _logger.LogInformation("Object detection completed for container: {ContainerNumber}. Containers: {Containers}, Vehicles: {Vehicles}, Anomalies: {Anomalies}",
                    containerNumber, detectionResult.ContainerRegions.Count, detectionResult.VehicleRegions.Count, detectionResult.Anomalies.Count);

                return Ok(detectionResult);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting object detection results for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, new { error = "Failed to process object detection", message = ex.Message });
            }
        }

        /// <summary>
        /// Get image quality assessment (Phase 4: Quality Assessment)
        /// Calculates sharpness, brightness, contrast, noise and provides recommendations
        /// </summary>
        [HttpGet("{containerNumber}/quality")]
        [ProducesResponseType(200, Type = typeof(QualityAssessment))]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<QualityAssessment>> GetQualityAssessment(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Getting quality assessment for container: {ContainerNumber}", containerNumber);

                var assessment = await _imageAnalysis.AssessQualityAsync(containerNumber);

                if (!assessment.Success)
                {
                    _logger.LogWarning("Quality assessment failed for container: {ContainerNumber}, Error: {Error}",
                        containerNumber, assessment.ErrorMessage);
                    return StatusCode(500, assessment);
                }

                _logger.LogInformation("Quality assessment completed for container: {ContainerNumber}. Overall: {Score:F2}, Acceptable: {Acceptable}",
                    containerNumber, assessment.OverallScore, assessment.IsAcceptable);

                return Ok(assessment);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting quality assessment for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, new { error = "Failed to assess image quality", message = ex.Message });
            }
        }

        /// <summary>
        /// Enhance annotation region (Phase 5: Enhanced Annotation Tools)
        /// Enhances a specific region of the image for better analysis
        /// </summary>
        [HttpPost("{containerNumber}/annotations/enhance")]
        [ProducesResponseType(200, Type = typeof(FileContentResult))]
        [ProducesResponseType(400)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> EnhanceAnnotationRegion(
            string containerNumber,
            [FromQuery] int x,
            [FromQuery] int y,
            [FromQuery] int width,
            [FromQuery] int height)
        {
            try
            {
                _logger.LogInformation("Enhancing annotation region for container: {ContainerNumber}, Region: ({X}, {Y}, {Width}, {Height})",
                    containerNumber, x, y, width, height);

                if (width <= 0 || height <= 0)
                {
                    return BadRequest(new { error = "Invalid region dimensions" });
                }

                // Get original image
                var base64Image = await _imageAnalysis.GetImageAsBase64Async(containerNumber);
                if (string.IsNullOrEmpty(base64Image))
                {
                    return NotFound(new { error = "Image not found" });
                }

                // Strip data URI prefix if present
                var base64Data = base64Image;
                if (base64Image.StartsWith("data:image/", StringComparison.OrdinalIgnoreCase))
                {
                    var commaIndex = base64Image.IndexOf(',');
                    if (commaIndex >= 0)
                    {
                        base64Data = base64Image.Substring(commaIndex + 1);
                    }
                }

                // ✅ MEMORY FIX: Use ArrayPool for base64 conversion to reduce LOH pressure
                byte[] imageBytes;
                var estimatedSize = (base64Data.Length * 3) / 4;
                var rentedArray = System.Buffers.ArrayPool<byte>.Shared.Rent(estimatedSize);
                try
                {
                    if (!Convert.TryFromBase64String(base64Data, rentedArray, out var bytesWritten))
                    {
                        imageBytes = Convert.FromBase64String(base64Data);
                    }
                    else
                    {
                        imageBytes = new byte[bytesWritten];
                        Array.Copy(rentedArray, imageBytes, bytesWritten);
                    }
                }
                finally
                {
                    System.Buffers.ArrayPool<byte>.Shared.Return(rentedArray, clearArray: true);
                }

                // Load image with ImageSharp
                using var image = await SixLabors.ImageSharp.Image.LoadAsync(new MemoryStream(imageBytes));

                // Crop to annotation region
                var cropRect = new SixLabors.ImageSharp.Rectangle(x, y, width, height);

                // Ensure crop rectangle is within image bounds
                cropRect = SixLabors.ImageSharp.Rectangle.Intersect(
                    cropRect,
                    new SixLabors.ImageSharp.Rectangle(0, 0, image.Width, image.Height));

                if (cropRect.Width <= 0 || cropRect.Height <= 0)
                {
                    return BadRequest(new { error = "Region is outside image bounds" });
                }

                image.Mutate(ctx => ctx.Crop(cropRect));

                // Apply enhancement to region
                image.Mutate(ctx => ctx
                    .Brightness(1.3f)
                    .Contrast(1.2f)
                    .GaussianSharpen(0.8f)
                );

                // Save enhanced region
                using var outputStream = new MemoryStream();
                await image.SaveAsJpegAsync(outputStream, new SixLabors.ImageSharp.Formats.Jpeg.JpegEncoder { Quality = 95 });

                _logger.LogInformation("Successfully enhanced annotation region for container: {ContainerNumber}", containerNumber);

                return File(outputStream.ToArray(), "image/jpeg", $"{containerNumber}_region_{x}_{y}.jpg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enhancing annotation region for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, new { error = "Failed to enhance region", message = ex.Message });
            }
        }

        /// <summary>
        /// ✅ PHASE 3: Get assignment metrics for monitoring and diagnostics
        /// </summary>
        [HttpGet("metrics")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<AssignmentMetricsResponse>> GetAssignmentMetrics()
        {
            try
            {
                var now = DateTime.UtcNow;

                // Get all assignments grouped by state
                var assignmentsByState = await _db.AnalysisAssignments
                    .GroupBy(a => a.State)
                    .Select(g => new { State = g.Key, Count = g.Count() })
                    .ToListAsync();

                var totalAssignments = assignmentsByState.Sum(x => x.Count);
                var activeCount = assignmentsByState.FirstOrDefault(x => x.State == "Active")?.Count ?? 0;
                var expiredCount = assignmentsByState.FirstOrDefault(x => x.State == "Expired")?.Count ?? 0;
                var releasedCount = assignmentsByState.FirstOrDefault(x => x.State == "Released")?.Count ?? 0;

                // Get active assignments with details
                var activeAssignments = await _db.AnalysisAssignments
                    .Where(a => a.State == "Active")
                    .ToListAsync();

                // Count by role
                var analystCount = activeAssignments.Count(a => a.Role == "Analyst");
                var auditCount = activeAssignments.Count(a => a.Role == "Audit");

                // Count expiring soon (within 5 minutes)
                var expiringSoon = activeAssignments.Count(a =>
                    a.LeaseUntilUtc.HasValue &&
                    a.LeaseUntilUtc.Value <= now.AddMinutes(5) &&
                    a.LeaseUntilUtc.Value > now);

                // Count expired but not reclaimed (lease expired but still Active)
                var expiredButActive = activeAssignments.Count(a =>
                    a.LeaseUntilUtc.HasValue &&
                    a.LeaseUntilUtc.Value < now);

                // Count with recent access (within 30 minutes)
                var recentlyAccessed = activeAssignments.Count(a =>
                    a.LastAccessedAtUtc.HasValue &&
                    a.LastAccessedAtUtc.Value >= now.AddMinutes(-30));

                // Count by user
                var assignmentsByUser = activeAssignments
                    .GroupBy(a => a.AssignedTo)
                    .Select(g => new { User = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(20)
                    .ToDictionary(x => x.User, x => x.Count);

                // Get assignment creation stats (last 24 hours)
                var last24Hours = now.AddHours(-24);
                var assignmentsCreated24h = await _db.AnalysisAssignments
                    .CountAsync(a => a.CreatedAtUtc >= last24Hours);

                // Get expired assignments (last 24 hours)
                var assignmentsExpired24h = await _db.AnalysisAssignments
                    .CountAsync(a => a.State == "Expired" &&
                                    a.UpdatedAtUtc.HasValue &&
                                    a.UpdatedAtUtc.Value >= last24Hours);

                // Get groups by status for context
                var readyGroups = await _db.AnalysisGroups.CountAsync(g => g.Status == AnalysisStatuses.Ready);
                var analystAssignedGroups = await _db.AnalysisGroups.CountAsync(g => g.Status == AnalysisStatuses.AnalystAssigned);
                var auditAssignedGroups = await _db.AnalysisGroups.CountAsync(g => g.Status == AnalysisStatuses.AuditAssigned);

                var response = new AssignmentMetricsResponse
                {
                    Timestamp = now,
                    TotalAssignments = totalAssignments,
                    ActiveAssignments = activeCount,
                    ExpiredAssignments = expiredCount,
                    ReleasedAssignments = releasedCount,
                    ByRole = new Dictionary<string, int>
                    {
                        { "Analyst", analystCount },
                        { "Audit", auditCount }
                    },
                    ExpiringSoon = expiringSoon,
                    ExpiredButActive = expiredButActive,
                    RecentlyAccessed = recentlyAccessed,
                    ByUser = assignmentsByUser,
                    CreatedLast24Hours = assignmentsCreated24h,
                    ExpiredLast24Hours = assignmentsExpired24h,
                    GroupsReady = readyGroups,
                    GroupsAnalystAssigned = analystAssignedGroups,
                    GroupsAuditAssigned = auditAssignedGroups
                };

                _logger.LogInformation("[METRICS] Assignment metrics requested by {User}", User.Identity?.Name ?? "Unknown");
                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[METRICS] Error retrieving assignment metrics");
                return StatusCode(500, new { error = "Failed to retrieve assignment metrics", message = ex.Message });
            }
        }

        // ✅ PHASE 3: DTO for assignment metrics
        public sealed class AssignmentMetricsResponse
        {
            public DateTime Timestamp { get; set; }
            public int TotalAssignments { get; set; }
            public int ActiveAssignments { get; set; }
            public int ExpiredAssignments { get; set; }
            public int ReleasedAssignments { get; set; }
            public Dictionary<string, int> ByRole { get; set; } = new();
            public int ExpiringSoon { get; set; }
            public int ExpiredButActive { get; set; }
            public int RecentlyAccessed { get; set; }
            public Dictionary<string, int> ByUser { get; set; } = new();
            public int CreatedLast24Hours { get; set; }
            public int ExpiredLast24Hours { get; set; }
            public int GroupsReady { get; set; }
            public int GroupsAnalystAssigned { get; set; }
            public int GroupsAuditAssigned { get; set; }
        }

        // GET /api/image-analysis/group-by-identifier?identifier=X&scannerType=Y
        [HttpGet("group-by-identifier")]
        public async Task<ActionResult> GetGroupByIdentifier([FromQuery] string identifier, [FromQuery] string? scannerType = null)
        {
            var query = _db.AnalysisGroups.AsNoTracking()
                .Where(g => g.GroupIdentifier == identifier);
            if (!string.IsNullOrEmpty(scannerType))
                query = query.Where(g => g.ScannerType == scannerType);

            var group = await query.FirstOrDefaultAsync();
            if (group == null)
                return Ok(new { groupId = (Guid?)null });

            return Ok(new { groupId = (Guid?)group.Id });
        }

        // GET /api/image-analysis/wave-context/{groupId}
        [HttpGet("wave-context/{groupId:guid}")]
        public async Task<ActionResult<WaveContextResponse>> GetWaveContext(Guid groupId)
        {
            var group = await _db.AnalysisGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.Id == groupId);

            if (group == null)
                return NotFound(new { error = "Group not found" });

            if (group.ParentGroupId == null)
                return Ok(new WaveContextResponse { IsWave = false });

            var parentGroup = await _db.AnalysisParentGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(p => p.Id == group.ParentGroupId);

            if (parentGroup == null)
                return Ok(new WaveContextResponse { IsWave = false });

            var totalWaves = await _db.AnalysisGroups
                .CountAsync(g => g.ParentGroupId == parentGroup.Id);

            // Count decided containers across all waves for this parent
            var allWaveGroupIds = await _db.AnalysisGroups
                .Where(g => g.ParentGroupId == parentGroup.Id)
                .Select(g => g.Id)
                .ToListAsync();

            var decidedContainers = await _db.AnalysisRecords
                .CountAsync(r => allWaveGroupIds.Contains(r.GroupId) && r.Status != "Ready");

            var totalDecidedAndReady = await _db.AnalysisRecords
                .CountAsync(r => allWaveGroupIds.Contains(r.GroupId));

            var pendingContainers = await _db.WavePendingContainers
                .CountAsync(w => w.ParentGroupId == parentGroup.Id && w.Status == "Pending");

            return Ok(new WaveContextResponse
            {
                IsWave = true,
                ParentGroupIdentifier = parentGroup.GroupIdentifier,
                WaveNumber = group.WaveNumber ?? 1,
                TotalWaves = totalWaves,
                TotalContainers = parentGroup.TotalExpectedContainers,
                DecidedContainers = decidedContainers,
                ContainersInWaves = totalDecidedAndReady,
                PendingContainers = pendingContainers,
                ParentStatus = parentGroup.Status
            });
        }

        public sealed class WaveContextResponse
        {
            public bool IsWave { get; set; }
            public string? ParentGroupIdentifier { get; set; }
            public int WaveNumber { get; set; }
            public int TotalWaves { get; set; }
            public int TotalContainers { get; set; }
            public int DecidedContainers { get; set; }
            public int ContainersInWaves { get; set; }
            public int PendingContainers { get; set; }
            public string? ParentStatus { get; set; }
        }
    }
}
