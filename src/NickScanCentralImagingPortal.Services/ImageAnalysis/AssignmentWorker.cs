using System;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    public class AssignmentWorker : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<AssignmentWorker> _logger;
        private readonly ReadyGroupsCacheService _readyGroupsCache;

        // ✅ FIX FLAW #1: Track last assigned user per role for true RoundRobin
        private static readonly Dictionary<string, string> _lastAssignedUserByRole = new();

        // ✅ OPTIMIZATION: Track last validation time to throttle validation frequency
        private static DateTime _lastValidationTime = DateTime.MinValue;

        public AssignmentWorker(
            IServiceScopeFactory scopeFactory,
            ILogger<AssignmentWorker> logger,
            ReadyGroupsCacheService readyGroupsCache)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
            _readyGroupsCache = readyGroupsCache;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var settings = await db.AnalysisSettings.FirstOrDefaultAsync(stoppingToken) ?? new AnalysisSettings();

                    if (!settings.Enabled)
                    {
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                        continue;
                    }

                    // Always reclaim expired leases (all modes)
                    var now = DateTime.UtcNow;
                    await ReclaimExpiredAssignmentsAsync(db, now, stoppingToken);

                    // ✅ FIX: Clean up expired user readiness - mark users as not ready if heartbeat expired
                    await CleanupExpiredUserReadinessAsync(db, now, stoppingToken);

                    // ✅ PHASE 2: Validate assignments periodically to identify and fix state inconsistencies
                    // Run validation BEFORE auto-assignment to clean up invalid assignments first
                    // ✅ OPTIMIZATION: Run validation every 30 seconds instead of every 5 seconds to reduce connection pool pressure
                    // Use a simple static variable to track last validation time
                    var timeSinceLastValidation = (now - _lastValidationTime).TotalSeconds;
                    if (timeSinceLastValidation >= 30)
                    {
                        await ValidateAssignmentsAsync(db, now, stoppingToken);
                        _lastValidationTime = now;
                    }

                    // Handle assignment based on mode
                    var assignmentMode = string.IsNullOrEmpty(settings.AssignmentMode) ? "Manual" : settings.AssignmentMode;

                    if (assignmentMode == "Auto")
                    {
                        await AutoAssignGroupsAsync(db, settings, now, stoppingToken);
                    }
                    // Manual and UserClaim modes don't auto-assign - handled by Admin/users

                    // ✅ REDUCED INTERVAL: Faster fallback for near real-time assignments (5 seconds instead of 20)
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
                catch (OperationCanceledException)
                {
                    // Service is being cancelled - exit gracefully
                    break;
                }
                catch (Exception ex)
                {
                    // Check if it's a database connectivity error
                    if (IsDatabaseConnectivityException(ex))
                    {
                        _logger.LogWarning(ex, "[ASSIGNMENT-WORKER] Database connectivity issue (This is normal during startup or when SQL Server is unavailable). Retrying in 30 seconds...");
                        // Wait longer before retrying when database is unavailable
                        await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                    }
                    else
                    {
                        _logger.LogError(ex, "[ASSIGNMENT-WORKER] Error in assignment worker. Retrying in 5 seconds...");
                        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                    }
                }
            }
        }

        /// <summary>
        /// Check if exception is a database connectivity issue
        /// </summary>
        private static bool IsDatabaseConnectivityException(Exception ex)
        {
            if (ex is PostgresException sqlEx)
            {
                // SQL Server error numbers for connectivity issues
                return sqlEx.SqlState == "00000" || sqlEx.SqlState == "00000" || sqlEx.SqlState == "00000" ||
                       sqlEx.SqlState == "00000" || sqlEx.SqlState == "00000" || sqlEx.SqlState == "00000" ||
                       sqlEx.SqlState == "00000" || sqlEx.SqlState == "00000" ||
                       sqlEx.Message.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("instance-specific error", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase) ||
                       sqlEx.Message.Contains("refused the network connection", StringComparison.OrdinalIgnoreCase);
            }

            // Check for common database connectivity error messages
            var errorMessage = ex.Message;
            return errorMessage.Contains("network-related", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("instance-specific error", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("cannot find the file specified", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("could not open a connection", StringComparison.OrdinalIgnoreCase) ||
                   errorMessage.Contains("refused the network connection", StringComparison.OrdinalIgnoreCase) ||
                   (ex.InnerException != null && IsDatabaseConnectivityException(ex.InnerException));
        }

        private async Task AutoAssignGroupsAsync(
            ApplicationDbContext db,
            AnalysisSettings settings,
            DateTime now,
            CancellationToken stoppingToken)
        {
            try
            {
                // Get available analysts (users with Analyst role, not at MaxConcurrent)
                await AutoAssignByRoleAsync(
                    db,
                    settings,
                    roleName: "Analyst",
                    eligibleStatus: AnalysisStatuses.Ready,
                    assignedStatus: AnalysisStatuses.AnalystAssigned,
                    now,
                    stoppingToken);

                await AutoAssignByRoleAsync(
                    db,
                    settings,
                    roleName: "Audit",
                    eligibleStatus: AnalysisStatuses.AnalystCompleted,
                    assignedStatus: AnalysisStatuses.AuditAssigned,
                    now,
                    stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in AutoAssignGroupsAsync");
            }
        }

        private async Task AutoAssignByRoleAsync(
            ApplicationDbContext db,
            AnalysisSettings settings,
            string roleName,
            string eligibleStatus,
            string assignedStatus,
            DateTime now,
            CancellationToken stoppingToken)
        {
            // ✅ DIAGNOSTIC: Log start of assignment cycle
            _logger.LogInformation("[AUTO-ASSIGN] Starting assignment cycle for Role: {Role}, EligibleStatus: {Status}, AssignmentMode: {Mode}",
                roleName, eligibleStatus, settings.AssignmentMode ?? "Manual");

            // ✅ OPTIMIZATION: Use shared cache service to reduce duplicate queries
            // This eliminates redundant database queries across AssignmentWorker, IntakeWorker, and HousekeepingWorker
            var readyGroups = await _readyGroupsCache.GetReadyGroupsForRoleAsync(roleName, eligibleStatus, stoppingToken);

            // ✅ DIAGNOSTIC: Log groups found
            _logger.LogInformation("[AUTO-ASSIGN] Found {Count} groups with status {Status} for {Role} role (after WorkflowStage filtering)",
                readyGroups.Count, eligibleStatus, roleName);

            if (!readyGroups.Any())
            {
                _logger.LogInformation("[AUTO-ASSIGN] No groups found with status {Status} for {Role} - skipping assignment",
                    eligibleStatus, roleName);
                return;
            }

            // ✅ DIAGNOSTIC: Log groups after WorkflowStage filtering
            _logger.LogInformation("[AUTO-ASSIGN] After WorkflowStage filtering: {Count} groups eligible for {Role} assignment",
                readyGroups.Count, roleName);

            // ✅ HYBRID APPROACH: Check for ready users (SignalR + Database)
            // Get users who are BOTH in database with correct role AND ready for assignment
            var readyUsers = await GetReadyUsersForRoleAsync(db, roleName, stoppingToken);

            // ✅ DIAGNOSTIC: Always log ready users found (even if no groups available)
            _logger.LogInformation("[AUTO-ASSIGN] Found {Count} ready users with {Role} role: {Users}",
                readyUsers.Count, roleName, string.Join(", ", readyUsers));

            // Also log database users for comparison (diagnostic)
            var dbUsers = await GetActiveUsersForRoleAsync(db, roleName, stoppingToken);
            if (dbUsers.Count > readyUsers.Count)
            {
                var notReady = dbUsers.Except(readyUsers).ToList();
                _logger.LogDebug("[AUTO-ASSIGN] {Count} users with {Role} role are not ready: {Users}",
                    notReady.Count, roleName, string.Join(", ", notReady));
            }

            if (!readyGroups.Any())
            {
                if (!readyUsers.Any())
                {
                    _logger.LogWarning("[AUTO-ASSIGN] No groups AND no ready users for {Role} - check configuration. No groups eligible after WorkflowStage filtering.",
                        roleName);
                }
                else
                {
                    _logger.LogInformation("[AUTO-ASSIGN] No groups eligible for {Role} after WorkflowStage filtering, but {Count} users are ready: {Users}",
                        roleName, readyUsers.Count, string.Join(", ", readyUsers));
                }
                return;
            }

            // Use readyUsers instead of usersWithRole for assignments
            var usersWithRole = readyUsers;

            if (!usersWithRole.Any())
            {
                _logger.LogWarning("[AUTO-ASSIGN] No active users found with role {Role} - skipping assignment", roleName);
                return;
            }

            // ✅ FIX INTERMITTENT: Simplified assignment count - only count for ready users to avoid slow queries
            // Only count assignments for users we know are ready (much faster than complex JOINs)
            // This avoids the performance issue with 30k+ stale assignments
            var userAssignmentCounts = new Dictionary<string, int>();

            foreach (var username in usersWithRole)
            {
                try
                {
                    // ✅ FIX: Load assignments first, then filter in memory to avoid Join() generating CTE
                    var assignments = await db.AnalysisAssignments
                        .Where(a => a.AssignedTo == username
                            && a.Role == roleName
                            && a.State == "Active"
                            && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                        .Select(a => a.GroupId)
                        .ToListAsync(stoppingToken);

                    if (!assignments.Any())
                    {
                        userAssignmentCounts[username] = 0;
                        continue;
                    }

                    // Load groups and filter in memory
                    var groupIds = assignments.Distinct().ToList();
                    var validGroups = await db.AnalysisGroups
                        .Where(g => groupIds.Contains(g.Id) &&
                            (roleName == "Analyst"
                                ? (g.Status != AnalysisStatuses.AnalystCompleted
                                    && g.Status != AnalysisStatuses.AuditCompleted
                                    && g.Status != AnalysisStatuses.Completed)
                                : (g.Status != AnalysisStatuses.AuditCompleted
                                    && g.Status != AnalysisStatuses.Completed)))
                        .Select(g => g.Id)
                        .ToListAsync(stoppingToken);

                    var count = validGroups.Count;

                    if (count > 0)
                    {
                        userAssignmentCounts[username] = count;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "[AUTO-ASSIGN] Error counting assignments for user {Username}, assuming 0", username);
                    userAssignmentCounts[username] = 0;
                }
            }

            // ✅ DEBUG: Log assignment counts for diagnosis
            var totalValidAssignments = userAssignmentCounts.Values.Sum();
            _logger.LogInformation(
                "[ASSIGNMENT-COUNT] Role: {Role}, Valid Assignments: {Count}, UserCounts: {Counts}",
                roleName,
                totalValidAssignments,
                string.Join(", ", userAssignmentCounts.Select(kvp => $"{kvp.Key}={kvp.Value}")));

            // Guard: MaxConcurrentPerUser must be at least 1
            var maxConcurrent = Math.Max(1, settings.MaxConcurrentPerUser);

            var availableUsers = usersWithRole
                .Where(username => !userAssignmentCounts.ContainsKey(username) || userAssignmentCounts[username] < maxConcurrent)
                .ToList();

            // ✅ DIAGNOSTIC: Log available users
            _logger.LogInformation("[AUTO-ASSIGN] Available {Role} users: {Count} (MaxConcurrent={Max}), UserCounts: {Counts}",
                roleName, availableUsers.Count, settings.MaxConcurrentPerUser,
                string.Join(", ", userAssignmentCounts.Select(kvp => $"{kvp.Key}={kvp.Value}")));

            if (!availableUsers.Any())
            {
                _logger.LogInformation("[AUTO-ASSIGN] No available {Role} users (all at MaxConcurrent={Max}) - skipping assignment",
                    roleName, settings.MaxConcurrentPerUser);
                return;
            }

            // Assign groups to available users using strategy
            var strategy = string.IsNullOrEmpty(settings.AutoAssignStrategy) ? "RoundRobin" : settings.AutoAssignStrategy;
            int assignedCount = 0;

            // ✅ DIAGNOSTIC: Log groups to process
            _logger.LogInformation("[AUTO-ASSIGN] Processing {Count} groups for {Role} assignment", readyGroups.Count, roleName);

            // ✅ FIX INTERMITTENT ASSIGNMENTS: Use transactions to prevent race conditions
            // Process assignments one-by-one with transaction isolation to prevent conflicts
            var assignmentsToCreate = new List<AnalysisAssignment>();
            var groupsToUpdate = new List<AnalysisGroup>();

            foreach (var group in readyGroups)
            {
                if (!availableUsers.Any())
                {
                    _logger.LogInformation("[AUTO-ASSIGN] No more available users - stopping assignment");
                    break;
                }

                // ✅ FIX: Re-check group availability in transaction to prevent race conditions
                var continueForeach = false;
                var breakForeach = false;
                var executionStrategy = db.Database.CreateExecutionStrategy();
                await executionStrategy.ExecuteAsync(async () =>
                {
                await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);
                try
                {
                    // Re-check if group still has no active assignment (inside transaction)
                    var hasActive = await db.AnalysisAssignments
                        .AnyAsync(a => a.GroupId == group.Id
                            && a.State == "Active"
                            && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now), stoppingToken);

                    if (hasActive)
                    {
                        await transaction.RollbackAsync(stoppingToken);
                        _logger.LogDebug("[AUTO-ASSIGN] Group {GroupId} ({GroupIdentifier}) already has active assignment - skipping (race condition detected)",
                            group.Id, group.GroupIdentifier);
                        continueForeach = true;
                        return;
                    }

                    var currentGroup = await db.AnalysisGroups
                        .AsTracking()
                        .FirstOrDefaultAsync(g => g.Id == group.Id, stoppingToken);
                    if (currentGroup == null || currentGroup.Status != eligibleStatus)
                    {
                        await transaction.RollbackAsync(stoppingToken);
                        _logger.LogDebug("[AUTO-ASSIGN] Group {GroupId} ({GroupIdentifier}) status changed - skipping",
                            group.Id, group.GroupIdentifier);
                        continueForeach = true;
                        return;
                    }

                    // Select user based on strategy, retrying with alternate users if at capacity
                    string? selectedUser = null;
                    int selectedUserActiveCount = 0;
                    var triedUsers = new HashSet<string>();
                    while (availableUsers.Count > triedUsers.Count)
                    {
                        var candidate = SelectUserByStrategy(
                            availableUsers.Where(u => !triedUsers.Contains(u)).ToList(),
                            userAssignmentCounts, strategy, roleName);

                        var candidateActiveCount = await db.AnalysisAssignments
                            .Where(a => a.AssignedTo == candidate
                                && a.Role == roleName
                                && a.State == "Active"
                                && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                            .CountAsync(stoppingToken);

                        if (candidateActiveCount < maxConcurrent)
                        {
                            selectedUser = candidate;
                            selectedUserActiveCount = candidateActiveCount;
                            break;
                        }

                        triedUsers.Add(candidate);
                        availableUsers.Remove(candidate);
                    }

                    if (selectedUser == null)
                    {
                        await transaction.RollbackAsync(stoppingToken);
                        _logger.LogInformation("[AUTO-ASSIGN] All users at capacity - stopping assignment");
                        breakForeach = true;
                        return;
                    }

                    // ✅ DIAGNOSTIC: Log assignment creation
                    _logger.LogInformation("[AUTO-ASSIGN] Assigning group {GroupId} ({GroupIdentifier}) to {User} ({Role})",
                        group.Id, group.GroupIdentifier, selectedUser, roleName);

                    // Create assignment
                    var assignment = new AnalysisAssignment
                    {
                        GroupId = group.Id,
                        AssignedTo = selectedUser,
                        Role = roleName,
                        LeaseUntilUtc = now.AddMinutes(Math.Max(1, settings.LeaseMinutes)),
                        State = "Active",
                        CreatedAtUtc = now
                    };
                    db.AnalysisAssignments.Add(assignment);

                    // ✅ PHASE 3: Event logging for assignment creation
                    _logger.LogInformation("[ASSIGNMENT-EVENT] Created | AssignmentId={AssignmentId} | GroupId={GroupId} | GroupIdentifier={GroupIdentifier} | User={User} | Role={Role} | LeaseUntil={LeaseUntil} | Reason=AutoAssign",
                        assignment.Id, assignment.GroupId, group.GroupIdentifier, selectedUser, roleName, assignment.LeaseUntilUtc);

                    // Update group status
                    currentGroup.Status = assignedStatus;
                    currentGroup.UpdatedAtUtc = now;

                    // Save changes in transaction
                    await db.SaveChangesAsync(stoppingToken);
                    await transaction.CommitAsync(stoppingToken);

                    // Update in-memory counts for next iteration
                    userAssignmentCounts[selectedUser] = selectedUserActiveCount + 1;
                    assignedCount++;

                    _logger.LogDebug("[AUTO-ASSIGN] ✅ Successfully assigned group {GroupId} to {User} (transaction committed)",
                        group.Id, selectedUser);

                    // Update materialized queue entry (after transaction, synchronous — db context is cleared in finally)
                    try
                    {
                        await _readyGroupsCache.UpsertQueueEntryAsync(db, assignment.Id, stoppingToken);
                    }
                    catch (Exception qEx)
                    {
                        // WARNING level (not Debug) — one-off upsert failures during
                        // assignment creation should be visible in dashboard RecentErrors.
                        // AssignmentQueue health check will catch and auto-repair divergence.
                        _logger.LogWarning(qEx, "[QUEUE] Upsert failed for assignment {Id} — reconciliation will fix", assignment.Id);
                    }
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync(stoppingToken);
                    _logger.LogWarning(ex, "[AUTO-ASSIGN] Failed to assign group {GroupId} - transaction rolled back", group.Id);
                    // Continue with next group
                }
                finally
                {
                    db.ChangeTracker.Clear();
                }
                });

                if (continueForeach)
                {
                    continue;
                }

                if (breakForeach)
                {
                    break;
                }
            }

            if (assignedCount > 0)
            {
                _logger.LogInformation("[AUTO-ASSIGN] ✅ Successfully auto-assigned {Count} groups to {Role} users using {Strategy} strategy",
                    assignedCount, roleName, strategy);
            }
            else
            {
                _logger.LogInformation("[AUTO-ASSIGN] No groups assigned for {Role} role (all groups may have active assignments or no available users)",
                    roleName);
            }
        }

        private string SelectUserByStrategy(
            List<string> availableUsers,
            Dictionary<string, int> assignmentCounts,
            string strategy,
            string roleName)
        {
            // ✅ FIX: Defensive check - should never be empty due to caller validation, but fail fast if it is
            if (!availableUsers.Any())
            {
                throw new InvalidOperationException("Cannot select user from empty available users list");
            }

            if (strategy == "LeastLoaded")
            {
                // Assign to user with fewest active assignments
                return availableUsers
                    .OrderBy(u => assignmentCounts.GetValueOrDefault(u, 0))
                    .ThenBy(u => u) // Tie-breaker: alphabetical
                    .First();
            }
            else // RoundRobin (default)
            {
                // ✅ FIX FLAW #1: Implement true RoundRobin - cycle through users in order per role
                // Find the last assigned user for this role and continue from the next user
                var roleKey = $"RoundRobin:{roleName}";
                var lastUser = _lastAssignedUserByRole.GetValueOrDefault(roleKey, null);
                var sortedUsers = availableUsers.OrderBy(u => u).ToList();

                if (string.IsNullOrEmpty(lastUser) || !sortedUsers.Contains(lastUser))
                {
                    // Start with first user for this role
                    var selected = sortedUsers.First();
                    _lastAssignedUserByRole[roleKey] = selected;
                    return selected;
                }

                // Find last user's position and select next user
                var lastIndex = sortedUsers.IndexOf(lastUser);
                var nextIndex = (lastIndex + 1) % sortedUsers.Count;
                var selectedUser = sortedUsers[nextIndex];
                _lastAssignedUserByRole[roleKey] = selectedUser;

                return selectedUser;
            }
        }

        private async Task<List<string>> GetActiveUsersForRoleAsync(
            ApplicationDbContext db,
            string roleName,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                _logger.LogWarning("[AUTO-ASSIGN] GetActiveUsersForRoleAsync called with null or empty role name");
                return new List<string>();
            }

            // ✅ FIX: Use case-insensitive comparison for role name
            var roleNameUpper = roleName.Trim().ToUpperInvariant();

            // PRIMARY METHOD: Query via RoleId with case-insensitive comparison
            // ✅ FIX: Load roles first, then load all active users, then filter in memory to avoid Contains() generating CTE
            var matchingRoles = await db.Roles
                .AsNoTracking()
                .Where(r => r.IsActive && r.Name.ToUpper() == roleNameUpper)
                .Select(r => r.Id)
                .ToListAsync(ct);

            var users = new List<string>();
            if (matchingRoles.Any())
            {
                // ✅ FIX: Load all active users with roles first, then filter in memory
                var allActiveUsers = await db.Users
                    .AsNoTracking()
                    .Where(u => u.IsActive && u.RoleId != null)
                    .ToListAsync(ct);

                // Filter in memory to avoid CTE generation
                var matchingRoleSet = new HashSet<int>(matchingRoles);
                users = allActiveUsers
                    .Where(u => u.RoleId.HasValue && matchingRoleSet.Contains(u.RoleId.Value))
                    .Select(u => u.Username)
                    .Distinct()
                    .ToList();
            }

            _logger.LogDebug("[AUTO-ASSIGN] Found {Count} users via RoleId join for role '{Role}' (searched as '{RoleUpper}')",
                users.Count, roleName, roleNameUpper);

            // FALLBACK: If no users via RoleId, check AssignedRole navigation (legacy data)
            if (!users.Any())
            {
                users = await db.Users
                    .AsNoTracking()
                    .Include(u => u.AssignedRole)  // ✅ FIX: Explicitly include navigation property
                    .Where(u => u.IsActive
                        && u.AssignedRole != null
                        && u.AssignedRole.Name.ToUpper() == roleNameUpper
                        && u.AssignedRole.IsActive)
                    .Select(u => u.Username)
                    .Distinct()
                    .ToListAsync(ct);

                _logger.LogDebug("[AUTO-ASSIGN] Found {Count} users via AssignedRole navigation for role '{Role}' (fallback method)",
                    users.Count, roleName);
            }

            return users;
        }

        /// <summary>
        /// Get users who are ready for assignment (hybrid approach: checks SignalR first, then database)
        /// Priority: SignalR (real-time) > Database (persistence)
        /// Only returns users who are BOTH ready AND have correct role in database
        /// </summary>
        private async Task<List<string>> GetReadyUsersForRoleAsync(
            ApplicationDbContext db,
            string roleName,
            CancellationToken ct)
        {
            if (string.IsNullOrWhiteSpace(roleName))
            {
                _logger.LogWarning("[AUTO-ASSIGN] GetReadyUsersForRoleAsync called with null or empty role name");
                return new List<string>();
            }

            var roleNameUpper = roleName.Trim().ToUpperInvariant();
            // ✅ FIX: Reduced heartbeat window from 5 to 2 minutes for faster detection of logged-out users
            var maxIdleMinutes = 2; // Users idle > 2 minutes are not ready (reduced from 5 minutes)
            var maxIdleTime = TimeSpan.FromMinutes(maxIdleMinutes);
            var dbMaxIdle = DateTime.UtcNow.AddMinutes(-maxIdleMinutes);

            // ✅ AUTHENTICATION CHECK: SignalR state is the PRIMARY source for authentication verification
            // SignalR connections require authentication by default, so users in SignalR are guaranteed to be logged in
            // This is our authentication check - only users with active SignalR connections are considered authenticated
            var signalRReadyUsers = UserReadinessStateProvider.GetReadyUsers(roleName, maxIdleTime);
            _logger.LogDebug("[AUTO-ASSIGN] Found {Count} ready users from SignalR for role '{Role}' (actively connected = authenticated)",
                signalRReadyUsers.Count, roleName);

            // ✅ FALLBACK: Only use database as fallback for users who might not be in SignalR but have very recent heartbeats
            // Database records are less reliable than SignalR because they can be stale
            // We prioritize SignalR because it indicates active connection (authentication)
            // Database is only used as fallback with strict 2-minute heartbeat requirement
            var allReadinessRecords = await db.UserReadiness
                .Where(r => r.Role.ToUpper() == roleNameUpper)
                .Select(r => new { r.Username, r.IsReady, r.LastHeartbeat })
                .ToListAsync(ct);

            _logger.LogInformation("[AUTO-ASSIGN] Total UserReadiness records for role '{Role}': {TotalCount}",
                roleName, allReadinessRecords.Count);

            // Log details about each record for diagnosis
            foreach (var record in allReadinessRecords)
            {
                var timeSinceHeartbeat = DateTime.UtcNow - record.LastHeartbeat;
                var isExpired = record.LastHeartbeat < dbMaxIdle;
                var status = !record.IsReady ? "NOT READY" : isExpired ? "HEARTBEAT EXPIRED" : "READY";
                _logger.LogDebug("[AUTO-ASSIGN] UserReadiness: {Username} - {Status} (IsReady: {IsReady}, Heartbeat: {Heartbeat}, Age: {AgeMinutes:F1} min)",
                    record.Username, status, record.IsReady, record.LastHeartbeat, timeSinceHeartbeat.TotalMinutes);
            }

            // ✅ FIX: Stricter database check - only include users with very recent heartbeats (< 2 minutes)
            // This ensures we only consider users who are actively logged in
            var dbReadyUsers = allReadinessRecords
                .Where(r => r.IsReady && r.LastHeartbeat >= dbMaxIdle)
                .Select(r => r.Username)
                .Distinct()
                .ToList();

            _logger.LogInformation("[AUTO-ASSIGN] Found {Count} ready users from database for role '{Role}' (idle timeout: {MaxIdleMinutes} min, cutoff: {CutoffTime})",
                dbReadyUsers.Count, roleName, maxIdleMinutes, dbMaxIdle);

            // ✅ FIX: Prioritize SignalR users (actively connected = authenticated) over database
            // SignalR connection requires authentication, so SignalR users are guaranteed to be logged in
            // Only add database users who are NOT already in SignalR (to avoid duplicates)
            // This ensures we only create assignments for users who are actively authenticated/connected
            var combinedReadyUsers = signalRReadyUsers
                .Union(dbReadyUsers.Where(u => !signalRReadyUsers.Contains(u))) // Only add DB users not in SignalR
                .Distinct()
                .ToList();

            _logger.LogInformation("[AUTO-ASSIGN] Combined ready users (SignalR + Database) for role '{Role}': {Count} (SignalR: {SignalRCount} [authenticated], DB: {DbCount} [fallback])",
                roleName, combinedReadyUsers.Count, signalRReadyUsers.Count, dbReadyUsers.Count);

            // ✅ AUTHENTICATION CHECK: SignalR users are authenticated (SignalR requires auth by default)
            // Database users are only included as fallback if they have very recent heartbeats (< 2 minutes)
            // This ensures assignments are only created for actively authenticated users

            // Verify these users exist in database and have correct role
            if (!combinedReadyUsers.Any())
            {
                return new List<string>();
            }

            // ✅ FIX: Batch Contains() and load data first, then join in memory to avoid EF Core CTE generation
            var validUsers = new List<string>();
            const int userBatchSize = 1000;

            // Load roles first to avoid Join() generating CTE
            var matchingRoles = await db.Roles
                .Where(r => r.IsActive && r.Name.ToUpper() == roleNameUpper)
                .Select(r => r.Id)
                .ToListAsync(ct);

            if (combinedReadyUsers.Count > 0 && matchingRoles.Any())
            {
                for (int i = 0; i < combinedReadyUsers.Count; i += userBatchSize)
                {
                    var batch = combinedReadyUsers.Skip(i).Take(userBatchSize).ToList();
                    var batchUsers = await db.Users
                        .Where(u => u.IsActive && u.RoleId != null && batch.Contains(u.Username) && matchingRoles.Contains(u.RoleId.Value))
                        .Select(u => u.Username)
                        .Distinct()
                        .ToListAsync(ct);
                    validUsers.AddRange(batchUsers);
                }
            }

            validUsers = validUsers.Distinct().ToList();

            _logger.LogDebug("[AUTO-ASSIGN] Found {Count} valid ready users (verified in database) for role '{Role}'",
                validUsers.Count, roleName);

            return validUsers;
        }

        private async Task ReclaimExpiredAssignmentsAsync(
            ApplicationDbContext db,
            DateTime now,
            CancellationToken ct)
        {
            // ✅ RELIABILITY FIX: Don't expire assignments that were accessed recently (active work session protection)
            // If LastAccessedAtUtc is within last 30 minutes, consider it an active work session and don't expire
            var activeWorkThreshold = now.AddMinutes(-30);

            List<AnalysisAssignment> expired;
            try
            {
                // ✅ OPTIMIZATION: Limit query to prevent connection pool exhaustion
                // Try query with LastAccessedAtUtc (requires migration: AddLastAccessedAtUtcToAnalysisAssignments)
                expired = await db.AnalysisAssignments
                    .AsTracking()
                    .Where(a => a.State == "Active"
                        && a.LeaseUntilUtc != null
                        && a.LeaseUntilUtc < now
                        // Don't expire if accessed recently (active work session)
                        && (a.LastAccessedAtUtc == null || a.LastAccessedAtUtc < activeWorkThreshold))
                    .Take(1000) // ✅ LIMIT: Process max 1000 at a time to prevent connection pool exhaustion
                    .ToListAsync(ct);
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx) when (sqlEx.SqlState == "00000") // Invalid column name
            {
                // ✅ FALLBACK: If LastAccessedAtUtc column doesn't exist yet, use simpler query
                // This handles the case where migration hasn't been applied yet
                _logger.LogWarning("LastAccessedAtUtc column not found. Using fallback query without active work session protection. Please run migration: AddLastAccessedAtUtcToAnalysisAssignments");
                expired = await db.AnalysisAssignments
                    .AsTracking()
                    .Where(a => a.State == "Active"
                        && a.LeaseUntilUtc != null
                        && a.LeaseUntilUtc < now)
                    .Take(1000) // ✅ LIMIT: Process max 1000 at a time
                    .ToListAsync(ct);
            }

            if (!expired.Any())
            {
                return;
            }

            // ✅ BATCH PROCESSING: Process in batches to avoid database timeouts with large numbers
            const int batchSize = 100;
            var totalReclaimed = 0;
            var batches = expired
                .Select((assignment, index) => new { Assignment = assignment, Index = index })
                .GroupBy(x => x.Index / batchSize)
                .Select(g => g.Select(x => x.Assignment).ToList())
                .ToList();

            if (batches.Count > 1)
            {
                _logger.LogInformation("[CLEANUP] Processing {Total} expired assignments in {BatchCount} batch(es) of {BatchSize}",
                    expired.Count, batches.Count, batchSize);
            }

            // ✅ OPTIMIZATION: Load all groups once for all batches with AsNoTracking
            // ✅ FIX: Use raw SQL to avoid CTE generation that requires semicolons in SQL Server 2014
            var groupIds = expired.Select(a => a.GroupId).Distinct().ToList();
            var groups = new List<AnalysisGroup>();

            if (groupIds.Count > 0)
            {
                const int groupBatchSize = 100;
                for (int i = 0; i < groupIds.Count; i += groupBatchSize)
                {
                    var batch = groupIds.Skip(i).Take(groupBatchSize).ToList();
                    var parameters = batch.Cast<object>().ToArray();
                    var placeholders = string.Join(",", batch.Select((_, idx) => $"{{{idx}}}"));

                    var batchGroups = await db.AnalysisGroups
                        .FromSqlRaw($"SELECT * FROM AnalysisGroups WHERE Id IN ({placeholders})", parameters)
                        .AsNoTracking() // ✅ CRITICAL: Don't track entities
                        .ToListAsync(ct);

                    groups.AddRange(batchGroups);
                }
            }
            var groupLookup = groups.ToDictionary(g => g.Id);

            foreach (var batch in batches)
            {
                try
                {
                    foreach (var assignment in batch)
                    {
                        assignment.State = "Expired";
                        assignment.UpdatedAtUtc = now;

                        // ✅ PHASE 3: Event logging for assignment expiration (only for first batch to reduce log noise)
                        if (batches.IndexOf(batch) == 0 && batch.IndexOf(assignment) < 5)
                        {
                            _logger.LogInformation("[ASSIGNMENT-EVENT] Expired | AssignmentId={AssignmentId} | GroupId={GroupId} | User={User} | Role={Role} | LeaseUntil={LeaseUntil} | LastAccessed={LastAccessed} | Reason=LeaseExpired",
                                assignment.Id, assignment.GroupId, assignment.AssignedTo, assignment.Role, assignment.LeaseUntilUtc, assignment.LastAccessedAtUtc);
                        }

                        if (groupLookup.TryGetValue(assignment.GroupId, out var group))
                        {
                            var groupToUpdate = await db.AnalysisGroups
                                .AsTracking()
                                .FirstOrDefaultAsync(g => g.Id == group.Id, ct);
                            if (groupToUpdate != null &&
                                (groupToUpdate.Status == AnalysisStatuses.AnalystAssigned || groupToUpdate.Status == AnalysisStatuses.AuditAssigned))
                            {
                                // 2026-05-04 (2.16.1): orphan-AG guard. If every container
                                // in this AG has no boedocumentid + no active CBR, the AG
                                // has no actionable work — transition to Cancelled instead
                                // of bouncing back to Ready/AnalystCompleted, otherwise the
                                // lease cycle re-issues the assignment every ~10 minutes.
                                if (await IsOrphanAnalysisGroupAsync(db, groupToUpdate.Id, ct))
                                {
                                    groupToUpdate.Status = AnalysisStatuses.Cancelled;
                                    _logger.LogInformation(
                                        "[CLEANUP] Orphan AG {GroupId} ({GroupIdentifier}) → Cancelled (no boedocumentid, no active CBR; lease sweeper would otherwise re-issue assignment)",
                                        groupToUpdate.Id, groupToUpdate.GroupIdentifier);
                                }
                                else
                                {
                                    groupToUpdate.Status = assignment.Role == "Audit"
                                        ? AnalysisStatuses.AnalystCompleted
                                        : AnalysisStatuses.Ready;
                                }
                                groupToUpdate.UpdatedAtUtc = now;
                            }
                        }
                    }

                    var savedCount = await db.SaveChangesAsync(ct);
                    totalReclaimed += batch.Count;

                    // Remove expired assignments from materialized queue
                    foreach (var assignment in batch)
                    {
                        try { await _readyGroupsCache.RemoveQueueEntryAsync(db, assignment.Id, ct); }
                        catch { /* reconciliation catches misses */ }
                    }

                    if (batches.Count > 1)
                    {
                        _logger.LogInformation("[CLEANUP] ✅ Reclaimed batch of {Count} expired assignments (Total so far: {Total}/{GrandTotal}, Saved: {SavedCount} changes)",
                            batch.Count, totalReclaimed, expired.Count, savedCount);
                    }
                }
                catch (Exception batchEx)
                {
                    _logger.LogError(batchEx, "[CLEANUP] ❌ Failed to reclaim batch of {Count} expired assignments: {Message} | Will continue with next batch",
                        batch.Count, batchEx.Message);
                    // Continue with next batch - don't let one batch failure stop the cleanup
                }
            }

            if (totalReclaimed > 0)
            {
                _logger.LogInformation("[CLEANUP] ✅ Successfully reclaimed {ReclaimedCount} out of {TotalCount} expired assignments",
                    totalReclaimed, expired.Count);
            }
            else if (expired.Any())
            {
                _logger.LogError("[CLEANUP] ❌ Failed to reclaim any expired assignments out of {TotalCount} - all batches failed",
                    expired.Count);
            }
        }

        /// <summary>
        /// Clean up expired user readiness - mark users as not ready if heartbeat expired (>2 minutes)
        /// This prevents stale readiness records from blocking assignments
        /// </summary>
        private async Task CleanupExpiredUserReadinessAsync(
            ApplicationDbContext db,
            DateTime now,
            CancellationToken ct)
        {
            // ✅ FIX: Reduced heartbeat window from 5 to 2 minutes for faster detection of logged-out users
            const int maxIdleMinutes = 2; // Reduced from 5 minutes
            var maxIdleTime = now.AddMinutes(-maxIdleMinutes);

            var expiredReadiness = await db.UserReadiness
                .AsTracking()
                .Where(r => r.IsReady && r.LastHeartbeat < maxIdleTime)
                .ToListAsync(ct);

            if (!expiredReadiness.Any())
            {
                return;
            }

            foreach (var record in expiredReadiness)
            {
                record.IsReady = false;
                record.LastChangedAt = now;
                _logger.LogInformation("[CLEANUP] Marking user {Username} ({Role}) as not ready - heartbeat expired ({MinutesSinceHeartbeat:F1} minutes ago)",
                    record.Username, record.Role, (now - record.LastHeartbeat).TotalMinutes);
            }

            await db.SaveChangesAsync(ct);
            _logger.LogInformation("[CLEANUP] Cleaned up {Count} expired user readiness records", expiredReadiness.Count);
        }

        /// <summary>
        /// ✅ PHASE 2: Validate all active assignments to identify state inconsistencies
        /// Checks if assignments are still valid (group exists, status matches role, etc.)
        /// Logs warnings for invalid assignments and optionally expires them
        /// </summary>
        private async Task ValidateAssignmentsAsync(
            ApplicationDbContext db,
            DateTime now,
            CancellationToken ct)
        {
            try
            {
                // ✅ DIAGNOSTIC: Log validation start (Info level so it shows in logs)
                _logger.LogInformation("[VALIDATION] 🔍 Starting assignment validation cycle");

                // ✅ OPTIMIZATION: Use AsNoTracking() and limit to prevent connection pool exhaustion
                // Only load assignments that might be invalid (have expired leases or are old)
                var cutoffDate = now.AddDays(-7); // Only check assignments from last 7 days
                var activeAssignments = await db.AnalysisAssignments
                    .AsNoTracking() // ✅ CRITICAL: Don't track entities to save memory and connections
                    .Where(a => a.State == "Active" && a.CreatedAtUtc >= cutoffDate)
                    .Take(2000) // ✅ LIMIT: Process max 2000 at a time to prevent connection pool exhaustion
                    .ToListAsync(ct);

                _logger.LogInformation("[VALIDATION] Found {Count} active assignments to validate (filtered to last 7 days, max 2000)", activeAssignments.Count);

                if (!activeAssignments.Any())
                {
                    return; // No active assignments to validate
                }

                var invalidAssignments = new List<(AnalysisAssignment Assignment, string Reason)>();
                var groupIds = activeAssignments.Select(a => a.GroupId).Distinct().ToList();

                // ✅ FIX: Batch Contains() to avoid EF Core CTE generation with large lists
                var groupsList = new List<AnalysisGroup>();
                const int groupIdBatchSize = 1000;

                if (groupIds.Count > 0)
                {
                    for (int i = 0; i < groupIds.Count; i += groupIdBatchSize)
                    {
                        var batch = groupIds.Skip(i).Take(groupIdBatchSize).ToList();
                        var batchGroups = await db.AnalysisGroups
                            .Where(g => batch.Contains(g.Id))
                            .AsNoTracking()
                            .ToListAsync(ct);
                        groupsList.AddRange(batchGroups);
                    }
                }

                var groups = groupsList.ToDictionary(g => g.Id);

                // Validate each assignment
                foreach (var assignment in activeAssignments)
                {
                    string? reason = null;
                    bool isValid = true;

                    // Check 1: Group exists
                    if (!groups.TryGetValue(assignment.GroupId, out var group))
                    {
                        isValid = false;
                        reason = $"Group {assignment.GroupId} does not exist";
                    }
                    else
                    {
                        // Check 2: Group status matches role expectations
                        if (assignment.Role == "Analyst")
                        {
                            // Analyst assignments should not exist for completed groups
                            if (group.Status == AnalysisStatuses.AnalystCompleted ||
                                group.Status == AnalysisStatuses.AuditCompleted ||
                                group.Status == AnalysisStatuses.Completed)
                            {
                                isValid = false;
                                reason = $"Group status is '{group.Status}' but assignment is for Analyst role";
                            }
                            // Analyst assignments should typically be in AnalystAssigned status
                            // But we allow Ready status as well (might be transitioning)
                            else if (group.Status != AnalysisStatuses.Ready &&
                                     group.Status != AnalysisStatuses.AnalystAssigned)
                            {
                                // Log as warning but don't mark invalid (might be in transition)
                                _logger.LogDebug("[VALIDATION] Analyst assignment {AssignmentId} for group {GroupId} ({GroupIdentifier}) has unexpected status '{Status}'",
                                    assignment.Id, group.Id, group.GroupIdentifier, group.Status);
                            }
                        }
                        else if (assignment.Role == "Audit")
                        {
                            // Audit assignments should not exist for completed groups
                            if (group.Status == AnalysisStatuses.AuditCompleted ||
                                group.Status == AnalysisStatuses.Completed)
                            {
                                isValid = false;
                                reason = $"Group status is '{group.Status}' but assignment is for Audit role";
                            }
                            // Audit assignments should typically be in AuditAssigned status
                            // But we allow AnalystCompleted status as well (might be transitioning)
                            else if (group.Status != AnalysisStatuses.AnalystCompleted &&
                                     group.Status != AnalysisStatuses.AuditAssigned)
                            {
                                // Log as warning but don't mark invalid (might be in transition)
                                _logger.LogDebug("[VALIDATION] Audit assignment {AssignmentId} for group {GroupId} ({GroupIdentifier}) has unexpected status '{Status}'",
                                    assignment.Id, group.Id, group.GroupIdentifier, group.Status);
                            }
                        }
                        else
                        {
                            // Unknown role
                            isValid = false;
                            reason = $"Unknown role '{assignment.Role}'";
                        }

                        // Check 3: Lease expiration (should have been handled by ReclaimExpiredAssignmentsAsync, but double-check)
                        if (assignment.LeaseUntilUtc.HasValue && assignment.LeaseUntilUtc < now)
                        {
                            // Check if it's an active work session (LastAccessedAtUtc within 30 minutes)
                            var activeWorkThreshold = now.AddMinutes(-30);
                            if (assignment.LastAccessedAtUtc == null || assignment.LastAccessedAtUtc < activeWorkThreshold)
                            {
                                isValid = false;
                                reason = $"Lease expired at {assignment.LeaseUntilUtc:O} (LastAccessed: {assignment.LastAccessedAtUtc?.ToString("O") ?? "never"})";
                            }
                        }
                    }

                    if (!isValid)
                    {
                        invalidAssignments.Add((assignment, reason!));
                        _logger.LogWarning("[VALIDATION] ❌ Invalid assignment {AssignmentId} for user {User} ({Role}): {Reason} | Group: {GroupId} ({GroupIdentifier})",
                            assignment.Id, assignment.AssignedTo, assignment.Role, reason,
                            assignment.GroupId, groups.TryGetValue(assignment.GroupId, out var g) ? g.GroupIdentifier : "N/A");
                    }
                }

                // Log summary
                if (invalidAssignments.Any())
                {
                    _logger.LogWarning("[VALIDATION] ⚠️ Found {InvalidCount} invalid assignments out of {TotalCount} active assignments",
                        invalidAssignments.Count, activeAssignments.Count);

                    // ✅ DIAGNOSTIC: Log sample of invalid reasons to understand the pattern
                    var sampleReasons = invalidAssignments.Take(5).Select(x => x.Reason).Distinct().ToList();
                    _logger.LogWarning("[VALIDATION] Sample invalid reasons: {Reasons}", string.Join("; ", sampleReasons));

                    // ✅ PHASE 2: Expire ALL invalid assignments to prevent them from blocking capacity
                    // If an assignment is invalid for any reason, it should be expired (no point keeping invalid state)
                    // This is safe because these assignments are already invalid and should not be active
                    var assignmentsToExpire = invalidAssignments
                        .Select(x => x.Assignment)
                        .ToList();

                    if (assignmentsToExpire.Any())
                    {
                        // ✅ OPTIMIZATION: For large backlogs (>500), use direct SQL update in chunks for performance
                        // For smaller numbers, use EF Core batching
                        if (assignmentsToExpire.Count > 500)
                        {
                            try
                            {
                                var assignmentIds = assignmentsToExpire.Select(a => a.Id).ToList();
                                var totalExpired = 0;
                                const int sqlChunkSize = 2000; // SQL Server limit is 2100 parameters, use 2000 to be safe

                                // Split into chunks to avoid SQL parameter limit
                                for (int i = 0; i < assignmentIds.Count; i += sqlChunkSize)
                                {
                                    var chunk = assignmentIds.Skip(i).Take(sqlChunkSize).ToList();
                                    var idList = string.Join(", ", chunk);

                                    // Use direct SQL for bulk update (much faster for large numbers)
                                    // Note: Using string interpolation for IDs is safe here because they're GUIDs from our own data
                                    var sql = $@"
                                        UPDATE AnalysisAssignments 
                                        SET State = 'Expired', UpdatedAtUtc = @p0
                                        WHERE Id IN ({idList})";

                                    var rowsAffected = await db.Database.ExecuteSqlRawAsync(sql,
                                        new NpgsqlParameter("@p0", now));

                                    totalExpired += rowsAffected;

                                    _logger.LogInformation("[VALIDATION] ✅ Bulk expired SQL chunk: {ChunkCount} assignments (Total so far: {Total}/{GrandTotal}, Rows affected: {RowsAffected})",
                                        chunk.Count, totalExpired, assignmentsToExpire.Count, rowsAffected);
                                }

                                _logger.LogInformation("[VALIDATION] ✅ Bulk expired {Count} invalid assignments using direct SQL (Total expired: {TotalExpired})",
                                    assignmentsToExpire.Count, totalExpired);
                            }
                            catch (Exception sqlEx)
                            {
                                _logger.LogError(sqlEx, "[VALIDATION] ❌ Failed bulk SQL expiration, falling back to EF Core batching: {Message}",
                                    sqlEx.Message);

                                // Fall back to EF Core batching if SQL fails
                                await ExpireAssignmentsInBatchesAsync(db, assignmentsToExpire, invalidAssignments, groups, now, ct);
                            }
                        }
                        else
                        {
                            // Use EF Core batching for smaller numbers
                            await ExpireAssignmentsInBatchesAsync(db, assignmentsToExpire, invalidAssignments, groups, now, ct);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("[VALIDATION] ⚠️ Found {Count} invalid assignments but assignmentsToExpire list is empty - this should not happen",
                            invalidAssignments.Count);
                    }
                }
                else
                {
                    _logger.LogDebug("[VALIDATION] ✅ All {Count} active assignments are valid", activeAssignments.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[VALIDATION] ❌ Error during assignment validation: {Message} | StackTrace: {StackTrace}",
                    ex.Message, ex.StackTrace);
                // Re-throw to ensure the error is visible in logs
                throw;
            }
        }

        /// <summary>
        /// Orphan-AG predicate (2026-05-04, 2.16.1): an AG is "orphan" when every
        /// container in it has NULL <c>BOEDocumentId</c> on its CCS row AND zero
        /// active <c>ContainerBOERelations</c>. These AGs have no actionable match
        /// data; the lease sweeper should not keep cycling them through analyst
        /// assignment.
        ///
        /// Mirrors the SQL predicate in <c>tools/.../OrphanAgSweep.cs</c> manual
        /// cleanup. Pushed down to SQL via EF Core (single round-trip per call).
        /// </summary>
        private static async Task<bool> IsOrphanAnalysisGroupAsync(
            ApplicationDbContext db,
            Guid groupId,
            CancellationToken ct)
        {
            var hasMatch = await db.AnalysisGroups
                .AsNoTracking()
                .Where(g => g.Id == groupId && (
                    db.AnalysisRecords.Any(r => r.GroupId == g.Id &&
                        db.ContainerCompletenessStatuses.Any(c =>
                            c.ContainerNumber == r.ContainerNumber && c.BOEDocumentId != null))
                    ||
                    db.AnalysisRecords.Any(r => r.GroupId == g.Id &&
                        db.ContainerBOERelations.Any(cbr =>
                            cbr.ContainerNumber == r.ContainerNumber && cbr.IsActive))
                ))
                .AnyAsync(ct);
            return !hasMatch;
        }

        /// <summary>
        /// Helper method to expire assignments in batches using EF Core
        /// </summary>
        private async Task ExpireAssignmentsInBatchesAsync(
            ApplicationDbContext db,
            List<AnalysisAssignment> assignmentsToExpire,
            List<(AnalysisAssignment Assignment, string Reason)> invalidAssignments,
            Dictionary<Guid, AnalysisGroup> groups,
            DateTime now,
            CancellationToken ct)
        {
            // ✅ BATCH PROCESSING: Process in batches to avoid database timeouts with large numbers
            const int batchSize = 100;
            var totalExpired = 0;
            var batches = assignmentsToExpire
                .Select((assignment, index) => new { Assignment = assignment, Index = index })
                .GroupBy(x => x.Index / batchSize)
                .Select(g => g.Select(x => x.Assignment).ToList())
                .ToList();

            _logger.LogInformation("[VALIDATION] Processing {Total} invalid assignments in {BatchCount} batch(es) of {BatchSize}",
                assignmentsToExpire.Count, batches.Count, batchSize);

            foreach (var batch in batches)
            {
                try
                {
                    var batchIds = batch.Select(a => a.Id).ToList();
                    var trackedAssignments = await db.AnalysisAssignments
                        .AsTracking()
                        .Where(a => batchIds.Contains(a.Id))
                        .ToListAsync(ct);

                    foreach (var trackedAssignment in trackedAssignments)
                    {
                        trackedAssignment.State = "Expired";
                        trackedAssignment.UpdatedAtUtc = now;

                        var originalAssignment = batch.FirstOrDefault(a => a.Id == trackedAssignment.Id);
                        if (originalAssignment != null && groups.TryGetValue(originalAssignment.GroupId, out var groupInfo))
                        {
                            var groupToUpdate = await db.AnalysisGroups
                                .AsTracking()
                                .FirstOrDefaultAsync(g => g.Id == groupInfo.Id, ct);
                            if (groupToUpdate != null &&
                                (groupToUpdate.Status == AnalysisStatuses.AnalystAssigned ||
                                 groupToUpdate.Status == AnalysisStatuses.AuditAssigned))
                            {
                                // 2026-05-04 (2.16.1): mirror the orphan-AG guard from
                                // ReclaimExpiredAssignmentsAsync. Same predicate, same goal —
                                // break the lease cycle for orphan AGs so the workbench
                                // doesn't keep showing phantom assignments.
                                if (await IsOrphanAnalysisGroupAsync(db, groupToUpdate.Id, ct))
                                {
                                    groupToUpdate.Status = AnalysisStatuses.Cancelled;
                                    _logger.LogInformation(
                                        "[VALIDATION] Orphan AG {GroupId} ({GroupIdentifier}) → Cancelled (no boedocumentid, no active CBR)",
                                        groupToUpdate.Id, groupToUpdate.GroupIdentifier);
                                }
                                else
                                {
                                    groupToUpdate.Status = originalAssignment.Role == "Audit"
                                        ? AnalysisStatuses.AnalystCompleted
                                        : AnalysisStatuses.Ready;
                                }
                                groupToUpdate.UpdatedAtUtc = now;
                            }
                        }
                    }

                    var savedCount = await db.SaveChangesAsync(ct);
                    totalExpired += trackedAssignments.Count;

                    _logger.LogInformation("[VALIDATION] ✅ Expired batch of {Count} invalid assignments (Total so far: {Total}/{GrandTotal}, Saved: {SavedCount} changes)",
                        batch.Count, totalExpired, assignmentsToExpire.Count, savedCount);
                }
                catch (Exception batchEx)
                {
                    _logger.LogError(batchEx, "[VALIDATION] ❌ Failed to save batch of {Count} expired assignments: {Message} | Will continue with next batch",
                        batch.Count, batchEx.Message);
                    // Continue with next batch - don't let one batch failure stop the cleanup
                }
            }

            // ✅ SUMMARY: Log final summary after all batches
            if (totalExpired > 0)
            {
                _logger.LogInformation("[VALIDATION] ✅ Successfully expired {ExpiredCount} out of {TotalCount} invalid assignments",
                    totalExpired, assignmentsToExpire.Count);

                // ✅ AGGRESSIVE: If we expired a lot, log a summary of reasons
                if (totalExpired > 10)
                {
                    var reasonGroups = invalidAssignments
                        .Take(totalExpired) // Only count successfully expired ones
                        .GroupBy(x => x.Reason)
                        .Select(g => new { Reason = g.Key, Count = g.Count() })
                        .OrderByDescending(x => x.Count)
                        .ToList();

                    _logger.LogWarning("[VALIDATION] Large cleanup completed: Expired {Total} invalid assignments. Breakdown: {Breakdown}",
                        totalExpired,
                        string.Join("; ", reasonGroups.Select(r => $"{r.Reason}: {r.Count}")));
                }
            }
            else
            {
                _logger.LogError("[VALIDATION] ❌ Failed to expire any invalid assignments out of {TotalCount} - all batches failed",
                    assignmentsToExpire.Count);
            }
        }
    }
}


