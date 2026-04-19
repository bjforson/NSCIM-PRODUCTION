using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API Controller for managing user readiness for assignments
    /// Provides REST endpoints for setting readiness status and sending heartbeats
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/image-analysis/user")]
    public class UserReadinessController : ControllerBase
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<UserReadinessController> _logger;

        public UserReadinessController(
            ApplicationDbContext dbContext,
            ILogger<UserReadinessController> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        /// <summary>
        /// Set user's readiness status for a specific role
        /// </summary>
        [HttpPost("ready")]
        public async Task<ActionResult> SetReady([FromBody] SetReadyRequest request)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized(new { error = "User is not authenticated" });
            }

            if (string.IsNullOrWhiteSpace(request.Role))
            {
                return BadRequest(new { error = "Role is required" });
            }

            try
            {
                _logger.LogInformation("🔔 [READINESS] User {Username} setting readiness to {IsReady} for role {Role}",
                    username, request.IsReady, request.Role);

                var now = DateTime.UtcNow;

                // Find or create readiness record (AsTracking required since DbContext defaults to NoTracking)
                var readiness = await _dbContext.UserReadiness
                    .AsTracking()
                    .FirstOrDefaultAsync(r => r.Username == username && r.Role == request.Role);

                if (readiness == null)
                {
                    _logger.LogInformation("🔔 [READINESS] Creating new readiness record for {Username} role {Role}", username, request.Role);
                    // Create new record
                    readiness = new UserReadiness
                    {
                        Username = username,
                        Role = request.Role,
                        IsReady = request.IsReady,
                        LastHeartbeat = now,
                        LastChangedAt = now,
                        ChangedBy = username,
                        SessionId = request.SessionId
                    };
                    _dbContext.UserReadiness.Add(readiness);
                }
                else
                {
                    var wasReady = readiness.IsReady;
                    var wasChanged = readiness.IsReady != request.IsReady;
                    readiness.IsReady = request.IsReady;
                    readiness.LastHeartbeat = now;

                    if (wasChanged)
                    {
                        _logger.LogInformation("🔔 [READINESS] Readiness changed for {Username} role {Role}: {OldStatus} → {NewStatus}",
                            username, request.Role, wasReady, request.IsReady);
                        readiness.LastChangedAt = now;
                        readiness.ChangedBy = username;
                    }
                    else
                    {
                        _logger.LogDebug("🔔 [READINESS] Readiness unchanged for {Username} role {Role}: {Status} (heartbeat updated)",
                            username, request.Role, request.IsReady);
                    }

                    if (!string.IsNullOrEmpty(request.SessionId))
                        readiness.SessionId = request.SessionId;
                }

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("✅ [READINESS] User {Username} readiness saved: IsReady={IsReady}, Role={Role}, LastHeartbeat={Heartbeat}",
                    username, readiness.IsReady, request.Role, readiness.LastHeartbeat);

                return Ok(new
                {
                    Username = username,
                    Role = request.Role,
                    IsReady = readiness.IsReady,
                    LastHeartbeat = readiness.LastHeartbeat
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting readiness for user {Username}", username);
                return StatusCode(500, new { error = "Failed to set readiness status", message = ex.Message });
            }
        }

        /// <summary>
        /// Send heartbeat to indicate user is still active
        /// </summary>
        [HttpPost("heartbeat")]
        public async Task<ActionResult> SendHeartbeat([FromBody] HeartbeatRequest request)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized(new { error = "User is not authenticated" });
            }

            if (string.IsNullOrWhiteSpace(request.Role))
            {
                return BadRequest(new { error = "Role is required" });
            }

            try
            {
                _logger.LogDebug("💓 [HEARTBEAT] Received heartbeat from {Username} for role {Role}", username, request.Role);

                var readiness = await _dbContext.UserReadiness
                    .AsTracking()
                    .FirstOrDefaultAsync(r => r.Username == username && r.Role == request.Role);

                if (readiness != null)
                {
                    var oldHeartbeat = readiness.LastHeartbeat;
                    readiness.LastHeartbeat = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync();
                    _logger.LogDebug("💓 [HEARTBEAT] Updated heartbeat for {Username} role {Role}: {OldHeartbeat} → {NewHeartbeat}",
                        username, request.Role, oldHeartbeat, readiness.LastHeartbeat);
                }
                else
                {
                    _logger.LogWarning("⚠️ [HEARTBEAT] Heartbeat received but no readiness record found for {Username} role {Role} - sync service should create it",
                        username, request.Role);
                }
                // If record doesn't exist, that's OK - sync service or SignalR will create it

                return Ok(new { Success = true, Timestamp = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing heartbeat for user {Username}", username);
                return StatusCode(500, new { error = "Failed to process heartbeat", message = ex.Message });
            }
        }

        /// <summary>
        /// Get current readiness status for the authenticated user
        /// </summary>
        [HttpGet("readiness")]
        public async Task<ActionResult<UserReadinessResponse>> GetReadiness([FromQuery] string? role = null)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized(new { error = "User is not authenticated" });
            }

            try
            {
                var query = _dbContext.UserReadiness.Where(r => r.Username == username);

                if (!string.IsNullOrEmpty(role))
                {
                    query = query.Where(r => r.Role == role);
                }

                var readinessRecords = await query.ToListAsync();

                if (!readinessRecords.Any())
                {
                    return Ok(new UserReadinessResponse
                    {
                        Username = username,
                        Role = role ?? "Unknown",
                        IsReady = false,
                        LastHeartbeat = null,
                        Message = "No readiness record found"
                    });
                }

                // If multiple roles, return the most recent one, or filter by requested role
                var readiness = string.IsNullOrEmpty(role)
                    ? readinessRecords.OrderByDescending(r => r.LastHeartbeat).First()
                    : readinessRecords.FirstOrDefault(r => r.Role == role);

                if (readiness == null)
                {
                    return Ok(new UserReadinessResponse
                    {
                        Username = username,
                        Role = role ?? "Unknown",
                        IsReady = false,
                        LastHeartbeat = null,
                        Message = $"No readiness record found for role {role}"
                    });
                }

                return Ok(new UserReadinessResponse
                {
                    Username = readiness.Username,
                    Role = readiness.Role,
                    IsReady = readiness.IsReady,
                    LastHeartbeat = readiness.LastHeartbeat,
                    LastChangedAt = readiness.LastChangedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting readiness for user {Username}", username);
                return StatusCode(500, new { error = "Failed to get readiness status", message = ex.Message });
            }
        }

        /// <summary>
        /// Get comprehensive diagnostic information about assignment state for current user
        /// </summary>
        [HttpGet("diagnostics")]
        public async Task<ActionResult<AssignmentDiagnosticsResponse>> GetDiagnostics([FromQuery] string? role = null)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrEmpty(username))
            {
                return Unauthorized(new { error = "User is not authenticated" });
            }

            try
            {
                var diagnostics = new AssignmentDiagnosticsResponse
                {
                    Username = username,
                    Timestamp = DateTime.UtcNow
                };

                // Determine role (from query or user's actual role)
                if (string.IsNullOrEmpty(role))
                {
                    // Try to determine from user's role in database
                    var user = await _dbContext.Users
                        .Include(u => u.AssignedRole)
                        .FirstOrDefaultAsync(u => u.Username == username);

                    if (user?.AssignedRole != null)
                    {
                        role = user.AssignedRole.Name;
                        diagnostics.DetectedRole = role;
                    }
                    else
                    {
                        role = "Analyst"; // Default fallback
                        diagnostics.Warnings.Add("Could not determine user role, defaulting to 'Analyst'");
                    }
                }
                diagnostics.Role = role;

                var now = DateTime.UtcNow;
                // ✅ Use 2 minutes to match AssignmentWorker.GetReadyUsersForRoleAsync (stricter than before)
                var maxIdleMinutes = 2;
                var maxIdle = now.AddMinutes(-maxIdleMinutes);

                // 1. Check user readiness status
                var readiness = await _dbContext.UserReadiness
                    .FirstOrDefaultAsync(r => r.Username == username && r.Role == role);

                if (readiness != null)
                {
                    diagnostics.ReadinessStatus = new ReadinessStatusInfo
                    {
                        IsReady = readiness.IsReady,
                        LastHeartbeat = readiness.LastHeartbeat,
                        LastChangedAt = readiness.LastChangedAt,
                        TimeSinceHeartbeat = now - readiness.LastHeartbeat,
                        IsHeartbeatValid = readiness.LastHeartbeat >= maxIdle
                    };

                    if (!readiness.IsReady)
                    {
                        diagnostics.Issues.Add($"User is marked as NOT ready for {role} assignments");
                    }
                    if (readiness.LastHeartbeat < maxIdle)
                    {
                        diagnostics.Issues.Add($"Heartbeat expired (last: {readiness.LastHeartbeat:O}, {diagnostics.ReadinessStatus.TimeSinceHeartbeat.TotalMinutes:F1} minutes ago, max: {maxIdleMinutes} min)");
                    }
                }
                else
                {
                    diagnostics.Issues.Add($"No readiness record found in UserReadiness table for role '{role}'");
                    diagnostics.ReadinessStatus = new ReadinessStatusInfo
                    {
                        IsReady = false,
                        LastHeartbeat = null,
                        IsHeartbeatValid = false
                    };
                }

                // 2. Check assignment settings
                var settings = await _dbContext.AnalysisSettings.FirstOrDefaultAsync() ?? new AnalysisSettings();
                diagnostics.AssignmentSettings = new AssignmentSettingsInfo
                {
                    Enabled = settings.Enabled,
                    AssignmentMode = settings.AssignmentMode ?? "Manual",
                    MaxConcurrentPerUser = settings.MaxConcurrentPerUser,
                    LeaseMinutes = settings.LeaseMinutes,
                    AutoAssignStrategy = settings.AutoAssignStrategy ?? "RoundRobin"
                };

                if (!settings.Enabled)
                {
                    diagnostics.Issues.Add("Assignment service is DISABLED");
                }
                if (settings.AssignmentMode != "Auto")
                {
                    diagnostics.Issues.Add($"AssignmentMode is '{settings.AssignmentMode}' (not 'Auto'), so automatic assignments won't occur");
                }

                // 3. Check ready groups count
                var readyStatus = role == "Audit" ? "AnalystCompleted" : "Ready";
                var readyGroupsCount = await _dbContext.AnalysisGroups
                    .Where(g => g.Status == readyStatus)
                    .CountAsync();

                diagnostics.ReadyGroups = new ReadyGroupsInfo
                {
                    ExpectedStatus = readyStatus,
                    Count = readyGroupsCount
                };

                if (readyGroupsCount == 0 && settings.AssignmentMode == "Auto" && settings.Enabled)
                {
                    diagnostics.Issues.Add($"No groups with status '{readyStatus}' available for assignment");
                }

                // 3b. For Audit: Check WorkflowStage - groups need containers in 'Audit' stage to be eligible
                if (role == "Audit" && readyGroupsCount > 0)
                {
                    var analystCompletedIds = await _dbContext.AnalysisGroups
                        .Where(g => g.Status == "AnalystCompleted")
                        .Select(g => g.GroupIdentifier)
                        .Where(g => g != null)
                        .ToListAsync();

                    var containerStageCounts = await _dbContext.ContainerCompletenessStatuses
                        .Where(c => analystCompletedIds.Contains(c.GroupIdentifier ?? ""))
                        .GroupBy(c => c.WorkflowStage ?? "")
                        .Select(g => new { Stage = g.Key, Count = g.Count() })
                        .ToListAsync();

                    var auditStageCount = containerStageCounts.FirstOrDefault(s => s.Stage == "Audit")?.Count ?? 0;
                    var completedStageCount = containerStageCounts.FirstOrDefault(s => s.Stage == "Completed")?.Count ?? 0;

                    if (auditStageCount == 0)
                    {
                        diagnostics.Issues.Add($"⚠️ WorkflowStage issue: {readyGroupsCount} AnalystCompleted groups exist but NO containers have WorkflowStage='Audit'. See DIAGNOSTIC_AUDIT_ASSIGNMENT_ISSUE.md for the fix script.");
                    }
                }

                // 4. Check user's active assignments
                var activeAssignments = await _dbContext.AnalysisAssignments
                    .Where(a => a.AssignedTo == username
                        && a.Role == role
                        && a.State == "Active"
                        && (a.LeaseUntilUtc == null || a.LeaseUntilUtc > now))
                    .ToListAsync();

                diagnostics.ActiveAssignments = new ActiveAssignmentsInfo
                {
                    Count = activeAssignments.Count,
                    MaxConcurrent = settings.MaxConcurrentPerUser,
                    IsAtCapacity = activeAssignments.Count >= settings.MaxConcurrentPerUser,
                    Assignments = activeAssignments.Select(a => new AssignmentInfo
                    {
                        GroupId = a.GroupId,
                        LeaseUntilUtc = a.LeaseUntilUtc,
                        TimeUntilExpiry = a.LeaseUntilUtc.HasValue ? (a.LeaseUntilUtc.Value - now) : (TimeSpan?)null
                    }).ToList()
                };

                if (activeAssignments.Count >= settings.MaxConcurrentPerUser)
                {
                    diagnostics.Issues.Add($"User is at capacity ({activeAssignments.Count}/{settings.MaxConcurrentPerUser} assignments)");
                }

                // 5. Check how many ready users exist (for context)
                var readyUsersCount = await _dbContext.UserReadiness
                    .Where(r => r.Role == role
                        && r.IsReady
                        && r.LastHeartbeat >= maxIdle)
                    .CountAsync();

                diagnostics.ReadyUsersCount = readyUsersCount;

                // 6. Check if user has correct role in database
                var userHasRole = await _dbContext.Users
                    .Where(u => u.Username == username && u.IsActive)
                    .Join(
                        _dbContext.Roles.Where(r => r.Name == role && r.IsActive),
                        user => user.RoleId,
                        role => role.Id,
                        (user, role) => user.Username)
                    .AnyAsync();

                diagnostics.UserHasRole = userHasRole;
                if (!userHasRole)
                {
                    diagnostics.Issues.Add($"User does not have '{role}' role assigned in database");
                }

                // 7. Summary
                diagnostics.Summary = new DiagnosticsSummary
                {
                    CanReceiveAssignments = settings.Enabled
                        && settings.AssignmentMode == "Auto"
                        && (readiness?.IsReady ?? false)
                        && (readiness?.LastHeartbeat ?? DateTime.MinValue) >= maxIdle
                        && activeAssignments.Count < settings.MaxConcurrentPerUser
                        && readyGroupsCount > 0
                        && userHasRole,
                    PrimaryIssue = diagnostics.Issues.FirstOrDefault() ?? "No issues detected",
                    IssueCount = diagnostics.Issues.Count
                };

                return Ok(diagnostics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting diagnostics for user {Username}", username);
                return StatusCode(500, new { error = "Failed to get diagnostics", message = ex.Message });
            }
        }

        /// <summary>
        /// Get readiness snapshot for all roles (Analyst + Audit) - for admins diagnosing "No ready users"
        /// </summary>
        [HttpGet("readiness-snapshot")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<ReadinessSnapshotResponse>> GetReadinessSnapshot([FromQuery] int maxIdleMinutes = 60)
        {
            try
            {
                var maxIdle = DateTime.UtcNow.AddMinutes(-maxIdleMinutes);
                var now = DateTime.UtcNow;

                var analystUsers = await _dbContext.UserReadiness
                    .Where(r => r.Role == "Analyst"
                        && r.IsReady
                        && r.LastHeartbeat >= maxIdle)
                    .OrderByDescending(r => r.LastHeartbeat)
                    .Select(r => new ReadyUserInfo
                    {
                        Username = r.Username,
                        Role = r.Role,
                        LastHeartbeat = r.LastHeartbeat,
                        LastChangedAt = r.LastChangedAt,
                        TimeSinceHeartbeat = now - r.LastHeartbeat
                    })
                    .ToListAsync();

                var auditUsers = await _dbContext.UserReadiness
                    .Where(r => r.Role == "Audit"
                        && r.IsReady
                        && r.LastHeartbeat >= maxIdle)
                    .OrderByDescending(r => r.LastHeartbeat)
                    .Select(r => new ReadyUserInfo
                    {
                        Username = r.Username,
                        Role = r.Role,
                        LastHeartbeat = r.LastHeartbeat,
                        LastChangedAt = r.LastChangedAt,
                        TimeSinceHeartbeat = now - r.LastHeartbeat
                    })
                    .ToListAsync();

                return Ok(new ReadinessSnapshotResponse
                {
                    Timestamp = now,
                    MaxIdleMinutes = maxIdleMinutes,
                    Analyst = new RoleReadinessInfo { Count = analystUsers.Count, Users = analystUsers },
                    Audit = new RoleReadinessInfo { Count = auditUsers.Count, Users = auditUsers }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting readiness snapshot");
                return StatusCode(500, new { error = "Failed to get readiness snapshot", message = ex.Message });
            }
        }

        /// <summary>
        /// Get all ready users for a specific role (for dashboard/admin)
        /// </summary>
        [HttpGet("ready-users")]
        [Authorize(Policy = "AdminOnly")] // Only admins can see all ready users
        public async Task<ActionResult<ReadyUsersResponse>> GetReadyUsers([FromQuery] string role, [FromQuery] int maxIdleMinutes = 5)
        {
            if (string.IsNullOrWhiteSpace(role))
            {
                return BadRequest(new { error = "Role is required" });
            }

            try
            {
                var maxIdle = DateTime.UtcNow.AddMinutes(-maxIdleMinutes);

                var readyUsers = await _dbContext.UserReadiness
                    .Where(r => r.Role == role
                        && r.IsReady
                        && r.LastHeartbeat >= maxIdle)
                    .OrderByDescending(r => r.LastHeartbeat)
                    .Select(r => new ReadyUserInfo
                    {
                        Username = r.Username,
                        Role = r.Role,
                        LastHeartbeat = r.LastHeartbeat,
                        LastChangedAt = r.LastChangedAt,
                        TimeSinceHeartbeat = DateTime.UtcNow - r.LastHeartbeat
                    })
                    .ToListAsync();

                return Ok(new ReadyUsersResponse
                {
                    Role = role,
                    Count = readyUsers.Count,
                    MaxIdleMinutes = maxIdleMinutes,
                    Users = readyUsers
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ready users for role {Role}", role);
                return StatusCode(500, new { error = "Failed to get ready users", message = ex.Message });
            }
        }
    }

    // Request/Response models
    public class SetReadyRequest
    {
        public string Role { get; set; } = ""; // "Analyst" or "Audit"
        public bool IsReady { get; set; }
        public string? SessionId { get; set; }
    }

    public class HeartbeatRequest
    {
        public string Role { get; set; } = ""; // "Analyst" or "Audit"
    }

    public class UserReadinessResponse
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public bool IsReady { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public DateTime? LastChangedAt { get; set; }
        public string? Message { get; set; }
    }

    public class ReadyUsersResponse
    {
        public string Role { get; set; } = "";
        public int Count { get; set; }
        public int MaxIdleMinutes { get; set; }
        public List<ReadyUserInfo> Users { get; set; } = new();
    }

    public class ReadyUserInfo
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public DateTime LastHeartbeat { get; set; }
        public DateTime LastChangedAt { get; set; }
        public TimeSpan TimeSinceHeartbeat { get; set; }
    }

    public class ReadinessSnapshotResponse
    {
        public DateTime Timestamp { get; set; }
        public int MaxIdleMinutes { get; set; }
        public RoleReadinessInfo Analyst { get; set; } = new();
        public RoleReadinessInfo Audit { get; set; } = new();
    }

    public class RoleReadinessInfo
    {
        public int Count { get; set; }
        public List<ReadyUserInfo> Users { get; set; } = new();
    }

    public class AssignmentDiagnosticsResponse
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public string? DetectedRole { get; set; }
        public DateTime Timestamp { get; set; }
        public ReadinessStatusInfo? ReadinessStatus { get; set; }
        public AssignmentSettingsInfo AssignmentSettings { get; set; } = new();
        public ReadyGroupsInfo ReadyGroups { get; set; } = new();
        public ActiveAssignmentsInfo ActiveAssignments { get; set; } = new();
        public int ReadyUsersCount { get; set; }
        public bool UserHasRole { get; set; }
        public DiagnosticsSummary Summary { get; set; } = new();
        public List<string> Issues { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
    }

    public class ReadinessStatusInfo
    {
        public bool IsReady { get; set; }
        public DateTime? LastHeartbeat { get; set; }
        public DateTime? LastChangedAt { get; set; }
        public TimeSpan TimeSinceHeartbeat { get; set; }
        public bool IsHeartbeatValid { get; set; }
    }

    public class AssignmentSettingsInfo
    {
        public bool Enabled { get; set; }
        public string AssignmentMode { get; set; } = "";
        public int MaxConcurrentPerUser { get; set; }
        public int LeaseMinutes { get; set; }
        public string AutoAssignStrategy { get; set; } = "";
    }

    public class ReadyGroupsInfo
    {
        public string ExpectedStatus { get; set; } = "";
        public int Count { get; set; }
    }

    public class ActiveAssignmentsInfo
    {
        public int Count { get; set; }
        public int MaxConcurrent { get; set; }
        public bool IsAtCapacity { get; set; }
        public List<AssignmentInfo> Assignments { get; set; } = new();
    }

    public class AssignmentInfo
    {
        public Guid GroupId { get; set; }
        public DateTime? LeaseUntilUtc { get; set; }
        public TimeSpan? TimeUntilExpiry { get; set; }
    }

    public class DiagnosticsSummary
    {
        public bool CanReceiveAssignments { get; set; }
        public string PrimaryIssue { get; set; } = "";
        public int IssueCount { get; set; }
    }
}

