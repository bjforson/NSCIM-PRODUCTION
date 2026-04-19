using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.Permissions
{
    public interface IRoleService
    {
        /// <summary>
        /// Get all active roles
        /// </summary>
        Task<List<Role>> GetAllRolesAsync();

        /// <summary>
        /// Get role by ID
        /// </summary>
        Task<Role?> GetRoleByIdAsync(int roleId);

        /// <summary>
        /// Get role by name
        /// </summary>
        Task<Role?> GetRoleByNameAsync(string roleName);

        /// <summary>
        /// Create a new custom role
        /// </summary>
        Task<Role> CreateRoleAsync(string name, string displayName, string description, string createdBy, UserRole? baseRole = null);

        /// <summary>
        /// Update an existing role
        /// </summary>
        Task<Role> UpdateRoleAsync(int roleId, string displayName, string description, string updatedBy);

        /// <summary>
        /// Delete a role (only custom roles, not system roles)
        /// </summary>
        Task DeleteRoleAsync(int roleId, string deletedBy);

        /// <summary>
        /// Assign a permission to a role
        /// </summary>
        Task AssignPermissionToRoleAsync(int roleId, string permissionName, string grantedBy);

        /// <summary>
        /// Remove a permission from a role
        /// </summary>
        Task RemovePermissionFromRoleAsync(int roleId, string permissionName);

        /// <summary>
        /// Assign multiple permissions to a role
        /// </summary>
        Task AssignPermissionsToRoleAsync(int roleId, List<string> permissionNames, string grantedBy);

        /// <summary>
        /// Replace all permissions for a role
        /// </summary>
        Task ReplaceRolePermissionsAsync(int roleId, List<string> permissionNames, string updatedBy);

        /// <summary>
        /// Get all permissions assigned to a role
        /// </summary>
        Task<List<Permission>> GetRolePermissionsAsync(int roleId);

        /// <summary>
        /// Assign a role to a user
        /// </summary>
        Task AssignRoleToUserAsync(int userId, int roleId, string assignedBy);

        /// <summary>
        /// Remove role from user
        /// </summary>
        Task RemoveRoleFromUserAsync(int userId);

        /// <summary>
        /// Get all users assigned to a role
        /// </summary>
        Task<List<User>> GetUsersInRoleAsync(int roleId);

        /// <summary>
        /// Clone a role with new name
        /// </summary>
        Task<Role> CloneRoleAsync(int sourceRoleId, string newName, string newDisplayName, string createdBy);

        /// <summary>
        /// Check if role name is available
        /// </summary>
        Task<bool> IsRoleNameAvailableAsync(string roleName, int? excludeRoleId = null);

        /// <summary>
        /// Resync all roles that have a base role to ensure their permissions align with the template
        /// </summary>
        Task<List<RoleResyncResult>> ResyncBaseRolePermissionsAsync(string updatedBy);
    }
}

