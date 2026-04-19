using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.API.Controllers
{
    public partial class AuthenticationController
    {
        /// <summary>
        /// Get full RBAC profile for the authenticated user, including permissions
        /// </summary>
        [HttpGet("profile")]
        [Authorize]
        [ProducesResponseType(typeof(UserProfileDto), 200)]
        [ProducesResponseType(401)]
        public async Task<ActionResult<UserProfileDto>> GetProfile(CancellationToken cancellationToken)
        {
            var username = User.Identity?.Name;
            if (string.IsNullOrWhiteSpace(username))
            {
                return Unauthorized(new { error = "User identity not found" });
            }

            var user = await _userRepository.GetUserByUsernameAsync(username);
            if (user == null || !user.IsActive)
            {
                return Unauthorized(new { error = "User not found or inactive" });
            }

            var permissions = await _permissionService.GetUserPermissionsAsync(user.Id);
            var normalizedPermissions = permissions
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(p => p, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var allPermissionSet = Permissions.GetAllPermissions()
                .Select(p => p.Name)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var userPermissionSet = new HashSet<string>(normalizedPermissions, StringComparer.OrdinalIgnoreCase);
            var hasAllPermissions = allPermissionSet.All(userPermissionSet.Contains);

            var displayName = string.Join(" ", new[] { user.FirstName, user.LastName }
                .Where(part => !string.IsNullOrWhiteSpace(part)))
                .Trim();
            if (string.IsNullOrWhiteSpace(displayName))
            {
                displayName = user.Username;
            }

            var primaryRole = user.AssignedRole?.DisplayName
                ?? user.AssignedRole?.Name
                ?? user.Role.ToString();

            var roles = new List<string>();
            if (!string.IsNullOrWhiteSpace(user.AssignedRole?.Name))
            {
                roles.Add(user.AssignedRole.Name);
            }
            else
            {
                roles.Add(user.Role.ToString());
            }

            if (!string.IsNullOrWhiteSpace(user.AssignedRole?.DisplayName) &&
                !roles.Contains(user.AssignedRole.DisplayName, StringComparer.OrdinalIgnoreCase))
            {
                roles.Add(user.AssignedRole.DisplayName);
            }

            var profile = new UserProfileDto(
                user.Id,
                user.Username,
                displayName,
                user.Email ?? string.Empty,
                primaryRole,
                roles,
                normalizedPermissions,
                hasAllPermissions,
                DateTime.UtcNow,
                user.IsActive);

            return Ok(profile);
        }
    }
}

