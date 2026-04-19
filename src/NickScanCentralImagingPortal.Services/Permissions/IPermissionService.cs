using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.Permissions
{
    public interface IPermissionService
    {
        /// <summary>
        /// Check if a user has a specific permission
        /// </summary>
        Task<bool> HasPermissionAsync(int userId, string permissionName);

        /// <summary>
        /// Check if a user has a specific permission (by username)
        /// </summary>
        Task<bool> HasPermissionAsync(string username, string permissionName);

        /// <summary>
        /// Check if a role has minimum required level
        /// </summary>
        bool HasPermission(UserRole userRole, UserRole requiredRole);

        /// <summary>
        /// Check if a legacy role has a specific permission (backward compatibility)
        /// </summary>
        bool HasPermission(UserRole userRole, string permissionName);

        /// <summary>
        /// Get all permissions for a user
        /// </summary>
        Task<List<string>> GetUserPermissionsAsync(int userId);

        /// <summary>
        /// Get all permissions for a role
        /// </summary>
        Task<List<string>> GetRolePermissionsAsync(int roleId);

        /// <summary>
        /// Grant a permission to a user (user-specific override)
        /// </summary>
        Task GrantPermissionToUserAsync(int userId, string permissionName, string grantedBy, DateTime? expiresAt = null, string? reason = null);

        /// <summary>
        /// Revoke a permission from a user (user-specific override)
        /// </summary>
        Task RevokePermissionFromUserAsync(int userId, string permissionName, string revokedBy, string? reason = null);

        /// <summary>
        /// Remove a user permission override
        /// </summary>
        Task RemoveUserPermissionOverrideAsync(int userId, string permissionName);

        /// <summary>
        /// Check if a user has multiple permissions (AND logic)
        /// </summary>
        Task<bool> HasAllPermissionsAsync(int userId, params string[] permissionNames);

        /// <summary>
        /// Check if a user has any of the specified permissions (OR logic)
        /// </summary>
        Task<bool> HasAnyPermissionAsync(int userId, params string[] permissionNames);

        /// <summary>
        /// Get expired user permissions
        /// </summary>
        Task<List<int>> GetExpiredUserPermissionsAsync();

        /// <summary>
        /// Clean up expired user permissions
        /// </summary>
        Task CleanupExpiredPermissionsAsync();
    }
}

