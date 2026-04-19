using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Audit
{
    public class AuditService : IAuditService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuditService> _logger;

        public AuditService(ApplicationDbContext context, ILogger<AuditService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task LogPermissionCheckAsync(int userId, string permissionName, bool result, string? ipAddress = null)
        {
            try
            {
                var auditLog = new PermissionAuditLog
                {
                    Action = "CHECK",
                    EntityType = "User",
                    EntityId = userId,
                    UserId = userId,
                    Result = result,
                    IPAddress = ipAddress,
                    Details = $"Permission check: {permissionName}",
                    Timestamp = DateTime.UtcNow
                };

                await _context.PermissionAuditLogs.AddAsync(auditLog);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUDIT] Error logging permission check: {Message}", ex.Message);
            }
        }

        public async Task LogPermissionGrantAsync(int targetUserId, string permissionName, int grantedByUserId, string? reason = null)
        {
            try
            {
                var auditLog = new PermissionAuditLog
                {
                    Action = "GRANT",
                    EntityType = "User",
                    EntityId = targetUserId,
                    UserId = grantedByUserId,
                    Details = $"Granted permission '{permissionName}' to user {targetUserId}. Reason: {reason ?? "N/A"}",
                    Timestamp = DateTime.UtcNow
                };

                await _context.PermissionAuditLogs.AddAsync(auditLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("[AUDIT] Permission '{Permission}' granted to user {TargetUser} by user {GrantedBy}",
                    permissionName, targetUserId, grantedByUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUDIT] Error logging permission grant: {Message}", ex.Message);
            }
        }

        public async Task LogPermissionRevokeAsync(int targetUserId, string permissionName, int revokedByUserId, string? reason = null)
        {
            try
            {
                var auditLog = new PermissionAuditLog
                {
                    Action = "REVOKE",
                    EntityType = "User",
                    EntityId = targetUserId,
                    UserId = revokedByUserId,
                    Details = $"Revoked permission '{permissionName}' from user {targetUserId}. Reason: {reason ?? "N/A"}",
                    Timestamp = DateTime.UtcNow
                };

                await _context.PermissionAuditLogs.AddAsync(auditLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("[AUDIT] Permission '{Permission}' revoked from user {TargetUser} by user {RevokedBy}",
                    permissionName, targetUserId, revokedByUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUDIT] Error logging permission revoke: {Message}", ex.Message);
            }
        }

        public async Task LogRoleAssignmentAsync(int userId, int roleId, int assignedByUserId)
        {
            try
            {
                var auditLog = new PermissionAuditLog
                {
                    Action = "ROLE_ASSIGN",
                    EntityType = "User",
                    EntityId = userId,
                    RoleId = roleId,
                    UserId = assignedByUserId,
                    Details = $"Role {roleId} assigned to user {userId}",
                    Timestamp = DateTime.UtcNow
                };

                await _context.PermissionAuditLogs.AddAsync(auditLog);
                await _context.SaveChangesAsync();

                _logger.LogInformation("[AUDIT] Role {RoleId} assigned to user {UserId} by user {AssignedBy}",
                    roleId, userId, assignedByUserId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUDIT] Error logging role assignment: {Message}", ex.Message);
            }
        }

        public async Task LogRoleCreatedAsync(int roleId, int createdByUserId)
        {
            try
            {
                var auditLog = new PermissionAuditLog
                {
                    Action = "ROLE_CREATE",
                    EntityType = "Role",
                    EntityId = roleId,
                    RoleId = roleId,
                    UserId = createdByUserId,
                    Details = $"Role {roleId} created",
                    Timestamp = DateTime.UtcNow
                };

                await _context.PermissionAuditLogs.AddAsync(auditLog);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUDIT] Error logging role creation: {Message}", ex.Message);
            }
        }

        public async Task LogRoleUpdatedAsync(int roleId, int updatedByUserId)
        {
            try
            {
                var auditLog = new PermissionAuditLog
                {
                    Action = "ROLE_UPDATE",
                    EntityType = "Role",
                    EntityId = roleId,
                    RoleId = roleId,
                    UserId = updatedByUserId,
                    Details = $"Role {roleId} updated",
                    Timestamp = DateTime.UtcNow
                };

                await _context.PermissionAuditLogs.AddAsync(auditLog);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUDIT] Error logging role update: {Message}", ex.Message);
            }
        }

        public async Task LogRoleDeletedAsync(int roleId, int deletedByUserId)
        {
            try
            {
                var auditLog = new PermissionAuditLog
                {
                    Action = "ROLE_DELETE",
                    EntityType = "Role",
                    EntityId = roleId,
                    RoleId = roleId,
                    UserId = deletedByUserId,
                    Details = $"Role {roleId} deleted",
                    Timestamp = DateTime.UtcNow
                };

                await _context.PermissionAuditLogs.AddAsync(auditLog);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUDIT] Error logging role deletion: {Message}", ex.Message);
            }
        }
    }
}

