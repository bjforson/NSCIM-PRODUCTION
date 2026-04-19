using Microsoft.AspNetCore.Authorization;

namespace NickScanCentralImagingPortal.API.Authorization
{
    /// <summary>
    /// Attribute to require specific permission for an endpoint
    /// Usage: [HasPermission("containers.approve")]
    /// </summary>
    public class HasPermissionAttribute : AuthorizeAttribute
    {
        public HasPermissionAttribute(string permissionName)
        {
            Policy = $"Permission:{permissionName}";
        }
    }
}

