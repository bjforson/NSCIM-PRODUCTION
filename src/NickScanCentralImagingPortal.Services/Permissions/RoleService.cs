using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Permissions
{
    public class RoleService : IRoleService
    {
        private readonly ApplicationDbContext _context;
        private readonly IMemoryCache _cache;
        private readonly IConfiguration _configuration;
        private readonly ILogger<RoleService> _logger;
        private const string CacheKeyPrefix = "Role_";
        private readonly TimeSpan CacheExpiration;

        public RoleService(
            ApplicationDbContext context,
            IMemoryCache cache,
            IConfiguration configuration,
            ILogger<RoleService> logger)
        {
            _context = context;
            _cache = cache;
            _configuration = configuration;
            _logger = logger;
            CacheExpiration = TimeSpan.FromMinutes(_configuration.GetValue<int>("Caching:RolesExpirationMinutes", 15));
        }

        public async Task<List<Role>> GetAllRolesAsync()
        {
            return await _context.Roles
                .Where(r => r.IsActive)
                .OrderBy(r => r.DisplayName)
                .ToListAsync();
        }

        public async Task<Role?> GetRoleByIdAsync(int roleId)
        {
            var cacheKey = $"{CacheKeyPrefix}{roleId}";
            if (_cache.TryGetValue<Role>(cacheKey, out var cachedRole) && cachedRole != null)
            {
                return cachedRole;
            }

            var role = await _context.Roles
                .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(r => r.Id == roleId);

            if (role != null)
            {
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheExpiration,
                    Size = 1 // Each role counts as 1 unit for memory limit compliance
                };
                _cache.Set(cacheKey, role, cacheOptions);
            }

            return role;
        }

        public async Task<Role?> GetRoleByNameAsync(string roleName)
        {
            return await _context.Roles
                .Include(r => r.RolePermissions)
                .ThenInclude(rp => rp.Permission)
                .FirstOrDefaultAsync(r => r.Name == roleName && r.IsActive);
        }

        public async Task<Role> CreateRoleAsync(string name, string displayName, string description,
            string createdBy, UserRole? baseRole = null)
        {
            try
            {
                var actor = string.IsNullOrWhiteSpace(createdBy) ? "System" : createdBy;

                // Check if name is available
                if (!await IsRoleNameAvailableAsync(name))
                {
                    throw new InvalidOperationException($"Role name '{name}' is already in use");
                }

                var role = new Role
                {
                    Name = name,
                    DisplayName = displayName,
                    Description = description,
                    BaseRole = baseRole,
                    IsSystemRole = false,
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow,
                    CreatedBy = actor
                };

                await _context.Roles.AddAsync(role);
                await _context.SaveChangesAsync();

                if (baseRole.HasValue)
                {
                    _ = await CopyPermissionsFromBaseRoleAsync(role.Id, baseRole.Value, actor);
                }

                _logger.LogInformation("[ROLE-SERVICE] Created new role: {RoleName} by {CreatedBy}",
                    name, actor);

                return role;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLE-SERVICE] Error creating role: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<Role> UpdateRoleAsync(int roleId, string displayName, string description,
            string updatedBy)
        {
            try
            {
                var role = await _context.Roles.AsTracking().FirstOrDefaultAsync(r => r.Id == roleId);
                if (role == null)
                {
                    throw new ArgumentException($"Role not found: {roleId}");
                }

                if (role.IsSystemRole)
                {
                    throw new InvalidOperationException("Cannot modify system roles");
                }

                role.DisplayName = displayName;
                role.Description = description;
                role.UpdatedAt = DateTime.UtcNow;
                role.UpdatedBy = updatedBy;

                await _context.SaveChangesAsync();

                // Clear cache
                _cache.Remove($"{CacheKeyPrefix}{roleId}");

                _logger.LogInformation("[ROLE-SERVICE] Updated role {RoleId} by {UpdatedBy}",
                    roleId, updatedBy);

                return role;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLE-SERVICE] Error updating role: {Message}", ex.Message);
                throw;
            }
        }

        public async Task DeleteRoleAsync(int roleId, string deletedBy)
        {
            try
            {
                var role = await _context.Roles.AsTracking().FirstOrDefaultAsync(r => r.Id == roleId);
                if (role == null)
                {
                    throw new ArgumentException($"Role not found: {roleId}");
                }

                if (role.IsSystemRole)
                {
                    throw new InvalidOperationException("Cannot delete system roles");
                }

                // Check if any users are assigned to this role
                var usersCount = await _context.Users.CountAsync(u => u.RoleId == roleId);
                if (usersCount > 0)
                {
                    throw new InvalidOperationException(
                        $"Cannot delete role: {usersCount} user(s) are currently assigned to this role");
                }

                // Soft delete
                role.IsActive = false;
                role.UpdatedAt = DateTime.UtcNow;
                role.UpdatedBy = deletedBy;

                await _context.SaveChangesAsync();

                // Clear cache
                _cache.Remove($"{CacheKeyPrefix}{roleId}");

                _logger.LogInformation("[ROLE-SERVICE] Deleted role {RoleId} by {DeletedBy}",
                    roleId, deletedBy);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLE-SERVICE] Error deleting role: {Message}", ex.Message);
                throw;
            }
        }

        public async Task AssignPermissionToRoleAsync(int roleId, string permissionName, string grantedBy)
        {
            // ✅ BOLSTER: Use database transaction to ensure atomic operation
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var role = await _context.Roles.AsTracking().FirstOrDefaultAsync(r => r.Id == roleId);
                if (role == null)
                {
                    throw new ArgumentException($"Role not found: {roleId}");
                }

                var permission = await _context.Permissions.FirstOrDefaultAsync(p => p.Name == permissionName);
                if (permission == null)
                {
                    throw new ArgumentException($"Permission not found: {permissionName}");
                }

                // Check if already assigned
                var existing = await _context.RolePermissions
                    .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permission.Id);

                if (existing == null)
                {
                    var rolePermission = new RolePermission
                    {
                        RoleId = roleId,
                        PermissionId = permission.Id,
                        GrantedAt = DateTime.UtcNow,
                        GrantedBy = grantedBy
                    };

                    await _context.RolePermissions.AddAsync(rolePermission);

                    // ✅ BOLSTER: Save changes within transaction
                    await _context.SaveChangesAsync();

                    // ✅ BOLSTER: Commit transaction before clearing cache
                    await transaction.CommitAsync();

                    // ✅ BOLSTER: Clear cache AFTER successful commit
                    _cache.Remove($"{CacheKeyPrefix}{roleId}");

                    // Clear all user permission caches for this role
                    await ClearUserCachesForRoleAsync(roleId);

                    _logger.LogInformation("[ROLE-SERVICE] ✅ Assigned permission '{Permission}' to role {RoleId} by {GrantedBy}",
                        permissionName, roleId, grantedBy);
                }
                else
                {
                    // Already assigned, just commit the transaction (no changes needed)
                    await transaction.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                // ✅ BOLSTER: Rollback transaction on error to prevent partial updates
                await transaction.RollbackAsync();
                _logger.LogError(ex, "[ROLE-SERVICE] ❌ Error assigning permission '{Permission}' to role {RoleId}: {Message}. Transaction rolled back.",
                    permissionName, roleId, ex.Message);
                throw;
            }
            });
        }

        public async Task RemovePermissionFromRoleAsync(int roleId, string permissionName)
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

                var rolePermission = await _context.RolePermissions
                    .FirstOrDefaultAsync(rp => rp.RoleId == roleId && rp.PermissionId == permission.Id);

                if (rolePermission != null)
                {
                    _context.RolePermissions.Remove(rolePermission);

                    // ✅ BOLSTER: Save changes within transaction
                    await _context.SaveChangesAsync();

                    // ✅ BOLSTER: Commit transaction before clearing cache
                    await transaction.CommitAsync();

                    // ✅ BOLSTER: Clear cache AFTER successful commit
                    _cache.Remove($"{CacheKeyPrefix}{roleId}");

                    // Clear all user permission caches for this role
                    await ClearUserCachesForRoleAsync(roleId);

                    _logger.LogInformation("[ROLE-SERVICE] ✅ Removed permission '{Permission}' from role {RoleId}",
                        permissionName, roleId);
                }
                else
                {
                    // Permission not assigned, just commit the transaction (no changes needed)
                    await transaction.CommitAsync();
                }
            }
            catch (Exception ex)
            {
                // ✅ BOLSTER: Rollback transaction on error to prevent partial updates
                await transaction.RollbackAsync();
                _logger.LogError(ex, "[ROLE-SERVICE] ❌ Error removing permission '{Permission}' from role {RoleId}: {Message}. Transaction rolled back.",
                    permissionName, roleId, ex.Message);
                throw;
            }
            });
        }

        public async Task AssignPermissionsToRoleAsync(int roleId, List<string> permissionNames, string grantedBy)
        {
            foreach (var permissionName in permissionNames)
            {
                await AssignPermissionToRoleAsync(roleId, permissionName, grantedBy);
            }
        }

        public async Task ReplaceRolePermissionsAsync(int roleId, List<string> permissionNames, string updatedBy)
        {
            // ✅ BOLSTER: Use database transaction to ensure atomic operation
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var role = await _context.Roles.AsTracking().FirstOrDefaultAsync(r => r.Id == roleId);
                if (role == null)
                {
                    throw new ArgumentException($"Role not found: {roleId}");
                }

                // ✅ BOLSTER: Load existing permissions within transaction
                var existingPermissions = await _context.RolePermissions
                    .Where(rp => rp.RoleId == roleId)
                    .ToListAsync();

                // ✅ BOLSTER: Remove existing permissions
                if (existingPermissions.Any())
                {
                    _context.RolePermissions.RemoveRange(existingPermissions);
                }

                // ✅ BOLSTER: Add new permissions (validate all exist before adding)
                var permissionsToAdd = new List<RolePermission>();
                foreach (var permissionName in permissionNames)
                {
                    var permission = await _context.Permissions.FirstOrDefaultAsync(p => p.Name == permissionName);
                    if (permission == null)
                    {
                        _logger.LogWarning("[ROLE-SERVICE] Permission '{Permission}' not found, skipping", permissionName);
                        continue;
                    }

                    permissionsToAdd.Add(new RolePermission
                    {
                        RoleId = roleId,
                        PermissionId = permission.Id,
                        GrantedAt = DateTime.UtcNow,
                        GrantedBy = updatedBy
                    });
                }

                if (permissionsToAdd.Any())
                {
                    await _context.RolePermissions.AddRangeAsync(permissionsToAdd);
                }

                // ✅ BOLSTER: Save changes within transaction
                await _context.SaveChangesAsync();

                // ✅ BOLSTER: Commit transaction before clearing cache
                await transaction.CommitAsync();

                // ✅ BOLSTER: Clear cache AFTER successful commit
                _cache.Remove($"{CacheKeyPrefix}{roleId}");

                // Clear all user permission caches for this role
                await ClearUserCachesForRoleAsync(roleId);

                _logger.LogInformation("[ROLE-SERVICE] ✅ Successfully replaced {Count} permissions for role {RoleId} by {UpdatedBy}",
                    permissionsToAdd.Count, roleId, updatedBy);
            }
            catch (Exception ex)
            {
                // ✅ BOLSTER: Rollback transaction on error to prevent partial updates
                await transaction.RollbackAsync();
                _logger.LogError(ex, "[ROLE-SERVICE] ❌ Error replacing role permissions for role {RoleId}: {Message}. Transaction rolled back.",
                    roleId, ex.Message);
                throw;
            }
            });
        }

        public async Task<List<Permission>> GetRolePermissionsAsync(int roleId)
        {
            var role = await GetRoleByIdAsync(roleId);
            if (role == null)
            {
                return new List<Permission>();
            }

            return role.RolePermissions
                .Where(rp => rp.Permission.IsActive)
                .Select(rp => rp.Permission)
                .ToList();
        }

        public async Task AssignRoleToUserAsync(int userId, int roleId, string assignedBy)
        {
            // ✅ BOLSTER: Use database transaction to ensure atomic operation
            var strategy = _context.Database.CreateExecutionStrategy();
            await strategy.ExecuteAsync(async () =>
            {
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                var user = await _context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    throw new ArgumentException($"User not found: {userId}");
                }

                var role = await _context.Roles.AsTracking().FirstOrDefaultAsync(r => r.Id == roleId);
                if (role == null)
                {
                    throw new ArgumentException($"Role not found: {roleId}");
                }

                user.RoleId = roleId;
                user.UpdatedAt = DateTime.UtcNow;
                user.UpdatedBy = assignedBy;

                // ✅ BOLSTER: Save changes within transaction
                await _context.SaveChangesAsync();

                // ✅ BOLSTER: Commit transaction before clearing cache
                await transaction.CommitAsync();

                // ✅ BOLSTER: Clear cache AFTER successful commit
                _cache.Remove($"UserPermissions_{userId}");

                _logger.LogInformation("[ROLE-SERVICE] ✅ Assigned role {RoleId} to user {UserId} by {AssignedBy}",
                    roleId, userId, assignedBy);
            }
            catch (Exception ex)
            {
                // ✅ BOLSTER: Rollback transaction on error to prevent partial updates
                await transaction.RollbackAsync();
                _logger.LogError(ex, "[ROLE-SERVICE] ❌ Error assigning role {RoleId} to user {UserId}: {Message}. Transaction rolled back.",
                    roleId, userId, ex.Message);
                throw;
            }
            });
        }

        public async Task RemoveRoleFromUserAsync(int userId)
        {
            try
            {
                var user = await _context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);
                if (user == null)
                {
                    throw new ArgumentException($"User not found: {userId}");
                }

                user.RoleId = null;
                user.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();

                // Clear user permission cache
                _cache.Remove($"UserPermissions_{userId}");

                _logger.LogInformation("[ROLE-SERVICE] Removed role from user {UserId}", userId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLE-SERVICE] Error removing role from user: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<List<User>> GetUsersInRoleAsync(int roleId)
        {
            return await _context.Users
                .Where(u => u.RoleId == roleId && u.IsActive)
                .OrderBy(u => u.Username)
                .ToListAsync();
        }

        public async Task<Role> CloneRoleAsync(int sourceRoleId, string newName, string newDisplayName,
            string createdBy)
        {
            try
            {
                var sourceRole = await GetRoleByIdAsync(sourceRoleId);
                if (sourceRole == null)
                {
                    throw new ArgumentException($"Source role not found: {sourceRoleId}");
                }

                // Create new role
                var newRole = await CreateRoleAsync(newName, newDisplayName,
                    $"Cloned from {sourceRole.DisplayName}", createdBy, sourceRole.BaseRole);

                // Copy all permissions
                var permissionNames = sourceRole.RolePermissions
                    .Select(rp => rp.Permission.Name)
                    .ToList();

                await AssignPermissionsToRoleAsync(newRole.Id, permissionNames, createdBy);

                _logger.LogInformation("[ROLE-SERVICE] Cloned role {SourceRoleId} to new role {NewRoleId} by {CreatedBy}",
                    sourceRoleId, newRole.Id, createdBy);

                return newRole;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLE-SERVICE] Error cloning role: {Message}", ex.Message);
                throw;
            }
        }

        public async Task<bool> IsRoleNameAvailableAsync(string roleName, int? excludeRoleId = null)
        {
            var query = _context.Roles.Where(r => r.Name == roleName);

            if (excludeRoleId.HasValue)
            {
                query = query.Where(r => r.Id != excludeRoleId.Value);
            }

            return !await query.AnyAsync();
        }

        private async Task ClearUserCachesForRoleAsync(int roleId)
        {
            // Get all users with this role and clear their permission caches
            var userIds = await _context.Users
                .Where(u => u.RoleId == roleId)
                .Select(u => u.Id)
                .ToListAsync();

            foreach (var userId in userIds)
            {
                _cache.Remove($"UserPermissions_{userId}");
            }
        }

        private async Task<int> CopyPermissionsFromBaseRoleAsync(int newRoleId, UserRole baseRole, string grantedBy)
        {
            try
            {
                var templateRole = await _context.Roles
                    .Include(r => r.RolePermissions)
                        .ThenInclude(rp => rp.Permission)
                    .Where(r => r.BaseRole == baseRole && r.IsActive)
                    .OrderByDescending(r => r.IsSystemRole) // prefer system role definitions
                    .ThenBy(r => r.Id)
                    .FirstOrDefaultAsync();

                if (templateRole == null)
                {
                    _logger.LogWarning("[ROLE-SERVICE] No template role found for base role {BaseRole}. New role {RoleId} will start empty.", baseRole, newRoleId);
                    return 0;
                }

                var templatePermissions = templateRole.RolePermissions
                    .Where(rp => rp.Permission.IsActive)
                    .Select(rp => rp.PermissionId)
                    .ToList();

                if (!templatePermissions.Any())
                {
                    _logger.LogWarning("[ROLE-SERVICE] Template role {TemplateRoleId} for base role {BaseRole} has no permissions to copy.", templateRole.Id, baseRole);
                    return 0;
                }

                var existingPermissionIds = await _context.RolePermissions
                    .Where(rp => rp.RoleId == newRoleId)
                    .Select(rp => rp.PermissionId)
                    .ToHashSetAsync();

                var permissionsToInsert = templatePermissions
                    .Where(permissionId => !existingPermissionIds.Contains(permissionId))
                    .Select(permissionId => new RolePermission
                    {
                        RoleId = newRoleId,
                        PermissionId = permissionId,
                        GrantedAt = DateTime.UtcNow,
                        GrantedBy = grantedBy
                    })
                    .ToList();

                if (!permissionsToInsert.Any())
                {
                    _logger.LogInformation("[ROLE-SERVICE] Template permissions already present for new role {RoleId}. Nothing to copy.", newRoleId);
                    return 0;
                }

                await _context.RolePermissions.AddRangeAsync(permissionsToInsert);
                await _context.SaveChangesAsync();

                _cache.Remove($"{CacheKeyPrefix}{newRoleId}");

                _logger.LogInformation(
                    "[ROLE-SERVICE] Copied {PermissionCount} permissions from base role {BaseRole} (template role {TemplateRoleId}) to new role {RoleId}",
                    permissionsToInsert.Count,
                    baseRole,
                    templateRole.Id,
                    newRoleId);
                return permissionsToInsert.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[ROLE-SERVICE] Error copying base role permissions to role {RoleId}: {Message}",
                    newRoleId,
                    ex.Message);
                return 0;
            }
        }
        public async Task<List<RoleResyncResult>> ResyncBaseRolePermissionsAsync(string updatedBy)
        {
            var actor = string.IsNullOrWhiteSpace(updatedBy) ? "System" : updatedBy;
            var roles = await _context.Roles
                .Where(r => r.IsActive && r.BaseRole.HasValue)
                .Select(r => new { r.Id, r.Name, r.BaseRole })
                .ToListAsync();

            var results = new List<RoleResyncResult>();

            foreach (var role in roles)
            {
                var inserted = await CopyPermissionsFromBaseRoleAsync(role.Id, role.BaseRole!.Value, actor);
                if (inserted > 0)
                {
                    await ClearUserCachesForRoleAsync(role.Id);
                }

                results.Add(new RoleResyncResult(role.Id, role.Name ?? string.Empty, role.BaseRole, inserted));
            }

            _logger.LogInformation("[ROLE-SERVICE] Resynced base role permissions for {Count} roles (actor: {Actor})",
                results.Count, actor);

            return results;
        }
    }

    public record RoleResyncResult(int RoleId, string RoleName, UserRole? BaseRole, int PermissionsAdded);
}
