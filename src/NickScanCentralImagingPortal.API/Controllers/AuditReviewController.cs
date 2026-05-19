using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageAnalysis;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class AuditReviewController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<AuditReviewController> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly ReadyGroupsCacheService? _readyGroupsCache;

        public AuditReviewController(
            ApplicationDbContext dbContext,
            ILogger<AuditReviewController> logger,
            IMemoryCache memoryCache,
            ReadyGroupsCacheService? readyGroupsCache = null)
        {
            _dbContext = dbContext;
            _logger = logger;
            _memoryCache = memoryCache;
            _readyGroupsCache = readyGroupsCache;
        }

        private string GetAuthenticatedUsername()
        {
            return User?.Identity?.Name
                ?? User?.FindFirst(ClaimTypes.Name)?.Value
                ?? User?.FindFirst("username")?.Value
                ?? string.Empty;
        }

        private bool HasPermissionClaim(params string[] permissions)
        {
            var permissionSet = new HashSet<string>(permissions, StringComparer.OrdinalIgnoreCase);
            return User?.Claims.Any(c =>
                string.Equals(c.Type, "Permission", StringComparison.OrdinalIgnoreCase) &&
                permissionSet.Contains(c.Value)) == true;
        }

        private bool HasElevatedAuditOverride()
        {
            return User?.Claims.Any(c =>
                    string.Equals(c.Type, "platform_admin", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Value, "true", StringComparison.OrdinalIgnoreCase)) == true
                || User?.IsInRole("SuperAdmin") == true
                || User?.IsInRole("Admin") == true
                || User?.IsInRole("Manager") == true
                || User?.IsInRole("Supervisor") == true
                || User?.IsInRole("Lead") == true
                || HasPermissionClaim(
                    Permissions.ControllersImageAnalysisAssign,
                    Permissions.ControllersImageAnalysisManagementSettings);
        }

        private static bool IsCompletedAnalystDecisionValue(string? decision)
        {
            return string.Equals(decision, "Normal", StringComparison.OrdinalIgnoreCase)
                || string.Equals(decision, "Abnormal", StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasCompletedAnalystDecision(ImageAnalysisDecision decision)
        {
            return IsCompletedAnalystDecisionValue(decision.Decision);
        }

        private static ImageAnalysisDecision? SelectImageDecisionForDisplay(
            IEnumerable<ImageAnalysisDecision> decisions,
            AnalysisRecord? analysisRecord = null)
        {
            var candidates = decisions;
            if (analysisRecord != null)
            {
                candidates = candidates.Where(d => SplitDecisionEligibility.IsDecisionCompatible(analysisRecord, d));
            }

            return candidates
                .OrderByDescending(d => !string.IsNullOrWhiteSpace(d.ReviewedBy))
                .ThenByDescending(d => d.ReviewedAt)
                .ThenByDescending(d => d.UpdatedAt ?? d.CreatedAt)
                .FirstOrDefault();
        }

        private static string GetImageAnalystDisplay(IEnumerable<ImageAnalysisDecision> decisions)
        {
            var analysts = decisions
                .Select(d => d.ReviewedBy?.Trim())
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            return analysts.Count == 0 ? "Unknown" : string.Join(", ", analysts);
        }

        private static string? BaseScannerType(string? scannerType)
        {
            if (string.IsNullOrWhiteSpace(scannerType))
                return null;

            var trimmed = scannerType.Trim();
            var dashIndex = trimmed.IndexOf('-');
            return dashIndex > 0 ? trimmed.Substring(0, dashIndex) : trimmed;
        }

        private static bool ScannerMatches(string? candidate, string? requested)
        {
            if (string.IsNullOrWhiteSpace(requested))
                return true;

            if (string.IsNullOrWhiteSpace(candidate))
                return true;

            var candidateTrimmed = candidate.Trim();
            var requestedTrimmed = requested.Trim();
            var requestedBase = BaseScannerType(requestedTrimmed);

            return string.Equals(candidateTrimmed, requestedTrimmed, StringComparison.OrdinalIgnoreCase)
                || (!string.IsNullOrWhiteSpace(requestedBase) &&
                    (string.Equals(candidateTrimmed, requestedBase, StringComparison.OrdinalIgnoreCase)
                     || candidateTrimmed.StartsWith(requestedBase + "-", StringComparison.OrdinalIgnoreCase)))
                || requestedTrimmed.StartsWith(candidateTrimmed + "-", StringComparison.OrdinalIgnoreCase);
        }

        private static List<string> BuildAuditDecisionGroupIdentifiers(params string?[] groupIdentifiers)
        {
            var identifiers = new List<string>();

            foreach (var groupIdentifier in groupIdentifiers)
            {
                if (string.IsNullOrWhiteSpace(groupIdentifier))
                    continue;

                identifiers.Add(groupIdentifier);

                var normalized = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier);
                if (!string.IsNullOrWhiteSpace(normalized))
                    identifiers.Add(normalized);
            }

            return identifiers
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool AnalystDecisionGroupMatches(string? decisionGroupIdentifier, IReadOnlyCollection<string> auditGroupIdentifiers)
        {
            if (auditGroupIdentifiers.Count == 0)
                return true;

            if (string.IsNullOrWhiteSpace(decisionGroupIdentifier))
                return false;

            if (auditGroupIdentifiers.Contains(decisionGroupIdentifier, StringComparer.OrdinalIgnoreCase))
                return true;

            var normalizedDecisionGroup = GroupIdentifierHelper.GetNormalizedGroupIdentifier(decisionGroupIdentifier);
            if (!string.IsNullOrWhiteSpace(normalizedDecisionGroup)
                && auditGroupIdentifiers.Contains(normalizedDecisionGroup, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            return auditGroupIdentifiers.Any(g =>
                !string.IsNullOrWhiteSpace(g)
                && decisionGroupIdentifier.StartsWith(g + "_", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsAuditableAnalystDecision(
            ImageAnalysisDecision decision,
            IReadOnlyCollection<string> auditGroupIdentifiers,
            string? scannerType,
            AnalysisRecord? analysisRecord = null)
        {
            return HasCompletedAnalystDecision(decision)
                && ScannerMatches(decision.ScannerType, scannerType)
                && AnalystDecisionGroupMatches(decision.GroupIdentifier, auditGroupIdentifiers)
                && (analysisRecord == null || SplitDecisionEligibility.IsDecisionCompatible(analysisRecord, decision));
        }

        private async Task<ImageAnalysisDecision?> FindCompletedAnalystDecisionForAuditAsync(
            string containerNumber,
            IReadOnlyCollection<string> auditGroupIdentifiers,
            string? scannerType,
            AnalysisRecord? analysisRecord = null)
        {
            var completedCandidates = await _dbContext.ImageAnalysisDecisions
                .Where(d => d.ContainerNumber == containerNumber
                    && (d.Decision == "Normal" || d.Decision == "Abnormal"))
                .ToListAsync();

            var scoped = completedCandidates
                .Where(d => IsAuditableAnalystDecision(d, auditGroupIdentifiers, scannerType, analysisRecord))
                .OrderByDescending(d => d.UpdatedAt ?? d.ReviewedAt)
                .ThenByDescending(d => d.Id)
                .FirstOrDefault();

            if (scoped != null)
                return scoped;

            var legacyUngrouped = completedCandidates
                .Where(d => string.IsNullOrWhiteSpace(d.GroupIdentifier)
                    && ScannerMatches(d.ScannerType, scannerType)
                    && (analysisRecord == null || SplitDecisionEligibility.IsDecisionCompatible(analysisRecord, d)))
                .OrderByDescending(d => d.UpdatedAt ?? d.ReviewedAt)
                .ThenByDescending(d => d.Id)
                .ToList();

            return legacyUngrouped.Count == 1 ? legacyUngrouped[0] : null;
        }

        private async Task<(List<AnalysisGroup> Groups, List<AnalysisRecord> Records)> LoadAuditAnalysisContextAsync(
            IEnumerable<ContainerCompletenessStatus> completenessRecords,
            string? preferredGroupIdentifier = null,
            string? scannerType = null)
        {
            var groupIdentifiers = completenessRecords
                .Select(r => r.GroupIdentifier)
                .Append(preferredGroupIdentifier)
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .SelectMany(g => new[] { g!, GroupIdentifierHelper.GetNormalizedGroupIdentifier(g!) })
                .Where(g => !string.IsNullOrWhiteSpace(g))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (groupIdentifiers.Count == 0)
                return (new List<AnalysisGroup>(), new List<AnalysisRecord>());

            var groups = await _dbContext.AnalysisGroups
                .AsNoTracking()
                .Where(g => groupIdentifiers.Contains(g.GroupIdentifier)
                    || (g.NormalizedGroupIdentifier != null && groupIdentifiers.Contains(g.NormalizedGroupIdentifier)))
                .ToListAsync();

            if (!string.IsNullOrWhiteSpace(scannerType))
            {
                groups = groups
                    .Where(g => ScannerMatches(g.ScannerType, scannerType))
                    .ToList();
            }

            if (groups.Count == 0)
                return (groups, new List<AnalysisRecord>());

            var groupIds = groups.Select(g => g.Id).Distinct().ToList();
            var records = await _dbContext.AnalysisRecords
                .AsNoTracking()
                .Where(r => groupIds.Contains(r.GroupId))
                .ToListAsync();

            return (groups, records);
        }

        private static AnalysisRecord? FindAnalysisRecordForAudit(
            ContainerCompletenessStatus completeness,
            IReadOnlyList<AnalysisGroup> groups,
            IReadOnlyList<AnalysisRecord> records)
        {
            var matchingGroups = groups
                .Where(g => string.Equals(g.GroupIdentifier, completeness.GroupIdentifier, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(g.NormalizedGroupIdentifier, completeness.GroupIdentifier, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(g => ScannerMatches(g.ScannerType, completeness.ScannerType))
                .ThenByDescending(g => !string.IsNullOrWhiteSpace(g.ScannerType))
                .ThenByDescending(g => g.UpdatedAtUtc)
                .ToList();

            foreach (var group in matchingGroups)
            {
                var record = records
                    .Where(r => r.GroupId == group.Id
                        && string.Equals(r.ContainerNumber, completeness.ContainerNumber, StringComparison.OrdinalIgnoreCase)
                        && ScannerMatches(r.ScannerType, completeness.ScannerType))
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .FirstOrDefault();

                if (record != null)
                    return record;
            }

            return null;
        }

        private static AnalysisRecord? FindAnalysisRecordForAudit(
            string containerNumber,
            string? scannerType,
            IReadOnlyList<AnalysisRecord> records)
        {
            return records
                .Where(r => string.Equals(r.ContainerNumber, containerNumber, StringComparison.OrdinalIgnoreCase)
                    && ScannerMatches(r.ScannerType, scannerType))
                .OrderByDescending(r => r.CreatedAtUtc)
                .FirstOrDefault();
        }

        private static ImageAnalysisDecision? SelectAuditDisplayDecision(
            ContainerCompletenessStatus completeness,
            IEnumerable<ImageAnalysisDecision> decisions,
            AnalysisRecord? analysisRecord)
        {
            var groupIdentifiers = BuildAuditDecisionGroupIdentifiers(completeness.GroupIdentifier);
            var candidates = decisions.Where(d =>
                string.Equals(d.ContainerNumber, completeness.ContainerNumber, StringComparison.OrdinalIgnoreCase)
                && ScannerMatches(d.ScannerType, completeness.ScannerType)
                && AnalystDecisionGroupMatches(d.GroupIdentifier, groupIdentifiers));

            return SelectImageDecisionForDisplay(candidates, analysisRecord);
        }

        /// <summary>
        /// Get records ready for audit (using WorkflowStage = 'Audit')
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-empty fallback.
        [HttpGet("ready")]
        public async Task<ActionResult<ApiResponse<List<AuditGroupDto>>>> GetRecordsReadyForAudit([FromQuery] string? scannerType = null)
        {
            try
            {
                // ✅ Use WorkflowStage approach - get IDs using raw SQL since column exists in DB but not entity
                var auditRecordIds = new List<int>();

                var connection = _dbContext.Database.GetDbConnection();
                var wasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!wasOpen) await connection.OpenAsync();

                try
                {
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = scannerType != null
                            ? "SELECT Id FROM ContainerCompletenessStatuses WHERE WorkflowStage = @p0 AND ScannerType = @p1"
                            : "SELECT Id FROM ContainerCompletenessStatuses WHERE WorkflowStage = @p0";

                        var param0 = command.CreateParameter();
                        param0.ParameterName = "@p0";
                        param0.Value = "Audit";
                        command.Parameters.Add(param0);

                        if (scannerType != null)
                        {
                            var param1 = command.CreateParameter();
                            param1.ParameterName = "@p1";
                            param1.Value = scannerType;
                            command.Parameters.Add(param1);
                        }

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                auditRecordIds.Add(reader.GetInt32(0));
                            }
                        }
                    }
                }
                finally
                {
                    if (!wasOpen) await connection.CloseAsync();
                }

                // Load full records using IDs
                // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid EF Core CTE generation (SQL Server 2014 compatibility)
                var auditRecords = new List<ContainerCompletenessStatus>();
                if (auditRecordIds.Any())
                {
                    const int idBatchSize = 100;
                    for (int i = 0; i < auditRecordIds.Count; i += idBatchSize)
                    {
                        var idBatch = auditRecordIds.Skip(i).Take(idBatchSize).ToList();

                        // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid CTE generation
                        // Build parameterized IN clause
                        var parameters = new List<object>();
                        var parameterPlaceholders = new List<string>();

                        for (int j = 0; j < idBatch.Count; j++)
                        {
                            parameterPlaceholders.Add($"{{{j}}}");
                            parameters.Add(idBatch[j]);
                        }

                        var inClause = string.Join(",", parameterPlaceholders);
                        // ✅ SQL Server 2014 FIX: Semicolon prefix required before WITH clause
                        var sql = $";SELECT * FROM ContainerCompletenessStatuses WHERE Id IN ({inClause})";

                        var batchRecords = await _dbContext.ContainerCompletenessStatuses
                            .FromSqlRaw(sql, parameters.ToArray())
                            .AsNoTracking()
                            .ToListAsync();
                        auditRecords.AddRange(batchRecords);
                    }
                }

                // ✅ PERFORMANCE FIX: Batch load all ImageAnalysisDecisions at once to avoid N+1 queries
                // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid EF Core CTE generation (SQL Server 2014 compatibility)
                // ✅ FIX: Process scannerTypes separately to avoid multiple Contains() in same query
                var containerNumbers = auditRecords.Select(r => r.ContainerNumber).Distinct().ToList();
                var scannerTypes = auditRecords.Select(r => r.ScannerType).Distinct().ToList();

                var allDecisionsList = new List<ImageAnalysisDecision>();
                const int batchSize = 10; // Very small batch size to avoid CTE generation

                if (containerNumbers.Any() && scannerTypes.Any())
                {
                    // Process each scanner type separately to avoid multiple Contains() in same query
                    foreach (var currentScannerType in scannerTypes)
                    {
                        for (int i = 0; i < containerNumbers.Count; i += batchSize)
                        {
                            var containerBatch = containerNumbers.Skip(i).Take(batchSize).ToList();

                            // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid CTE generation
                            // Build parameterized IN clause
                            var parameters = new List<object>();
                            var parameterPlaceholders = new List<string>();

                            for (int j = 0; j < containerBatch.Count; j++)
                            {
                                parameterPlaceholders.Add($"{{{j}}}");
                                parameters.Add(containerBatch[j]);
                            }

                            var inClause = string.Join(",", parameterPlaceholders);
                            // ✅ SQL Server 2014 FIX: Semicolon prefix required before WITH clause
                            var sql = $";SELECT * FROM ImageAnalysisDecisions WHERE ContainerNumber IN ({inClause}) AND ScannerType = {{{containerBatch.Count}}}";
                            parameters.Add(currentScannerType);

                            var batchDecisions = await _dbContext.ImageAnalysisDecisions
                                .FromSqlRaw(sql, parameters.ToArray())
                                .AsNoTracking()
                                .ToListAsync();
                            allDecisionsList.AddRange(batchDecisions);
                        }
                    }
                }

                var (analysisGroups, analysisRecords) = await LoadAuditAnalysisContextAsync(auditRecords, scannerType: scannerType);

                // Group by GroupIdentifier
                var groups = auditRecords
                    .GroupBy(s => new { s.GroupIdentifier, s.ScannerType, s.IsConsolidated })
                    .Select(g => new AuditGroupDto
                    {
                        GroupIdentifier = g.Key.GroupIdentifier ?? "",
                        ScannerType = g.Key.ScannerType,
                        IsConsolidated = g.Key.IsConsolidated,
                        TotalContainers = g.Count(),
                        SubmittedAt = g.Max(s => s.UpdatedAt > s.CreatedAt ? s.UpdatedAt : s.CreatedAt),
                        SubmittedBy = "System", // Can be enhanced later
                        ImageAnalysisDecisions = g.Select(s =>
                        {
                            var analysisRecord = FindAnalysisRecordForAudit(s, analysisGroups, analysisRecords);
                            var decision = SelectAuditDisplayDecision(s, allDecisionsList, analysisRecord);

                            return new ImageAnalysisDecisionSummary
                            {
                                ContainerNumber = s.ContainerNumber,
                                ImageAnalysisDecisionId = decision?.Id,
                                Decision = decision?.Decision ?? "Pending",
                                ReviewedBy = decision?.ReviewedBy ?? "",
                                ReviewedAt = decision?.ReviewedAt ?? DateTime.UtcNow,
                                Comments = decision?.Comments,
                                Tags = decision?.Tags, // 2026-05-05 operator-reported tag persistence: was silently dropped by projection; renders chips on audit view
                                SplitJobId = decision?.SplitJobId,
                                SplitResultId = decision?.SplitResultId
                            };
                        }).ToList()
                    })
                    .ToList();

                return Ok(new ApiResponse<List<AuditGroupDto>>
                {
                    Success = true,
                    Data = groups
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting records ready for audit");
                // ✅ FIX: Return empty response instead of 500 to prevent frontend errors
                return Ok(new ApiResponse<List<AuditGroupDto>>
                {
                    Success = false,
                    Data = new List<AuditGroupDto>(),
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get a specific audit group by GroupIdentifier (for assigned groups)
        /// </summary>
        [HttpGet("group/{groupIdentifier}")]
        [Authorize]
        public async Task<ActionResult<ApiResponse<AuditGroupDto>>> GetAuditGroup(string groupIdentifier, [FromQuery] string? scannerType = null)
        {
            try
            {
                // Get ContainerCompletenessStatus records for this group
                // ✅ Fallback: try normalized GroupIdentifier when request has date-suffix (e.g. 41025661190_20250101_20250131)
                // AnalysisGroups use date-suffixed IDs; ContainerCompletenessStatuses preserve original
                var completenessRecords = await _dbContext.ContainerCompletenessStatuses
                    .Where(s => s.GroupIdentifier == groupIdentifier)
                    .Where(s => scannerType == null || s.ScannerType == scannerType)
                    .ToListAsync();

                if (!completenessRecords.Any())
                {
                    var normalized = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier);
                    if (normalized != null && normalized != groupIdentifier)
                    {
                        completenessRecords = await _dbContext.ContainerCompletenessStatuses
                            .Where(s => s.GroupIdentifier == normalized)
                            .Where(s => scannerType == null || s.ScannerType == scannerType)
                            .ToListAsync();
                    }
                }

                // B2′-C (2026-05-09): third hop of the parallel-state-surface drift.
                // Wave-child AGs (and any AG whose CCS rows pre-date the BL-grouping era)
                // have CCS.GroupIdentifier set to the container number rather than the
                // AG's BL/declaration GroupIdentifier. Both filters above miss them,
                // and the audit page returns "No records found" even after a successful
                // AuditAssigned transition. The authoritative chain is
                // AnalysisGroup.GroupIdentifier → AnalysisRecord.GroupId → AnalysisRecord.ContainerNumber → CCS.ContainerNumber.
                // Query through that chain when the CCS.GroupIdentifier filters fail.
                // Sprint 5G2 / B1 made AG.Status state-machine-authoritative; this third
                // fallback extends the same principle to the audit-review fetch path.
                if (!completenessRecords.Any())
                {
                    var ag = await _dbContext.AnalysisGroups
                        .AsNoTracking()
                        .Where(g => g.GroupIdentifier == groupIdentifier
                                 || g.NormalizedGroupIdentifier == groupIdentifier)
                        .Where(g => scannerType == null || g.ScannerType == scannerType)
                        .OrderByDescending(g => g.UpdatedAtUtc)
                        .Select(g => new { g.Id, g.GroupIdentifier })
                        .FirstOrDefaultAsync();

                    if (ag != null)
                    {
                        completenessRecords = await (
                            from r in _dbContext.AnalysisRecords.AsNoTracking()
                            join c in _dbContext.ContainerCompletenessStatuses.AsNoTracking()
                              on r.ContainerNumber equals c.ContainerNumber
                            where r.GroupId == ag.Id
                               && (scannerType == null || c.ScannerType == scannerType)
                            select c
                        ).Distinct().ToListAsync();

                        if (completenessRecords.Any())
                        {
                            _logger.LogInformation(
                                "[AUDIT-REVIEW] Resolved {Count} CCS row(s) for {GroupIdentifier} via AG.Id→AnalysisRecord join (CCS.GroupIdentifier was stale/container-number)",
                                completenessRecords.Count, groupIdentifier);
                        }
                    }
                }

                if (!completenessRecords.Any())
                {
                    return Ok(new ApiResponse<AuditGroupDto>
                    {
                        Success = false,
                        Data = null,
                        Message = $"No records found for group: {groupIdentifier}"
                    });
                }

                // Get ImageAnalysisDecisions for these containers
                // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid EF Core CTE generation (SQL Server 2014 compatibility)
                // ✅ FIX: Process scannerTypes separately to avoid multiple Contains() in same query
                var containerNumbers = completenessRecords.Select(r => r.ContainerNumber).Distinct().ToList();
                var scannerTypes = completenessRecords.Select(r => r.ScannerType).Distinct().ToList();

                var allDecisionsList = new List<ImageAnalysisDecision>();
                const int batchSize = 10; // Very small batch size to avoid CTE generation with complex OR conditions

                if (containerNumbers.Any() && scannerTypes.Any())
                {
                    // Process each scanner type separately to avoid multiple Contains() in same query
                    foreach (var currentScannerType in scannerTypes)
                    {
                        for (int i = 0; i < containerNumbers.Count; i += batchSize)
                        {
                            var containerBatch = containerNumbers.Skip(i).Take(batchSize).ToList();

                            // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid CTE generation
                            // Build parameterized IN clause
                            var parameters = new List<object>();
                            var parameterPlaceholders = new List<string>();

                            for (int j = 0; j < containerBatch.Count; j++)
                            {
                                parameterPlaceholders.Add($"{{{j}}}");
                                parameters.Add(containerBatch[j]);
                            }

                            var inClause = string.Join(",", parameterPlaceholders);
                            // ✅ SQL Server 2014 FIX: Semicolon prefix required before WITH clause
                            // Parameterize groupIdentifier for SQL safety
                            var scannerTypeParamIndex = containerBatch.Count;
                            string sql;
                            string groupIdentifierCondition;

                            if (string.IsNullOrEmpty(groupIdentifier))
                            {
                                // No groupIdentifier filter - match null or empty
                                groupIdentifierCondition = "(GroupIdentifier IS NULL OR GroupIdentifier = '')";
                                sql = $";SELECT * FROM ImageAnalysisDecisions WHERE ContainerNumber IN ({inClause}) AND ScannerType = {{{scannerTypeParamIndex}}} AND {groupIdentifierCondition}";
                                parameters.Add(currentScannerType);
                            }
                            else
                            {
                                // Filter by specific groupIdentifier; include normalized ID when date-suffixed
                                var normalized = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier);
                                var groupIdentifierParamIndex = containerBatch.Count + 1;
                                if (normalized != null && normalized != groupIdentifier)
                                {
                                    // Date-suffix format: also match normalized (e.g. 41025661190)
                                    var normalizedParamIndex = containerBatch.Count + 2;
                                    groupIdentifierCondition = $"(GroupIdentifier = {{{groupIdentifierParamIndex}}} OR GroupIdentifier = {{{normalizedParamIndex}}} OR GroupIdentifier IS NULL OR GroupIdentifier = '')";
                                    parameters.Add(currentScannerType);
                                    parameters.Add(groupIdentifier);
                                    parameters.Add(normalized);
                                }
                                else
                                {
                                    groupIdentifierCondition = $"(GroupIdentifier = {{{groupIdentifierParamIndex}}} OR GroupIdentifier IS NULL OR GroupIdentifier = '')";
                                    parameters.Add(currentScannerType);
                                    parameters.Add(groupIdentifier);
                                }
                                sql = $";SELECT * FROM ImageAnalysisDecisions WHERE ContainerNumber IN ({inClause}) AND ScannerType = {{{scannerTypeParamIndex}}} AND {groupIdentifierCondition}";
                            }

                            var batchDecisions = await _dbContext.ImageAnalysisDecisions
                                .FromSqlRaw(sql, parameters.ToArray())
                                .AsNoTracking()
                                .ToListAsync();
                            allDecisionsList.AddRange(batchDecisions);
                        }
                    }
                }

                // Fallback: match date-suffixed GroupIdentifiers in a single batch query
                if (!string.IsNullOrEmpty(groupIdentifier) && containerNumbers.Any() && scannerTypes.Any())
                {
                    var prefix = groupIdentifier + "_";
                    var auditGroupIdentifiersForFallback = BuildAuditDecisionGroupIdentifiers(groupIdentifier);
                    var alreadyMatched = allDecisionsList
                        .Where(d => AnalystDecisionGroupMatches(d.GroupIdentifier, auditGroupIdentifiersForFallback))
                        .Select(d => $"{d.ContainerNumber}|{d.ScannerType}")
                        .ToHashSet(StringComparer.OrdinalIgnoreCase);
                    var unmatchedContainers = containerNumbers.Where(cn => scannerTypes.Any(st => !alreadyMatched.Contains($"{cn}|{st}"))).ToList();

                    if (unmatchedContainers.Any())
                    {
                        var fallbackResults = await _dbContext.ImageAnalysisDecisions
                            .AsNoTracking()
                            .Where(d => unmatchedContainers.Contains(d.ContainerNumber) &&
                                        scannerTypes.Contains(d.ScannerType) &&
                                        d.GroupIdentifier != null && d.GroupIdentifier.StartsWith(prefix))
                            .ToListAsync();

                        foreach (var fb in fallbackResults)
                        {
                            var key = $"{fb.ContainerNumber}|{fb.ScannerType}";
                            if (!alreadyMatched.Contains(key))
                            {
                                allDecisionsList.Add(fb);
                                alreadyMatched.Add(key);
                            }
                        }
                    }
                }

                var (analysisGroups, analysisRecords) = await LoadAuditAnalysisContextAsync(
                    completenessRecords,
                    groupIdentifier,
                    scannerType);

                // Build AuditGroupDto
                var firstRecord = completenessRecords.First();
                var auditGroup = new AuditGroupDto
                {
                    GroupIdentifier = groupIdentifier,
                    ScannerType = firstRecord.ScannerType,
                    IsConsolidated = firstRecord.IsConsolidated,
                    TotalContainers = completenessRecords.Count,
                    SubmittedAt = completenessRecords.Max(s => s.UpdatedAt > s.CreatedAt ? s.UpdatedAt : s.CreatedAt),
                    SubmittedBy = "System",
                    ImageAnalysisDecisions = completenessRecords.Select(s =>
                    {
                        var analysisRecord = FindAnalysisRecordForAudit(s, analysisGroups, analysisRecords);
                        var decision = SelectAuditDisplayDecision(s, allDecisionsList, analysisRecord);

                        return new ImageAnalysisDecisionSummary
                        {
                            ContainerNumber = s.ContainerNumber,
                            ImageAnalysisDecisionId = decision?.Id,
                            Decision = decision?.Decision ?? "Pending",
                            ReviewedBy = decision?.ReviewedBy ?? "",
                            ReviewedAt = decision?.ReviewedAt ?? DateTime.UtcNow,
                            Comments = decision?.Comments,
                            Tags = decision?.Tags, // 2026-05-05 operator-reported tag persistence: was silently dropped by projection; renders chips on audit view
                            SplitJobId = decision?.SplitJobId,
                            SplitResultId = decision?.SplitResultId
                        };
                    }).ToList()
                };

                return Ok(new ApiResponse<AuditGroupDto>
                {
                    Success = true,
                    Data = auditGroup
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audit group: {GroupIdentifier}", groupIdentifier);
                return Ok(new ApiResponse<AuditGroupDto>
                {
                    Success = false,
                    Data = null,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get audit statistics
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-zero fallback.
        [HttpGet("stats")]
        public async Task<ActionResult<ApiResponse<AuditStats>>> GetStats()
        {
            try
            {
                var connection = _dbContext.Database.GetDbConnection();
                var wasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!wasOpen) await connection.OpenAsync();

                int readyForAudit = 0;
                int completed = 0;
                int approved = 0;
                int rejected = 0;

                try
                {
                    // Get ready for audit count
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM ContainerCompletenessStatuses WHERE WorkflowStage = @p0";
                        var param0 = command.CreateParameter();
                        param0.ParameterName = "@p0";
                        param0.Value = "Audit";
                        command.Parameters.Add(param0);

                        var result = await command.ExecuteScalarAsync();
                        readyForAudit = result != null ? Convert.ToInt32(result) : 0;
                    }

                    // Get completed count
                    using (var command = connection.CreateCommand())
                    {
                        command.CommandText = "SELECT COUNT(*) FROM ContainerCompletenessStatuses WHERE WorkflowStage = @p0";
                        var param0 = command.CreateParameter();
                        param0.ParameterName = "@p0";
                        param0.Value = "Completed";
                        command.Parameters.Add(param0);

                        var result = await command.ExecuteScalarAsync();
                        completed = result != null ? Convert.ToInt32(result) : 0;
                    }

                    // Get approved/rejected counts from AuditDecisions
                    try
                    {
                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT COUNT(*) FROM AuditDecisions WHERE Decision = @p0";
                            var param0 = command.CreateParameter();
                            param0.ParameterName = "@p0";
                            param0.Value = "Approved";
                            command.Parameters.Add(param0);

                            var result = await command.ExecuteScalarAsync();
                            approved = result != null ? Convert.ToInt32(result) : 0;
                        }

                        using (var command = connection.CreateCommand())
                        {
                            command.CommandText = "SELECT COUNT(*) FROM AuditDecisions WHERE Decision = @p0";
                            var param0 = command.CreateParameter();
                            param0.ParameterName = "@p0";
                            param0.Value = "Rejected";
                            command.Parameters.Add(param0);

                            var result = await command.ExecuteScalarAsync();
                            rejected = result != null ? Convert.ToInt32(result) : 0;
                        }
                    }
                    catch (Exception ex)
                    {
                        // AuditDecisions table may not exist yet - use 0
                        _logger.LogWarning(ex, "Failed to get audit decision counts (table may not exist yet)");
                        approved = 0;
                        rejected = 0;
                    }
                }
                finally
                {
                    if (!wasOpen) await connection.CloseAsync();
                }

                var approvalRate = (approved + rejected) > 0
                    ? (int)Math.Round((approved * 100.0) / (approved + rejected))
                    : 0;

                return Ok(new ApiResponse<AuditStats>
                {
                    Success = true,
                    Data = new AuditStats
                    {
                        ReadyForAudit = readyForAudit,
                        Completed = completed,
                        Approved = approved,
                        Rejected = rejected,
                        ApprovalRate = approvalRate
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting audit stats");
                // ✅ FIX: Return default stats instead of 500 to prevent frontend errors
                return Ok(new ApiResponse<AuditStats>
                {
                    Success = false,
                    Data = new AuditStats
                    {
                        ReadyForAudit = 0,
                        Completed = 0,
                        Approved = 0,
                        Rejected = 0,
                        ApprovalRate = 0
                    },
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Get auto-auditor status
        /// </summary>
        [HttpGet("auto-auditor/status")]
        public async Task<ActionResult<AutoAuditorStatusResponse>> GetAutoAuditorStatus()
        {
            try
            {
                var setting = await _dbContext.SystemSettings
                    .AsTracking()
                    .FirstOrDefaultAsync(s => s.SettingKey == "AutoAuditorEnabled");

                var enabled = setting != null && setting.SettingValue?.ToLower() == "true";

                return Ok(new AutoAuditorStatusResponse
                {
                    Success = true,
                    Enabled = enabled
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting auto-auditor status");
                return Ok(new AutoAuditorStatusResponse
                {
                    Success = false,
                    Enabled = false
                });
            }
        }

        /// <summary>
        /// Toggle auto-auditor
        /// </summary>
        [HttpPost("auto-auditor/toggle")]
        public async Task<ActionResult<AutoAuditorToggleResponse>> ToggleAutoAuditor([FromBody] ToggleRequest request)
        {
            try
            {
                var setting = await _dbContext.SystemSettings
                    .AsTracking()
                    .FirstOrDefaultAsync(s => s.SettingKey == "AutoAuditorEnabled");

                if (setting == null)
                {
                    setting = new SystemSetting
                    {
                        Category = "Audit",
                        SettingKey = "AutoAuditorEnabled",
                        SettingValue = request.Enabled.ToString(),
                        LastModifiedBy = request.ModifiedBy,
                        LastModifiedAt = DateTime.UtcNow
                    };
                    _dbContext.SystemSettings.Add(setting);
                }
                else
                {
                    setting.SettingValue = request.Enabled.ToString();
                    setting.LastModifiedBy = request.ModifiedBy;
                    setting.LastModifiedAt = DateTime.UtcNow;
                }

                await _dbContext.SaveChangesAsync();

                return Ok(new AutoAuditorToggleResponse
                {
                    Success = true,
                    Enabled = request.Enabled,
                    Message = $"Auto Auditor {(request.Enabled ? "enabled" : "disabled")} successfully"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error toggling auto-auditor");
                return StatusCode(500, new AutoAuditorToggleResponse
                {
                    Success = false,
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        /// <summary>
        /// Submit audit decisions for a group
        /// </summary>
        [HttpPost("submit")]
        [HasPermission(Permissions.ControllersImageAnalysisDecisionAudit)]
        public async Task<ActionResult<AuditSubmissionResponse>> SubmitAudit([FromBody] AuditSubmissionRequest request)
        {
            try
            {
                if (string.IsNullOrEmpty(request.GroupIdentifier))
                {
                    return BadRequest(new AuditSubmissionResponse
                    {
                        Success = false,
                        Message = "GroupIdentifier is required"
                    });
                }

                if (request.ContainerDecisions == null || !request.ContainerDecisions.Any())
                {
                    return BadRequest(new AuditSubmissionResponse
                    {
                        Success = false,
                        Message = "At least one container decision is required"
                    });
                }

                var username = GetAuthenticatedUsername();
                if (string.IsNullOrWhiteSpace(username))
                {
                    return Unauthorized(new AuditSubmissionResponse
                    {
                        Success = false,
                        Message = "Authenticated username is required to submit an audit.",
                        OverallDecision = null,
                        AuditedCount = 0,
                        NextContainerNumber = null,
                        AllContainersAudited = false
                    });
                }

                var now = DateTime.UtcNow;
                var auditedCount = 0;
                var hasRejected = false;

                _logger.LogInformation("Submitting audit for group {GroupIdentifier} by {User} with {Count} decisions (containers: {Containers})",
                    request.GroupIdentifier, username, request.ContainerDecisions.Count,
                    string.Join(", ", request.ContainerDecisions.Select(c => c.ContainerNumber)));

                var group = await _dbContext.AnalysisGroups
                    .AsTracking()
                    .FirstOrDefaultAsync(g => g.GroupIdentifier == request.GroupIdentifier);

                if (group == null)
                {
                    var normalized = GroupIdentifierHelper.GetNormalizedGroupIdentifier(request.GroupIdentifier);
                    if (normalized != null && normalized != request.GroupIdentifier)
                    {
                        group = await _dbContext.AnalysisGroups
                            .AsTracking()
                            .FirstOrDefaultAsync(g => g.GroupIdentifier == normalized);
                    }
                }

                // ✅ FALLBACK: AnalysisGroup may have date-suffixed GroupIdentifier (e.g. 70825542327_20250101_20250131) while
                // request/CCS use base (70825542327). Search for groups where base matches.
                if (group == null && !string.IsNullOrEmpty(request.GroupIdentifier))
                {
                    var candidates = await _dbContext.AnalysisGroups
                        .AsTracking()
                        .Where(g => g.GroupIdentifier == request.GroupIdentifier ||
                                    (g.GroupIdentifier != null && g.GroupIdentifier.StartsWith(request.GroupIdentifier + "_")))
                        .ToListAsync();
                    group = candidates.FirstOrDefault(g =>
                        g.GroupIdentifier == request.GroupIdentifier ||
                        GroupIdentifierHelper.GetNormalizedGroupIdentifier(g.GroupIdentifier) == request.GroupIdentifier);
                    if (group != null)
                        _logger.LogInformation("AnalysisGroup resolved via date-suffix fallback for request {Request}: found {Found}", request.GroupIdentifier, group.GroupIdentifier);
                }

                // ✅ FALLBACK: Resolve via AnalysisRecord (e.g. when GroupIdentifier is container number for consolidated cargo)
                if (group == null && request.ContainerDecisions.Any())
                {
                    var firstContainer = request.ContainerDecisions.First().ContainerNumber;
                    var analysisRecord = await _dbContext.AnalysisRecords
                        .FirstOrDefaultAsync(r => r.ContainerNumber == firstContainer);
                    if (analysisRecord != null)
                    {
                        group = await _dbContext.AnalysisGroups
                            .AsTracking()
                            .FirstOrDefaultAsync(g => g.Id == analysisRecord.GroupId);
                        if (group != null)
                            _logger.LogInformation("AnalysisGroup resolved via AnalysisRecord for container {Container}: GroupId={GroupId}", firstContainer, group.Id);
                    }
                }

                if (group == null)
                {
                    _logger.LogWarning("Analysis group not found: {GroupIdentifier}", request.GroupIdentifier);
                    return NotFound(new AuditSubmissionResponse
                    {
                        Success = false,
                        Message = $"Analysis group not found: {request.GroupIdentifier}"
                    });
                }

                var guardNormalizedGroupIdentifier = GroupIdentifierHelper.GetNormalizedGroupIdentifier(request.GroupIdentifier);
                var resolvedGroupIdentifier = group.GroupIdentifier;
                var matchingAssignmentGroupIds = await _dbContext.AnalysisGroups
                    .AsNoTracking()
                    .Where(g => g.Id == group.Id
                        || (!string.IsNullOrEmpty(request.GroupIdentifier) && g.GroupIdentifier == request.GroupIdentifier)
                        || (!string.IsNullOrEmpty(resolvedGroupIdentifier) && g.GroupIdentifier == resolvedGroupIdentifier)
                        || (!string.IsNullOrEmpty(guardNormalizedGroupIdentifier) && g.GroupIdentifier == guardNormalizedGroupIdentifier)
                        || (!string.IsNullOrEmpty(request.GroupIdentifier)
                            && g.GroupIdentifier != null
                            && g.GroupIdentifier.StartsWith(request.GroupIdentifier + "_")))
                    .Select(g => g.Id)
                    .Distinct()
                    .ToListAsync();

                if (!matchingAssignmentGroupIds.Contains(group.Id))
                {
                    matchingAssignmentGroupIds.Add(group.Id);
                }

                var activeAuditAssignments = await _dbContext.AnalysisAssignments
                    .AsNoTracking()
                    .Where(a => matchingAssignmentGroupIds.Contains(a.GroupId)
                        && a.Role == "Audit"
                        && a.State == "Active")
                    .ToListAsync();

                var ownsActiveAuditAssignment = activeAuditAssignments.Any(a =>
                    string.Equals(a.AssignedTo, username, StringComparison.OrdinalIgnoreCase));
                var hasElevatedOverride = HasElevatedAuditOverride();

                if (!ownsActiveAuditAssignment && !hasElevatedOverride)
                {
                    var assignmentState = activeAuditAssignments.Any()
                        ? "an active audit assignment exists for another user"
                        : "no active audit assignment exists";

                    _logger.LogWarning(
                        "SubmitAudit denied for {User} on group {GroupIdentifier}: {AssignmentState}",
                        username, request.GroupIdentifier, assignmentState);

                    return StatusCode(403, new AuditSubmissionResponse
                    {
                        Success = false,
                        Message = $"Audit submission denied: {assignmentState}.",
                        OverallDecision = null,
                        AuditedCount = 0,
                        NextContainerNumber = null,
                        AllContainersAudited = false
                    });
                }

                // ✅ FIX: Fallback when group.ScannerType is null (legacy groups) - use request, then completeness, then decision
                var scannerType = group.ScannerType ?? request.ScannerType ?? "";
                if (string.IsNullOrEmpty(scannerType) && request.ContainerDecisions.Any())
                {
                    var firstContainer = request.ContainerDecisions.First().ContainerNumber;
                    var completeness = await _dbContext.ContainerCompletenessStatuses
                        .Where(c => c.ContainerNumber == firstContainer && !string.IsNullOrEmpty(c.ScannerType))
                        .Select(c => c.ScannerType)
                        .FirstOrDefaultAsync();
                    if (!string.IsNullOrEmpty(completeness))
                    {
                        scannerType = completeness;
                        _logger.LogInformation("ScannerType derived from ContainerCompletenessStatus for container {Container}: {ScannerType}", firstContainer, scannerType);
                    }
                    else
                    {
                        var decisionScanner = await _dbContext.ImageAnalysisDecisions
                            .Where(d => d.ContainerNumber == firstContainer)
                            .Select(d => d.ScannerType)
                            .FirstOrDefaultAsync();
                        if (!string.IsNullOrEmpty(decisionScanner))
                        {
                            scannerType = decisionScanner;
                            _logger.LogInformation("ScannerType derived from ImageAnalysisDecision for container {Container}: {ScannerType}", firstContainer, scannerType);
                        }
                    }
                }

                _logger.LogInformation("SubmitAudit: GroupId={GroupId}, GroupIdentifier={GroupIdentifier}, ScannerType={ScannerType}",
                    group.Id, group.GroupIdentifier, string.IsNullOrEmpty(scannerType) ? "(empty)" : scannerType);

                var auditDecisionGroupIdentifiers = BuildAuditDecisionGroupIdentifiers(
                    request.GroupIdentifier,
                    guardNormalizedGroupIdentifier,
                    resolvedGroupIdentifier,
                    group.NormalizedGroupIdentifier);

                var requestedContainerNumbers = request.ContainerDecisions
                    .Select(c => c.ContainerNumber)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var auditAnalysisRecords = await _dbContext.AnalysisRecords
                    .AsNoTracking()
                    .Where(r => r.GroupId == group.Id)
                    .ToListAsync();

                var completedAnalystDecisionsByContainer = new Dictionary<string, ImageAnalysisDecision>(StringComparer.OrdinalIgnoreCase);
                string? missingCompletedAnalystDecision = null;
                string? unresolvedSplitContainer = null;
                foreach (var container in requestedContainerNumbers)
                {
                    var analysisRecord = FindAnalysisRecordForAudit(container, scannerType, auditAnalysisRecords);
                    if (SplitDecisionEligibility.RequiresSplitResolution(analysisRecord))
                    {
                        unresolvedSplitContainer = container;
                        break;
                    }

                    var completedDecision = await FindCompletedAnalystDecisionForAuditAsync(
                        container,
                        auditDecisionGroupIdentifiers,
                        scannerType,
                        analysisRecord);

                    if (completedDecision == null)
                    {
                        missingCompletedAnalystDecision = container;
                        break;
                    }

                    completedAnalystDecisionsByContainer[container] = completedDecision;
                }

                if (unresolvedSplitContainer != null)
                {
                    _logger.LogWarning(
                        "SubmitAudit rejected before writes: split choice unresolved for container {Container} in group {GroupIdentifier}",
                        unresolvedSplitContainer, request.GroupIdentifier);

                    return BadRequest(new AuditSubmissionResponse
                    {
                        Success = false,
                        Message = $"Split choice is still required for container {unresolvedSplitContainer}. Return it to image analysis and choose or skip the split before submitting audit.",
                        OverallDecision = null,
                        AuditedCount = 0,
                        NextContainerNumber = null,
                        AllContainersAudited = false
                    });
                }

                if (missingCompletedAnalystDecision != null)
                {
                    _logger.LogWarning(
                        "SubmitAudit rejected before writes: missing completed ImageAnalysisDecision for container {Container} in group {GroupIdentifier}",
                        missingCompletedAnalystDecision, request.GroupIdentifier);

                    return BadRequest(new AuditSubmissionResponse
                    {
                        Success = false,
                        Message = $"Missing analyst decision for container {missingCompletedAnalystDecision}. Complete image analysis before submitting audit.",
                        OverallDecision = null,
                        AuditedCount = 0,
                        NextContainerNumber = null,
                        AllContainersAudited = false
                    });
                }

                // Process each container decision
                foreach (var containerDecision in request.ContainerDecisions)
                {
                    completedAnalystDecisionsByContainer.TryGetValue(containerDecision.ContainerNumber, out var imageDecision);
                    var analysisRecord = FindAnalysisRecordForAudit(
                        containerDecision.ContainerNumber,
                        scannerType,
                        auditAnalysisRecords);

                    // Audit must be based on a real analyst decision. Do not synthesize
                    // placeholder "Pending" rows here; that hides incomplete analysis.
                    if (imageDecision == null
                        || !IsAuditableAnalystDecision(imageDecision, auditDecisionGroupIdentifiers, scannerType, analysisRecord))
                    {
                        _logger.LogWarning("SubmitAudit rejected: missing completed ImageAnalysisDecision for container {Container} in group {GroupIdentifier} (ScannerType={ScannerType}, Decision={Decision})",
                            containerDecision.ContainerNumber, request.GroupIdentifier, scannerType, imageDecision?.Decision ?? "(none)");

                        return BadRequest(new AuditSubmissionResponse
                        {
                            Success = false,
                            Message = $"Missing analyst decision for container {containerDecision.ContainerNumber}. Complete image analysis before submitting audit.",
                            OverallDecision = null,
                            AuditedCount = auditedCount,
                            NextContainerNumber = null,
                            AllContainersAudited = false
                        });
                    }

                    // Normalize decision value (handle both "APPROVED"/"REJECTED" and "Approved"/"Rejected")
                    var decision = containerDecision.Decision?.Trim();
                    if (string.IsNullOrEmpty(decision))
                    {
                        _logger.LogWarning("Empty decision for container {Container}", containerDecision.ContainerNumber);
                        continue;
                    }

                    // Normalize to "Approved" or "Rejected"
                    if (decision.Equals("APPROVED", StringComparison.OrdinalIgnoreCase))
                        decision = "Approved";
                    else if (decision.Equals("REJECTED", StringComparison.OrdinalIgnoreCase))
                        decision = "Rejected";
                    else if (!decision.Equals("Approved", StringComparison.OrdinalIgnoreCase) &&
                             !decision.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogWarning("Invalid decision value '{Decision}' for container {Container}. Expected 'Approved' or 'Rejected'",
                            decision, containerDecision.ContainerNumber);
                        continue;
                    }

                    // ─── Per-image audit rollup (deferred plan request 1) ───
                    // When ImageDecisions are supplied, derive the parent rollup:
                    // any Rejected child forces parent Rejected. Otherwise honour
                    // the explicit Decision the caller sent. This keeps the legacy
                    // single-row API working unchanged while letting the new UI
                    // submit per-image granularity.
                    if (containerDecision.ImageDecisions != null && containerDecision.ImageDecisions.Any())
                    {
                        var anyChildRejected = containerDecision.ImageDecisions.Any(i =>
                            !string.IsNullOrEmpty(i.Decision) &&
                            i.Decision.Equals("Rejected", StringComparison.OrdinalIgnoreCase));
                        if (anyChildRejected)
                        {
                            decision = "Rejected";
                        }
                    }

                    if (decision == "Rejected")
                        hasRejected = true;

                    // Use group.GroupIdentifier for storage so SubmissionWorker (queries by AnalysisGroup.GroupIdentifier) finds them
                    var storageGroupId = group.GroupIdentifier ?? request.GroupIdentifier;

                    // Use raw SQL to insert/update AuditDecision (bypasses EF Core trigger issue)
                    var connection = _dbContext.Database.GetDbConnection();
                    var wasOpen = connection.State == System.Data.ConnectionState.Open;
                    if (!wasOpen) await connection.OpenAsync();

                    try
                    {
                        // Check if audit decision exists
                        using (var checkCommand = connection.CreateCommand())
                        {
                            checkCommand.CommandText = @"
                                SELECT Id FROM AuditDecisions 
                                WHERE ContainerNumber = @p0 
                                  AND (GroupIdentifier = @p1 OR GroupIdentifier = @p2) 
                                  AND ScannerType = @p3";

                            checkCommand.Parameters.Add(new NpgsqlParameter("@p0", containerDecision.ContainerNumber));
                            checkCommand.Parameters.Add(new NpgsqlParameter("@p1", storageGroupId));
                            checkCommand.Parameters.Add(new NpgsqlParameter("@p2", request.GroupIdentifier));
                            checkCommand.Parameters.Add(new NpgsqlParameter("@p3", scannerType));

                            var existingId = await checkCommand.ExecuteScalarAsync();

                            if (existingId == null || existingId == DBNull.Value)
                            {
                                // Insert new audit decision (use storageGroupId so SubmissionWorker finds by AnalysisGroup.GroupIdentifier)
                                using (var insertCommand = connection.CreateCommand())
                                {
                                    insertCommand.CommandText = @"
                                        INSERT INTO AuditDecisions 
                                        (ContainerNumber, GroupIdentifier, ScannerType, ImageAnalysisDecisionId, 
                                         Decision, AuditNotes, AuditedBy, AuditedAt, CreatedAt, IsCompleted)
                                        VALUES 
                                        (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9)";

                                    insertCommand.Parameters.Add(new NpgsqlParameter("@p0", containerDecision.ContainerNumber));
                                    insertCommand.Parameters.Add(new NpgsqlParameter("@p1", storageGroupId));
                                    insertCommand.Parameters.Add(new NpgsqlParameter("@p2", scannerType));
                                    insertCommand.Parameters.Add(new NpgsqlParameter("@p3", imageDecision.Id));
                                    insertCommand.Parameters.Add(new NpgsqlParameter("@p4", decision));
                                    insertCommand.Parameters.Add(new NpgsqlParameter("@p5", containerDecision.Notes != null ? (object)containerDecision.Notes : DBNull.Value));
                                    insertCommand.Parameters.Add(new NpgsqlParameter("@p6", username));
                                    insertCommand.Parameters.Add(new NpgsqlParameter("@p7", now));
                                    insertCommand.Parameters.Add(new NpgsqlParameter("@p8", now));
                                    insertCommand.Parameters.Add(new NpgsqlParameter("@p9", false));

                                    await insertCommand.ExecuteNonQueryAsync();
                                }
                            }
                            else
                            {
                                // Update existing audit decision
                                using (var updateCommand = connection.CreateCommand())
                                {
                                    updateCommand.CommandText = @"
                                        UPDATE AuditDecisions
                                        SET ImageAnalysisDecisionId = @p0,
                                            Decision = @p1,
                                            AuditNotes = @p2,
                                            AuditedBy = @p3,
                                            AuditedAt = @p4,
                                            UpdatedAt = @p5
                                        WHERE Id = @p6";

                                    updateCommand.Parameters.Add(new NpgsqlParameter("@p0", imageDecision.Id));
                                    updateCommand.Parameters.Add(new NpgsqlParameter("@p1", decision));
                                    updateCommand.Parameters.Add(new NpgsqlParameter("@p2", containerDecision.Notes != null ? (object)containerDecision.Notes : DBNull.Value));
                                    updateCommand.Parameters.Add(new NpgsqlParameter("@p3", username));
                                    updateCommand.Parameters.Add(new NpgsqlParameter("@p4", now));
                                    updateCommand.Parameters.Add(new NpgsqlParameter("@p5", now));
                                    updateCommand.Parameters.Add(new NpgsqlParameter("@p6", existingId));

                                    await updateCommand.ExecuteNonQueryAsync();
                                }
                            }
                        }
                    }
                    finally
                    {
                        if (!wasOpen) await connection.CloseAsync();
                    }

                    // ─── Per-image audit children (deferred plan request 1) ───
                    // When the caller supplied per-image verdicts, persist them as
                    // AuditImageDecision rows. We do this AFTER the parent insert/update
                    // so we can resolve the parent's id via EF (the raw SQL above doesn't
                    // RETURNING). Best-effort: a failure here logs a warning but does NOT
                    // fail the parent audit submission, because the parent rollup row is
                    // already persisted and SubmissionWorker / Dashboard only need that.
                    if (containerDecision.ImageDecisions != null && containerDecision.ImageDecisions.Any())
                    {
                        try
                        {
                            var auditDecisionRow = await _dbContext.AuditDecisions
                                .AsTracking()
                                .Where(a => a.ContainerNumber == containerDecision.ContainerNumber
                                            && a.ScannerType == scannerType
                                            && (a.GroupIdentifier == storageGroupId
                                                || a.GroupIdentifier == request.GroupIdentifier))
                                .OrderByDescending(a => a.AuditedAt)
                                .FirstOrDefaultAsync();

                            if (auditDecisionRow != null)
                            {
                                // Replace any prior children for this audit row so a re-submission
                                // doesn't double-count. EF tracks the existing rows so we can
                                // delete them in one statement.
                                var priorChildren = await _dbContext.AuditImageDecisions
                                    .Where(c => c.AuditDecisionId == auditDecisionRow.Id)
                                    .ToListAsync();
                                if (priorChildren.Any())
                                {
                                    _dbContext.AuditImageDecisions.RemoveRange(priorChildren);
                                }

                                foreach (var img in containerDecision.ImageDecisions)
                                {
                                    var childDecision = (img.Decision ?? string.Empty).Trim();
                                    if (childDecision.Equals("APPROVED", StringComparison.OrdinalIgnoreCase))
                                        childDecision = "Approved";
                                    else if (childDecision.Equals("REJECTED", StringComparison.OrdinalIgnoreCase))
                                        childDecision = "Rejected";

                                    if (!childDecision.Equals("Approved", StringComparison.OrdinalIgnoreCase) &&
                                        !childDecision.Equals("Rejected", StringComparison.OrdinalIgnoreCase))
                                    {
                                        _logger.LogWarning("Skipping invalid per-image decision '{Decision}' on container {Container} image {Idx}",
                                            img.Decision, containerDecision.ContainerNumber, img.ImageIndex);
                                        continue;
                                    }

                                    _dbContext.AuditImageDecisions.Add(new AuditImageDecision
                                    {
                                        AuditDecisionId = auditDecisionRow.Id,
                                        ContainerNumber = containerDecision.ContainerNumber,
                                        ScannerType = string.IsNullOrWhiteSpace(img.ScannerType) ? scannerType : img.ScannerType,
                                        ImageIndex = img.ImageIndex,
                                        Decision = childDecision,
                                        Notes = img.Notes,
                                        AuditedBy = username,
                                        AuditedAtUtc = now,
                                        CreatedAtUtc = now,
                                    });
                                }

                                await _dbContext.SaveChangesAsync();
                            }
                            else
                            {
                                _logger.LogWarning("Per-image audit children skipped: no AuditDecision row resolvable for {Container}/{Scanner}",
                                    containerDecision.ContainerNumber, scannerType);
                            }
                        }
                        catch (Exception childEx)
                        {
                            _logger.LogWarning(childEx,
                                "Failed to persist per-image audit children for {Container} ({Scanner}); parent audit row still committed",
                                containerDecision.ContainerNumber, scannerType);
                        }
                    }

                    auditedCount++;
                }

                // ✅ AUTO-PROGRESSION: Determine if all containers in THIS AUDIT RECORD are audited
                // CONSOLIDATED: CCS.GroupIdentifier = ContainerNumber (1 row per container). One audit record = 1 container.
                //   When that container is audited → allContainersAudited = true → progress.
                // NON-CONSOLIDATED: CCS.GroupIdentifier = DeclarationNumber (N rows share same declaration). One audit record = N containers.
                //   When all N are audited → allContainersAudited = true → progress.
                var autoProgStorageGroupId = group.GroupIdentifier ?? request.GroupIdentifier;
                var autoProgNormalized = GroupIdentifierHelper.GetNormalizedGroupIdentifier(request.GroupIdentifier);
                var completenessGroupIds = new List<string> { request.GroupIdentifier };
                if (group.GroupIdentifier != request.GroupIdentifier && !string.IsNullOrEmpty(group.GroupIdentifier))
                    completenessGroupIds.Add(group.GroupIdentifier);

                List<string> recordContainers;
                if (request.IsConsolidated)
                {
                    // Consolidated: GroupIdentifier = container#. CCS has exactly 1 row for this container.
                    recordContainers = await _dbContext.ContainerCompletenessStatuses
                        .Where(s => s.GroupIdentifier == request.GroupIdentifier && s.ScannerType == scannerType)
                        .Select(s => s.ContainerNumber)
                        .Distinct()
                        .OrderBy(c => c)
                        .ToListAsync();
                }
                else
                {
                    // Non-consolidated: GroupIdentifier = declaration. CCS has N rows (all containers under declaration).
                    if (completenessGroupIds.Count == 1)
                    {
                        var ccsList = await _dbContext.ContainerCompletenessStatuses
                            .Where(s => s.GroupIdentifier == completenessGroupIds[0] && s.ScannerType == scannerType)
                            .Select(s => s.ContainerNumber)
                            .Distinct()
                            .ToListAsync();
                        recordContainers = ccsList.OrderBy(c => c).ToList();
                    }
                    else
                    {
                        recordContainers = await _dbContext.ContainerCompletenessStatuses
                            .Where(s => (s.GroupIdentifier == completenessGroupIds[0] || s.GroupIdentifier == completenessGroupIds[1]) && s.ScannerType == scannerType)
                            .Select(s => s.ContainerNumber)
                            .Distinct()
                            .OrderBy(c => c)
                            .ToListAsync();
                    }
                }

                // Fallback: if IsConsolidated was wrong/missing and we got no rows, try the other path
                if (!recordContainers.Any())
                {
                    if (request.IsConsolidated)
                    {
                        recordContainers = await _dbContext.ContainerCompletenessStatuses
                            .Where(s => (s.GroupIdentifier == completenessGroupIds[0] || (completenessGroupIds.Count > 1 && s.GroupIdentifier == completenessGroupIds[1])) && s.ScannerType == scannerType)
                            .Select(s => s.ContainerNumber)
                            .Distinct()
                            .OrderBy(c => c)
                            .ToListAsync();
                    }
                    else
                    {
                        recordContainers = await _dbContext.ContainerCompletenessStatuses
                            .Where(s => s.GroupIdentifier == request.GroupIdentifier && s.ScannerType == scannerType)
                            .Select(s => s.ContainerNumber)
                            .Distinct()
                            .OrderBy(c => c)
                            .ToListAsync();
                    }
                }

                // Fallback to AnalysisGroup containers if no CCS rows (e.g. legacy data)
                if (!recordContainers.Any())
                {
                    recordContainers = await _dbContext.AnalysisRecords
                        .Where(r => r.GroupId == group.Id)
                        .Select(r => r.ContainerNumber)
                        .Distinct()
                        .OrderBy(c => c)
                        .ToListAsync();
                }
                if (!recordContainers.Any())
                {
                    recordContainers = await _dbContext.ImageAnalysisDecisions
                        .Where(d => d.ScannerType == scannerType && (
                            d.GroupIdentifier == autoProgStorageGroupId ||
                            d.GroupIdentifier == request.GroupIdentifier ||
                            (autoProgNormalized != null && d.GroupIdentifier == autoProgNormalized)))
                        .Select(d => d.ContainerNumber)
                        .Distinct()
                        .OrderBy(c => c)
                        .ToListAsync();
                }

                var containersWithAudit = await _dbContext.AuditDecisions
                    .Where(ad => ad.ScannerType == scannerType && (
                        ad.GroupIdentifier == autoProgStorageGroupId ||
                        ad.GroupIdentifier == request.GroupIdentifier ||
                        (autoProgNormalized != null && ad.GroupIdentifier == autoProgNormalized)))
                    .Select(ad => ad.ContainerNumber)
                    .Distinct()
                    .ToListAsync();

                var undecidedContainers = recordContainers
                    .Where(c => !containersWithAudit.Contains(c, StringComparer.OrdinalIgnoreCase))
                    .OrderBy(c => c)
                    .ToList();

                var allContainersAudited = !undecidedContainers.Any();
                var nextContainerNumber = undecidedContainers.FirstOrDefault();

                _logger.LogInformation("🔍 [AUDIT-AUTO-PROGRESSION] Group {GroupIdentifier}: RecordContainers={Record}, Audited={Audited}, Undecided={Undecided}, AllDone={AllDone}, Next={Next}",
                    request.GroupIdentifier, recordContainers.Count, containersWithAudit.Count,
                    string.Join(",", undecidedContainers), allContainersAudited, nextContainerNumber ?? "(none)");

                // Determine overall group decision (for response)
                var overallDecision = hasRejected ? "Rejected" : "Approved";

                // Only run completion logic when ALL containers are audited
                if (allContainersAudited)
                {
                    // Update all audit decisions for this group with overall decision and completion status
                    // Use raw SQL to avoid EF Core trigger issues
                    var connectionForUpdate = _dbContext.Database.GetDbConnection();
                    var wasOpenForUpdate = connectionForUpdate.State == System.Data.ConnectionState.Open;
                    if (!wasOpenForUpdate) await connectionForUpdate.OpenAsync();

                    try
                    {
                        var updateGroupIds = new List<string> { group.GroupIdentifier ?? request.GroupIdentifier };
                        if (request.GroupIdentifier != group.GroupIdentifier)
                            updateGroupIds.Add(request.GroupIdentifier);
                        using (var updateCommand = connectionForUpdate.CreateCommand())
                        {
                            updateCommand.CommandText = @"
                            UPDATE AuditDecisions 
                            SET OverallGroupDecision = @p0,
                                IsCompleted = true,
                                CompletedAt = @p1,
                                UpdatedAt = @p2
                            WHERE (GroupIdentifier = @p3 OR GroupIdentifier = @p4) AND ScannerType = @p5";

                            updateCommand.Parameters.Add(new NpgsqlParameter("@p0", overallDecision));
                            updateCommand.Parameters.Add(new NpgsqlParameter("@p1", now));
                            updateCommand.Parameters.Add(new NpgsqlParameter("@p2", now));
                            updateCommand.Parameters.Add(new NpgsqlParameter("@p3", updateGroupIds[0]));
                            updateCommand.Parameters.Add(new NpgsqlParameter("@p4", updateGroupIds.Count > 1 ? updateGroupIds[1] : updateGroupIds[0]));
                            updateCommand.Parameters.Add(new NpgsqlParameter("@p5", scannerType));

                            await updateCommand.ExecuteNonQueryAsync();
                        }
                    }
                    finally
                    {
                        if (!wasOpenForUpdate) await connectionForUpdate.CloseAsync();
                    }

                    // Update AnalysisGroup status to AuditCompleted
                    // ✅ FIX: Update ALL matching AnalysisGroups (assignment may point to date-suffixed variant, e.g. 10925597967_20250101_20250131)
                    var allMatchingGroups = await _dbContext.AnalysisGroups
                        .AsTracking()
                        .Where(g => g.GroupIdentifier == request.GroupIdentifier ||
                            g.GroupIdentifier == group.GroupIdentifier ||
                            (g.GroupIdentifier != null && g.GroupIdentifier.StartsWith(request.GroupIdentifier + "_")))
                        .ToListAsync();
                    // Sprint 5G2 / B1: route every status flip through the state-machine facade so a
                    // status_transitions audit row is written and the legal-transition table is enforced.
                    // Idempotent calls (already in AuditCompleted) are no-ops inside the facade.
                    foreach (var g in allMatchingGroups)
                    {
                        await AnalysisGroupStateMachine.TransitionAsync(
                            _dbContext, g, AnalysisStatuses.AuditCompleted,
                            triggerName: "AuditSubmissionAllContainersAudited",
                            actor: username,
                            reason: $"Audit submission completed for group {request.GroupIdentifier} (overall decision: {overallDecision}).",
                            correlationId: HttpContext?.TraceIdentifier);
                        g.UpdatedAtUtc = now;
                    }

                    // ✅ FIX: Release the assignment so it immediately disappears from My Assignments
                    // Use raw SQL to avoid EF Core CTE (WITH) which breaks on SQL Server 2014
                    var matchingGroupIds = allMatchingGroups.Select(g => g.Id).ToList();
                    if (matchingGroupIds.Any())
                    {
                        // Use per-GroupId parameterized updates to avoid invalid IN (...) string interpolation
                        // (which can cause uniqueidentifier conversion errors).
                        var releaseCount = 0;
                        foreach (var groupId in matchingGroupIds)
                        {
                            releaseCount += await _dbContext.Database.ExecuteSqlRawAsync(
                                "UPDATE AnalysisAssignments SET State = 'Released', UpdatedAtUtc = now() AT TIME ZONE 'UTC' WHERE GroupId = {0} AND Role = 'Audit' AND State = 'Active'",
                                groupId);

                            if (_readyGroupsCache != null)
                            {
                                await _readyGroupsCache.RemoveQueueEntriesForGroupAsync(_dbContext, groupId);
                            }
                            else
                            {
                                await _dbContext.Database.ExecuteSqlRawAsync(
                                    "DELETE FROM analysisqueueentries WHERE groupid = {0}",
                                    groupId);
                            }
                        }

                        if (releaseCount > 0)
                            _logger.LogInformation("🔔 [AuditReview] Released {Count} Audit assignment(s) and removed materialized queue entries for completed groups", releaseCount);
                    }

                    // ✅ FIX: Invalidate my-assignments cache so completed record disappears from queue immediately
                    if (!string.IsNullOrEmpty(username))
                    {
                        var cacheKey = $"my-assignments:{username}:Audit";
                        _memoryCache.Remove(cacheKey);
                        _logger.LogInformation("🔔 [AuditReview] Invalidated cache {CacheKey} after audit completion", cacheKey);
                    }

                    // Manually update WorkflowStage as a backup (in case trigger didn't fire or didn't match)
                    // This ensures records are moved to Completed stage even if trigger has issues
                    // Note: The database trigger trg_AuditDecision_AdvanceStage should automatically
                    // update WorkflowStage from 'Audit' to 'Completed' when AuditDecisions are saved,
                    // but we do this manually as a safety measure.
                    // completenessGroupIds already defined above for auto-progression
                    // ✅ SQL Server 2014 FIX: Use FromSqlRaw to avoid EF Core CTE generation from Contains() (semicolon prefix required)
                    List<ContainerCompletenessStatus> completenessRecords;
                    if (completenessGroupIds.Count == 1)
                    {
                        completenessRecords = await _dbContext.ContainerCompletenessStatuses
                            .FromSqlRaw(";SELECT * FROM ContainerCompletenessStatuses WHERE GroupIdentifier = {0} AND ScannerType = {1}",
                                completenessGroupIds[0], scannerType)
                            .AsNoTracking()
                            .ToListAsync();
                    }
                    else
                    {
                        completenessRecords = await _dbContext.ContainerCompletenessStatuses
                            .FromSqlRaw(";SELECT * FROM ContainerCompletenessStatuses WHERE (GroupIdentifier = {0} OR GroupIdentifier = {1}) AND ScannerType = {2}",
                                completenessGroupIds[0], completenessGroupIds[1], scannerType)
                            .AsNoTracking()
                            .ToListAsync();
                    }

                    if (completenessRecords.Any())
                    {
                        // Batch update using GroupIdentifier and ScannerType (safer than ID list)
                        var connection = _dbContext.Database.GetDbConnection();
                        var wasOpen = connection.State == System.Data.ConnectionState.Open;
                        if (!wasOpen) await connection.OpenAsync();

                        try
                        {
                            using (var command = connection.CreateCommand())
                            {
                                var whereGroupId = completenessGroupIds.Count > 1
                                    ? "(GroupIdentifier = @p2 OR GroupIdentifier = @p4)"
                                    : "GroupIdentifier = @p2";
                                command.CommandText = $@"
                                UPDATE ContainerCompletenessStatuses 
                                SET WorkflowStage = @p0, UpdatedAt = @p1 
                                WHERE {whereGroupId} AND ScannerType = @p3";

                                var param0 = command.CreateParameter();
                                param0.ParameterName = "@p0";
                                param0.Value = "Completed";
                                command.Parameters.Add(param0);

                                var param1 = command.CreateParameter();
                                param1.ParameterName = "@p1";
                                param1.Value = now;
                                command.Parameters.Add(param1);

                                var param2 = command.CreateParameter();
                                param2.ParameterName = "@p2";
                                param2.Value = completenessGroupIds[0];
                                command.Parameters.Add(param2);

                                var param3 = command.CreateParameter();
                                param3.ParameterName = "@p3";
                                param3.Value = scannerType;
                                command.Parameters.Add(param3);
                                if (completenessGroupIds.Count > 1)
                                {
                                    var param4 = command.CreateParameter();
                                    param4.ParameterName = "@p4";
                                    param4.Value = completenessGroupIds[1];
                                    command.Parameters.Add(param4);
                                }

                                var rowsAffected = await command.ExecuteNonQueryAsync();
                                _logger.LogInformation("✅ Updated {Count} container(s) WorkflowStage to 'Completed' for group {GroupIdentifier}",
                                    rowsAffected, request.GroupIdentifier);
                            }
                        }
                        finally
                        {
                            if (!wasOpen) await connection.CloseAsync();
                        }
                    }
                } // end if (allContainersAudited)

                if (auditedCount == 0)
                {
                    _logger.LogWarning("SubmitAudit: No containers could be matched - ImageAnalysisDecision lookup failed for all {Count} containers in group {GroupIdentifier}",
                        request.ContainerDecisions.Count, request.GroupIdentifier);
                    return BadRequest(new AuditSubmissionResponse
                    {
                        Success = false,
                        Message = "Could not match any container to analyst decisions. Ensure image analysis is complete for this group.",
                        OverallDecision = null,
                        AuditedCount = 0,
                        NextContainerNumber = null,
                        AllContainersAudited = false
                    });
                }

                _logger.LogInformation("✅ Audit submitted successfully for group {GroupIdentifier}: {Decision} ({Count} containers), AllDone={AllDone}, Next={Next}",
                    request.GroupIdentifier, overallDecision, auditedCount, allContainersAudited, nextContainerNumber ?? "(none)");

                return Ok(new AuditSubmissionResponse
                {
                    Success = true,
                    Message = allContainersAudited ? $"Audit completed: {overallDecision}" : $"Container(s) audited. {undecidedContainers.Count} remaining.",
                    OverallDecision = allContainersAudited ? overallDecision : null,
                    AuditedCount = auditedCount,
                    NextContainerNumber = nextContainerNumber,
                    AllContainersAudited = allContainersAudited
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error submitting audit for group {GroupIdentifier}", request?.GroupIdentifier);
                return StatusCode(500, new AuditSubmissionResponse
                {
                    Success = false,
                    Message = $"Error submitting audit: {ex.Message}",
                    OverallDecision = null,
                    AuditedCount = 0,
                    NextContainerNumber = null,
                    AllContainersAudited = false
                });
            }
        }

        /// <summary>
        /// Get completed records (containers in terminal workflow stages: PendingSubmission, Submitted, Completed)
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-empty fallback.
        [HttpGet("completed")]
        public async Task<ActionResult<ApiResponse<List<CompletedRecordDto>>>> GetCompletedRecords(
            [FromQuery] string? scannerType = null,
            [FromQuery] string? decision = null)
        {
            try
            {
                // Get IDs of completed records using raw SQL
                var completedRecordIds = new List<int>();

                var connection = _dbContext.Database.GetDbConnection();
                var wasOpen = connection.State == System.Data.ConnectionState.Open;
                if (!wasOpen) await connection.OpenAsync();

                try
                {
                    using (var command = connection.CreateCommand())
                    {
                        // Include all terminal workflow stages (PendingSubmission, Submitted, Completed)
                        // as "completed" — these all represent containers that have passed audit
                        var sql = "SELECT Id FROM ContainerCompletenessStatuses WHERE WorkflowStage IN (@p0, @p1, @p2)";
                        if (scannerType != null)
                        {
                            sql += " AND ScannerType = @p3";
                        }
                        command.CommandText = sql;

                        var param0 = command.CreateParameter();
                        param0.ParameterName = "@p0";
                        param0.Value = "Completed";
                        command.Parameters.Add(param0);

                        var param1 = command.CreateParameter();
                        param1.ParameterName = "@p1";
                        param1.Value = "PendingSubmission";
                        command.Parameters.Add(param1);

                        var param2 = command.CreateParameter();
                        param2.ParameterName = "@p2";
                        param2.Value = "Submitted";
                        command.Parameters.Add(param2);

                        if (scannerType != null)
                        {
                            var param3 = command.CreateParameter();
                            param3.ParameterName = "@p3";
                            param3.Value = scannerType;
                            command.Parameters.Add(param3);
                        }

                        using (var reader = await command.ExecuteReaderAsync())
                        {
                            while (await reader.ReadAsync())
                            {
                                completedRecordIds.Add(reader.GetInt32(0));
                            }
                        }
                    }
                }
                finally
                {
                    if (!wasOpen) await connection.CloseAsync();
                }

                // ✅ PERFORMANCE FIX: Use database-level grouping instead of loading all records
                if (!completedRecordIds.Any())
                {
                    return Ok(new ApiResponse<List<CompletedRecordDto>>
                    {
                        Success = true,
                        Data = new List<CompletedRecordDto>()
                    });
                }

                // Load full records using IDs
                // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid EF Core CTE generation (SQL Server 2014 compatibility)
                var completedRecords = new List<ContainerCompletenessStatus>();
                const int idBatchSize = 100; // Use smaller batch to avoid CTE generation

                if (completedRecordIds.Any())
                {
                    for (int i = 0; i < completedRecordIds.Count; i += idBatchSize)
                    {
                        var idBatch = completedRecordIds.Skip(i).Take(idBatchSize).ToList();

                        // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid CTE generation
                        // Build parameterized IN clause
                        var parameters = new List<object>();
                        var parameterPlaceholders = new List<string>();

                        for (int j = 0; j < idBatch.Count; j++)
                        {
                            parameterPlaceholders.Add($"{{{j}}}");
                            parameters.Add(idBatch[j]);
                        }

                        var inClause = string.Join(",", parameterPlaceholders);
                        // ✅ SQL Server 2014 FIX: Semicolon prefix required before WITH clause
                        var sql = $";SELECT * FROM ContainerCompletenessStatuses WHERE Id IN ({inClause})";

                        var batchRecords = await _dbContext.ContainerCompletenessStatuses
                            .FromSqlRaw(sql, parameters.ToArray())
                            .AsNoTracking()
                            .ToListAsync();
                        completedRecords.AddRange(batchRecords);
                    }
                }

                // ✅ PERFORMANCE FIX: Batch load decisions using container numbers
                var containerNumbers = completedRecords.Select(c => c.ContainerNumber).Distinct().ToList();

                // Get all audit decisions for these containers
                // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid EF Core CTE generation (SQL Server 2014 compatibility)
                var allAuditDecisionsList = new List<AuditDecision>();
                const int containerBatchSize = 100;

                if (containerNumbers.Any())
                {
                    for (int i = 0; i < containerNumbers.Count; i += containerBatchSize)
                    {
                        var containerBatch = containerNumbers.Skip(i).Take(containerBatchSize).ToList();

                        // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid CTE generation
                        // Build parameterized IN clause
                        var parameters = new List<object>();
                        var parameterPlaceholders = new List<string>();

                        for (int j = 0; j < containerBatch.Count; j++)
                        {
                            parameterPlaceholders.Add($"{{{j}}}");
                            parameters.Add(containerBatch[j]);
                        }

                        var inClause = string.Join(",", parameterPlaceholders);
                        // ✅ SQL Server 2014 FIX: Semicolon prefix required before WITH clause
                        var sql = $";SELECT * FROM AuditDecisions WHERE ContainerNumber IN ({inClause})";

                        var batchDecisions = await _dbContext.AuditDecisions
                            .FromSqlRaw(sql, parameters.ToArray())
                            .AsNoTracking()
                            .ToListAsync();
                        allAuditDecisionsList.AddRange(batchDecisions);
                    }
                }

                // ✅ FIX: Use most recent audit decision if multiple exist for same container+scanner
                var allAuditDecisions = allAuditDecisionsList
                    .GroupBy(a => $"{a.ContainerNumber}|{a.ScannerType}")
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.AuditedAt).First());

                // Get all image analysis decisions
                // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid EF Core CTE generation (SQL Server 2014 compatibility)
                var allImageDecisionsList = new List<ImageAnalysisDecision>();

                if (containerNumbers.Any())
                {
                    for (int i = 0; i < containerNumbers.Count; i += containerBatchSize)
                    {
                        var containerBatch = containerNumbers.Skip(i).Take(containerBatchSize).ToList();

                        // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid CTE generation
                        // Build parameterized IN clause
                        var parameters = new List<object>();
                        var parameterPlaceholders = new List<string>();

                        for (int j = 0; j < containerBatch.Count; j++)
                        {
                            parameterPlaceholders.Add($"{{{j}}}");
                            parameters.Add(containerBatch[j]);
                        }

                        var inClause = string.Join(",", parameterPlaceholders);
                        // ✅ SQL Server 2014 FIX: Semicolon prefix required before WITH clause
                        var sql = $";SELECT * FROM ImageAnalysisDecisions WHERE ContainerNumber IN ({inClause})";

                        var batchDecisions = await _dbContext.ImageAnalysisDecisions
                            .FromSqlRaw(sql, parameters.ToArray())
                            .AsNoTracking()
                            .ToListAsync();
                        allImageDecisionsList.AddRange(batchDecisions);
                    }
                }

                // ✅ FIX: Use most recent decision if multiple exist for same container+scanner
                var allImageDecisions = allImageDecisionsList
                    .GroupBy(i => $"{i.ContainerNumber}|{i.ScannerType}")
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(d => d.ReviewedAt).First());

                // Group by GroupIdentifier
                var groups = completedRecords
                    .GroupBy(s => new { s.GroupIdentifier, s.ScannerType })
                    .Select(g =>
                    {
                        var groupId = g.Key.GroupIdentifier ?? "";
                        var containers = g.ToList();

                        // ✅ FIX: Look up decisions from pre-loaded dictionaries
                        var groupAuditDecisions = containers
                            .Select(c =>
                            {
                                var key = $"{c.ContainerNumber}|{c.ScannerType}";
                                return allAuditDecisions.TryGetValue(key, out var audit) ? audit : null;
                            })
                            .Where(a => a != null)
                            .Cast<AuditDecision>()
                            .ToList();

                        var groupImageDecisions = containers
                            .Select(c =>
                            {
                                var key = $"{c.ContainerNumber}|{c.ScannerType}";
                                return allImageDecisions.TryGetValue(key, out var img) ? img : null;
                            })
                            .Where(i => i != null)
                            .Cast<ImageAnalysisDecision>()
                            .ToList();

                        // Determine overall decision (if any rejected, group is rejected; otherwise approved)
                        var overallDecision = groupAuditDecisions.Any(a => a.Decision == "Rejected")
                            ? "Rejected"
                            : (groupAuditDecisions.Any(a => a.Decision == "Approved") ? "Approved" : "Pending");

                        // Get most recent auditor
                        var mostRecentAudit = groupAuditDecisions
                            .OrderByDescending(a => a.AuditedAt)
                            .FirstOrDefault();

                        var imageAnalyst = GetImageAnalystDisplay(groupImageDecisions);

                        // Build audit details
                        var auditDetails = containers.Select(c =>
                        {
                            // ✅ FIX: Match by both ContainerNumber AND ScannerType to ensure correct decision lookup
                            var audit = groupAuditDecisions.FirstOrDefault(a =>
                                a.ContainerNumber == c.ContainerNumber &&
                                a.ScannerType == c.ScannerType);
                            var image = groupImageDecisions.FirstOrDefault(i =>
                                i.ContainerNumber == c.ContainerNumber &&
                                i.ScannerType == c.ScannerType);

                            return new AuditDetailDto
                            {
                                ContainerNumber = c.ContainerNumber,
                                OriginalDecision = image?.Decision ?? "Pending",
                                AuditDecision = audit?.Decision ?? "Pending",
                                ImageAnalysisNotes = image?.Comments,
                                AuditNotes = audit?.AuditNotes
                            };
                        }).ToList();

                        // ✅ FIX: Count Image Analysis decisions (Normal/Abnormal) for summary
                        var normalCount = groupImageDecisions.Count(i => i.Decision == "Normal");
                        var abnormalCount = groupImageDecisions.Count(i => i.Decision == "Abnormal");

                        // Count Audit decisions (Approved/Rejected) for audit summary
                        var approvedCount = groupAuditDecisions.Count(a => a.Decision == "Approved");
                        var rejectedCount = groupAuditDecisions.Count(a => a.Decision == "Rejected");

                        return new CompletedRecordDto
                        {
                            GroupIdentifier = groupId,
                            OverallDecision = overallDecision,
                            CompletedAt = mostRecentAudit?.CompletedAt ?? mostRecentAudit?.AuditedAt ?? DateTime.UtcNow,
                            AuditedBy = mostRecentAudit?.AuditedBy ?? "Unknown",
                            ImageAnalyst = imageAnalyst,
                            TotalContainers = containers.Count,
                            ApprovedCount = approvedCount, // Audit decisions: Approved
                            RejectedCount = rejectedCount, // Audit decisions: Rejected
                            NormalCount = normalCount, // ✅ Image Analysis: Normal
                            AbnormalCount = abnormalCount, // ✅ Image Analysis: Abnormal
                            AuditDetails = auditDetails
                        };
                    })
                    .ToList();

                // Apply decision filter if specified
                if (decision != null && decision != "All")
                {
                    groups = groups.Where(g => g.OverallDecision == decision).ToList();
                }

                return Ok(new ApiResponse<List<CompletedRecordDto>>
                {
                    Success = true,
                    Data = groups,
                    Message = $"{groups.Count} completed record(s) retrieved."
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting completed records");
                // ✅ FIX: Return empty response instead of 500 to prevent frontend errors
                return Ok(new ApiResponse<List<CompletedRecordDto>>
                {
                    Success = false,
                    Data = new List<CompletedRecordDto>(),
                    Message = $"Error: {ex.Message}"
                });
            }
        }

        // DTOs
        public class ApiResponse<T>
        {
            public bool Success { get; set; }
            public T? Data { get; set; }
            public string? Message { get; set; }
        }

        public class AuditGroupDto
        {
            public string GroupIdentifier { get; set; } = "";
            public string ScannerType { get; set; } = "";
            public bool IsConsolidated { get; set; }
            public int TotalContainers { get; set; }
            public DateTime SubmittedAt { get; set; }
            public string SubmittedBy { get; set; } = "";
            public List<ImageAnalysisDecisionSummary> ImageAnalysisDecisions { get; set; } = new();
        }

        public class ImageAnalysisDecisionSummary
        {
            public int? ImageAnalysisDecisionId { get; set; }
            public string ContainerNumber { get; set; } = "";
            public string Decision { get; set; } = "";
            public string ReviewedBy { get; set; } = "";
            public DateTime ReviewedAt { get; set; }
            public string? Comments { get; set; }
            // 2026-05-05 operator-reported tag persistence: analyst's image tags weren't
            // surfaced on the audit dialog because this DTO didn't carry them. Source:
            // imageanalysisdecisions.tags (varchar 500). Audit dialog reads via
            // AuditReviewDialog.razor:185-194 + 932 (forwarded as OriginalAnalystTags).
            public string? Tags { get; set; }
            public Guid? SplitJobId { get; set; }
            public Guid? SplitResultId { get; set; }
        }

        public class AuditStats
        {
            public int ReadyForAudit { get; set; }
            public int Completed { get; set; }
            public int Approved { get; set; }
            public int Rejected { get; set; }
            public int ApprovalRate { get; set; }
        }

        public class AutoAuditorStatusResponse
        {
            public bool Success { get; set; }
            public bool Enabled { get; set; }
        }

        public class AutoAuditorToggleResponse
        {
            public bool Success { get; set; }
            public bool Enabled { get; set; }
            public string? Message { get; set; }
        }

        public class ToggleRequest
        {
            public bool Enabled { get; set; }
            public string ModifiedBy { get; set; } = "";
        }

        public class CompletedRecordDto
        {
            public string GroupIdentifier { get; set; } = "";
            public string OverallDecision { get; set; } = "";
            public DateTime CompletedAt { get; set; }
            public string AuditedBy { get; set; } = "";
            public string ImageAnalyst { get; set; } = "";
            public int TotalContainers { get; set; }
            public int ApprovedCount { get; set; } // Audit decisions: Approved
            public int RejectedCount { get; set; } // Audit decisions: Rejected
            public int NormalCount { get; set; } // ✅ Image Analysis: Normal
            public int AbnormalCount { get; set; } // ✅ Image Analysis: Abnormal
            public List<AuditDetailDto> AuditDetails { get; set; } = new();
        }

        public class AuditDetailDto
        {
            public string ContainerNumber { get; set; } = "";
            public string OriginalDecision { get; set; } = "";
            public string AuditDecision { get; set; } = "";
            public string? ImageAnalysisNotes { get; set; }
            public string? AuditNotes { get; set; }
        }

        public class AuditSubmissionRequest
        {
            public string GroupIdentifier { get; set; } = "";
            public string? ScannerType { get; set; } // Optional: use when group.ScannerType is null (from Record)
            public string AuditedBy { get; set; } = "";
            /// <summary>True when GroupIdentifier is container number (1 container per record). False when GroupIdentifier is declaration (N containers per record).</summary>
            public bool IsConsolidated { get; set; }
            public List<ContainerAuditDecisionDto> ContainerDecisions { get; set; } = new();
        }

        public class ContainerAuditDecisionDto
        {
            public string ContainerNumber { get; set; } = "";
            public string Decision { get; set; } = "";
            public string? Notes { get; set; }

            /// <summary>
            /// Per-image audit verdicts (deferred plan request 1). Optional —
            /// when omitted, the controller writes only the parent
            /// AuditDecision row, preserving the legacy single-row behaviour.
            /// When present, each entry is persisted as an
            /// AuditImageDecision child row and the parent's Decision is
            /// rolled up: any Rejected child → parent Rejected, otherwise
            /// the explicit Decision is honoured.
            /// </summary>
            public List<ImageAuditDecisionDto>? ImageDecisions { get; set; }
        }

        /// <summary>
        /// Per-image verdict captured by the auditor. Mirrors
        /// <c>AuditImageDecision</c> on the wire.
        /// </summary>
        public class ImageAuditDecisionDto
        {
            /// <summary>0-based ordering of the image within the container's image set.</summary>
            public int ImageIndex { get; set; }

            /// <summary>"FS6000-Main", "FS6000-Side", "ASE", etc.</summary>
            public string ScannerType { get; set; } = "";

            /// <summary>"Approved" or "Rejected".</summary>
            public string Decision { get; set; } = "";

            public string? Notes { get; set; }
        }

        public class AuditSubmissionResponse
        {
            public bool Success { get; set; }
            public string Message { get; set; } = "";
            public string? OverallDecision { get; set; }
            public int AuditedCount { get; set; }
            /// <summary>Next container to audit (auto-progression). Null when all done.</summary>
            public string? NextContainerNumber { get; set; }
            /// <summary>True when all containers in group have been audited.</summary>
            public bool AllContainersAudited { get; set; }
        }

    }
}

