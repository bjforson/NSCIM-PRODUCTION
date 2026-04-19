using Microsoft.AspNetCore.Authorization;

namespace NickScanCentralImagingPortal.API.Authorization
{
    /// <summary>
    /// Authorization requirement for permission-based access
    /// </summary>
    public class PermissionRequirement : IAuthorizationRequirement
    {
        public string PermissionName { get; }

        public PermissionRequirement(string permissionName)
        {
            PermissionName = permissionName;
        }
    }
}

