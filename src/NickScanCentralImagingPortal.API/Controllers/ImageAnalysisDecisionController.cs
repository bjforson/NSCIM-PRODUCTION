using Npgsql;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageAnalysis;
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
        private readonly IAiWorkflowLineageService _aiLineage;
        private readonly IManifestSnapshotService _manifestSnapshot;

        // Track unexpected 0-rows WorkflowStage UPDATEs for monitoring/alerting
        private static int _workflowStageUpdateZeroRowsUnexpectedCount;

        public ImageAnalysisDecisionController(
            ApplicationDbContext context,
            IcumDownloadsDbContext icumDb,
            ILogger<ImageAnalysisDecisionController> logger,
            IAiWorkflowLineageService aiLineage,
            IManifestSnapshotService manifestSnapshot,
            ReadyGroupsCacheService? readyGroupsCache = null,
            IMemoryCache? memoryCache = null)
        {
            _context = context;
            _icumDb = icumDb;
            _logger = logger;
            _aiLineage = aiLineage;
            _manifestSnapshot = manifestSnapshot;
            _readyGroupsCache = readyGroupsCache;
            _memoryCache = memoryCache;
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
        private async Task EnsureGroupStatusFromWorkflowStageAsync(string groupIdentifier)
        {
            if (string.IsNullOrWhiteSpace(groupIdentifier))
                return;

            try
            {
                // ✅ CRITICAL: Load with tracking to ensure changes persist
                var group = await _context.AnalysisGroups
                    .AsTracking()
                    .FirstOrDefaultAsync(g => g.GroupIdentifier == groupIdentifier);

                if (group == null)
                    return;

                // Only consider groups that are still in the analyst stage
                if (group.Status != AnalysisStatuses.Ready &&
                    group.Status != AnalysisStatuses.AnalystAssigned)
                {
                    return;
                }

                // Get all containers for this group from completeness table
                var containers = await _context.ContainerCompletenessStatuses
                    .Where(c => c.GroupIdentifier == groupIdentifier)
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

                var decidedContainers = await _context.ImageAnalysisDecisions
                    .Where(d => d.GroupIdentifier == groupIdentifier &&
                                (d.Decision == "Normal" || d.Decision == "Abnormal"))
                    .Select(d => d.ContainerNumber)
                    .Distinct()
                    .ToListAsync();

                if (decidedContainers.Count < containerNumbers.Count)
                {
                    // Not all containers have real decisions yet – don't promote.
                    return;
                }

                var oldStatus = group.Status;
                group.Status = AnalysisStatuses.AnalystCompleted;
                group.UpdatedAtUtc = DateTime.UtcNow;

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

                await _context.SaveChangesAsync();
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

        /// <summary>
        /// Check if analyst has completed ALL their assignments and trigger immediate assignment if in Auto mode
        /// </summary>
        private async Task CheckAndTriggerImmediateAssignmentAsync(string analystUsername)
        {
            try
            {
                _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Checking immediate assignment eligibility for analyst {Username}", analystUsername);
                var now = DateTime.UtcNow;

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

                // Find available "Ready" groups (without active assignments)
                // First, get all Ready groups
                var allReadyGroups = await _context.AnalysisGroups
                    .AsTracking()
                    .Where(g => g.Status == AnalysisStatuses.Ready)
                    .OrderByDescending(g => g.Priority)
                    .ToListAsync();

                _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Found {Count} total Ready groups in system", allReadyGroups.Count);

                // Get group IDs that have active assignments
                var groupIdsWithActiveAssignments = await _context.AnalysisAssignments
                    .Where(a => a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .Select(a => a.GroupId)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Found {Count} groups with active assignments", groupIdsWithActiveAssignments.Count);

                // Filter out groups with active assignments and take up to max
                var readyGroups = allReadyGroups
                    .Where(g => !groupIdsWithActiveAssignments.Contains(g.Id))
                    .Take(settings.MaxConcurrentPerUser - activeCount) // Fill up to max
                    .ToList();

                _logger.LogInformation("🔍 [IMMEDIATE-ASSIGNMENT] Found {Count} available Ready groups for analyst {Username} (will assign up to {Max})",
                    readyGroups.Count, analystUsername, settings.MaxConcurrentPerUser - activeCount);

                if (!readyGroups.Any())
                {
                    _logger.LogWarning("⚠️ [IMMEDIATE-ASSIGNMENT] No Ready groups available for immediate assignment to analyst {Username} (Total Ready: {Total}, With Active Assignments: {Active})",
                        analystUsername, allReadyGroups.Count, groupIdsWithActiveAssignments.Count);
                    return;
                }

                // Assign groups immediately
                var assignedCount = 0;
                foreach (var group in readyGroups)
                {
                    var leaseUntil = now.AddMinutes(Math.Max(1, settings.LeaseMinutes));
                    _context.AnalysisAssignments.Add(new AnalysisAssignment
                    {
                        GroupId = group.Id,
                        AssignedTo = analystUsername,
                        Role = "Analyst",
                        LeaseUntilUtc = leaseUntil,
                        State = "Active",
                        CreatedAtUtc = now
                    });

                    group.Status = AnalysisStatuses.AnalystAssigned;
                    group.UpdatedAtUtc = now;
                    assignedCount++;
                }

                await _context.SaveChangesAsync();

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
                        && a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now));

                if (hasActiveAssignment)
                {
                    _logger.LogDebug("[IMMEDIATE-AUDIT-ASSIGNMENT] Group {GroupId} already has active audit assignment - skipping", groupId);
                    return;
                }

                // Verify containers are in Audit WorkflowStage
                if (string.IsNullOrEmpty(group.GroupIdentifier))
                {
                    _logger.LogWarning("[IMMEDIATE-AUDIT-ASSIGNMENT] Group {GroupId} has no GroupIdentifier - skipping", groupId);
                    return;
                }

                var containers = await _context.ContainerCompletenessStatuses
                    .Where(c => c.GroupIdentifier == group.GroupIdentifier)
                    .ToListAsync();

                var auditContainers = containers.Count(c => c.WorkflowStage == "Audit");
                var totalContainers = containers.Count;

                if (auditContainers == 0 || auditContainers < totalContainers)
                {
                    _logger.LogDebug("[IMMEDIATE-AUDIT-ASSIGNMENT] Group {GroupId} has {AuditCount}/{TotalCount} containers in Audit stage - not ready",
                        groupId, auditContainers, totalContainers);
                    return;
                }

                // ✅ FIX: Load roles first, then filter users to avoid Join() generating CTE
                var auditRoleIds = await _context.Roles
                    .Where(r => r.IsActive && r.Name == "Audit")
                    .Select(r => r.Id)
                    .ToListAsync();

                var auditUsers = auditRoleIds.Any()
                    ? await _context.Users
                        .Where(u => u.IsActive && u.RoleId != null && auditRoleIds.Contains(u.RoleId.Value))
                        .Select(u => u.Username)
                        .Distinct()
                        .ToListAsync()
                    : new List<string>();

                if (!auditUsers.Any())
                {
                    _logger.LogDebug("[IMMEDIATE-AUDIT-ASSIGNMENT] No audit users available - skipping");
                    return;
                }

                // Find user with fewest active assignments
                var userAssignmentCounts = new Dictionary<string, int>();
                foreach (var username in auditUsers)
                {
                    // ✅ FIX: Load assignments first, then filter groups in memory to avoid Join() generating CTE
                    var assignments = await _context.AnalysisAssignments
                        .Where(a => a.AssignedTo == username
                            && a.Role == "Audit"
                            && a.State == "Active"
                            && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                        .Select(a => a.GroupId)
                        .ToListAsync();

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
                        .ToListAsync();

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
                _context.AnalysisAssignments.Add(new AnalysisAssignment
                {
                    GroupId = groupId,
                    AssignedTo = selectedUser,
                    Role = "Audit",
                    LeaseUntilUtc = leaseUntil,
                    State = "Active",
                    CreatedAtUtc = now
                });

                group.Status = AnalysisStatuses.AuditAssigned;
                group.UpdatedAtUtc = now;

                await _context.SaveChangesAsync();

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

                // ✅ FIX: Load groups and containers separately, then join in memory to avoid CTE generation
                var analystCompletedGroups = await _context.AnalysisGroups
                    .AsTracking()
                    .Where(g => g.Status == AnalysisStatuses.AnalystCompleted && !string.IsNullOrEmpty(g.GroupIdentifier))
                    .ToListAsync();

                var groupIdentifiers = analystCompletedGroups.Select(g => g.GroupIdentifier).Distinct().ToList();

                // ✅ FIX: Batch Contains() to avoid CTE generation
                var containers = new List<ContainerCompletenessStatus>();
                const int containerBatchSize2 = 1000;

                if (groupIdentifiers.Count > 0)
                {
                    for (int i = 0; i < groupIdentifiers.Count; i += containerBatchSize2)
                    {
                        var batch = groupIdentifiers.Skip(i).Take(containerBatchSize2).Where(g => g != null).ToList();
                        var batchContainers = await _context.ContainerCompletenessStatuses
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
                            CompletedContainers = containerGroup.Count(c => c.WorkflowStage == "Completed")
                        })
                    .Where(w => w.AuditContainers > 0 && w.CompletedContainers < w.TotalContainers)
                    .Select(x => x.Group)
                    .OrderByDescending(g => g.Priority)
                    .ToList();

                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Found {Count} total AnalystCompleted groups with Audit WorkflowStage", allAnalystCompletedGroups.Count);

                // Get group IDs that have active assignments
                var groupIdsWithActiveAssignments = await _context.AnalysisAssignments
                    .Where(a => a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .Select(a => a.GroupId)
                    .Distinct()
                    .ToListAsync();

                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Found {Count} groups with active assignments", groupIdsWithActiveAssignments.Count);

                // Filter out groups with active assignments and take up to max
                var readyGroups = allAnalystCompletedGroups
                    .Where(g => !groupIdsWithActiveAssignments.Contains(g.Id))
                    .Take(settings.MaxConcurrentPerUser - activeCount) // Fill up to max
                    .ToList();

                _logger.LogInformation("🔍 [IMMEDIATE-AUDIT-ASSIGNMENT] Found {Count} available AnalystCompleted groups for auditor {Username} (will assign up to {Max})",
                    readyGroups.Count, auditorUsername, settings.MaxConcurrentPerUser - activeCount);

                if (!readyGroups.Any())
                {
                    _logger.LogWarning("⚠️ [IMMEDIATE-AUDIT-ASSIGNMENT] No AnalystCompleted groups available for immediate assignment to auditor {Username} (Total: {Total}, With Active Assignments: {Active})",
                        auditorUsername, allAnalystCompletedGroups.Count, groupIdsWithActiveAssignments.Count);
                    return;
                }

                // Assign groups immediately
                var assignedCount = 0;
                foreach (var group in readyGroups)
                {
                    var leaseUntil = now.AddMinutes(Math.Max(1, settings.LeaseMinutes));
                    _context.AnalysisAssignments.Add(new AnalysisAssignment
                    {
                        GroupId = group.Id,
                        AssignedTo = auditorUsername,
                        Role = "Audit",
                        LeaseUntilUtc = leaseUntil,
                        State = "Active",
                        CreatedAtUtc = now
                    });

                    group.Status = AnalysisStatuses.AuditAssigned;
                    group.UpdatedAtUtc = now;
                    assignedCount++;
                }

                await _context.SaveChangesAsync();

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
        public async Task<ActionResult<object>> SaveRectangles([FromBody] RectangleSaveRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ContainerNumber) || string.IsNullOrWhiteSpace(request.ScannerType))
                {
                    return BadRequest(new { success = false, error = "ContainerNumber and ScannerType are required" });
                }

                var decision = await _context.ImageAnalysisDecisions
                    .AsTracking()
                    .FirstOrDefaultAsync(d => d.ContainerNumber == request.ContainerNumber && d.ScannerType == request.ScannerType);

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
                        new NpgsqlParameter("@p3", request.ReviewedBy ?? "System"),
                        new NpgsqlParameter("@p4", DateTime.UtcNow),
                        new NpgsqlParameter("@p5", (object?)request.GroupIdentifier ?? DBNull.Value),
                        new NpgsqlParameter("@p6", request.IsConsolidated)
                    );
                    _logger.LogInformation("[SaveRectangles] INSERTED new decision for {Container}", request.ContainerNumber);
                }
                else
                {
                    decision.SuspiciousAreas = request.SuspiciousAreas;
                    decision.UpdatedAt = DateTime.UtcNow;
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
                var decidedContainersQuery = _context.ImageAnalysisDecisions
                    .Where(d => d.GroupIdentifier == groupIdentifier &&
                               (d.Decision == "Normal" || d.Decision == "Abnormal"));

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
                request.ReviewedBy = Truncate((request.ReviewedBy ?? "System").Trim(), 100);

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

                // Prefer to allow saving even if completeness record is missing (log only)
                var hasContainer = await _context.ContainerCompletenessStatuses
                    .AnyAsync(s => s.ContainerNumber == request.ContainerNumber);
                if (!hasContainer)
                {
                    _logger.LogWarning("Saving decision for container not found in completeness tracking: {Container}", request.ContainerNumber);
                }

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

                var strategy = _context.Database.CreateExecutionStrategy();
                return await strategy.ExecuteAsync<ActionResult<object>>(async () =>
                {
                using var tx = await _context.Database.BeginTransactionAsync();

                var existing = await _context.ImageAnalysisDecisions
                    .FirstOrDefaultAsync(d => d.ContainerNumber == request.ContainerNumber && d.ScannerType == request.ScannerType);

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
                            RevenueAnomalyCategoryId = @p9
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
                        new NpgsqlParameter("@p10", existing.Id)
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
                    var insertSql = @"
                        INSERT INTO ImageAnalysisDecisions
                            (ContainerNumber, ScannerType, Decision, Comments, Tags, SuspiciousAreas, ReviewedBy, ReviewedAt, GroupIdentifier, IsConsolidated, ThreatCategoryId, RevenueAnomalyCategoryId, CreatedAt)
                        VALUES
                            (@p0, @p1, @p2, @p3, @p4, @p5, @p6, @p7, @p8, @p9, @p10, @p11, now() AT TIME ZONE 'UTC');";

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
                        new NpgsqlParameter("@p11", (object?)request.RevenueAnomalyCategoryId ?? DBNull.Value)
                    );

                    // Resolve the just-inserted id by the unique (ContainerNumber, ScannerType)
                    // tuple. The raw INSERT path can't return a generated id directly, but
                    // the controller already enforces a single decision row per pair.
                    decisionIdForSnapshot = await _context.ImageAnalysisDecisions
                        .Where(d => d.ContainerNumber == request.ContainerNumber &&
                                    d.ScannerType == request.ScannerType)
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
                    var sideEffects = new NickScanCentralImagingPortal.Services.ImageAnalysis.DecisionSideEffectsService(_logger);
                    await sideEffects.ApplyAsync(_context, request.ContainerNumber,
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
                            analysisGroup = await _context.AnalysisGroups
                                .AsTracking()
                                .FirstOrDefaultAsync(g => g.GroupIdentifier == resolvedGroupIdentifier);
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
                            var allMatchingDecisions = await _context.ImageAnalysisDecisions
                                .Where(d => (d.ScannerType == request.ScannerType || d.ScannerType.StartsWith(baseScannerType + "-") || d.ScannerType == baseScannerType) &&
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

                                    analysisGroup = new AnalysisGroup
                                    {
                                        Id = Guid.NewGuid(),
                                        GroupIdentifier = resolvedGroupIdentifier,
                                        ScannerType = firstContainerStatus?.ScannerType ?? request.ScannerType,
                                        Status = AnalysisStatuses.PartiallyCompleted, // Set directly to PartiallyCompleted
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
                                    analysisGroup.Status = AnalysisStatuses.PartiallyCompleted;
                                    analysisGroup.PartiallyCompletedDate = DateTime.UtcNow;
                                    analysisGroup.TotalContainerCount = groupContainers.Count;
                                    analysisGroup.SubmittedContainerCount = 0; // No containers with images to submit
                                    analysisGroup.PendingContainerCount = containersWithoutImages.Count;
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
                                originalGroupIdentifier, groupContainers, groupScannerType);

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

                                // ✅ FIX: Set status to PartiallyCompleted when some containers have no images
                                analysisGroup.Status = AnalysisStatuses.PartiallyCompleted;
                                analysisGroup.PartiallyCompletedDate = DateTime.UtcNow;
                                analysisGroup.TotalContainerCount = groupContainers.Count;
                                analysisGroup.SubmittedContainerCount = containersWithImages.Count; // Containers with images that were decided
                                analysisGroup.PendingContainerCount = containersWithoutImages.Count; // Containers without images
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

                                await _context.SaveChangesAsync();

                                // ✅ FIX: Update WorkflowStage for containers WITHOUT images to 'PartiallyCompleted'
                                // Use LINQ to update entities directly (safer than raw SQL)
                                if (containersWithoutImages.Any())
                                {
                                    var containersWithoutImagesStatuses = await _context.ContainerCompletenessStatuses
                                        .AsTracking()
                                        .Where(c => containersWithoutImages.Contains(c.ContainerNumber) &&
                                                   c.GroupIdentifier == normalizedForCompleteness &&
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
                                    var containersWithImagesStatuses = await _context.ContainerCompletenessStatuses
                                        .AsTracking()
                                        .Where(c => containersWithImages.Contains(c.ContainerNumber) &&
                                                   c.GroupIdentifier == normalizedForCompleteness &&
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

                                // ✅ FIX: Set status to AnalystCompleted (not Completed) to make it available for Audit assignment
                                analysisGroup.Status = AnalysisStatuses.AnalystCompleted;
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
                            var sql = "UPDATE ContainerCompletenessStatuses SET WorkflowStage = @p0, UpdatedAt = now() AT TIME ZONE 'UTC' WHERE GroupIdentifier = @p1 AND WorkflowStage <> @p0 AND WorkflowStage <> 'Completed'";
                            var affected = await _context.Database.ExecuteSqlRawAsync(sql,
                                new NpgsqlParameter("@p0", "Audit"),
                                new NpgsqlParameter("@p1", normalizedForCompleteness));
                            advanced = affected > 0;

                            _logger.LogInformation("All containers in group {Group} have decisions. WorkflowStage updated to 'Audit': {Count} records (skipped containers already in 'Completed' stage)",
                                resolvedGroupIdentifier, affected);

                            // ✅ CRITICAL FIX: If no containers were updated, check why and log detailed warning
                            if (affected == 0)
                            {
                                // Check WorkflowStage distribution for this group (use normalized - Completeness uses base identifier)
                                var workflowStats = await _context.ContainerCompletenessStatuses
                                    .Where(c => c.GroupIdentifier == normalizedForCompleteness)
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
                    await EnsureGroupStatusFromWorkflowStageAsync(resolvedGroupIdentifier);
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

