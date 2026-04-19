using System;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using CorePermissions = NickScanCentralImagingPortal.Core.Constants.Permissions;

namespace NickScanCentralImagingPortal.Services.Permissions
{
    public class PermissionService : IPermissionService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<PermissionService> _logger;
        private const string CacheKeyPrefix = "UserPermissions_";
        private readonly TimeSpan CacheExpiration;

        public PermissionService(
            ApplicationDbContext context,
            IMemoryCache cache,
            IConfiguration configuration,
            ILogger<PermissionService> logger)
        {
            _context = context;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
            CacheExpiration = TimeSpan.FromHours(_configuration.GetValue<int>("Caching:PermissionsExpirationHours", 8));
        }

        public async Task<bool> HasPermissionAsync(int userId, string permissionName)
        {
            try
            {
                // Get all user permissions (cached)
                var userPermissions = await GetUserPermissionsAsync(userId);

                // Check if user has this permission (case-insensitive)
                var hasPermission = userPermissions.Any(p =>
                    string.Equals(p, permissionName, StringComparison.OrdinalIgnoreCase));

                _logger.LogDebug("[PERMISSION-CHECK] User {UserId} permission '{Permission}': {Result}",
                    userId, permissionName, hasPermission);

                return hasPermission;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSION-CHECK] Error checking permission for user {UserId}: {Message}",
                    userId, ex.Message);
                return false;
            }
        }

        public async Task<bool> HasPermissionAsync(string username, string permissionName)
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Username == username && u.IsActive);
            if (user == null)
            {
                _logger.LogWarning("[PERMISSION-CHECK] User not found: {Username}", username);
                return false;
            }

            return await HasPermissionAsync(user.Id, permissionName);
        }

        public bool HasPermission(UserRole userRole, UserRole requiredRole)
        {
            // Simple role hierarchy check (backward compatibility)
            return (int)userRole >= (int)requiredRole;
        }

        public bool HasPermission(UserRole userRole, string permissionName)
        {
            // For backward compatibility - use default role permissions
            // This is a simplified version; for full permission checks, use HasPermissionAsync

            // SuperAdmin receives all permissions by definition
            if (userRole == UserRole.SuperAdmin)
            {
                return CorePermissions.GetAllPermissions()
                    .Any(p => string.Equals(p.Name, permissionName, StringComparison.OrdinalIgnoreCase));
            }

            // Basic permission mapping for common permissions
            var basicPermissions = new Dictionary<UserRole, List<string>>
            {
                [UserRole.Viewer] = new() { "dashboard.view", "containers.view", "icums.view", "images.view" },
                [UserRole.Operator] = new() { "containers.edit", "icums.download", "images.annotate" },
                [UserRole.ScannerOperator] = new() { "scanners.configure", "images.edit" },
                [UserRole.Supervisor] = new() { "containers.approve", "containers.reject", "system.logs.view" },
                [UserRole.Manager] = new() { "users.view", "users.create", "audit.view" },
                [UserRole.Admin] = new() { "system.services.control", "system.database.view", "users.delete" }
            };

            // Check if this role or any lower role has the permission
            for (int i = (int)userRole; i >= 0; i--)
            {
                var role = (UserRole)i;
                if (basicPermissions.ContainsKey(role) && basicPermissions[role].Contains(permissionName))
                {
                    return true;
                }
            }

            return false;
        }

        public async Task<List<string>> GetUserPermissionsAsync(int userId)
        {
            // Check cache first
            var cacheKey = $"{CacheKeyPrefix}{userId}";
            if (_cache.TryGetValue<List<string>>(cacheKey, out var cachedPermissions) && cachedPermissions != null)
            {
                return cachedPermissions;
            }

            try
            {
                var permissions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Get user with role
                var user = await _context.Users
                    .Include(u => u.AssignedRole)
                    .ThenInclude(r => r!.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
                    .Include(u => u.UserPermissions)
                    .ThenInclude(up => up.Permission)
                    .FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);

                if (user == null)
                {
                    _logger.LogWarning("[PERMISSION-SERVICE] User not found or inactive: {UserId}", userId);
                    return new List<string>();
                }

                // Step 1: Get permissions from assigned role
                if (user.AssignedRole != null && user.AssignedRole.IsActive)
                {
                    var rolePermissions = user.AssignedRole.RolePermissions
                        .Where(rp => rp.Permission.IsActive)
                        .Select(rp => rp.Permission.Name);

                    permissions.UnionWith(rolePermissions);
                }

                // Step 2: Apply user-specific permission overrides
                var activeUserPermissions = user.UserPermissions
                    .Where(up => up.Permission.IsActive)
                    .Where(up => !up.ExpiresAt.HasValue || up.ExpiresAt.Value > DateTime.UtcNow);

                foreach (var userPermission in activeUserPermissions)
                {
                    if (userPermission.IsGranted)
                    {
                        // Grant: Add permission
                        permissions.Add(userPermission.Permission.Name);
                    }
                    else
                    {
                        // Revoke: Remove permission (even if role has it)
                        permissions.Remove(userPermission.Permission.Name);
                    }
                }

                var permissionList = permissions.ToList();

                // Cache the result with size for memory limit compliance
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheExpiration,
                    Size = 1 // Each permission list counts as 1 unit
                };
                _cache.Set(cacheKey, permissionList, cacheOptions);

                _logger.LogDebug("[PERMISSION-SERVICE] User {UserId} has {Count} permissions", userId, permissionList.Count);

                return permissionList;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSION-SERVICE] Error loading permissions for user {UserId}: {Message}",
                    userId, ex.Message);
                return new List<string>();
            }
        }

        public async Task<List<string>> GetRolePermissionsAsync(int roleId)
        {
            try
            {
                var role = await _context.Roles
                    .Include(r => r.RolePermissions)
                    .ThenInclude(rp => rp.Permission)
                    .FirstOrDefaultAsync(r => r.Id == roleId && r.IsActive);

                if (role == null)
                {
                    _logger.LogWarning("[PERMISSION-SERVICE] Role not found or inactive: {RoleId}", roleId);
                    return new List<string>();
                }

                return role.RolePermissions
                    .Where(rp => rp.Permission.IsActive)
                    .Select(rp => rp.Permission.Name)
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSION-SERVICE] Error loading permissions for role {RoleId}: {Message}",
                    roleId, ex.Message);
                return new List<string>();
            }
        }

        public async Task GrantPermissionToUserAsync(int userId, string permissionName, string grantedBy,
            DateTime? expiresAt = null, string? reason = null)
        {
            // ✅ BOLSTER: Use database transaction to ensure atomic operation
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var permission = await _context.Permissions.FirstOrDefaultAsync(p => p.Name == permissionName);
                if (permission == null)
                {
                    throw new ArgumentException($"Permission not found: {permissionName}");
                }

                var existingOverride = await _context.UserPermissions
                    .FirstOrDefaultAsync(up => up.UserId == userId && up.PermissionId == permission.Id);

                if (existingOverride != null)
                {
                    // Update existing override
                    existingOverride.IsGranted = true;
                    existingOverride.GrantedAt = DateTime.UtcNow;
                    existingOverride.GrantedBy = grantedBy;
                    existingOverride.ExpiresAt = expiresAt;
                    existingOverride.Reason = reason;
                }
                else
                {
                    // Create new override
                    var userPermission = new UserPermission
                    {
                        UserId = userId,
                        PermissionId = permission.Id,
                        IsGranted = true,
                        GrantedAt = DateTime.UtcNow,
                        GrantedBy = grantedBy,
                        ExpiresAt = expiresAt,
                        Reason = reason
                    };
                    await _context.UserPermissions.AddAsync(userPermission);
                }

                // ✅ BOLSTER: Save changes within transaction
                await _context.SaveChangesAsync();

                // ✅ BOLSTER: Commit transaction before clearing cache
                await transaction.CommitAsync();

                // ✅ BOLSTER: Clear cache AFTER successful commit
                _cache.Remove($"{CacheKeyPrefix}{userId}");

                _logger.LogInformation("[PERMISSION-SERVICE] ✅ Granted permission '{Permission}' to user {UserId} by {GrantedBy}",
                    permissionName, userId, grantedBy);
            }
            catch (Exception ex)
            {
                // ✅ BOLSTER: Rollback transaction on error to prevent partial updates
                await transaction.RollbackAsync();
                _logger.LogError(ex, "[PERMISSION-SERVICE] ❌ Error granting permission '{Permission}' to user {UserId}: {Message}. Transaction rolled back.",
                    permissionName, userId, ex.Message);
                throw;
            }
            });
        }

        public async Task RevokePermissionFromUserAsync(int userId, string permissionName, string revokedBy,
            string? reason = null)
        {
            // ✅ BOLSTER: Use database transaction to ensure atomic operation
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var permission = await _context.Permissions.FirstOrDefaultAsync(p => p.Name == permissionName);
                if (permission == null)
                {
                    throw new ArgumentException($"Permission not found: {permissionName}");
                }

                var existingOverride = await _context.UserPermissions
                    .FirstOrDefaultAsync(up => up.UserId == userId && up.PermissionId == permission.Id);

                if (existingOverride != null)
                {
                    // Update existing override to revoke
                    existingOverride.IsGranted = false;
                    existingOverride.GrantedAt = DateTime.UtcNow;
                    existingOverride.GrantedBy = revokedBy;
                    existingOverride.ExpiresAt = null; // Revocations don't expire
                    existingOverride.Reason = reason;
                }
                else
                {
                    // Create new revocation
                    var userPermission = new UserPermission
                    {
                        UserId = userId,
                        PermissionId = permission.Id,
                        IsGranted = false,
                        GrantedAt = DateTime.UtcNow,
                        GrantedBy = revokedBy,
                        Reason = reason
                    };
                    await _context.UserPermissions.AddAsync(userPermission);
                }

                // ✅ BOLSTER: Save changes within transaction
                await _context.SaveChangesAsync();

                // ✅ BOLSTER: Commit transaction before clearing cache
                await transaction.CommitAsync();

                // ✅ BOLSTER: Clear cache AFTER successful commit
                _cache.Remove($"{CacheKeyPrefix}{userId}");

                _logger.LogInformation("[PERMISSION-SERVICE] ✅ Revoked permission '{Permission}' from user {UserId} by {RevokedBy}",
                    permissionName, userId, revokedBy);
            }
            catch (Exception ex)
            {
                // ✅ BOLSTER: Rollback transaction on error to prevent partial updates
                await transaction.RollbackAsync();
                _logger.LogError(ex, "[PERMISSION-SERVICE] ❌ Error revoking permission '{Permission}' from user {UserId}: {Message}. Transaction rolled back.",
                    permissionName, userId, ex.Message);
                throw;
            }
            });
        }

        public async Task RemoveUserPermissionOverrideAsync(int userId, string permissionName)
        {
            try
            {
                var permission = await _context.Permissions.FirstOrDefaultAsync(p => p.Name == permissionName);
                if (permission == null)
                {
                    throw new ArgumentException($"Permission not found: {permissionName}");
                }

                var userPermission = await _context.UserPermissions
                    .FirstOrDefaultAsync(up => up.UserId == userId && up.PermissionId == permission.Id);

                if (userPermission != null)
                {
                    _context.UserPermissions.Remove(userPermission);
                    await _context.SaveChangesAsync();

                    // Clear cache
                    _cache.Remove($"{CacheKeyPrefix}{userId}");

                    _logger.LogInformation("[PERMISSION-SERVICE] Removed permission override for '{Permission}' from user {UserId}",
                        permissionName, userId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSION-SERVICE] Error removing permission override: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<bool> HasAllPermissionsAsync(int userId, params string[] permissionNames)
        {
            var userPermissions = await GetUserPermissionsAsync(userId);
            return permissionNames.All(p => userPermissions.Contains(p));
        }

        public async Task<bool> HasAnyPermissionAsync(int userId, params string[] permissionNames)
        {
            var userPermissions = await GetUserPermissionsAsync(userId);
            return permissionNames.Any(p => userPermissions.Contains(p));
        }

        public async Task<List<int>> GetExpiredUserPermissionsAsync()
        {
            return await _context.UserPermissions
                .Where(up => up.ExpiresAt.HasValue && up.ExpiresAt.Value <= DateTime.UtcNow)
                .Select(up => up.Id)
                .ToListAsync();
        }

        public async Task CleanupExpiredPermissionsAsync()
        {
            try
            {
                var expiredPermissions = await _context.UserPermissions
                    .Where(up => up.ExpiresAt.HasValue && up.ExpiresAt.Value <= DateTime.UtcNow)
                    .ToListAsync();

                if (expiredPermissions.Any())
                {
                    _context.UserPermissions.RemoveRange(expiredPermissions);
                    await _context.SaveChangesAsync();

                    // Clear caches for affected users
                    foreach (var up in expiredPermissions.Select(p => p.UserId).Distinct())
                    {
                        _cache.Remove($"{CacheKeyPrefix}{up}");
                    }

                    _logger.LogInformation("[PERMISSION-SERVICE] Cleaned up {Count} expired permissions",
                        expiredPermissions.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSION-SERVICE] Error cleaning up expired permissions: {Message}",
                    ex.Message);
            }
        }
    }
}

