using System;
using System.Linq;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using NickScanCentralImagingPortal.Services.Permissions;

namespace NickScanCentralImagingPortal.API.Authorization
{
    /// <summary>
    /// Handles permission-based authorization
    /// </summary>
    public class PermissionAuthorizationHandler : AuthorizationHandler<PermissionRequirement>
    {
        private readonly IPermissionService _permissionService;
        private readonly ILogger<PermissionAuthorizationHandler> _logger;

        public PermissionAuthorizationHandler(
            IPermissionService permissionService,
            ILogger<PermissionAuthorizationHandler> logger)
        {
            _permissionService = permissionService;
            _logger = logger;
        }

        protected override async Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            PermissionRequirement requirement)
        {
            try
            {
                // ✅ First: check permission claims already present on the principal
                var hasClaimPermission = context.User.Claims.Any(c =>
                    string.Equals(c.Type, "Permission", StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(c.Value, requirement.PermissionName, StringComparison.OrdinalIgnoreCase));

                if (hasClaimPermission)
                {
                    _logger.LogDebug("[AUTH] Claim granted access to '{Permission}'", requirement.PermissionName);
                    context.Succeed(requirement);
                    return;
                }

                // Try to get user ID from claims
                var userIdClaim = context.User.FindFirst(ClaimTypes.NameIdentifier);
                int userId = 0;

                if (userIdClaim != null && int.TryParse(userIdClaim.Value, out userId))
                {
                    // Use user ID from claim
                }
                else
                {
                    // Fallback: Get user ID from username claim
                    var usernameClaim = context.User.FindFirst(ClaimTypes.Name) ?? context.User.FindFirst("username");
                    if (usernameClaim != null)
                    {
                        var hasPermission = await _permissionService.HasPermissionAsync(usernameClaim.Value, requirement.PermissionName);
                        if (hasPermission)
                        {
                            _logger.LogDebug("[AUTH] User {Username} has permission '{Permission}'",
                                usernameClaim.Value, requirement.PermissionName);
                            context.Succeed(requirement);
                        }
                        else
                        {
                            _logger.LogWarning("[AUTH] User {Username} denied access - missing permission '{Permission}'",
                                usernameClaim.Value, requirement.PermissionName);
                            context.Fail();
                        }
                        return;
                    }
                    else
                    {
                        _logger.LogWarning("[AUTH] User ID and username not found in claims");
                        context.Fail();
                        return;
                    }
                }

                // Check if user has the required permission
                var hasPermissionById = await _permissionService.HasPermissionAsync(userId, requirement.PermissionName);

                if (hasPermissionById)
                {
                    _logger.LogDebug("[AUTH] User {UserId} has permission '{Permission}'",
                        userId, requirement.PermissionName);
                    context.Succeed(requirement);
                }
                else
                {
                    _logger.LogWarning("[AUTH] User {UserId} denied access - missing permission '{Permission}'",
                        userId, requirement.PermissionName);
                    context.Fail();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AUTH] Exception in authorization handler for permission '{Permission}': {Message}",
                    requirement.PermissionName, ex.Message);
                // On exception, deny access (fail secure)
                context.Fail();
            }
        }
    }
}

