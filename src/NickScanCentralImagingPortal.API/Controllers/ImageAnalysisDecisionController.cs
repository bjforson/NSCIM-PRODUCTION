using Npgsql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using System.Security.Claims;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageAnalysis;
using NickScanCentralImagingPortal.Services.ImageSplitter;
using NickScanCentralImagingPortal.Services.Manifest;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API Controller for Image Analysis Decisions
    /// Localized to Image Analysis feature only
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ImageAnalysisDecisionController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly IcumDownloadsDbContext _icumDb;
        private readonly ILogger<ImageAnalysisDecisionController> _logger;
        private readonly ReadyGroupsCacheService? _readyGroupsCache;
        private readonly IMemoryCache? _memoryCache;
        private readonly IConfiguration? _configuration;
        private readonly IAiWorkflowLineageService _aiLineage;
        private readonly IManifestSnapshotService _manifestSnapshot;
        private readonly DecisionSideEffectsService _decisionSideEffects;
        // v2.9.6: used to tag each typed ContainerAnnotation row with the
        // dimensions of the image it was drawn against, so CocoExport /
        // future scalers can map fractional coords back to real pixels on
        // whichever image the scan is currently being served with
        // (vendor JPEG ~2295w vs 16-bit composite ~3256w).
        private readonly IImageProcessingService _imageProcessingService;

        // Track unexpected 0-rows WorkflowStage UPDATEs for monitoring/alerting
        private static int _workflowStageUpdateZeroRowsUnexpectedCount;

        public ImageAnalysisDecisionController(
            ApplicationDbContext context,
            IcumDownloadsDbContext icumDb,
            ILogger<ImageAnalysisDecisionController> logger,
            IAiWorkflowLineageService aiLineage,
            IManifestSnapshotService manifestSnapshot,
            IImageProcessingService imageProcessingService,
            DecisionSideEffectsService decisionSideEffects,
            ReadyGroupsCacheService? readyGroupsCache = null,
            IMemoryCache? memoryCache = null,
            IConfiguration? configuration = null)
        {
            _context = context;
            _icumDb = icumDb;
            _logger = logger;
            _aiLineage = aiLineage;
            _manifestSnapshot = manifestSnapshot;
            _imageProcessingService = imageProcessingService;
            _decisionSideEffects = decisionSideEffects;
            _readyGroupsCache = readyGroupsCache;
            _memoryCache = memoryCache;
            _configuration = configuration;
        }

        /// <summary>
        /// Extra safety net for auto-progression:
        /// Ensure that the AnalysisGroup.Status and Analyst assignments for a single group
        /// are consistent with the containers' WorkflowStage and ImageAnalysisDecisions.
        /// 
        /// This is intentionally scoped to a single group (identified by GroupIdentifier)
        /// and is called at the end of SaveDecision. It is idempotent and lightweight:
        /// - Only acts when:
        ///   - The group is still in an analyst-facing status (Ready or AnalystAssigned)
        ///   - All containers for the group have real (Normal/Abnormal) decisions
        ///   - All containers are past ImageAnalysis stage (WorkflowStage = Audit or Completed)
        /// - In that case it:
        ///   - Promotes Status -> AnalystCompleted
        ///   - Releases active Analyst assignments
        /// 
        /// This mirrors the behaviour of ImageAnalysisManagementController.FixStuckAnalystGroups,
        /// but in a focused, per-group fashion, so that recently completed groups flow
        /// cleanly into the audit queue without waiting for background maintenance.
        /// </summary>
        private async Task EnsureGroupStatusFromWorkflowStageAsync(string groupIdentifier, string? scannerType = null)
        {
            if (string.IsNullOrWhiteSpace(groupIdentifier))
                return;

            try
            {
                var group = await ResolveAnalysisGroupForDecisionAsync(groupIdentifier, scannerType);

                if (group == null)
                    return;

                // Only consider groups that are still in the analyst stage
                if (group.Status != AnalysisStatuses.Ready &&
                    group.Status != AnalysisStatuses.AnalystAssigned)
                {
                    return;
                }

                // Get all containers for this group from completeness table
                var normalizedGroupIdentifier = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier) ?? groupIdentifier;
                var groupScannerType = group.ScannerType ?? scannerType;

                var containersQuery = _context.ContainerCompletenessStatuses
                    .Where(c => c.GroupIdentifier == normalizedGroupIdentifier);

                if (!string.IsNullOrWhiteSpace(groupScannerType))
                {
                    var scannerBase = BaseScannerType(groupScannerType) ?? groupScannerType;
                    containersQuery = containersQuery.Where(c =>
                        c.ScannerType == groupScannerType
                        || c.ScannerType == scannerBase
                        || c.ScannerType.StartsWith(scannerBase + "-"));
                }

                var containers = await containersQuery
                    .Select(c => new { c.ContainerNumber, c.WorkflowStage })
                    .ToListAsync();

                if (!containers.Any())
                    return;

                var totalContainers = containers.Count;
                var imageAnalysisCount = containers.Count(c => c.WorkflowStage == "ImageAnalysis");
                var auditCount = containers.Count(c => c.WorkflowStage == "Audit");
                var completedCount = containers.Count(c => c.WorkflowStage == "Completed");

                // We only promote when all containers are past ImageAnalysis stage
                // (Audit and/or Completed). If any are still in ImageAnalysis, we exit.
                if (imageAnalysisCount > 0)
                    return;

                // Ensure all containers have actual decisions (Normal/Abnormal) before promotion
                var containerNumbers = containers
                    .Select(c => c.ContainerNumber)
                    .Distinct()
                    .ToList();

                var decisionGroupIdentifiers = new[] { groupIdentifier, normalizedGroupIdentifier, group.GroupIdentifier, group.NormalizedGroupIdentifier }
                    .Where(g => !string.IsNullOrWhiteSpace(g))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var decisionsQuery = _context.ImageAnalysisDecisions
                    .Where(d => d.GroupIdentifier != null
                        && decisionGroupIdentifiers.Contains(d.GroupIdentifier)
                        && (d.Decision == "Normal" || d.Decision == "Abnormal"));

                if (!string.IsNullOrWhiteSpace(groupScannerType))
                {
                    var scannerBase = BaseScannerType(groupScannerType) ?? groupScannerType;
                    decisionsQuery = decisionsQuery.Where(d =>
                        d.ScannerType == groupScannerType
                        || d.ScannerType == scannerBase
                        || d.ScannerType.StartsWith(scannerBase + "-"));
                }

                var decidedContainers = await decisionsQuery
                    .Select(d => d.ContainerNumber)
                    .Distinct()
                    .ToListAsync();

                if (decidedContainers.Count < containerNumbers.Count)
                {
                    // Not all containers have real decisions yet – don't promote.
                    return;
                }

                var oldStatus = group.Status;

                var analystAssignments = await _context.AnalysisAssignments
                    .AsTracking()
                    .Where(a => a.GroupId == group.Id &&
                                a.Role == "Analyst" &&
                                a.State == "Active")
                    .ToListAsync();

                foreach (var assignment in analystAssignments)
                {
                    assignment.State = "Released";
                    assignment.UpdatedAtUtc = DateTime.UtcNow;
                }

                await RemoveQueueEntriesForAssignmentsAsync(analystAssignments);

                // Sprint 5G2 / B1: route through the state-machine facade. Both Ready and AnalystAssigned
                // are legal predecessors to AnalystCompleted in the validator table.
                await AnalysisGroupStateMachine.TransitionAsync(
                    _context, group, AnalysisStatuses.AnalystCompleted,
                    triggerName: "WorkflowStagePromotionAllContainersDecided",
                    actor: GetAuthenticatedActor(),
                    reason: $"All containers for group {groupIdentifier} have decisions and have moved past ImageAnalysis stage.",
                    correlationId: HttpContext?.TraceIdentifier);
                group.UpdatedAtUtc = DateTime.UtcNow;

                _logger.LogInformation(
                    "EnsureGroupStatusFromWorkflowStageAsync: Promoted group {GroupIdentifier} from {OldStatus} to AnalystCompleted and released {Count} analyst assignment(s)",
                    groupIdentifier, oldStatus, analystAssignments.Count);

                _readyGroupsCache?.InvalidateCache("Audit", "AnalystCompleted");

                // Invalidate my-assignments cache so completed record disappears from analyst's queue immediately
                foreach (var assignment in analystAssignments)
                {
                    var cacheKey = $"my-assignments:{assignment.AssignedTo}:Analyst";
                    _memoryCache?.Remove(cacheKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "EnsureGroupStatusFromWorkflowStageAsync: Failed to synchronize group status for {GroupIdentifier}",
                    groupIdentifier);
            }
        }

        private static string Truncate(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLen ? value : value.Substring(0, maxLen);
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

        private async Task<AnalysisGroup?> ResolveAnalysisGroupForDecisionAsync(string? groupIdentifier, string? scannerType)
        {
            if (string.IsNullOrWhiteSpace(groupIdentifier))
                return null;

            var normalizedGroupIdentifier = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier) ?? groupIdentifier;
            var candidates = await _context.AnalysisGroups
                .AsTracking()
                .Where(g => g.GroupIdentifier == groupIdentifier
                    || g.NormalizedGroupIdentifier == groupIdentifier
                    || g.GroupIdentifier == normalizedGroupIdentifier
                    || g.NormalizedGroupIdentifier == normalizedGroupIdentifier)
                .ToListAsync();

            return candidates
                .OrderByDescending(g => ScannerMatches(g.ScannerType, scannerType))
                .ThenByDescending(g => string.Equals(g.GroupIdentifier, groupIdentifier, StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
        }

        private static List<string> BuildDecisionGroupIdentifiers(params string?[] groupIdentifiers)
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

        private static bool DecisionGroupMatches(string? decisionGroupIdentifier, IReadOnlyCollection<string> groupIdentifiers)
        {
            if (groupIdentifiers.Count == 0)
                return true;

            if (string.IsNullOrWhiteSpace(decisionGroupIdentifier))
                return false;

            if (groupIdentifiers.Contains(decisionGroupIdentifier, StringComparer.OrdinalIgnoreCase))
                return true;

            var normalizedDecisionGroup = GroupIdentifierHelper.GetNormalizedGroupIdentifier(decisionGroupIdentifier);
            if (!string.IsNullOrWhiteSpace(normalizedDecisionGroup)
                && groupIdentifiers.Contains(normalizedDecisionGroup, StringComparer.OrdinalIgnoreCase))
            {
                return true;
            }

            return groupIdentifiers.Any(g =>
                !string.IsNullOrWhiteSpace(g)
                && decisionGroupIdentifier.StartsWith(g + "_", StringComparison.OrdinalIgnoreCase));
        }

        private async Task<ImageAnalysisDecision?> FindExistingDecisionForSaveAsync(
            string containerNumber,
            string scannerType,
            IReadOnlyCollection<string> groupIdentifiers)
        {
            var candidates = await _context.ImageAnalysisDecisions
                .Where(d => d.ContainerNumber == containerNumber
                    && d.ScannerType == scannerType)
                .ToListAsync();

            var scoped = candidates
                .Where(d => DecisionGroupMatches(d.GroupIdentifier, groupIdentifiers))
                .OrderByDescending(d => d.UpdatedAt ?? d.ReviewedAt)
                .ThenByDescending(d => d.Id)
                .FirstOrDefault();

            if (scoped != null)
                return scoped;

            var legacyUngrouped = candidates
                .Where(d => string.IsNullOrWhiteSpace(d.GroupIdentifier))
                .OrderByDescending(d => d.UpdatedAt ?? d.ReviewedAt)
                .ThenByDescending(d => d.Id)
                .ToList();

            return legacyUngrouped.Count == 1 ? legacyUngrouped[0] : null;
        }

        private async Task<AnalysisRecord?> ResolveAnalysisRecordForDecisionAsync(ImageDecisionRequest request)
        {
            if (request.AnalysisRecordId.HasValue)
            {
                var byId = await _context.AnalysisRecords
                    .FirstOrDefaultAsync(r => r.Id == request.AnalysisRecordId.Value);

                if (byId != null)
                    return byId;
            }

            var containerNumber = (request.ContainerNumber ?? string.Empty).Trim();
            if (!string.IsNullOrWhiteSpace(containerNumber))
            {
                var direct = await _context.AnalysisRecords
                    .Where(r => r.ContainerNumber == containerNumber &&
                                (r.ScannerType == request.ScannerType || string.IsNullOrEmpty(r.ScannerType)))
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .FirstOrDefaultAsync();

                if (direct != null)
                    return direct;
            }

            if (string.IsNullOrWhiteSpace(request.GroupIdentifier))
                return null;

            var group = await ResolveAnalysisGroupForDecisionAsync(request.GroupIdentifier, request.ScannerType);
            if (group == null)
                return null;

            var groupRecords = await _context.AnalysisRecords
                .Where(r => r.GroupId == group.Id)
                .OrderByDescending(r => r.CreatedAtUtc)
                .ToListAsync();

            if (groupRecords.Count == 0)
                return null;

            var scannerScoped = groupRecords
                .Where(r => ScannerMatches(r.ScannerType, request.ScannerType))
                .ToList();

            var candidates = scannerScoped.Count > 0 ? scannerScoped : groupRecords;
            var exactContainer = candidates.FirstOrDefault(r =>
                string.Equals(r.ContainerNumber, containerNumber, StringComparison.OrdinalIgnoreCase));
            if (exactContainer != null)
                return exactContainer;

            return candidates.Count == 1 ? candidates[0] : null;
        }

        private async Task ApplySplitSelectionFromDecisionRequestAsync(AnalysisRecord record, ImageDecisionRequest request)
        {
            if (!record.IsMultiContainerScan || !request.SplitResultId.HasValue)
                return;

            var changed = false;
            if (request.SplitJobId.HasValue && record.SplitJobId != request.SplitJobId)
            {
                record.SplitJobId = request.SplitJobId;
                changed = true;
            }

            if (record.SplitResultId != request.SplitResultId)
            {
                record.SplitResultId = request.SplitResultId;
                changed = true;
            }

            if (!string.Equals(record.SplitStatus, SplitAnalysisStatus.Chosen, StringComparison.OrdinalIgnoreCase))
            {
                record.SplitStatus = SplitAnalysisStatus.Chosen;
                changed = true;
            }

            if (!string.IsNullOrWhiteSpace(request.SplitSide)
                && !string.Equals(record.SplitPosition, request.SplitSide, StringComparison.OrdinalIgnoreCase))
            {
                record.SplitPosition = request.SplitSide.Trim();
                changed = true;
            }

            if (!changed)
                return;

            await _context.SaveChangesAsync();
            _logger.LogInformation(
                "Applied split choice from decision save for analysis record {AnalysisRecordId} ({Container}): job={SplitJobId}, result={SplitResultId}, side={SplitSide}",
                record.Id,
                record.ContainerNumber,
                record.SplitJobId,
                record.SplitResultId,
                record.SplitPosition);
        }

        private string? GetAuthenticatedUsername()
        {
            return User?.Identity?.Name
                ?? User?.FindFirst(ClaimTypes.Name)?.Value
                ?? User?.Claims.FirstOrDefault(c => string.Equals(c.Type, "username", StringComparison.OrdinalIgnoreCase))?.Value
                ?? User?.Claims.FirstOrDefault(c => string.Equals(c.Type, "preferred_username", StringComparison.OrdinalIgnoreCase))?.Value;
        }

        private string GetAuthenticatedActor()
        {
            var actor = GetAuthenticatedUsername()
                ?? User?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                ?? User?.Claims.FirstOrDefault(c => string.Equals(c.Type, "sub", StringComparison.OrdinalIgnoreCase))?.Value;

            return Truncate(string.IsNullOrWhiteSpace(actor) ? "System" : actor.Trim(), 100);
        }

        private bool HasPermissionClaim(params string[] permissionNames)
        {
            if (User == null || permissionNames.Length == 0)
                return false;

            var permissionSet = new HashSet<string>(permissionNames, StringComparer.OrdinalIgnoreCase);
            return User.Claims.Any(c =>
                string.Equals(c.Type, "Permission", StringComparison.OrdinalIgnoreCase)
                && permissionSet.Contains(c.Value));
        }

        private bool IsSystemOrPrivilegedDecisionCaller(string? username)
        {
            if (!string.IsNullOrWhiteSpace(username))
            {
                var normalized = username.Trim();
                if (string.Equals(normalized, "System", StringComparison.OrdinalIgnoreCase)
                    || normalized.StartsWith("SYSTEM-", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("agent", StringComparison.OrdinalIgnoreCase)
                    || normalized.Contains("service", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return User?.IsInRole("Admin") == true
                || User?.IsInRole("SuperAdmin") == true
                || User?.IsInRole("Manager") == true
                || User?.IsInRole("Supervisor") == true
                || HasPermissionClaim(
                    Permissions.ControllersImageAnalysisAssign,
                    Permissions.ControllersImageAnalysisManagementSettings,
                    Permissions.ControllersImageAnalysisDecisionAudit);
        }

        private async Task<ActionResult<object>?> ValidateDecisionAssignmentOwnershipAsync(AnalysisGroup? group, string operationName)
        {
            if (group == null)
                return null;

            var username = GetAuthenticatedUsername();
            var now = DateTime.UtcNow;
            var activeAssignments = await _context.AnalysisAssignments
                .AsNoTracking()
                .Where(a => a.GroupId == group.Id
                    && a.Role == "Analyst"
                    && a.State == "Active"
                    && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                .ToListAsync();

            if (!activeAssignments.Any())
                return null;

            if (!string.IsNullOrWhiteSpace(username)
                && activeAssignments.Any(a => string.Equals(a.AssignedTo, username, StringComparison.OrdinalIgnoreCase)))
            {
                return null;
            }

            if (IsSystemOrPrivilegedDecisionCaller(username))
                return null;

            _logger.LogWarning(
                "{Operation} denied for user {User}: group {GroupIdentifier} is actively assigned to {AssignedUsers}",
                operationName,
                username ?? "(unknown)",
                group.GroupIdentifier,
                string.Join(", ", activeAssignments.Select(a => a.AssignedTo).Distinct()));

            return StatusCode(403, new
            {
                success = false,
                error = "This group is assigned to another analyst."
            });
        }

        private async Task RemoveQueueEntriesForAssignmentsAsync(IEnumerable<AnalysisAssignment> assignments, CancellationToken ct = default)
        {
            if (_readyGroupsCache == null)
                return;

            foreach (var assignment in assignments)
            {
                try
                {
                    await _readyGroupsCache.RemoveQueueEntryAsync(_context, assignment.Id, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to remove queue entry for released assignment {AssignmentId}", assignment.Id);
                }
            }
        }

        private async Task UpsertQueueEntryBestEffortAsync(AnalysisAssignment assignment, CancellationToken ct = default)
        {
            if (_readyGroupsCache != null)
            {
                try
                {
                    await _readyGroupsCache.UpsertQueueEntryAsync(_context, assignment.Id, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to upsert queue entry for assignment {AssignmentId}", assignment.Id);
                }
            }

            InvalidateMyAssignmentsCache(assignment.AssignedTo, assignment.Role);
        }

        private void InvalidateMyAssignmentsCache(string? username, string? role)
        {
            if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(role))
                return;

            _memoryCache?.Remove($"my-assignments:{username}:{role}");
        }

        private async Task<List<string>> GetReadyUsersForImmediateAssignmentAsync(
            string roleName,
            DateTime now,
            CancellationToken ct = default)
        {
            var roleNameUpper = roleName.ToUpperInvariant();
            var maxIdleMinutes = _configuration?.GetValue<int>("ImageAnalysis:MaxIdleMinutesForReadiness", 60) ?? 60;
            var maxIdleTime = TimeSpan.FromMinutes(maxIdleMinutes);
            var dbMaxIdle = now.AddMinutes(-maxIdleMinutes);

            var dbReadyUsers = await _context.UserReadiness
                .AsNoTracking()
                .Where(r => r.Role.ToUpper() == roleNameUpper
                    && r.IsReady
                    && r.LastHeartbeat >= dbMaxIdle)
                .Select(r => r.Username)
                .ToListAsync(ct);

            var signalRReadyUsers = UserReadinessStateProvider.GetReadyUsers(roleName, maxIdleTime);
            var readyUserSet = new HashSet<string>(
                dbReadyUsers.Concat(signalRReadyUsers),
                StringComparer.OrdinalIgnoreCase);

            if (readyUserSet.Count == 0)
                return new List<string>();

            var matchingRoleIds = await _context.Roles
                .AsNoTracking()
                .Where(r => r.IsActive && r.Name.ToUpper() == roleNameUpper)
                .Select(r => r.Id)
                .ToListAsync(ct);

            if (matchingRoleIds.Count == 0)
                return new List<string>();

            var matchingRoleSet = matchingRoleIds.ToHashSet();
            var activeUsers = await _context.Users
                .AsNoTracking()
                .Where(u => u.IsActive && u.RoleId != null)
                .Select(u => new { u.Username, u.RoleId })
                .ToListAsync(ct);

            return activeUsers
                .Where(u => readyUserSet.Contains(u.Username)
                    && u.RoleId.HasValue
                    && matchingRoleSet.Contains(u.RoleId.Value))
                .Select(u => u.Username)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private async Task<List<AnalysisGroup>> GetEligibleGroupsForImmediateAssignmentAsync(
            string roleName,
            string eligibleStatus,
            int take,
            CancellationToken ct = default)
        {
            if (take <= 0)
                return new List<AnalysisGroup>();

            if (_readyGroupsCache != null)
            {
                await _readyGroupsCache.InvalidateCacheAsync(roleName, eligibleStatus, ct);
                return await _readyGroupsCache.GetReadyGroupsForRoleAsync(roleName, eligibleStatus, ct);
            }

            return await _context.AnalysisGroups
                .AsNoTracking()
                .Where(g => g.Status == eligibleStatus)
                .OrderByDescending(g => g.Priority)
                .Take(take)
                .ToListAsync(ct);
        }

        private async Task<List<AnalysisGroup>> LoadTrackedGroupsInEligibilityOrderAsync(
            IEnumerable<Guid> groupIds,
            string eligibleStatus,
            CancellationToken ct = default)
        {
            var orderedIds = groupIds.ToList();
            if (orderedIds.Count == 0)
                return new List<AnalysisGroup>();

            var groups = await _context.AnalysisGroups
                .AsTracking()
                .Where(g => orderedIds.Contains(g.Id) && g.Status == eligibleStatus)
                .ToListAsync(ct);

            var order = orderedIds
                .Select((id, index) => new { id, index })
                .ToDictionary(x => x.id, x => x.index);

            return groups
                .OrderBy(g => order.GetValueOrDefault(g.Id, int.MaxValue))
                .ToList();
        }

        /// <summary>
        /// Check if analyst has completed ALL their assignments and trigger immediate assignment if in Auto mode
        /// </summary>
        private async Task CheckAndTriggerImmediateAssignmentAsync(string analystUsername)
        {
            try
            {
                _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Checking immediate assignment eligibility for analyst {Username}", analystUsername);
                var now = DateTime.UtcNow;
                var ct = CancellationToken.None;

                // Check if analyst has any remaining active assignments
                var remainingActiveAssignments = await _context.AnalysisAssignments
                    .Where(a => a.AssignedTo == analystUsername
                        && a.Role == "Analyst"
                        && a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .CountAsync();

                _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Analyst {Username} has {Count} remaining active assignments",
                    analystUsername, remainingActiveAssignments);

                if (remainingActiveAssignments > 0)
                {
                    _logger.LogInformation("⚠️ [IMMEDIATE-ASSIGNMENT] Analyst {Username} still has {Count} active assignments - not ready for new assignments yet",
                        analystUsername, remainingActiveAssignments);
                    return;
                }

                // ✅ All assignments complete - analyst is ready for new assignments
                _logger.LogInformation("✅ [IMMEDIATE-ASSIGNMENT] Analyst {Username} has completed ALL assigned groups - ready for new assignments", analystUsername);

                // Check if Auto mode is enabled
                var settings = await _context.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();

                _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Settings: Enabled={Enabled}, Mode={Mode}, MaxConcurrent={Max}",
                    settings.Enabled, settings.AssignmentMode, settings.MaxConcurrentPerUser);

                if (!settings.Enabled)
                {
                    _logger.LogWarning("⚠️ [IMMEDIATE-ASSIGNMENT] Assignment service is disabled - skipping immediate assignment for analyst {Username}", analystUsername);
                    return;
                }

                var assignmentMode = string.IsNullOrEmpty(settings.AssignmentMode) ? "Manual" : settings.AssignmentMode;
                if (assignmentMode != "Auto")
                {
                    _logger.LogWarning("⚠️ [IMMEDIATE-ASSIGNMENT] Assignment mode is '{Mode}' (not Auto) - skipping immediate assignment for analyst {Username}",
                        assignmentMode, analystUsername);
                    return;
                }

                // Double-check active count (race condition protection)
                var activeCount = await _context.AnalysisAssignments
                    .Where(a => a.AssignedTo == analystUsername
                        && a.Role == "Analyst"
                        && a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .CountAsync();

                if (activeCount >= settings.MaxConcurrentPerUser)
                {
                    _logger.LogWarning("⚠️ [IMMEDIATE-ASSIGNMENT] Analyst {Username} already at max concurrent assignments ({Count} >= {Max}) - race condition",
                        analystUsername, activeCount, settings.MaxConcurrentPerUser);
                    return;
                }

                var readyUsers = await GetReadyUsersForImmediateAssignmentAsync("Analyst", now, ct);
                if (!readyUsers.Contains(analystUsername, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("[IMMEDIATE-ASSIGNMENT] Analyst {Username} is not Ready for Analyst assignments - skipping", analystUsername);
                    return;
                }

                var eligibleGroups = await GetEligibleGroupsForImmediateAssignmentAsync(
                    "Analyst",
                    AnalysisStatuses.Ready,
                    settings.MaxConcurrentPerUser - activeCount,
                    ct);

                _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Found {Count} eligible Ready groups from ReadyGroupsCache path", eligibleGroups.Count);

                // Get group IDs that have active assignments
                var groupIdsWithActiveAssignments = await _context.AnalysisAssignments
                    .Where(a => a.State == "Active")
                    .Select(a => a.GroupId)
                    .Distinct()
                    .ToListAsync(ct);
                var activeGroupIdSet = groupIdsWithActiveAssignments.ToHashSet();

                _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Found {Count} groups with active assignments", groupIdsWithActiveAssignments.Count);

                // Filter out groups with active assignments and take up to max
                var readyGroupIds = eligibleGroups
                    .Where(g => !activeGroupIdSet.Contains(g.Id))
                    .Take(settings.MaxConcurrentPerUser - activeCount) // Fill up to max
                    .Select(g => g.Id)
                    .ToList();
                var readyGroups = await LoadTrackedGroupsInEligibilityOrderAsync(readyGroupIds, AnalysisStatuses.Ready, ct);

                _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Found {Count} available Ready groups for analyst {Username} (will assign up to {Max})",
                    readyGroups.Count, analystUsername, settings.MaxConcurrentPerUser - activeCount);

                if (!readyGroups.Any())
                {
                    _logger.LogWarning("⚠️ [IMMEDIATE-ASSIGNMENT] No Ready groups available for immediate assignment to analyst {Username} (Total Ready: {Total}, With Active Assignments: {Active})",
                        analystUsername, eligibleGroups.Count, groupIdsWithActiveAssignments.Count);
                    return;
                }

                // Assign groups immediately
                var assignedCount = 0;
                var createdAssignments = new List<AnalysisAssignment>();
                foreach (var group in readyGroups)
                {
                    var leaseUntil = now.AddMinutes(Math.Max(1, settings.LeaseMinutes));
                    var assignment = new AnalysisAssignment
                    {
                        GroupId = group.Id,
                        AssignedTo = analystUsername,
                        Role = "Analyst",
                        LeaseUntilUtc = leaseUntil,
                        State = "Active",
                        CreatedAtUtc = now
                    };
                    _context.AnalysisAssignments.Add(assignment);

                    // Sprint 5G2 / B1: route through the state-machine facade. Ready → AnalystAssigned
                    // is in the legal table.
                    await AnalysisGroupStateMachine.TransitionAsync(
                        _context, group, AnalysisStatuses.AnalystAssigned,
                        triggerName: "ImmediateAnalystAssignment",
                        actor: "SYSTEM-IMMEDIATE-ASSIGNMENT",
                        reason: $"Auto-assignment to analyst {analystUsername} after they completed all prior assignments.",
                        correlationId: null);
                    group.UpdatedAtUtc = now;
                    createdAssignments.Add(assignment);
                    assignedCount++;
                }

                foreach (var assignment in createdAssignments)
                {
                    await UpsertQueueEntryBestEffortAsync(assignment, ct);
                }

                if (_readyGroupsCache != null)
                {
                    await _readyGroupsCache.InvalidateCacheAsync("Analyst", AnalysisStatuses.Ready, ct);
                }

                _logger.LogInformation("✅ Immediately assigned {Count} new groups to analyst {Username} after completing all previous work",
                    assignedCount, analystUsername);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/triggering immediate assignment for analyst {Username}", analystUsername);
                // Don't throw - this is a background process, shouldn't fail the main request
            }
        }

        /// <summary>
        /// Trigger immediate audit assignment for a newly completed AnalystCompleted group
        /// </summary>
        private async Task TriggerImmediateAuditAssignmentForGroupAsync(Guid groupId)
        {
            try
            {
                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Triggering immediate audit assignment for group {GroupId}", groupId);
                var now = DateTime.UtcNow;
                var ct = CancellationToken.None;

                // Check if Auto mode is enabled
                var settings = await _context.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();

                if (!settings.Enabled || string.IsNullOrEmpty(settings.AssignmentMode) || settings.AssignmentMode != "Auto")
                {
                    _logger.LogDebug("[IMMEDIATE-AUDIT-ASSIGNMENT] Assignment mode is not Auto - skipping immediate assignment");
                    return;
                }

                // Verify group is still AnalystCompleted and has containers in Audit WorkflowStage
                var group = await _context.AnalysisGroups
                    .AsTracking()
                    .FirstOrDefaultAsync(g => g.Id == groupId);

                if (group == null || group.Status != AnalysisStatuses.AnalystCompleted)
                {
                    _logger.LogDebug("[IMMEDIATE-AUDIT-ASSIGNMENT] Group {GroupId} is not AnalystCompleted - skipping", groupId);
                    return;
                }

                // Check if group already has active audit assignment
                var hasActiveAssignment = await _context.AnalysisAssignments
                    .AnyAsync(a => a.GroupId == groupId
                        && a.Role == "Audit"
                        && a.State == "Active", ct);

                if (hasActiveAssignment)
                {
                    _logger.LogDebug("[IMMEDIATE-AUDIT-ASSIGNMENT] Group {GroupId} already has active audit assignment - skipping", groupId);
                    return;
                }

                var eligibleAuditGroups = await GetEligibleGroupsForImmediateAssignmentAsync(
                    "Audit",
                    AnalysisStatuses.AnalystCompleted,
                    settings.MaxConcurrentPerUser,
                    ct);
                if (!eligibleAuditGroups.Any(g => g.Id == groupId))
                {
                    _logger.LogDebug("[IMMEDIATE-AUDIT-ASSIGNMENT] Group {GroupId} is not eligible by ReadyGroupsCache Audit rules - skipping", groupId);
                    return;
                }

                var readyAuditUsers = await GetReadyUsersForImmediateAssignmentAsync("Audit", now, ct);
                if (!readyAuditUsers.Any())
                {
                    _logger.LogDebug("[IMMEDIATE-AUDIT-ASSIGNMENT] No ready audit users available - skipping");
                    return;
                }

                // Find user with fewest active assignments
                var userAssignmentCounts = new Dictionary<string, int>();
                foreach (var username in readyAuditUsers)
                {
                    // ✅ FIX: Load assignments first, then filter groups in memory to avoid Join() generating CTE
                    var assignments = await _context.AnalysisAssignments
                        .Where(a => a.AssignedTo == username
                            && a.Role == "Audit"
                            && a.State == "Active"
                            && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                        .Select(a => a.GroupId)
                        .ToListAsync(ct);

                    if (!assignments.Any())
                    {
                        userAssignmentCounts[username] = 0;
                        continue;
                    }

                    var validGroupIds = await _context.AnalysisGroups
                        .Where(g => assignments.Contains(g.Id) &&
                            g.Status != AnalysisStatuses.AuditCompleted
                            && g.Status != AnalysisStatuses.Completed)
                        .Select(g => g.Id)
                        .ToListAsync(ct);

                    var count = validGroupIds.Count;

                    if (count < settings.MaxConcurrentPerUser)
                    {
                        userAssignmentCounts[username] = count;
                    }
                }

                if (!userAssignmentCounts.Any())
                {
                    _logger.LogDebug("[IMMEDIATE-AUDIT-ASSIGNMENT] No audit users available (all at MaxConcurrent) - skipping");
                    return;
                }

                // Select user with fewest assignments
                var selectedUser = userAssignmentCounts
                    .OrderBy(kvp => kvp.Value)
                    .ThenBy(kvp => kvp.Key)
                    .First().Key;

                // Assign group immediately
                var leaseUntil = now.AddMinutes(Math.Max(1, settings.LeaseMinutes));
                var assignment = new AnalysisAssignment
                {
                    GroupId = groupId,
                    AssignedTo = selectedUser,
                    Role = "Audit",
                    LeaseUntilUtc = leaseUntil,
                    State = "Active",
                    CreatedAtUtc = now
                };
                _context.AnalysisAssignments.Add(assignment);

                // Sprint 5G2 / B1: route through the state-machine facade. AnalystCompleted → AuditAssigned
                // is in the legal table.
                await AnalysisGroupStateMachine.TransitionAsync(
                    _context, group, AnalysisStatuses.AuditAssigned,
                    triggerName: "ImmediateAuditAssignmentForGroup",
                    actor: "SYSTEM-IMMEDIATE-AUDIT-ASSIGNMENT",
                    reason: $"Group reached AnalystCompleted; auto-assigned to auditor {selectedUser}.",
                    correlationId: null);
                group.UpdatedAtUtc = now;

                await UpsertQueueEntryBestEffortAsync(assignment, ct);

                if (_readyGroupsCache != null)
                {
                    await _readyGroupsCache.InvalidateCacheAsync("Audit", AnalysisStatuses.AnalystCompleted, ct);
                }

                _logger.LogInformation("✅ [IMMEDIATE-AUDIT-ASSIGNMENT] Immediately assigned group {GroupId} ({GroupIdentifier}) to auditor {Username}",
                    groupId, group.GroupIdentifier, selectedUser);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error triggering immediate audit assignment for group {GroupId}", groupId);
                // Don't throw - this is a background process
            }
        }

        /// <summary>
        /// Check if auditor has completed ALL their assignments and trigger immediate assignment if in Auto mode
        /// </summary>
        private async Task CheckAndTriggerImmediateAuditAssignmentAsync(string auditorUsername)
        {
            try
            {
                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Checking immediate assignment eligibility for auditor {Username}", auditorUsername);
                var now = DateTime.UtcNow;
                var ct = CancellationToken.None;

                // Check if auditor has any remaining active assignments
                var remainingActiveAssignments = await _context.AnalysisAssignments
                    .Where(a => a.AssignedTo == auditorUsername
                        && a.Role == "Audit"
                        && a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .CountAsync();

                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Auditor {Username} has {Count} remaining active assignments",
                    auditorUsername, remainingActiveAssignments);

                if (remainingActiveAssignments > 0)
                {
                    _logger.LogInformation("⚠️ [IMMEDIATE-AUDIT-ASSIGNMENT] Auditor {Username} still has {Count} active assignments - not ready for new assignments yet",
                        auditorUsername, remainingActiveAssignments);
                    return;
                }

                // ✅ All assignments complete - auditor is ready for new assignments
                _logger.LogInformation("✅ [IMMEDIATE-AUDIT-ASSIGNMENT] Auditor {Username} has completed ALL assigned groups - ready for new assignments", auditorUsername);

                // Check if Auto mode is enabled
                var settings = await _context.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();

                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Settings: Enabled={Enabled}, Mode={Mode}, MaxConcurrent={Max}",
                    settings.Enabled, settings.AssignmentMode, settings.MaxConcurrentPerUser);

                if (!settings.Enabled)
                {
                    _logger.LogWarning("⚠️ [IMMEDIATE-AUDIT-ASSIGNMENT] Assignment service is disabled - skipping immediate assignment for auditor {Username}", auditorUsername);
                    return;
                }

                var assignmentMode = string.IsNullOrEmpty(settings.AssignmentMode) ? "Manual" : settings.AssignmentMode;
                if (assignmentMode != "Auto")
                {
                    _logger.LogWarning("⚠️ [IMMEDIATE-AUDIT-ASSIGNMENT] Assignment mode is '{Mode}' (not Auto) - skipping immediate assignment for auditor {Username}",
                        assignmentMode, auditorUsername);
                    return;
                }

                // Double-check active count (race condition protection)
                var activeCount = await _context.AnalysisAssignments
                    .Where(a => a.AssignedTo == auditorUsername
                        && a.Role == "Audit"
                        && a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .CountAsync();

                if (activeCount >= settings.MaxConcurrentPerUser)
                {
                    _logger.LogWarning("⚠️ [IMMEDIATE-AUDIT-ASSIGNMENT] Auditor {Username} already at max concurrent assignments ({Count} >= {Max}) - race condition",
                        auditorUsername, activeCount, settings.MaxConcurrentPerUser);
                    return;
                }

                var readyUsers = await GetReadyUsersForImmediateAssignmentAsync("Audit", now, ct);
                if (!readyUsers.Contains(auditorUsername, StringComparer.OrdinalIgnoreCase))
                {
                    _logger.LogInformation("[IMMEDIATE-AUDIT-ASSIGNMENT] Auditor {Username} is not Ready for Audit assignments - skipping", auditorUsername);
                    return;
                }

                var eligibleGroups = await GetEligibleGroupsForImmediateAssignmentAsync(
                    "Audit",
                    AnalysisStatuses.AnalystCompleted,
                    settings.MaxConcurrentPerUser - activeCount,
                    ct);

                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Found {Count} eligible AnalystCompleted groups from ReadyGroupsCache path", eligibleGroups.Count);

                // Get group IDs that have active assignments
                var groupIdsWithActiveAssignments = await _context.AnalysisAssignments
                    .Where(a => a.State == "Active")
                    .Select(a => a.GroupId)
                    .Distinct()
                    .ToListAsync(ct);
                var activeGroupIdSet = groupIdsWithActiveAssignments.ToHashSet();

                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Found {Count} groups with active assignments", groupIdsWithActiveAssignments.Count);

                // Filter out groups with active assignments and take up to max
                var readyGroupIds = eligibleGroups
                    .Where(g => !activeGroupIdSet.Contains(g.Id))
                    .Take(settings.MaxConcurrentPerUser - activeCount) // Fill up to max
                    .Select(g => g.Id)
                    .ToList();
                var readyGroups = await LoadTrackedGroupsInEligibilityOrderAsync(readyGroupIds, AnalysisStatuses.AnalystCompleted, ct);

                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Found {Count} available AnalystCompleted groups for auditor {Username} (will assign up to {Max})",
                    readyGroups.Count, auditorUsername, settings.MaxConcurrentPerUser - activeCount);

                if (!readyGroups.Any())
                {
                    _logger.LogWarning("⚠️ [IMMEDIATE-AUDIT-ASSIGNMENT] No AnalystCompleted groups available for immediate assignment to auditor {Username} (Total: {Total}, With Active Assignments: {Active})",
                        auditorUsername, eligibleGroups.Count, groupIdsWithActiveAssignments.Count);
                    return;
                }

                // Assign groups immediately
                var assignedCount = 0;
                var createdAssignments = new List<AnalysisAssignment>();
                foreach (var group in readyGroups)
                {
                    var leaseUntil = now.AddMinutes(Math.Max(1, settings.LeaseMinutes));
                    var assignment = new AnalysisAssignment
                    {
                        GroupId = group.Id,
                        AssignedTo = auditorUsername,
                        Role = "Audit",
                        LeaseUntilUtc = leaseUntil,
                        State = "Active",
                        CreatedAtUtc = now
                    };
                    _context.AnalysisAssignments.Add(assignment);

                    // Sprint 5G2 / B1: route through the state-machine facade. AnalystCompleted → AuditAssigned
                    // is in the legal table.
                    await AnalysisGroupStateMachine.TransitionAsync(
                        _context, group, AnalysisStatuses.AuditAssigned,
                        triggerName: "ImmediateAuditAssignment",
                        actor: "SYSTEM-IMMEDIATE-AUDIT-ASSIGNMENT",
                        reason: $"Auto-assignment to auditor {auditorUsername} after they completed all prior assignments.",
                        correlationId: null);
                    group.UpdatedAtUtc = now;
                    createdAssignments.Add(assignment);
                    assignedCount++;
                }

                foreach (var assignment in createdAssignments)
                {
                    await UpsertQueueEntryBestEffortAsync(assignment, ct);
                }

                if (_readyGroupsCache != null)
                {
                    await _readyGroupsCache.InvalidateCacheAsync("Audit", AnalysisStatuses.AnalystCompleted, ct);
                }

                _logger.LogInformation("✅ [IMMEDIATE-AUDIT-ASSIGNMENT] Immediately assigned {Count} new groups to auditor {Username} after completing all previous work",
                    assignedCount, auditorUsername);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking/triggering immediate assignment for auditor {Username}", auditorUsername);
                // Don't throw - this is a background process, shouldn't fail the main request
            }
        }

        /// <summary>
        /// Save or update suspicious area rectangles only (does not change WorkflowStage)
        /// </summary>
        [HttpPost("rectangles")]
        [HasPermission(Permissions.ImagesAnnotate)]
        public async Task<ActionResult<object>> SaveRectangles([FromBody] RectangleSaveRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ContainerNumber) || string.IsNullOrWhiteSpace(request.ScannerType))
                {
                    return BadRequest(new { success = false, error = "ContainerNumber and ScannerType are required" });
                }

                var actor = GetAuthenticatedActor();
                var decision = await _context.ImageAnalysisDecisions
                    .AsTracking()
                    .FirstOrDefaultAsync(d => d.ContainerNumber == request.ContainerNumber && d.ScannerType == request.ScannerType);

                var rectangleGroupIdentifier = string.IsNullOrWhiteSpace(request.GroupIdentifier)
                    ? decision?.GroupIdentifier
                    : request.GroupIdentifier;
                var rectangleGroup = await ResolveAnalysisGroupForDecisionAsync(rectangleGroupIdentifier, request.ScannerType);
                if (rectangleGroup == null)
                {
                    var record = await _context.AnalysisRecords
                        .AsNoTracking()
                        .Where(r => r.ContainerNumber == request.ContainerNumber
                            && (r.ScannerType == request.ScannerType || string.IsNullOrEmpty(r.ScannerType)))
                        .OrderByDescending(r => r.CreatedAtUtc)
                        .FirstOrDefaultAsync();

                    if (record != null)
                    {
                        rectangleGroup = await _context.AnalysisGroups
                            .AsTracking()
                            .FirstOrDefaultAsync(g => g.Id == record.GroupId);
                    }
                }

                var ownershipFailure = await ValidateDecisionAssignmentOwnershipAsync(rectangleGroup, nameof(SaveRectangles));
                if (ownershipFailure != null)
                {
                    return ownershipFailure;
                }

                _logger.LogInformation("[SaveRectangles] Container={Container}, Scanner={Scanner}, Existing={Exists}, AreasLength={Length}",
                    request.ContainerNumber, request.ScannerType, decision != null, request.SuspiciousAreas?.Length ?? 0);

                if (decision == null)
                {
                    // Insert using raw SQL to avoid OUTPUT + trigger conflict
                    var insertSql = @"
                        INSERT INTO imageanalysisdecisions
                            (containernumber, scannertype, decision, comments, tags, suspiciousareas, reviewedby, reviewedat, groupidentifier, isconsolidated, createdat)
                        VALUES
                            (@p0, @p1, 'Pending', NULL, NULL, @p2, @p3, @p4, @p5, @p6, now() AT TIME ZONE 'UTC');";

                    await _context.Database.ExecuteSqlRawAsync(
                        insertSql,
                        new NpgsqlParameter("@p0", request.ContainerNumber),
                        new NpgsqlParameter("@p1", request.ScannerType),
                        new NpgsqlParameter("@p2", (object?)request.SuspiciousAreas ?? DBNull.Value),
                        new NpgsqlParameter("@p3", actor),
                        new NpgsqlParameter("@p4", DateTime.UtcNow),
                        new NpgsqlParameter("@p5", (object?)request.GroupIdentifier ?? DBNull.Value),
                        new NpgsqlParameter("@p6", request.IsConsolidated)
                    );
                    _logger.LogInformation("[SaveRectangles] INSERTED new decision for {Container}", request.ContainerNumber);
                }
                else
                {
                    var nowUtc = DateTime.UtcNow;
                    decision.SuspiciousAreas = request.SuspiciousAreas;
                    decision.ReviewedBy = actor;
                    decision.ReviewedAt = nowUtc;
                    decision.UpdatedAt = nowUtc;
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("[SaveRectangles] UPDATED existing decision for {Container}, SuspiciousAreas={Areas}",
                        request.ContainerNumber, request.SuspiciousAreas?.Substring(0, Math.Min(100, request.SuspiciousAreas?.Length ?? 0)));
                }

                return Ok(new { success = true, message = "Rectangles saved" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving rectangles for {Container} {Scanner}", request.ContainerNumber, request.ScannerType);
                return StatusCode(500, new { success = false, error = "Failed to save rectangles", details = ex.Message });
            }
        }

        /// <summary>
        /// ✅ CRITICAL VALIDATION: Verify that all containers WITH images have decisions
        /// This is required before allowing group progression
        /// </summary>
        private async Task<bool> ValidateAllContainersWithImagesHaveDecisionsAsync(
            string groupIdentifier,
            List<string> groupContainers,
            string? scannerType = null,
            string? resolvedGroupIdentifierForBackwardCompat = null)
        {
            try
            {
                // ✅ FIX: Use normalized GroupIdentifier - ContainerCompletenessStatus uses base id
                var normalizedGroupId = GroupIdentifierHelper.GetNormalizedGroupIdentifier(groupIdentifier) ?? groupIdentifier;
                var escapedGroupId = (normalizedGroupId ?? "").Replace("'", "''");
                var containersWithImages = new List<string>();
                const int batchSize = 500; // Process in batches to avoid SQL parameter limits

                if (groupContainers.Count > 0)
                {
                    for (int i = 0; i < groupContainers.Count; i += batchSize)
                    {
                        var batch = groupContainers.Skip(i).Take(batchSize).ToList();

                        // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid EF Core CTE generation
                        var escapedContainerNumbers = batch.Select(cn => $"'{cn.Replace("'", "''")}'").ToList();
                        var inClause = string.Join(",", escapedContainerNumbers);

                        // Build WHERE clause - include GroupIdentifier to scope to this group
                        var whereConditions = new List<string> { $"ContainerNumber IN ({inClause})", $"GroupIdentifier = '{escapedGroupId}'", "HasImageData = true" };
                        if (!string.IsNullOrEmpty(scannerType))
                        {
                            var escapedScannerType = scannerType.Replace("'", "''");
                            whereConditions.Add($"(ScannerType = '{escapedScannerType}' OR ScannerType LIKE '{escapedScannerType}-%')");
                        }

                        var whereClause = string.Join(" AND ", whereConditions);

                        // ✅ SQL Server 2014 FIX: Semicolon prefix required before WITH clause
                        var sql = $";SELECT * FROM ContainerCompletenessStatuses WHERE {whereClause}";

                        var batchResults = await _context.ContainerCompletenessStatuses
                            .FromSqlRaw(sql)
                            .AsNoTracking()
                            .ToListAsync();

                        // Filter and project in memory (no EF Core SQL generation)
                        var batchContainerNumbers = batchResults
                            .Select(c => c.ContainerNumber)
                            .Distinct()
                            .ToList();

                        containersWithImages.AddRange(batchContainerNumbers);
                    }

                    // Remove duplicates (in case same container appears in multiple batches)
                    containersWithImages = containersWithImages.Distinct().ToList();
                }

                if (!containersWithImages.Any())
                {
                    // No containers with images - validation passes (nothing to validate)
                    return true;
                }

                // ✅ FIX AUTO-PROGRESSION: Filter decisions by scanner type if provided
                // Since groups are separated by scanner type, we should only count decisions for the group's scanner type
                // ✅ SCANNER TYPE FIX: Normalize scanner type comparison (handle "FS6000-Main" vs "FS6000")
                var decisionGroupIdentifiers = new[] { groupIdentifier, resolvedGroupIdentifierForBackwardCompat }
                    .Where(g => !string.IsNullOrWhiteSpace(g))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var decidedContainersQuery = _context.ImageAnalysisDecisions
                    .Where(d => d.GroupIdentifier != null
                        && decisionGroupIdentifiers.Contains(d.GroupIdentifier)
                        && (d.Decision == "Normal" || d.Decision == "Abnormal"));

                // Add scanner type filter if provided (normalize to handle image-specific variants)
                if (!string.IsNullOrEmpty(scannerType))
                {
                    var baseScannerType = scannerType.Split('-')[0]; // Extract base scanner type
                    decidedContainersQuery = decidedContainersQuery.Where(d =>
                        d.ScannerType == scannerType ||
                        d.ScannerType.StartsWith(baseScannerType + "-") ||
                        d.ScannerType == baseScannerType);
                }

                var decidedContainers = await decidedContainersQuery
                    .Select(d => d.ContainerNumber)
                    .Distinct()
                    .ToListAsync();

                // Find containers WITH images that are missing decisions
                var undecidedContainersWithImages = containersWithImages
                    .Where(c => !decidedContainers.Contains(c))
                    .ToList();

                if (undecidedContainersWithImages.Any())
                {
                    _logger.LogWarning("❌ [VALIDATION] Cannot progress: {Count} container(s) with images are missing decisions: {Containers} (ScannerType: {ScannerType})",
                        undecidedContainersWithImages.Count, string.Join(", ", undecidedContainersWithImages), scannerType ?? "ALL");
                    return false;
                }

                _logger.LogInformation("✅ [VALIDATION] All {Count} container(s) with images have decisions (ScannerType: {ScannerType})",
                    containersWithImages.Count, scannerType ?? "ALL");
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating containers with images have decisions for group {Group} (ScannerType: {ScannerType})",
                    groupIdentifier, scannerType ?? "ALL");
                return false; // Fail safe - don't allow progression if validation fails
            }
        }

        /// <summary>
        /// Propagate container decision to all BOEs for consolidated cargo
        /// </summary>
        private async Task PropagateDecisionToBOEsAsync(string containerNumber, string decision, string reviewedBy, string? groupIdentifier)
        {
            try
            {
                // Get all BOEs for this container (consolidated cargo)
                var boes = await _icumDb.BOEDocuments
                    .Where(b => b.ContainerNumber == containerNumber && b.IsConsolidated)
                    .ToListAsync();

                if (!boes.Any())
                {
                    _logger.LogDebug("No BOEs found for consolidated container {Container}", containerNumber);
                    return;
                }

                // For each BOE, create/update decision record
                foreach (var boe in boes)
                {
                    // Use DeclarationNumber as the container identifier for BOE decisions
                    // Note: This creates a decision per BOE, but they all inherit the container's decision
                    var boeContainerIdentifier = boe.DeclarationNumber ?? containerNumber;

                    var existingBOEDecision = await _context.ImageAnalysisDecisions
                        .FirstOrDefaultAsync(d => d.ContainerNumber == boeContainerIdentifier &&
                                                  d.GroupIdentifier == groupIdentifier);

                    if (existingBOEDecision == null)
                    {
                        // Create new decision for this BOE
                        var insertSql = @"
                            INSERT INTO ImageAnalysisDecisions
                                (ContainerNumber, ScannerType, Decision, Comments, Tags, SuspiciousAreas, ReviewedBy, ReviewedAt, GroupIdentifier, IsConsolidated, CreatedAt)
                            VALUES
                                (@p0, @p1, @p2, NULL, NULL, NULL, @p3, @p4, @p5, @p6, now() AT TIME ZONE 'UTC');";

                        await _context.Database.ExecuteSqlRawAsync(
                            insertSql,
                            new NpgsqlParameter("@p0", boeContainerIdentifier),
                            new NpgsqlParameter("@p1", "BOE"), // ScannerType for BOE decisions
                            new NpgsqlParameter("@p2", decision),
                            new NpgsqlParameter("@p3", reviewedBy),
                            new NpgsqlParameter("@p4", DateTime.UtcNow),
                            new NpgsqlParameter("@p5", (object?)groupIdentifier ?? DBNull.Value),
                            new NpgsqlParameter("@p6", true) // IsConsolidated
                        );
                    }
                    else
                    {
                        // Update existing decision
                        var updateSql = @"
                            UPDATE ImageAnalysisDecisions
                            SET Decision = @p0,
                                ReviewedBy = @p1,
                                ReviewedAt = @p2,
                                UpdatedAt = now() AT TIME ZONE 'UTC'
                            WHERE Id = @p3;";

                        await _context.Database.ExecuteSqlRawAsync(
                            updateSql,
                            new NpgsqlParameter("@p0", decision),
                            new NpgsqlParameter("@p1", reviewedBy),
                            new NpgsqlParameter("@p2", DateTime.UtcNow),
                            new NpgsqlParameter("@p3", existingBOEDecision.Id)
                        );
                    }
                }

                _logger.LogInformation("Propagated decision '{Decision}' to {Count} BOEs for consolidated container {Container}",
                    decision, boes.Count, containerNumber);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error propagating decision to BOEs for container {Container}", containerNumber);
                // Don't throw - this is a best-effort operation
            }
        }

        /// <summary>
        /// Save or update an image decision
        /// </summary>
        [HttpPost]
        [HasPermission(Permissions.ControllersImageAnalysisDecisionAnalyst)]
        public async Task<ActionResult<object>> SaveDecision([FromBody] ImageDecisionRequest request)
        {
            try
            {
                // Basic validation
                if (string.IsNullOrWhiteSpace(request.ContainerNumber) || string.IsNullOrWhiteSpace(request.ScannerType))
                {
                    return BadRequest(new { success = false, error = "ContainerNumber and ScannerType are required" });
                }

                // Capture original nulls before sanitization (null = "not provided by caller")
                bool tagsProvided = request.Tags != null;
                bool areasProvided = request.SuspiciousAreas != null;

                // Sanitize inputs to satisfy DB length constraints
                request.ContainerNumber = (request.ContainerNumber ?? string.Empty).Trim();
                request.ScannerType = (request.ScannerType ?? string.Empty).Trim();
                request.Decision = (request.Decision ?? string.Empty).Trim();
                request.Comments = Truncate((request.Comments ?? string.Empty).Trim(), 500);
                request.Tags = Truncate((request.Tags ?? string.Empty).Trim(), 500);
                request.GroupIdentifier = Truncate((request.GroupIdentifier ?? string.Empty).Trim(), 100);
                request.SplitSide = Truncate((request.SplitSide ?? string.Empty).Trim(), 10);
                request.ReviewedBy = GetAuthenticatedActor();

                // ✅ BULLETPROOF FIX: Validate and normalize decision value
                // Normalize to proper case and validate it's a valid decision
                var normalizedDecision = string.IsNullOrWhiteSpace(request.Decision)
                    ? null
                    : request.Decision.Trim();

                if (normalizedDecision != null)
                {
                    // Normalize case: "normal" -> "Normal", "abnormal" -> "Abnormal"
                    if (string.Equals(normalizedDecision, "normal", StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedDecision = "Normal";
                    }
                    else if (string.Equals(normalizedDecision, "abnormal", StringComparison.OrdinalIgnoreCase))
                    {
                        normalizedDecision = "Abnormal";
                    }
                    else if (string.Equals(normalizedDecision, "pending", StringComparison.OrdinalIgnoreCase))
                    {
                        // Reject "Pending" as an invalid decision - users must choose Normal or Abnormal
                        _logger.LogWarning("Rejecting 'Pending' decision for {Container} {Scanner} - user must choose Normal or Abnormal",
                            request.ContainerNumber, request.ScannerType);
                        return BadRequest(new { success = false, error = "Invalid decision: 'Pending' is not allowed. Please choose 'Normal' or 'Abnormal'." });
                    }
                    else
                    {
                        // Unknown decision value
                        _logger.LogWarning("Invalid decision value '{Decision}' for {Container} {Scanner} - must be 'Normal' or 'Abnormal'",
                            normalizedDecision, request.ContainerNumber, request.ScannerType);
                        return BadRequest(new { success = false, error = $"Invalid decision: '{normalizedDecision}'. Must be 'Normal' or 'Abnormal'." });
                    }
                }

                // Resolve the underlying AnalysisRecord before completeness checks
                // and split gates. Group-level views can pass a wave/group id as
                // ContainerNumber, but analyst decisions must be stored against
                // the actual child container record.
                var arForSplit = await ResolveAnalysisRecordForDecisionAsync(request);
                if (arForSplit != null
                    && !string.Equals(request.ContainerNumber, arForSplit.ContainerNumber, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "Decision save remapped request container {RequestedContainer} to analysis record container {RecordContainer} (analysisRecordId={AnalysisRecordId}, group={GroupIdentifier})",
                        request.ContainerNumber,
                        arForSplit.ContainerNumber,
                        arForSplit.Id,
                        request.GroupIdentifier);
                    request.ContainerNumber = arForSplit.ContainerNumber;
                }

                if (arForSplit != null)
                {
                    await ApplySplitSelectionFromDecisionRequestAsync(arForSplit, request);
                }

                // Prefer to allow saving even if completeness record is missing (log only)
                var hasContainer = await _context.ContainerCompletenessStatuses
                    .AnyAsync(s => s.ContainerNumber == request.ContainerNumber);
                if (!hasContainer)
                {
                    _logger.LogWarning("Saving decision for container not found in completeness tracking: {Container}", request.ContainerNumber);
                }

                // ── Multi-container split gate + lineage capture (2026-05-07) ──
                // Load the analysisrecord up front so we can both block decisions on
                // multi-container records that haven't picked a split, AND copy the
                // split-job/result IDs onto the persisted IAD so the audit trail
                // captures which crop the analyst was looking at.
                if (arForSplit != null
                    && arForSplit.IsMultiContainerScan
                    && string.Equals(arForSplit.SplitStatus, "Ready", StringComparison.OrdinalIgnoreCase)
                    && arForSplit.SplitResultId == null)
                {
                    _logger.LogWarning(
                        "Decision blocked for {Container}: multi-container scan with split candidates ready but no choice made (jobId={JobId})",
                        request.ContainerNumber, arForSplit.SplitJobId);
                    return BadRequest(new
                    {
                        success = false,
                        error = "Multi-container scan: pick the correct split crop (Option A or Option B) or click \"Skip — Use Original Combined Image\" before saving the decision.",
                        code = "split_choice_required"
                    });
                }

                Guid? splitJobIdForIad = arForSplit?.SplitJobId;
                Guid? splitResultIdForIad = arForSplit?.SplitResultId;

                // ✅ DATE-BASED GROUPING FIX: Extract original GroupIdentifier BEFORE saving decision
                // Format: "OriginalGroupIdentifier_YYYYMMDD_YYYYMMDD"
                // We normalize to original GroupIdentifier for storage to match ContainerCompletenessStatus
                string? resolvedGroupIdentifier = request.GroupIdentifier;
                string originalGroupIdentifier = resolvedGroupIdentifier ?? string.Empty;
                AnalysisGroup? analysisGroup = null; // ✅ Declare at outer scope to avoid conflicts

                // If GroupIdentifier is missing, try to find it from AnalysisRecords
                if (string.IsNullOrWhiteSpace(resolvedGroupIdentifier))
                {
                    var record = await _context.AnalysisRecords
                        .Where(r => r.ContainerNumber == request.ContainerNumber &&
                                   (r.ScannerType == request.ScannerType || string.IsNullOrEmpty(r.ScannerType)))
                        .FirstOrDefaultAsync();

                    if (record != null)
                    {
                        analysisGroup = await _context.AnalysisGroups
                            .AsTracking()
                            .FirstOrDefaultAsync(g => g.Id == record.GroupId);
                        if (analysisGroup != null)
                        {
                            resolvedGroupIdentifier = analysisGroup.GroupIdentifier;
                            _logger.LogInformation("Resolved missing GroupIdentifier for container {Container}: {GroupId}",
                                request.ContainerNumber, resolvedGroupIdentifier);
                        }
                    }
                }

                // Extract original GroupIdentifier if it has date suffix
                if (!string.IsNullOrEmpty(resolvedGroupIdentifier) && resolvedGroupIdentifier.Contains("_") && resolvedGroupIdentifier.Length > 17)
                {
                    // Check if it ends with date pattern (YYYYMMDD_YYYYMMDD = 17 chars)
                    var lastUnderscoreIndex = resolvedGroupIdentifier.LastIndexOf("_");
                    var secondLastUnderscoreIndex = resolvedGroupIdentifier.LastIndexOf("_", lastUnderscoreIndex - 1);
                    if (secondLastUnderscoreIndex > 0 &&
                        lastUnderscoreIndex - secondLastUnderscoreIndex == 9 && // "_YYYYMMDD"
                        resolvedGroupIdentifier.Length - lastUnderscoreIndex == 9) // "_YYYYMMDD" at end
                    {
                        // Extract original GroupIdentifier (everything before the date suffix)
                        originalGroupIdentifier = resolvedGroupIdentifier.Substring(0, secondLastUnderscoreIndex);
                        _logger.LogInformation("🔍 [AUTO-PROGRESSION] Normalizing GroupIdentifier: {Original} from modified: {Modified} (for decision storage)",
                            originalGroupIdentifier, resolvedGroupIdentifier);
                    }
                    else
                    {
                        originalGroupIdentifier = resolvedGroupIdentifier;
                    }
                }
                else if (!string.IsNullOrEmpty(resolvedGroupIdentifier))
                {
                    originalGroupIdentifier = resolvedGroupIdentifier;
                }

                // ✅ DATE-BASED GROUPING FIX: Use original GroupIdentifier for decision storage
                // This ensures decisions match ContainerCompletenessStatus records (which use original GroupIdentifier)
                var normalizedGroupIdentifierForStorage = originalGroupIdentifier;

                if (analysisGroup == null && !string.IsNullOrWhiteSpace(resolvedGroupIdentifier))
                {
                    analysisGroup = await ResolveAnalysisGroupForDecisionAsync(resolvedGroupIdentifier, request.ScannerType);
                }

                var ownershipFailure = await ValidateDecisionAssignmentOwnershipAsync(analysisGroup, nameof(SaveDecision));
                if (ownershipFailure != null)
                {
                    return ownershipFailure;
                }

                var decisionGroupIdentifiersForSave = BuildDecisionGroupIdentifiers(
                    normalizedGroupIdentifierForStorage,
                    resolvedGroupIdentifier,
                    analysisGroup?.GroupIdentifier,
                    analysisGroup?.NormalizedGroupIdentifier);

                var strategy = _context.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync<ActionResult<object>>(async () =>
                {
                using var tx = await _context.Database.BeginTransactionAsync();

                var existing = await FindExistingDecisionForSaveAsync(
                    request.ContainerNumber,
                    request.ScannerType,
                    decisionGroupIdentifiersForSave);

                // Captured below in both branches so the manifest-snapshot hook
                // (Gap 0) can fire against the right decision id after the upsert.
                int decisionIdForSnapshot = 0;

                if (existing != null)
                {
                    // ✅ BULLETPROOF FIX: Use normalized decision, reject null/empty
                    if (normalizedDecision == null)
                    {
                        _logger.LogWarning("Cannot update decision to null for {Container} {Scanner} - keeping existing decision: {Existing}",
                            request.ContainerNumber, request.ScannerType, existing.Decision);
                        return BadRequest(new { success = false, error = "Decision is required. Please choose 'Normal' or 'Abnormal'." });
                    }

                    var newDecision = normalizedDecision; // Use normalized value

                    _logger.LogInformation("Updating decision for {Container} {Scanner}: '{Old}' -> '{New}' by {User} | Tags: {Tags} | Comments: {Comments}",
                        request.ContainerNumber, request.ScannerType, existing.Decision ?? "Pending", newDecision, request.ReviewedBy,
                        request.Tags ?? "(none)", request.Comments ?? "(none)");
                    // ✅ Gap 1a — AI training flywheel: persist optional structured finding categories.
                    // Caller may omit either or both ids; existing values are preserved when not supplied.
                    var effectiveThreatCategoryId = request.ThreatCategoryId ?? existing.ThreatCategoryId;
                    var effectiveRevenueAnomalyCategoryId = request.RevenueAnomalyCategoryId ?? existing.RevenueAnomalyCategoryId;

                    // SplitJobId/SplitResultId: prefer the values from the live analysisrecord
                    // when present (analyst's current pick) but preserve the existing IAD values
                    // if the analysisrecord doesn't have them (e.g. re-decision on a non-split path).
                    var effectiveSplitJobId = splitJobIdForIad ?? existing.SplitJobId;
                    var effectiveSplitResultId = splitResultIdForIad ?? existing.SplitResultId;

                    var updateSql = @"
                        UPDATE ImageAnalysisDecisions
                        SET Decision = @p0,
                            Comments = @p1,
                            Tags = @p2,
                            SuspiciousAreas = @p3,
                            ReviewedBy = @p4,
                            ReviewedAt = @p5,
                            UpdatedAt = now() AT TIME ZONE 'UTC',
                            GroupIdentifier = @p6,
                            IsConsolidated = @p7,
                            ThreatCategoryId = @p8,
                            RevenueAnomalyCategoryId = @p9,
                            SplitJobId = @p11,
                            SplitResultId = @p12
                        WHERE Id = @p10;";

                    var effectiveTags = tagsProvided ? request.Tags : existing.Tags;
                    var effectiveAreas = areasProvided ? request.SuspiciousAreas : existing.SuspiciousAreas;

                    await _context.Database.ExecuteSqlRawAsync(
                        updateSql,
                        new NpgsqlParameter("@p0", newDecision),
                        new NpgsqlParameter("@p1", (object?)request.Comments ?? DBNull.Value),
                        new NpgsqlParameter("@p2", (object?)effectiveTags ?? DBNull.Value),
                        new NpgsqlParameter("@p3", (object?)effectiveAreas ?? DBNull.Value),
                        new NpgsqlParameter("@p4", string.IsNullOrWhiteSpace(request.ReviewedBy) ? "System" : request.ReviewedBy),
                        new NpgsqlParameter("@p5", DateTime.UtcNow),
                        new NpgsqlParameter("@p6", (object?)normalizedGroupIdentifierForStorage ?? DBNull.Value),
                        new NpgsqlParameter("@p7", request.IsConsolidated),
                        new NpgsqlParameter("@p8", (object?)effectiveThreatCategoryId ?? DBNull.Value),
                        new NpgsqlParameter("@p9", (object?)effectiveRevenueAnomalyCategoryId ?? DBNull.Value),
                        new NpgsqlParameter("@p10", existing.Id),
                        new NpgsqlParameter("@p11", (object?)effectiveSplitJobId ?? DBNull.Value),
                        new NpgsqlParameter("@p12", (object?)effectiveSplitResultId ?? DBNull.Value)
                    );

                    decisionIdForSnapshot = existing.Id;
                }
                else
                {
                    // ✅ BULLETPROOF FIX: Require valid decision for new records
                    if (normalizedDecision == null)
                    {
                        _logger.LogWarning("Cannot create new decision with null value for {Container} {Scanner}",
                            request.ContainerNumber, request.ScannerType);
                        return BadRequest(new { success = false, error = "Decision is required. Please choose 'Normal' or 'Abnormal'." });
                    }

                    var newDecision = normalizedDecision; // Use normalized value

                    _logger.LogInformation("Creating new decision for {Container} {Scanner}: '{Decision}' by {User} | Tags: {Tags} | Comments: {Comments}",
                        request.ContainerNumber, request.ScannerType, newDecision, request.ReviewedBy,
                        request.Tags ?? "(none)", request.Comments ?? "(none)");

                    // Create new (raw SQL to avoid OUTPUT + trigger conflict)
                    // ✅ Gap 1a — AI training flywheel: include optional structured finding category FK ids.
                    // 2026-05-07 — also persist SplitJobId/SplitResultId so the IAD captures
                    // which split crop the analyst was looking at (audit lineage).
                    var insertSql = @"
                        INSERT INTO ImageAnalysisDecisions
                            (ContainerNumber, ScannerType, Decision, Comments, Tags, SuspiciousAreas, ReviewedBy, ReviewedAt, GroupIdentifier, IsConsolidated, ThreatCategoryId, RevenueAnomalyCategoryId, SplitJobId, SplitResultId, CreatedAt)
                        VALUES
                            (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, @p12, @p13, now() AT TIME ZONE 'UTC');";

                    // ✅ BULLETPROOF FIX: Use normalized decision (never null due to validation above)
                    await _context.Database.ExecuteSqlRawAsync(
                        insertSql,
                        new NpgsqlParameter("@p0", request.ContainerNumber),
                        new NpgsqlParameter("@p1", request.ScannerType),
                        new NpgsqlParameter("@p2", newDecision), // ✅ Use normalized value
                        new NpgsqlParameter("@p3", (object?)request.Comments ?? DBNull.Value),
                        new NpgsqlParameter("@p4", (object?)request.Tags ?? DBNull.Value),
                        new NpgsqlParameter("@p5", (object?)request.SuspiciousAreas ?? DBNull.Value),
                        new NpgsqlParameter("@p6", string.IsNullOrWhiteSpace(request.ReviewedBy) ? "System" : request.ReviewedBy),
                        new NpgsqlParameter("@p7", DateTime.UtcNow),
                        new NpgsqlParameter("@p8", (object?)normalizedGroupIdentifierForStorage ?? DBNull.Value), // ✅ Use normalized GroupIdentifier
                        new NpgsqlParameter("@p9", request.IsConsolidated),
                        new NpgsqlParameter("@p10", (object?)request.ThreatCategoryId ?? DBNull.Value),
                        new NpgsqlParameter("@p11", (object?)request.RevenueAnomalyCategoryId ?? DBNull.Value),
                        new NpgsqlParameter("@p12", (object?)splitJobIdForIad ?? DBNull.Value),
                        new NpgsqlParameter("@p13", (object?)splitResultIdForIad ?? DBNull.Value)
                    );

                    // Resolve the just-inserted id by container/scanner/group. Container
                    // numbers can recur across groups, so the group predicate is required
                    // to avoid attaching snapshots or annotations to an older wave.
                    decisionIdForSnapshot = await _context.ImageAnalysisDecisions
                        .Where(d => d.ContainerNumber == request.ContainerNumber &&
                                    d.ScannerType == request.ScannerType &&
                                    d.GroupIdentifier == normalizedGroupIdentifierForStorage)
                        .OrderByDescending(d => d.Id)
                        .Select(d => d.Id)
                        .FirstOrDefaultAsync();
                }

                // No SaveChanges needed; using raw SQL for both insert and update paths

                // ── Gap 0: capture a manifest snapshot bound to this decision id.
                // Best-effort by design: a snapshot failure (missing BOE link, ICUMS
                // unreachable, persistence error) must never block the analyst's
                // SaveDecision. The snapshot service absorbs ICUMS failures and
                // records them as "no_data" rows so gaps stay visible to training-data
                // curation.
                if (decisionIdForSnapshot > 0)
                {
                    try
                    {
                        await _manifestSnapshot.CaptureAsync(
                            decisionIdForSnapshot,
                            request.ContainerNumber,
                            request.ScannerType);
                    }
                    catch (Exception snapshotEx)
                    {
                        _logger.LogWarning(snapshotEx,
                            "ManifestSnapshot capture failed for decision {DecisionId} ({Container}/{Scanner}); decision still saved",
                            decisionIdForSnapshot, request.ContainerNumber, request.ScannerType);
                    }
                }

                // ── Gap 2: dual-write SuspiciousAreas JSON into typed ContainerAnnotation rows.
                // The JSON column stays as the source of truth for the existing drawing
                // tools / image overlays during the transition. The typed rows give the
                // COCO export and any future per-decision query a clean join, with the
                // decision's finding categories propagated onto each box. Best-effort:
                // a parser or persistence failure logs a warning but never blocks the
                // analyst's SaveDecision. Idempotent: any prior typed annotations linked
                // to this decision are removed first so re-saves don't duplicate.
                //
                // 1.12.0 fixes:
                //  - Removed the ThreatCategoryId/RevenueAnomalyCategoryId precondition.
                //    Historical decisions and uncategorised newer ones still need typed
                //    rows so the JSON column can eventually be dropped; the COCO export
                //    already filters by category at read time.
                //  - JSON property lookup is now case-insensitive (existing prod data
                //    uses PascalCase keys X/Y/Width/Height; the prior lowercase-only
                //    lookup silently wrote zero rows for every decision since 1.10.0).
                if (decisionIdForSnapshot > 0
                    && areasProvided
                    && !string.IsNullOrWhiteSpace(request.SuspiciousAreas))
                {
                    try
                    {
                        var priorTyped = await _context.ContainerAnnotations
                            .Where(a => a.ImageAnalysisDecisionId == decisionIdForSnapshot && !a.IsDeleted)
                            .ToListAsync();
                        if (priorTyped.Any())
                        {
                            _context.ContainerAnnotations.RemoveRange(priorTyped);
                        }

                        // v2.9.6: capture the dimensions of the image these
                        // annotations were drawn against. The frontend stores
                        // rects as 0-1 fractions for new annotations, so x/y/w/h
                        // here are in [0,1]; the coord space dims let
                        // downstream consumers (CocoExport, future per-decision
                        // scalers) know which image dimensions the fractions
                        // map to — important now that FS6000 can be served as
                        // a 16-bit composite (~3256 wide) or vendor JPEG
                        // (~2295 wide) for the same container.
                        ServedImageDimensions? servedDims = null;
                        try
                        {
                            servedDims = await _imageProcessingService.GetServedImageDimensionsAsync(request.ContainerNumber);
                        }
                        catch (Exception dimsEx)
                        {
                            _logger.LogWarning(dimsEx,
                                "v2.9.6 served-image-dimension lookup failed for decision {DecisionId} ({Container}); saving annotation rows without coord-space tag",
                                decisionIdForSnapshot, request.ContainerNumber);
                        }

                        using var areasDoc = System.Text.Json.JsonDocument.Parse(request.SuspiciousAreas);
                        if (areasDoc.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                        {
                            var nowUtc = DateTime.UtcNow;
                            foreach (var rect in areasDoc.RootElement.EnumerateArray())
                            {
                                if (rect.ValueKind != System.Text.Json.JsonValueKind.Object) continue;

                                if (!TryReadDouble(rect, "x", out var x)) continue;
                                if (!TryReadDouble(rect, "y", out var y)) continue;
                                if (!TryReadDouble(rect, "width", out var w)) continue;
                                if (!TryReadDouble(rect, "height", out var h)) continue;

                                _context.ContainerAnnotations.Add(new ContainerAnnotation
                                {
                                    ContainerNumber = request.ContainerNumber,
                                    Type = "Rectangle",
                                    X1 = x,
                                    Y1 = y,
                                    X2 = x + w,
                                    Y2 = y + h,
                                    Color = "#ff0000",
                                    Width = 2,
                                    CreatedAt = nowUtc,
                                    CreatedBy = string.IsNullOrWhiteSpace(request.ReviewedBy) ? "System" : request.ReviewedBy,
                                    IsDeleted = false,
                                    ImageAnalysisDecisionId = decisionIdForSnapshot,
                                    ThreatCategoryId = request.ThreatCategoryId,
                                    RevenueAnomalyCategoryId = request.RevenueAnomalyCategoryId,
                                    // v2.9.6: coord-space provenance (null if lookup failed)
                                    CoordSpaceWidth = servedDims?.Width > 0 ? servedDims.Width : (int?)null,
                                    CoordSpaceHeight = servedDims?.Height > 0 ? servedDims.Height : (int?)null,
                                });
                            }
                            await _context.SaveChangesAsync();
                        }
                    }
                    catch (Exception annEx)
                    {
                        _logger.LogWarning(annEx,
                            "Gap 2 dual-write of typed ContainerAnnotation rows failed for decision {DecisionId} ({Container}); JSON column still authoritative",
                            decisionIdForSnapshot, request.ContainerNumber);
                    }
                }

                // ✅ Centralized side effects: update AnalysisRecord, release assignments, advance workflow
                if (normalizedDecision != null && !string.IsNullOrWhiteSpace(request.ContainerNumber))
                {
                    await _decisionSideEffects.ApplyAsync(_context, request.ContainerNumber,
                        resolvedGroupIdentifier ?? request.GroupIdentifier ?? "", request.ScannerType);
                }

                // ✅ AUTO-PROGRESSION: Handle consolidated cargo BOE propagation and find next container
                string? nextContainerNumber = null;
                bool allContainersDecided = false;
                bool backFilledRecordsRequireDecisions = false; // ✅ FIX LATE-ARRIVING DROP: blocks AnalystCompleted transition when back-fill added new Ready records
                var advanced = false;
                Guid? pendingAuditGroupId = null;

                // ✅ CRITICAL FIX: Look up AnalysisGroup for auto-progression (resolvedGroupIdentifier and analysisGroup already set above)
                // If analysisGroup wasn't found earlier, try to find it now

                if (!string.IsNullOrWhiteSpace(resolvedGroupIdentifier))
                {
                    try
                    {
                        // ✅ STEP 1: For consolidated cargo, propagate container decision to all BOEs
                        // ✅ DATE-BASED GROUPING FIX: Use normalized GroupIdentifier (original, without date suffix)
                        // originalGroupIdentifier is already extracted above and stored in normalizedGroupIdentifierForStorage
                        // ✅ DATE-BASED GROUPING FIX: Use normalized GroupIdentifier (original, without date suffix)
                        if (request.IsConsolidated && normalizedDecision != null)
                        {
                            await PropagateDecisionToBOEsAsync(request.ContainerNumber, normalizedDecision, request.ReviewedBy, normalizedGroupIdentifierForStorage);
                        }

                        // ✅ STEP 2: Get all containers in the group
                        // ✅ CRITICAL FIX: Use ContainerCompletenessStatus as PRIMARY source - it has ALL containers.
                        // AnalysisRecords can be incomplete (e.g. only 1 of 2 containers), causing "two containers one image"
                        // groups to stick because we miss the container-without-image in the progression check.
                        if (analysisGroup == null)
                        {
                            analysisGroup = await ResolveAnalysisGroupForDecisionAsync(resolvedGroupIdentifier, request.ScannerType);
                        }

                        var normalizedForCompleteness = GroupIdentifierHelper.GetNormalizedGroupIdentifier(resolvedGroupIdentifier) ?? resolvedGroupIdentifier;
                        var groupScannerType = analysisGroup?.ScannerType ?? request.ScannerType;
                        List<string> groupContainers = new();

                        // Primary: ContainerCompletenessStatus (source of truth, normalized GroupIdentifier)
                        // Filter by ScannerType so we get containers for this group's scanner only
                        groupContainers = await _context.ContainerCompletenessStatuses
                            .Where(s => s.GroupIdentifier == normalizedForCompleteness &&
                                       (s.ScannerType == groupScannerType || (s.ScannerType != null && s.ScannerType.StartsWith(groupScannerType + "-"))))
                            .Select(s => s.ContainerNumber!)
                            .Where(c => !string.IsNullOrEmpty(c))
                            .Distinct()
                            .OrderBy(c => c)
                            .ToListAsync();

                        if (groupContainers.Count == 0 && analysisGroup != null)
                        {
                            // Fallback: AnalysisRecords (in case CCStatus not yet synced)
                            groupContainers = await _context.AnalysisRecords
                                .Where(r => r.GroupId == analysisGroup.Id)
                                .Select(r => r.ContainerNumber)
                                .Where(c => !string.IsNullOrEmpty(c))
                                .Distinct()
                                .OrderBy(c => c)
                                .ToListAsync();
                            _logger.LogInformation("🔍 [AUTO-PROGRESSION] Group {Group} has {Count} containers from AnalysisRecords (fallback): {Containers}",
                                resolvedGroupIdentifier, groupContainers.Count, string.Join(", ", groupContainers));
                        }
                        else
                        {
                            _logger.LogInformation("🔍 [AUTO-PROGRESSION] Group {Group} has {Count} containers from ContainerCompletenessStatuses: {Containers}",
                                resolvedGroupIdentifier, groupContainers.Count, string.Join(", ", groupContainers));
                        }

                        // ✅ DECLARE at higher scope for use in status update section
                        List<string> containersWithImages = new();
                        List<string> containersWithoutImages = new();

                        // ✅ PRIORITY 1.1: Validate groupContainers is not empty
                        if (!groupContainers.Any())
                        {
                            _logger.LogError("❌ [AUTO-PROGRESSION] Group {Group} has no containers - cannot determine next container. This indicates a data integrity issue.",
                                resolvedGroupIdentifier);

                            // Set allContainersDecided = true with warning message
                            allContainersDecided = true;
                            nextContainerNumber = null;

                            // Return error response with warning
                            await tx.CommitAsync();
                            await NotifyAiLineageAsync(request, normalizedDecision, normalizedGroupIdentifierForStorage);
                            return Ok(new
                            {
                                success = true,
                                advancedToAudit = false,
                                allContainersDecided = true,
                                nextContainerNumber = (string?)null,
                                message = "Warning: Group has no containers. This may indicate a data integrity issue. Decision saved successfully.",
                                warning = "Group has no containers - cannot determine progression. Please contact support if this persists."
                            });
                        }
                        else
                        {
                            // ✅ STEP 3: Get containers with decisions in the group
                            // groupScannerType already set above
                            // ✅ DATE-BASED GROUPING FIX: Use original GroupIdentifier for decision queries
                            // Decisions are stored with original GroupIdentifier (normalized when saved), not the modified one with date suffix
                            // Also check modified GroupIdentifier for backward compatibility (in case some decisions were saved before normalization)
                            var decisionGroupIdentifier = originalGroupIdentifier;

                            // ✅ CRITICAL FIX: Only count containers with valid decisions (Normal/Abnormal, not Pending) for the group's scanner type
                            // Check both original and modified GroupIdentifier for backward compatibility
                            // ✅ SCANNER TYPE FIX: Decisions may have image-specific scanner types (e.g., "FS6000-Main", "FS6000-Side")
                            // but groups use base scanner type (e.g., "FS6000"). Normalize by checking if decision ScannerType starts with group ScannerType
                            var decidedContainersByGroupId = await _context.ImageAnalysisDecisions
                                .Where(d => (d.GroupIdentifier == decisionGroupIdentifier || d.GroupIdentifier == resolvedGroupIdentifier) &&
                                           (d.ScannerType == groupScannerType || d.ScannerType.StartsWith(groupScannerType + "-")) &&  // ✅ Match base scanner type or image-specific variants
                                           (d.Decision == "Normal" || d.Decision == "Abnormal"))
                                .Select(d => d.ContainerNumber)
                                .Distinct()
                                .ToListAsync();

                            _logger.LogInformation("🔍 [AUTO-PROGRESSION] Found {Count} decided containers by GroupIdentifier+ScannerType ({ScannerType}): {Containers} (Query used: original={Original}, modified={Modified})",
                                decidedContainersByGroupId.Count, groupScannerType, string.Join(", ", decidedContainersByGroupId), decisionGroupIdentifier, resolvedGroupIdentifier);

                            // ✅ FIX: Load all decisions for matching scanner type first, then filter in memory to avoid CTE
                            // ✅ SCANNER TYPE FIX: Normalize scanner type comparison (handle "FS6000-Main" vs "FS6000")
                            var groupContainersHashSet = new HashSet<string>(groupContainers, StringComparer.OrdinalIgnoreCase);
                            var baseScannerType = request.ScannerType.Split('-')[0]; // Extract base scanner type (e.g., "FS6000" from "FS6000-Main")
                            var decisionGroupIdentifiers = new[] { decisionGroupIdentifier, resolvedGroupIdentifier }
                                .Where(g => !string.IsNullOrWhiteSpace(g))
                                .Distinct(StringComparer.OrdinalIgnoreCase)
                                .ToList();
                            var allMatchingDecisions = await _context.ImageAnalysisDecisions
                                .Where(d => (d.ScannerType == request.ScannerType || d.ScannerType.StartsWith(baseScannerType + "-") || d.ScannerType == baseScannerType) &&
                                           (d.GroupIdentifier == null || decisionGroupIdentifiers.Contains(d.GroupIdentifier)) &&
                                           (d.Decision == "Normal" || d.Decision == "Abnormal"))
                                .Select(d => d.ContainerNumber)
                                .Distinct()
                                .ToListAsync();

                            // Filter in memory to avoid CTE generation
                            var decidedContainersByMatch = allMatchingDecisions
                                .Where(c => groupContainersHashSet.Contains(c))
                                .ToList();

                            _logger.LogInformation("🔍 [AUTO-PROGRESSION] Found {Count} decided containers by ScannerType match: {Containers}",
                                decidedContainersByMatch.Count, string.Join(", ", decidedContainersByMatch));

                            // Combine both lists (decisions with matching GroupIdentifier+ScannerType OR matching ContainerNumber+ScannerType)
                            // Since both queries now filter by scanner type, this ensures consistency
                            var decidedContainers = decidedContainersByGroupId
                                .Union(decidedContainersByMatch)
                                .Distinct()
                                .ToList();

                            // ✅ FIX AUTO-PROGRESSION: Log scanner type distribution for debugging
                            // Since groups are separated by scanner type, all decisions should match the group's scanner type
                            // ✅ DATE-BASED GROUPING FIX: Use original GroupIdentifier for decision queries
                            var decisionsByScannerType = await _context.ImageAnalysisDecisions
                                .Where(d => d.GroupIdentifier == decisionGroupIdentifier &&
                                           (d.Decision == "Normal" || d.Decision == "Abnormal"))
                                .GroupBy(d => d.ScannerType)
                                .Select(g => new { ScannerType = g.Key ?? "NULL", Count = g.Count() })
                                .ToListAsync();

                            if (decisionsByScannerType.Any())
                            {
                                _logger.LogInformation("🔍 [AUTO-PROGRESSION] Decisions by scanner type: {ScannerTypes} (Group scanner type: {GroupScannerType})",
                                    string.Join(", ", decisionsByScannerType.Select(s => $"{s.ScannerType}={s.Count}")), groupScannerType);
                            }

                            _logger.LogInformation("🔍 [AUTO-PROGRESSION] Total decided containers: {DecidedCount}/{TotalCount} - {Decided} | Undecided: {Undecided}",
                                decidedContainers.Count, groupContainers.Count,
                                string.Join(", ", decidedContainers),
                                string.Join(", ", groupContainers.Except(decidedContainers)));

                            // ✅ STEP 4: Get containers with images (filter out containers without images from auto-progression)
                            // ✅ FIX: Filter by GroupIdentifier so we only get HasImageData for THIS group (prevents cross-group contamination)
                            var escapedGroupId = (normalizedForCompleteness ?? "").Replace("'", "''");
                            containersWithImages = new List<string>();
                            const int batchSize = 500; // Process in batches to avoid SQL parameter limits

                            if (groupContainers.Count > 0)
                            {
                                for (int i = 0; i < groupContainers.Count; i += batchSize)
                                {
                                    var batch = groupContainers.Skip(i).Take(batchSize).ToList();

                                    // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid EF Core CTE generation
                                    // Escape container numbers for SQL injection safety
                                    var escapedContainerNumbers = batch.Select(cn => $"'{cn.Replace("'", "''")}'").ToList();
                                    var inClause = string.Join(",", escapedContainerNumbers);
                                    var escapedScannerType = (groupScannerType ?? "").Replace("'", "''");

                                    // ✅ SQL Server 2014 FIX: Semicolon prefix required before WITH clause
                                    // ✅ CRITICAL: Include GroupIdentifier to scope to this group only (e.g. 40825534001 with 2 containers, 1 with image)
                                    // ✅ SCANNER TYPE FIX: Match base scanner type or image-specific variants (e.g. FS6000 matches FS6000-Main)
                                    var sql = $";SELECT * FROM ContainerCompletenessStatuses WHERE ContainerNumber IN ({inClause}) AND GroupIdentifier = '{escapedGroupId}' AND (ScannerType = '{escapedScannerType}' OR ScannerType LIKE '{escapedScannerType}-%') AND HasImageData = true";

                                    var batchResults = await _context.ContainerCompletenessStatuses
                                        .FromSqlRaw(sql)
                                        .AsNoTracking()
                                        .ToListAsync();

                                    // Filter and project in memory (no EF Core SQL generation)
                                    var batchContainerNumbers = batchResults
                                        .Select(c => c.ContainerNumber)
                                        .Distinct()
                                        .ToList();

                                    containersWithImages.AddRange(batchContainerNumbers);
                                }

                                // Remove duplicates (in case same container appears in multiple batches)
                                containersWithImages = containersWithImages.Distinct().ToList();
                            }

                            containersWithoutImages = groupContainers
                                .Where(c => !containersWithImages.Contains(c))
                                .ToList();

                            // ✅ FIX: Ensure all containers with images have AnalysisRecords
                            // Intake may have only created records for containers that were "Complete" at intake time,
                            // leaving later-arriving containers without records. The analyst can't decide on containers
                            // that don't have AnalysisRecords, so the group gets stuck.
                            if (analysisGroup != null && containersWithImages.Any())
                            {
                                var existingRecordContainers = await _context.AnalysisRecords
                                    .Where(r => r.GroupId == analysisGroup.Id)
                                    .Select(r => r.ContainerNumber)
                                    .ToListAsync();
                                var existingRecordSet = new HashSet<string>(existingRecordContainers, StringComparer.OrdinalIgnoreCase);

                                var missingRecordContainers = containersWithImages
                                    .Where(c => !existingRecordSet.Contains(c))
                                    .ToList();

                                if (missingRecordContainers.Any())
                                {
                                    foreach (var container in missingRecordContainers)
                                    {
                                        _context.AnalysisRecords.Add(new AnalysisRecord
                                        {
                                            GroupId = analysisGroup.Id,
                                            ContainerNumber = container,
                                            ScannerType = groupScannerType,
                                            Status = "Ready",
                                            CreatedAtUtc = DateTime.UtcNow
                                        });
                                    }
                                    await _context.SaveChangesAsync();
                                    _logger.LogInformation("[AUTO-PROGRESSION] Created {Count} missing AnalysisRecords for group {Group}: {Containers}",
                                        missingRecordContainers.Count, resolvedGroupIdentifier, string.Join(", ", missingRecordContainers));

                                    // ✅ FIX LATE-ARRIVING DROP: After back-filling missing records, the group now has
                                    // additional Ready records that the analyst has NOT yet decided. We must NOT
                                    // auto-progress the group to AnalystCompleted in that case — the analyst will
                                    // pick those up via the normal flow.
                                    var justDecidedContainer = (request.ContainerNumber ?? string.Empty).Trim();
                                    var remainingReadyRecords = await _context.AnalysisRecords
                                        .Where(r => r.GroupId == analysisGroup.Id && r.Status == "Ready")
                                        .Select(r => r.ContainerNumber)
                                        .ToListAsync();

                                    var remainingReadyExcludingJustDecided = remainingReadyRecords
                                        .Where(c => !string.Equals((c ?? string.Empty).Trim(), justDecidedContainer, StringComparison.OrdinalIgnoreCase))
                                        .ToList();

                                    if (remainingReadyExcludingJustDecided.Count > 0)
                                    {
                                        _logger.LogWarning("[AUTO-PROGRESSION] Group {GroupId} has {Count} Ready records remaining after back-fill; skipping AnalystCompleted transition. Containers: {Containers}",
                                            resolvedGroupIdentifier, remainingReadyExcludingJustDecided.Count, string.Join(", ", remainingReadyExcludingJustDecided));

                                        // Abort auto-progression and surface the next container for the analyst.
                                        // The downstream undecided-check (around line 1485) may overwrite these,
                                        // but the guard flag below also blocks the AnalystCompleted transition.
                                        backFilledRecordsRequireDecisions = true;
                                        allContainersDecided = false;
                                        nextContainerNumber = remainingReadyExcludingJustDecided.First();
                                    }
                                }
                            }

                            // ✅ ENHANCED LOGGING: Log detailed breakdown for debugging (after containersWithImages is declared)
                            _logger.LogInformation("🔍 [AUTO-PROGRESSION] Detailed breakdown - GroupIdentifier: {Group} (original: {Original}), ScannerType: {ScannerType}, GroupContainers: {GroupContainers}, ContainersWithImages: {WithImages}, DecidedContainers: {Decided}",
                                resolvedGroupIdentifier, originalGroupIdentifier, groupScannerType,
                                string.Join(", ", groupContainers),
                                string.Join(", ", containersWithImages),
                                string.Join(", ", decidedContainers));

                            if (containersWithoutImages.Any())
                            {
                                _logger.LogInformation("🔍 [AUTO-PROGRESSION] Found {Count} container(s) without images: {Containers}",
                                    containersWithoutImages.Count, string.Join(", ", containersWithoutImages));
                            }

                            // ✅ PRIORITY 1.3: Detect if ALL containers have no images
                            if (containersWithImages.Count == 0 && containersWithoutImages.Count > 0)
                            {
                                // All containers have no images - automatically mark as PartiallyCompleted
                                _logger.LogInformation("🔍 [AUTO-PROGRESSION] Group {Group} has {Count} containers, all without images - will mark as PartiallyCompleted",
                                    resolvedGroupIdentifier, containersWithoutImages.Count);

                                allContainersDecided = true;
                                nextContainerNumber = null;

                                // ✅ CRITICAL FIX: Create AnalysisGroup if it doesn't exist (IntakeWorker may not have run yet)
                                if (analysisGroup == null && !string.IsNullOrEmpty(resolvedGroupIdentifier))
                                {
                                    _logger.LogWarning("⚠️ [AUTO-PROGRESSION] AnalysisGroup not found for {Group} - creating it now", resolvedGroupIdentifier);

                                    // Get scanner type from first container's completeness status or request
                                    var firstContainerStatus = await _context.ContainerCompletenessStatuses
                                        .Where(c => c.GroupIdentifier == originalGroupIdentifier)
                                        .FirstOrDefaultAsync();

                                    // Sprint 5G2 / B1 lock-the-door: Status="PartiallyCompleted"
                                    // removed from initializer. The downstream block at line ~1681
                                    // ("Mark group as PartiallyCompleted immediately") already
                                    // routes the Ready→PartiallyCompleted transition through the
                                    // facade, so the new group lands in the right state via the
                                    // audited path.
                                    analysisGroup = new AnalysisGroup
                                    {
                                        Id = Guid.NewGuid(),
                                        GroupIdentifier = resolvedGroupIdentifier,
                                        ScannerType = firstContainerStatus?.ScannerType ?? request.ScannerType,
                                        TotalContainerCount = groupContainers.Count,
                                        SubmittedContainerCount = 0,
                                        PendingContainerCount = containersWithoutImages.Count,
                                        PartiallyCompletedDate = DateTime.UtcNow,
                                        CreatedAtUtc = DateTime.UtcNow,
                                        UpdatedAtUtc = DateTime.UtcNow
                                    };

                                    _context.AnalysisGroups.Add(analysisGroup);

                                    // Create AnalysisRecords for all containers in the group
                                    foreach (var container in groupContainers)
                                    {
                                        var analysisRecord = new AnalysisRecord
                                        {
                                            // Id is auto-incrementing int, don't set it
                                            GroupId = analysisGroup.Id,
                                            ContainerNumber = container,
                                            ScannerType = firstContainerStatus?.ScannerType ?? request.ScannerType,
                                            Status = "PartiallyCompleted",
                                            CreatedAtUtc = DateTime.UtcNow
                                        };
                                        _context.AnalysisRecords.Add(analysisRecord);
                                    }

                                    await _context.SaveChangesAsync();

                                    _logger.LogInformation("✅ [AUTO-PROGRESSION] Created AnalysisGroup {GroupId} for {Group} with {Count} containers (all without images)",
                                        analysisGroup.Id, resolvedGroupIdentifier, groupContainers.Count);
                                }

                                // Mark group as PartiallyCompleted immediately (no need for analysis/audit)
                                if (analysisGroup != null &&
                                    (analysisGroup.Status == AnalysisStatuses.Ready ||
                                     analysisGroup.Status == AnalysisStatuses.AnalystAssigned ||
                                     analysisGroup.Status == AnalysisStatuses.PartiallyCompleted)) // ✅ Allow update if already PartiallyCompleted
                                {
                                    analysisGroup.PartiallyCompletedDate = DateTime.UtcNow;
                                    analysisGroup.TotalContainerCount = groupContainers.Count;
                                    analysisGroup.SubmittedContainerCount = 0; // No containers with images to submit
                                    analysisGroup.PendingContainerCount = containersWithoutImages.Count;

                                    // Sprint 5G2 / B1: route through the state-machine facade. Ready/AnalystAssigned/PartiallyCompleted
                                    // → PartiallyCompleted are now in the legal table (extended for this auto-progression edge).
                                    await AnalysisGroupStateMachine.TransitionAsync(
                                        _context, analysisGroup, AnalysisStatuses.PartiallyCompleted,
                                        triggerName: "AutoProgressionAllContainersImageless",
                                        actor: request.ReviewedBy,
                                        reason: $"All {containersWithoutImages.Count} containers in group {resolvedGroupIdentifier} have no images - marked PartiallyCompleted.",
                                        correlationId: HttpContext?.TraceIdentifier);
                                    analysisGroup.UpdatedAtUtc = DateTime.UtcNow;

                                    _logger.LogInformation("✅ [AUTO-PROGRESSION] Group {Group} marked as PartiallyCompleted (all {Count} containers have no images)",
                                        resolvedGroupIdentifier, containersWithoutImages.Count);

                                    // Release any active Analyst assignments
                                    var analystAssignments = await _context.AnalysisAssignments
                                        .AsTracking()
                                        .Where(a => a.GroupId == analysisGroup.Id
                                            && a.Role == "Analyst"
                                            && a.State == "Active")
                                        .ToListAsync();

                                    foreach (var assignment in analystAssignments)
                                    {
                                        assignment.State = "Released";
                                        assignment.UpdatedAtUtc = DateTime.UtcNow;
                                        _logger.LogInformation("Released Analyst assignment {AssignmentId} for group {Group} (all containers have no images)",
                                            assignment.Id, resolvedGroupIdentifier);
                                    }

                                    await RemoveQueueEntriesForAssignmentsAsync(analystAssignments);
                                }
                            }
                            // ✅ STEP 5: Find next undecided container (only from containers WITH images)
                            else if (containersWithImages.Any())
                            {
                                var undecidedContainersWithImages = containersWithImages
                                    .Where(c => !decidedContainers.Contains(c))
                                    .ToList();

                                // ✅ ENHANCED LOGGING: Log detailed comparison for debugging
                                _logger.LogInformation("🔍 [AUTO-PROGRESSION] Container comparison - ContainersWithImages: {WithImages} ({Count}), DecidedContainers: {Decided} ({DecidedCount}), UndecidedContainersWithImages: {Undecided} ({UndecidedCount})",
                                    string.Join(", ", containersWithImages), containersWithImages.Count,
                                    string.Join(", ", decidedContainers), decidedContainers.Count,
                                    string.Join(", ", undecidedContainersWithImages), undecidedContainersWithImages.Count);

                                if (undecidedContainersWithImages.Any())
                                {
                                    // Get the first undecided container with images (ordered by container number)
                                    nextContainerNumber = undecidedContainersWithImages.First();

                                    // ✅ PRIORITY 2.3: Validate nextContainerNumber belongs to the group
                                    if (!groupContainers.Contains(nextContainerNumber))
                                    {
                                        _logger.LogError("❌ [AUTO-PROGRESSION] Validation failed: nextContainerNumber '{Container}' does not belong to group {Group}. Group containers: {Containers}",
                                            nextContainerNumber, resolvedGroupIdentifier, string.Join(", ", groupContainers));
                                        nextContainerNumber = null;
                                        allContainersDecided = true; // Set to true to prevent infinite loop
                                    }
                                    else
                                    {
                                        _logger.LogInformation("✅ [AUTO-PROGRESSION] Next undecided container in group {Group}: {Container} (remaining: {Remaining})",
                                            resolvedGroupIdentifier, nextContainerNumber, string.Join(", ", undecidedContainersWithImages.Skip(1)));
                                    }
                                }
                                else
                                {
                                    // ✅ All containers WITH images are decided
                                    // ✅ FIX: If there are containers WITHOUT images, mark group as PartiallyCompleted
                                    if (containersWithoutImages.Any())
                                    {
                                        _logger.LogInformation("✅ [AUTO-PROGRESSION] All containers with images are decided. {Count} container(s) without images will be marked as PartiallyCompleted: {Containers}",
                                            containersWithoutImages.Count, string.Join(", ", containersWithoutImages));

                                        // ✅ CRITICAL FIX: Mark group as PartiallyCompleted when some containers have no images
                                        allContainersDecided = true; // Set to true to trigger status update
                                        // Note: We'll handle PartiallyCompleted status update in the status update section below
                                    }
                                    else
                                    {
                                        // ✅ All containers WITH images are now decided (no containers without images)
                                        allContainersDecided = true;
                                        _logger.LogInformation("✅ [AUTO-PROGRESSION] All containers WITH images in group {Group} have been decided ({WithImages} with images, all have images)",
                                            resolvedGroupIdentifier, containersWithImages.Count);
                                    }
                                }
                            }
                            else
                            {
                                // Edge case: No containers with images and no containers without images (shouldn't happen)
                                _logger.LogWarning("⚠️ [AUTO-PROGRESSION] Group {Group} has no containers with images and no containers without images - unexpected state",
                                    resolvedGroupIdentifier);
                                allContainersDecided = true;
                                nextContainerNumber = null;
                            }
                        }

                        // ✅ FIX LATE-ARRIVING DROP: If back-fill added new Ready records this transaction,
                        // we MUST NOT auto-progress to AnalystCompleted even if other code paths set
                        // allContainersDecided = true. Force the analyst to decide the back-filled containers.
                        if (backFilledRecordsRequireDecisions && allContainersDecided)
                        {
                            _logger.LogWarning("[AUTO-PROGRESSION] Group {GroupId} back-filled new Ready records this request; forcing allContainersDecided=false to prevent premature AnalystCompleted transition",
                                resolvedGroupIdentifier);
                            allContainersDecided = false;
                        }

                        // ✅ STEP 5: Mark group as AnalystCompleted or PartiallyCompleted and release Analyst assignment
                        // ✅ CRITICAL: Only if all containers WITH images have decisions
                        // ✅ FIX: If there are containers WITHOUT images, mark as PartiallyCompleted (not AnalystCompleted)
                        if (allContainersDecided && groupContainers.Any() && analysisGroup != null)
                        {
                            // ✅ CRITICAL VALIDATION: Verify all containers WITH images have decisions before progression
                            // groupScannerType already in scope from above
                            // ✅ DATE-BASED GROUPING FIX: Use original GroupIdentifier for validation
                            // ContainerCompletenessStatus and ImageAnalysisDecisions use original GroupIdentifier
                            var validationPassed = await ValidateAllContainersWithImagesHaveDecisionsAsync(
                                originalGroupIdentifier, groupContainers, groupScannerType, resolvedGroupIdentifier);

                            if (!validationPassed)
                            {
                                _logger.LogWarning("❌ [VALIDATION] Cannot progress group {Group} - some containers with images are missing decisions",
                                    resolvedGroupIdentifier);
                                // Don't update status - allow decision to be saved but don't progress the group
                                allContainersDecided = false; // Reset flag to prevent progression
                            }
                            // ✅ FIX: Check if there are containers WITHOUT images - mark as PartiallyCompleted
                            else if (containersWithoutImages.Any() &&
                                     (analysisGroup.Status == AnalysisStatuses.AnalystAssigned ||
                                      analysisGroup.Status == AnalysisStatuses.Ready ||
                                      analysisGroup.Status == AnalysisStatuses.PartiallyCompleted))
                            {
                                _logger.LogInformation("Moving group {Group} from {OldStatus} to PartiallyCompleted (all containers WITH images have decisions, {WithoutImages} containers without images)",
                                    resolvedGroupIdentifier, analysisGroup.Status, containersWithoutImages.Count);

                                analysisGroup.PartiallyCompletedDate = DateTime.UtcNow;
                                analysisGroup.TotalContainerCount = groupContainers.Count;
                                analysisGroup.SubmittedContainerCount = containersWithImages.Count; // Containers with images that were decided
                                analysisGroup.PendingContainerCount = containersWithoutImages.Count; // Containers without images

                                // Sprint 5G2 / B1: route through the state-machine facade. Ready/AnalystAssigned/PartiallyCompleted
                                // → PartiallyCompleted are now in the legal table (extended for this auto-progression edge).
                                await AnalysisGroupStateMachine.TransitionAsync(
                                    _context, analysisGroup, AnalysisStatuses.PartiallyCompleted,
                                    triggerName: "AutoProgressionMixedImageContainers",
                                    actor: request.ReviewedBy,
                                    reason: $"All {containersWithImages.Count} containers with images decided; {containersWithoutImages.Count} have no images - marked PartiallyCompleted.",
                                    correlationId: HttpContext?.TraceIdentifier);
                                analysisGroup.UpdatedAtUtc = DateTime.UtcNow;

                                // Release any active Analyst assignments
                                var analystAssignments = await _context.AnalysisAssignments
                                    .AsTracking()
                                    .Where(a => a.GroupId == analysisGroup.Id
                                        && a.Role == "Analyst"
                                        && a.State == "Active")
                                    .ToListAsync();

                                foreach (var assignment in analystAssignments)
                                {
                                    assignment.State = "Released";
                                    assignment.UpdatedAtUtc = DateTime.UtcNow;
                                    _logger.LogInformation("Released Analyst assignment {AssignmentId} for group {Group} (some containers have no images)",
                                        assignment.Id, resolvedGroupIdentifier);
                                }

                                await RemoveQueueEntriesForAssignmentsAsync(analystAssignments);

                                await _context.SaveChangesAsync();

                                // ✅ FIX: Update WorkflowStage for containers WITHOUT images to 'PartiallyCompleted'
                                // Use LINQ to update entities directly (safer than raw SQL)
                                if (containersWithoutImages.Any())
                                {
                                    var scannerBaseForCompleteness = BaseScannerType(groupScannerType) ?? groupScannerType;
                                    var containersWithoutImagesStatuses = await _context.ContainerCompletenessStatuses
                                        .AsTracking()
                                        .Where(c => containersWithoutImages.Contains(c.ContainerNumber) &&
                                                   c.GroupIdentifier == normalizedForCompleteness &&
                                                   (c.ScannerType == groupScannerType ||
                                                    c.ScannerType == scannerBaseForCompleteness ||
                                                    c.ScannerType.StartsWith(scannerBaseForCompleteness + "-")) &&
                                                   c.WorkflowStage != "Completed")
                                        .ToListAsync();

                                    foreach (var status in containersWithoutImagesStatuses)
                                    {
                                        status.WorkflowStage = "PartiallyCompleted";
                                        status.UpdatedAt = DateTime.UtcNow;
                                    }

                                    var affectedPartiallyCompleted = await _context.SaveChangesAsync();
                                    _logger.LogInformation("Updated {Count} container(s) WITHOUT images to 'PartiallyCompleted' stage for group {Group}",
                                        affectedPartiallyCompleted, resolvedGroupIdentifier);
                                }

                                // ✅ FIX: Update WorkflowStage for containers WITH images to 'Audit' (they have decisions)
                                if (containersWithImages.Any())
                                {
                                    var scannerBaseForCompleteness = BaseScannerType(groupScannerType) ?? groupScannerType;
                                    var containersWithImagesStatuses = await _context.ContainerCompletenessStatuses
                                        .AsTracking()
                                        .Where(c => containersWithImages.Contains(c.ContainerNumber) &&
                                                   c.GroupIdentifier == normalizedForCompleteness &&
                                                   (c.ScannerType == groupScannerType ||
                                                    c.ScannerType == scannerBaseForCompleteness ||
                                                    c.ScannerType.StartsWith(scannerBaseForCompleteness + "-")) &&
                                                   c.WorkflowStage != "Completed")
                                        .ToListAsync();

                                    foreach (var status in containersWithImagesStatuses)
                                    {
                                        status.WorkflowStage = "Audit";
                                        status.UpdatedAt = DateTime.UtcNow;
                                    }

                                    var affectedAudit = await _context.SaveChangesAsync();
                                    _logger.LogInformation("Updated {Count} container(s) WITH images to 'Audit' stage for group {Group}",
                                        affectedAudit, resolvedGroupIdentifier);
                                }

                                advanced = true; // Mark as advanced since we updated workflow stages
                            }
                            else if (analysisGroup.Status == AnalysisStatuses.AnalystAssigned || analysisGroup.Status == AnalysisStatuses.Ready)
                            {
                                _logger.LogInformation("Moving group {Group} from {OldStatus} to AnalystCompleted (all containers WITH images have decisions)",
                                    resolvedGroupIdentifier, analysisGroup.Status);

                                // Sprint 5G2 / B1: route through the state-machine facade. Both Ready and AnalystAssigned
                                // are legal predecessors to AnalystCompleted in the validator table.
                                await AnalysisGroupStateMachine.TransitionAsync(
                                    _context, analysisGroup, AnalysisStatuses.AnalystCompleted,
                                    triggerName: "AnalystSubmittedAllContainerDecisions",
                                    actor: request.ReviewedBy,
                                    reason: $"All {containersWithImages.Count} containers with images in group {resolvedGroupIdentifier} now have decisions.",
                                    correlationId: HttpContext?.TraceIdentifier);
                                analysisGroup.UpdatedAtUtc = DateTime.UtcNow;

                                // ✅ FIX: Release the Analyst assignment so the record moves to Audit queue
                                var analystAssignments = await _context.AnalysisAssignments
                                    .AsTracking()
                                    .Where(a => a.GroupId == analysisGroup.Id
                                        && a.Role == "Analyst"
                                        && a.State == "Active")
                                    .ToListAsync();

                                // Collect analyst usernames before releasing (for immediate assignment check)
                                var analystUsernames = analystAssignments.Select(a => a.AssignedTo).Distinct().ToList();

                                foreach (var assignment in analystAssignments)
                                {
                                    assignment.State = "Released";
                                    assignment.UpdatedAtUtc = DateTime.UtcNow;
                                    _logger.LogInformation("Released Analyst assignment {AssignmentId} for group {Group} - moving to Audit queue",
                                        assignment.Id, resolvedGroupIdentifier);

                                    // ✅ PHASE 3: Event logging for assignment release
                                    _logger.LogInformation("[ASSIGNMENT-EVENT] Released | AssignmentId={AssignmentId} | GroupId={GroupId} | User={User} | Role={Role} | Reason=AnalystCompleted",
                                        assignment.Id, assignment.GroupId, assignment.AssignedTo, assignment.Role);
                                }

                                await RemoveQueueEntriesForAssignmentsAsync(analystAssignments);

                                await _context.SaveChangesAsync();

                                // Invalidate ReadyGroupsCache so Audit assignment sees the new AnalystCompleted group immediately
                                _readyGroupsCache?.InvalidateCache("Audit", "AnalystCompleted");

                                // Invalidate my-assignments cache so completed record disappears from analyst's queue immediately
                                foreach (var analystUsername in analystUsernames)
                                {
                                    var cacheKey = $"my-assignments:{analystUsername}:Analyst";
                                    _memoryCache?.Remove(cacheKey);
                                }

                                // ✅ NEW: Check if analyst has completed ALL their assignments and trigger immediate assignment if in Auto mode
                                if (analystUsernames.Any())
                                {
                                    _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Triggering immediate assignment check for {Count} analyst(s): {Analysts}",
                                        analystUsernames.Count, string.Join(", ", analystUsernames));

                                    foreach (var analystUsername in analystUsernames)
                                    {
                                        await CheckAndTriggerImmediateAssignmentAsync(analystUsername);
                                    }
                                }
                                else
                                {
                                    _logger.LogWarning("⚠️ [IMMEDIATE-ASSIGNMENT] No analyst usernames found in released assignments - skipping immediate assignment check");
                                }

                                // Defer audit assignment to after transaction commits (avoid concurrent DbContext access)
                                pendingAuditGroupId = analysisGroup.Id;
                            }
                            else if (analysisGroup.Status == AnalysisStatuses.AnalystCompleted)
                            {
                                // ✅ FIX: Group is already AnalystCompleted - ensure WorkflowStage is updated (may have been missed)
                                _logger.LogInformation("Group {Group} already in AnalystCompleted status - ensuring WorkflowStage is updated to 'Audit'",
                                    resolvedGroupIdentifier);
                            }
                            else
                            {
                                _logger.LogInformation("Group {Group} already in status {Status}, skipping status update",
                                    resolvedGroupIdentifier, analysisGroup.Status);
                            }

                            // ✅ CRITICAL FIX: ALWAYS update WorkflowStage to 'Audit' when all containers are decided
                            // Use NORMALIZED GroupIdentifier - ContainerCompletenessStatus never has date suffix
                            // resolvedGroupIdentifier can be date-suffixed (e.g. 41025634146_20250101_20250131)
                            var workflowScannerType = groupScannerType ?? request.ScannerType;
                            var workflowScannerBase = BaseScannerType(workflowScannerType) ?? workflowScannerType;
                            var sql = @"
                                UPDATE ContainerCompletenessStatuses
                                SET WorkflowStage = @p0, UpdatedAt = now() AT TIME ZONE 'UTC'
                                WHERE GroupIdentifier = @p1
                                  AND WorkflowStage <> @p0
                                  AND WorkflowStage <> 'Completed'
                                  AND (ScannerType = @p2 OR ScannerType = @p3 OR ScannerType LIKE @p4)";
                            var affected = await _context.Database.ExecuteSqlRawAsync(sql,
                                new NpgsqlParameter("@p0", "Audit"),
                                new NpgsqlParameter("@p1", normalizedForCompleteness),
                                new NpgsqlParameter("@p2", workflowScannerType),
                                new NpgsqlParameter("@p3", workflowScannerBase),
                                new NpgsqlParameter("@p4", workflowScannerBase + "-%"));
                            advanced = affected > 0;

                            _logger.LogInformation("All containers in group {Group} have decisions. WorkflowStage updated to 'Audit': {Count} records (skipped containers already in 'Completed' stage)",
                                resolvedGroupIdentifier, affected);

                            // ✅ CRITICAL FIX: If no containers were updated, check why and log detailed warning
                            if (affected == 0)
                            {
                                // Check WorkflowStage distribution for this group (use normalized - Completeness uses base identifier)
                                var workflowStats = await _context.ContainerCompletenessStatuses
                                    .Where(c => c.GroupIdentifier == normalizedForCompleteness
                                        && (c.ScannerType == workflowScannerType
                                            || c.ScannerType == workflowScannerBase
                                            || c.ScannerType.StartsWith(workflowScannerBase + "-")))
                                    .GroupBy(c => c.WorkflowStage)
                                    .Select(g => new { WorkflowStage = g.Key, Count = g.Count() })
                                    .ToListAsync();

                                var totalCount = workflowStats.Sum(s => s.Count);
                                var completedCount = workflowStats.FirstOrDefault(s => s.WorkflowStage == "Completed")?.Count ?? 0;
                                var auditCount = workflowStats.FirstOrDefault(s => s.WorkflowStage == "Audit")?.Count ?? 0;
                                var imageAnalysisCount = workflowStats.FirstOrDefault(s => s.WorkflowStage == "ImageAnalysis")?.Count ?? 0;

                                if (completedCount == totalCount && totalCount > 0)
                                {
                                    _logger.LogWarning("⚠️ [AUDIT-ASSIGNMENT] Group {Group} marked as AnalystCompleted but ALL {Count} containers have WorkflowStage = 'Completed'. This will prevent audit assignment! Containers should be in 'Audit' stage. This may indicate containers were incorrectly marked as Completed.",
                                        resolvedGroupIdentifier, totalCount);
                                }
                                else if (auditCount > 0)
                                {
                                    _logger.LogInformation("✅ [AUDIT-ASSIGNMENT] Group {Group} already has {AuditCount}/{TotalCount} containers in 'Audit' stage - no update needed",
                                        resolvedGroupIdentifier, auditCount, totalCount);
                                }
                                else
                                {
                                    var count = System.Threading.Interlocked.Increment(ref _workflowStageUpdateZeroRowsUnexpectedCount);
                                    _logger.LogWarning("⚠️ [AUDIT-ASSIGNMENT] Group {Group} marked as AnalystCompleted but no containers were updated to 'Audit' stage. WorkflowStage distribution: {Stats}. This may prevent audit assignment! (Total unexpected occurrences: {Count})",
                                        resolvedGroupIdentifier, string.Join(", ", workflowStats.Select(s => $"{s.WorkflowStage}={s.Count}")), count);
                                }
                            }
                        }
                    }
                    catch (Exception stageEx)
                    {
                        _logger.LogError(stageEx, "❌ CRITICAL: Could not process auto-progression for group {Group}. Error: {Error}",
                            resolvedGroupIdentifier ?? request.GroupIdentifier ?? "UNKNOWN", stageEx.Message);
                        // Don't rethrow - decision was saved successfully, just progression failed
                    }
                }
                else
                {
                    _logger.LogWarning("Cannot process auto-progression: GroupIdentifier is missing for container {Container} {Scanner}",
                        request.ContainerNumber, request.ScannerType);
                }

                // Extra safety: if auto-progression logic above did not fully synchronize
                // the AnalysisGroup.Status with the containers' WorkflowStage and decisions,
                // run a focused, group-level consistency check. This mirrors the
                // FixStuckAnalystGroups behaviour but only for the current group.
                if (!string.IsNullOrWhiteSpace(resolvedGroupIdentifier))
                {
                    await EnsureGroupStatusFromWorkflowStageAsync(resolvedGroupIdentifier, request.ScannerType);
                }

                await tx.CommitAsync();

                await NotifyAiLineageAsync(request, normalizedDecision, normalizedGroupIdentifierForStorage);

                if (pendingAuditGroupId.HasValue)
                {
                    await TriggerImmediateAuditAssignmentForGroupAsync(pendingAuditGroupId.Value);
                }

                _logger.LogInformation("✅ Image decision saved successfully: {Container} - {Scanner} - {Decision} by {User} | Tags: {Tags} | Comments: {Comments}",
                    request.ContainerNumber, request.ScannerType, normalizedDecision ?? "NULL", request.ReviewedBy,
                    request.Tags ?? "(none)", request.Comments ?? "(none)");

                // ✅ PRIORITY 3.1: Enhanced logging for auto-progression response
                var responseMessage = allContainersDecided
                    ? "All containers in group have been decided. Group moved to audit queue."
                    : nextContainerNumber != null
                        ? $"Decision saved. Next container: {nextContainerNumber}"
                        : "Decision saved successfully";

                _logger.LogInformation("📤 [AUTO-PROGRESSION] Returning response: allDecided={AllDecided}, nextContainer={NextContainer}, message={Message}",
                    allContainersDecided, nextContainerNumber ?? "null", responseMessage);

                // ✅ Return auto-progression info
                return Ok(new
                {
                    success = true,
                    advancedToAudit = advanced,
                    allContainersDecided = allContainersDecided,
                    nextContainerNumber = nextContainerNumber,
                    message = responseMessage
                });
                }); // end strategy.ExecuteAsync
            }
            catch (DbUpdateException dbEx)
            {
                var inner = dbEx.InnerException?.Message ?? dbEx.Message;
                _logger.LogError(dbEx, "DB error saving image decision for {Container} {Scanner}. Inner: {Inner}", request.ContainerNumber, request.ScannerType, inner);
                return StatusCode(500, new { success = false, error = "Database error while saving decision", details = inner });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving image decision for {Container} {Scanner}", request.ContainerNumber, request.ScannerType);
                return StatusCode(500, new { success = false, error = "Failed to save decision", details = ex.Message });
            }
        }

        /// <summary>
        /// Get decisions for a specific container
        /// </summary>
        [HttpGet("container/{containerNumber}")]
        public async Task<ActionResult<List<ImageAnalysisDecision>>> GetContainerDecisions(string containerNumber)
        {
            try
            {
                var decisions = await _context.ImageAnalysisDecisions
                    .Where(d => d.ContainerNumber == containerNumber)
                    .OrderBy(d => d.ScannerType)
                    .ToListAsync();

                return Ok(decisions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching decisions for container {Container}", containerNumber);
                return StatusCode(500, new { error = "Failed to fetch decisions" });
            }
        }

        /// <summary>
        /// Get overall decision for a group (container or BOE)
        /// </summary>
        [HttpGet("group/{groupIdentifier}/overall")]
        public async Task<ActionResult<OverallDecisionResponse>> GetOverallDecision(string groupIdentifier)
        {
            try
            {
                var decisions = await _context.ImageAnalysisDecisions
                    .Where(d => d.GroupIdentifier == groupIdentifier)
                    .ToListAsync();

                if (!decisions.Any())
                {
                    return Ok(new OverallDecisionResponse
                    {
                        OverallDecision = "Pending",
                        TotalImages = 0,
                        NormalCount = 0,
                        AbnormalCount = 0,
                        ReviewedCount = 0
                    });
                }

                var totalImages = decisions.Count;
                var normalCount = decisions.Count(d => d.Decision == "Normal");
                var abnormalCount = decisions.Count(d => d.Decision == "Abnormal");
                var reviewedCount = decisions.Count();

                // Overall logic: If ANY image is Abnormal → Overall is Abnormal
                var overallDecision = abnormalCount > 0 ? "Abnormal" : "Normal";

                return Ok(new OverallDecisionResponse
                {
                    OverallDecision = overallDecision,
                    TotalImages = totalImages,
                    NormalCount = normalCount,
                    AbnormalCount = abnormalCount,
                    ReviewedCount = reviewedCount,
                    LastReviewedAt = decisions.Max(d => d.ReviewedAt),
                    LastReviewedBy = decisions.OrderByDescending(d => d.ReviewedAt).First().ReviewedBy
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating overall decision for group {Group}", groupIdentifier);
                return StatusCode(500, new { error = "Failed to calculate overall decision" });
            }
        }

        /// <summary>
        /// Get decisions for multiple groups (for table display)
        /// </summary>
        [HttpPost("groups/batch")]
        public async Task<ActionResult<Dictionary<string, OverallDecisionResponse>>> GetBatchOverallDecisions([FromBody] List<string> groupIdentifiers)
        {
            try
            {
                // ✅ FIX: Load all decisions first, then filter in memory to avoid CTE generation
                var groupIdentifiersHashSet = new HashSet<string>(groupIdentifiers, StringComparer.OrdinalIgnoreCase);
                var allDecisions = await _context.ImageAnalysisDecisions
                    .Where(d => d.GroupIdentifier != null)
                    .ToListAsync();

                var decisions = allDecisions
                    .Where(d => groupIdentifiersHashSet.Contains(d.GroupIdentifier ?? ""))
                    .ToList();

                var result = new Dictionary<string, OverallDecisionResponse>();

                foreach (var groupId in groupIdentifiers)
                {
                    var groupDecisions = decisions.Where(d => d.GroupIdentifier == groupId).ToList();

                    if (!groupDecisions.Any())
                    {
                        result[groupId] = new OverallDecisionResponse { OverallDecision = "Pending", TotalImages = 0 };
                        continue;
                    }

                    var totalImages = groupDecisions.Count;
                    var normalCount = groupDecisions.Count(d => d.Decision == "Normal");
                    var abnormalCount = groupDecisions.Count(d => d.Decision == "Abnormal");

                    result[groupId] = new OverallDecisionResponse
                    {
                        OverallDecision = abnormalCount > 0 ? "Abnormal" : "Normal",
                        TotalImages = totalImages,
                        NormalCount = normalCount,
                        AbnormalCount = abnormalCount,
                        ReviewedCount = totalImages,
                        LastReviewedAt = groupDecisions.Max(d => d.ReviewedAt),
                        LastReviewedBy = groupDecisions.OrderByDescending(d => d.ReviewedAt).First().ReviewedBy
                    };
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching batch overall decisions");
                return StatusCode(500, new { error = "Failed to fetch batch decisions" });
            }
        }

        /// <summary>
        /// Delete a decision
        /// </summary>
        [HttpDelete("{id}")]
        [HasPermission(Permissions.ControllersImageAnalysisManagementSettings)]
        public async Task<IActionResult> DeleteDecision(int id)
        {
            try
            {
                var decision = await _context.ImageAnalysisDecisions.FindAsync(id);
                if (decision == null)
                {
                    return NotFound();
                }

                _context.ImageAnalysisDecisions.Remove(decision);
                await _context.SaveChangesAsync();

                return Ok(new { success = true });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting decision {Id}", id);
                return StatusCode(500, new { error = "Failed to delete decision" });
            }
        }

        /// <summary>
        /// Case-insensitive double-or-int reader for SuspiciousAreas JSON.
        /// Existing prod data uses PascalCase keys (X / Y / Width / Height); newer
        /// writers may use lowercase. Tries the lowercase form first, then a
        /// PascalCase fallback. Accepts both Number and string values.
        /// </summary>
        private static bool TryReadDouble(System.Text.Json.JsonElement obj, string lowerKey, out double value)
        {
            value = 0;
            System.Text.Json.JsonElement el = default;
            var found = obj.TryGetProperty(lowerKey, out el)
                     || obj.TryGetProperty(char.ToUpperInvariant(lowerKey[0]) + lowerKey.Substring(1), out el);
            if (!found) return false;

            if (el.ValueKind == System.Text.Json.JsonValueKind.Number)
            {
                value = el.GetDouble();
                return true;
            }
            if (el.ValueKind == System.Text.Json.JsonValueKind.String
                && double.TryParse(el.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var parsed))
            {
                value = parsed;
                return true;
            }
            return false;
        }

        private async Task NotifyAiLineageAsync(ImageDecisionRequest request, string? normalizedDecision, string? normalizedGroupIdentifierForStorage)
        {
            try
            {
                await _aiLineage.NotifyHumanDecisionAsync(
                    request.ContainerNumber,
                    request.ScannerType,
                    normalizedDecision,
                    normalizedGroupIdentifierForStorage,
                    string.IsNullOrWhiteSpace(request.ReviewedBy) ? "System" : request.ReviewedBy,
                    HttpContext.RequestAborted);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "AI lineage notification failed for {Container}", request.ContainerNumber);
            }
        }
    }

    // DTOs
    public class ImageDecisionRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public string Decision { get; set; } = string.Empty;
        public string? Comments { get; set; }
        public string? Tags { get; set; }
        public string? SuspiciousAreas { get; set; }
        public string ReviewedBy { get; set; } = string.Empty;
        public string? GroupIdentifier { get; set; }
        public bool IsConsolidated { get; set; }
        public int? AnalysisRecordId { get; set; }
        public Guid? SplitJobId { get; set; }
        public Guid? SplitResultId { get; set; }
        public string? SplitSide { get; set; }

        // ── Controlled-vocabulary finding categories (Gap 1a — AI training flywheel) ──
        // Both nullable. A decision may carry a security finding, a revenue finding,
        // both, or neither. Front-ends that don't know about these fields can omit
        // them entirely; the existing free-text Tags / Comments path keeps working.
        // FK ids reference threatcategories.id and revenueanomalycategories.id.
        public int? ThreatCategoryId { get; set; }
        public int? RevenueAnomalyCategoryId { get; set; }
    }



    public class OverallDecisionResponse
    {
        public string OverallDecision { get; set; } = "Pending";
        public int TotalImages { get; set; }
        public int NormalCount { get; set; }
        public int AbnormalCount { get; set; }
        public int ReviewedCount { get; set; }
        public DateTime? LastReviewedAt { get; set; }
        public string? LastReviewedBy { get; set; }
    }

    public class RectangleSaveRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public string? GroupIdentifier { get; set; }
        public bool IsConsolidated { get; set; }
        public string? ReviewedBy { get; set; }
        public string? SuspiciousAreas { get; set; }
    }
}

