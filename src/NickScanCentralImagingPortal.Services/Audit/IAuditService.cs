namespace NickScanCentralImagingPortal.Services.Audit
{
    public interface IAuditService
    {
        Task LogPermissionCheckAsync(int userId, string permissionName, bool result, string? ipAddress = null);
        Task LogPermissionGrantAsync(int targetUserId, string permissionName, int grantedByUserId, string? reason = null);
        Task LogPermissionRevokeAsync(int targetUserId, string permissionName, int revokedByUserId, string? reason = null);
        Task LogRoleAssignmentAsync(int userId, int roleId, int assignedByUserId);
        Task LogRoleCreatedAsync(int roleId, int createdByUserId);
        Task LogRoleUpdatedAsync(int roleId, int updatedByUserId);
        Task LogRoleDeletedAsync(int roleId, int deletedByUserId);
    }
}

