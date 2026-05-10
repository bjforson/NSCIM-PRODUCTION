using System.Linq;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Permissions;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize] // Require authentication for all role management endpoints
    [ApiController]
    [Route("api/[controller]")]
    //[HasPermission("roles.view")] // TODO: Enable fine-grained permissions when ready
    public class RolesController : ControllerBase
    {
        private readonly IRoleService _roleService;
        private readonly IPermissionService _permissionService;
        private readonly ILogger<RolesController> _logger;
        private readonly IConfiguration _configuration;

        public RolesController(
            IRoleService roleService,
            IPermissionService permissionService,
            ILogger<RolesController> logger,
            IConfiguration configuration)
        {
            _roleService = roleService;
            _permissionService = permissionService;
            _logger = logger;
            _configuration = configuration;
        }

        private bool IsValidServiceApiKey(string? providedKey)
        {
            return ServiceApiKeyValidator.IsValid(_configuration, providedKey);
        }

        /// <summary>
        /// Service-to-service: Get list of active roles (for NickHR to populate dropdowns).
        /// Protected by NICKSCAN_SERVICE_API_KEY via ?apiKey=... query parameter.
        /// </summary>
        [HttpGet("service/list")]
        [AllowAnonymous]
        public async Task<ActionResult<List<RoleDto>>> ServiceListRoles([FromQuery] string apiKey)
        {
            if (!IsValidServiceApiKey(apiKey))
            {
                return Unauthorized(new { error = "Invalid service API key" });
            }

            try
            {
                var roles = await _roleService.GetAllRolesAsync();
                var roleDtos = roles
                    .Where(r => r.IsActive)
                    .Select(r => new RoleDto
                    {
                        Id = r.Id,
                        Name = r.Name,
                        DisplayName = r.DisplayName,
                        Description = r.Description,
                        BaseRole = r.BaseRole,
                        IsSystemRole = r.IsSystemRole,
                        IsActive = r.IsActive
                    }).ToList();

                return Ok(roleDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "service/list: Error getting roles");
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Get all roles
        /// </summary>
        [HttpGet]
        [HasPermission(Permissions.RolesView)]
        public async Task<ActionResult<List<RoleDto>>> GetAllRoles()
        {
            try
            {
                var roles = await _roleService.GetAllRolesAsync();
                var roleDtos = roles.Select(r => new RoleDto
                {
                    Id = r.Id,
                    Name = r.Name,
                    DisplayName = r.DisplayName,
                    Description = r.Description,
                    BaseRole = r.BaseRole,
                    IsSystemRole = r.IsSystemRole,
                    IsActive = r.IsActive,
                    CreatedAt = r.CreatedAt,
                    CreatedBy = r.CreatedBy,
                    UpdatedAt = r.UpdatedAt,
                    UpdatedBy = r.UpdatedBy
                }).ToList();

                return Ok(roleDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error getting roles: {Message}", ex.Message);
                return StatusCode(500, "Error retrieving roles");
            }
        }

        /// <summary>
        /// Get role by ID
        /// </summary>
        [HttpGet("{id}")]
        [HasPermission(Permissions.RolesView)]
        public async Task<ActionResult<RoleDto>> GetRole(int id)
        {
            try
            {
                var role = await _roleService.GetRoleByIdAsync(id);
                if (role == null)
                {
                    return NotFound($"Role with ID {id} not found");
                }

                var roleDto = new RoleDto
                {
                    Id = role.Id,
                    Name = role.Name,
                    DisplayName = role.DisplayName,
                    Description = role.Description,
                    BaseRole = role.BaseRole,
                    IsSystemRole = role.IsSystemRole,
                    IsActive = role.IsActive,
                    CreatedAt = role.CreatedAt,
                    CreatedBy = role.CreatedBy,
                    UpdatedAt = role.UpdatedAt,
                    UpdatedBy = role.UpdatedBy
                };

                return Ok(roleDto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error getting role: {Message}", ex.Message);
                return StatusCode(500, "Error retrieving role");
            }
        }

        /// <summary>
        /// Get permissions for a role
        /// </summary>
        [HttpGet("{id}/permissions")]
        [HasPermission(Permissions.RolesView)]
        public async Task<ActionResult<List<string>>> GetRolePermissions(int id)
        {
            try
            {
                var permissions = await _permissionService.GetRolePermissionsAsync(id);
                return Ok(permissions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error getting role permissions: {Message}", ex.Message);
                return StatusCode(500, "Error retrieving role permissions");
            }
        }

        /// <summary>
        /// Create a new role
        /// </summary>
        [HttpPost]
        [HasPermission(Permissions.RolesManagePermissions)]
        public async Task<ActionResult<RoleDto>> CreateRole([FromBody] CreateRoleRequest request)
        {
            try
            {
                var role = await _roleService.CreateRoleAsync(
                    request.Name,
                    request.DisplayName,
                    request.Description ?? string.Empty,
                    request.CreatedBy ?? "System",
                    request.BaseRole
                );

                var roleDto = new RoleDto
                {
                    Id = role.Id,
                    Name = role.Name,
                    DisplayName = role.DisplayName,
                    Description = role.Description,
                    BaseRole = role.BaseRole,
                    IsSystemRole = role.IsSystemRole,
                    IsActive = role.IsActive,
                    CreatedAt = role.CreatedAt,
                    CreatedBy = role.CreatedBy
                };

                return CreatedAtAction(nameof(GetRole), new { id = role.Id }, roleDto);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error creating role: {Message}", ex.Message);
                return StatusCode(500, "Error creating role");
            }
        }

        /// <summary>
        /// Update a role
        /// </summary>
        [HttpPut("{id}")]
        [HasPermission(Permissions.RolesManagePermissions)]
        public async Task<ActionResult<RoleDto>> UpdateRole(int id, [FromBody] UpdateRoleRequest request)
        {
            try
            {
                var role = await _roleService.UpdateRoleAsync(
                    id,
                    request.DisplayName,
                    request.Description ?? string.Empty,
                    request.UpdatedBy ?? "System"
                );

                var roleDto = new RoleDto
                {
                    Id = role.Id,
                    Name = role.Name,
                    DisplayName = role.DisplayName,
                    Description = role.Description,
                    BaseRole = role.BaseRole,
                    IsSystemRole = role.IsSystemRole,
                    IsActive = role.IsActive,
                    CreatedAt = role.CreatedAt,
                    CreatedBy = role.CreatedBy,
                    UpdatedAt = role.UpdatedAt,
                    UpdatedBy = role.UpdatedBy
                };

                return Ok(roleDto);
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error updating role: {Message}", ex.Message);
                return StatusCode(500, "Error updating role");
            }
        }

        /// <summary>
        /// Delete a role
        /// </summary>
        [HttpDelete("{id}")]
        [HasPermission(Permissions.RolesManagePermissions)]
        public async Task<ActionResult> DeleteRole(int id, [FromQuery] string? deletedBy = null)
        {
            try
            {
                await _roleService.DeleteRoleAsync(id, deletedBy ?? "System");
                return NoContent();
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error deleting role: {Message}", ex.Message);
                return StatusCode(500, "Error deleting role");
            }
        }

        /// <summary>
        /// Assign permission to role
        /// </summary>
        [HttpPost("{id}/permissions")]
        [HasPermission(Permissions.RolesManagePermissions)]
        public async Task<ActionResult> AssignPermission(int id, [FromBody] AssignPermissionRequest request)
        {
            try
            {
                await _roleService.AssignPermissionToRoleAsync(
                    id,
                    request.PermissionName,
                    request.GrantedBy ?? "System"
                );

                return Ok(new { message = "Permission assigned successfully" });
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error assigning permission: {Message}", ex.Message);
                return StatusCode(500, "Error assigning permission");
            }
        }

        /// <summary>
        /// Remove permission from role
        /// </summary>
        [HttpDelete("{id}/permissions/{permissionName}")]
        [HasPermission(Permissions.RolesManagePermissions)]
        public async Task<ActionResult> RemovePermission(int id, string permissionName)
        {
            try
            {
                await _roleService.RemovePermissionFromRoleAsync(id, permissionName);
                return Ok(new { message = "Permission removed successfully" });
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error removing permission: {Message}", ex.Message);
                return StatusCode(500, "Error removing permission");
            }
        }

        /// <summary>
        /// Replace all permissions for a role
        /// </summary>
        [HttpPut("{id}/permissions")]
        [HasPermission(Permissions.RolesManagePermissions)]
        public async Task<ActionResult> ReplacePermissions(int id, [FromBody] ReplacePermissionsRequest request)
        {
            try
            {
                await _roleService.ReplaceRolePermissionsAsync(
                    id,
                    request.PermissionNames,
                    request.UpdatedBy ?? "System"
                );

                return Ok(new { message = "Permissions updated successfully" });
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error replacing permissions: {Message}", ex.Message);
                return StatusCode(500, "Error updating permissions");
            }
        }

        /// <summary>
        /// Resync all roles with a BaseRole to ensure their permissions match the template
        /// </summary>
        [HttpPost("resync-base-permissions")]
        [HasPermission(Permissions.RolesManagePermissions)]
        public async Task<ActionResult<ResyncRolesResponse>> ResyncBasePermissions([FromBody] ResyncRolesRequest? request)
        {
            try
            {
                var actor = request?.UpdatedBy ?? User?.Identity?.Name ?? "System";
                var results = await _roleService.ResyncBaseRolePermissionsAsync(actor);

                var response = new ResyncRolesResponse
                {
                    UpdatedBy = actor,
                    TotalRolesProcessed = results.Count,
                    TotalPermissionsAdded = results.Sum(r => r.PermissionsAdded),
                    Roles = results.Select(r => new ResyncRoleResult
                    {
                        RoleId = r.RoleId,
                        RoleName = r.RoleName,
                        BaseRole = r.BaseRole,
                        PermissionsAdded = r.PermissionsAdded
                    }).ToList()
                };

                return Ok(response);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error resyncing base role permissions: {Message}", ex.Message);
                return StatusCode(500, "Error resyncing role permissions");
            }
        }

        /// <summary>
        /// Clone a role
        /// </summary>
        [HttpPost("{id}/clone")]
        [HasPermission(Permissions.RolesManagePermissions)]
        public async Task<ActionResult<RoleDto>> CloneRole(int id, [FromBody] CloneRoleRequest request)
        {
            try
            {
                var newRole = await _roleService.CloneRoleAsync(
                    id,
                    request.NewName,
                    request.NewDisplayName,
                    request.CreatedBy ?? "System"
                );

                var roleDto = new RoleDto
                {
                    Id = newRole.Id,
                    Name = newRole.Name,
                    DisplayName = newRole.DisplayName,
                    Description = newRole.Description,
                    BaseRole = newRole.BaseRole,
                    IsSystemRole = newRole.IsSystemRole,
                    IsActive = newRole.IsActive,
                    CreatedAt = newRole.CreatedAt,
                    CreatedBy = newRole.CreatedBy
                };

                return CreatedAtAction(nameof(GetRole), new { id = newRole.Id }, roleDto);
            }
            catch (ArgumentException ex)
            {
                return NotFound(ex.Message);
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error cloning role: {Message}", ex.Message);
                return StatusCode(500, "Error cloning role");
            }
        }

        /// <summary>
        /// Get users in a role
        /// </summary>
        [HttpGet("{id}/users")]
        [HasPermission(Permissions.RolesView)]
        public async Task<ActionResult<List<UserDto>>> GetUsersInRole(int id)
        {
            try
            {
                var users = await _roleService.GetUsersInRoleAsync(id);
                var userDtos = users.Select(u => new UserDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Email = u.Email,
                    FirstName = u.FirstName,
                    LastName = u.LastName,
                    Role = u.Role,
                    IsActive = u.IsActive,
                    LastLoginAt = u.LastLoginAt
                }).ToList();

                return Ok(userDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ROLES-API] Error getting users in role: {Message}", ex.Message);
                return StatusCode(500, "Error retrieving users");
            }
        }
    }

    // DTOs
    public class RoleDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public UserRole? BaseRole { get; set; }
        public bool IsSystemRole { get; set; }
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public string? CreatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class UserDto
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public UserRole Role { get; set; }
        public bool IsActive { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    public class CreateRoleRequest
    {
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public UserRole? BaseRole { get; set; }
        public string? CreatedBy { get; set; }
    }

    public class UpdateRoleRequest
    {
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class AssignPermissionRequest
    {
        public string PermissionName { get; set; } = string.Empty;
        public string? GrantedBy { get; set; }
    }

    public class ReplacePermissionsRequest
    {
        public List<string> PermissionNames { get; set; } = new();
        public string? UpdatedBy { get; set; }
    }

    public class CloneRoleRequest
    {
        public string NewName { get; set; } = string.Empty;
        public string NewDisplayName { get; set; } = string.Empty;
        public string? CreatedBy { get; set; }
    }

    public class ResyncRolesRequest
    {
        public string? UpdatedBy { get; set; }
    }

    public class ResyncRolesResponse
    {
        public string UpdatedBy { get; set; } = "System";
        public int TotalRolesProcessed { get; set; }
        public int TotalPermissionsAdded { get; set; }
        public List<ResyncRoleResult> Roles { get; set; } = new();
    }

    public class ResyncRoleResult
    {
        public int RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public UserRole? BaseRole { get; set; }
        public int PermissionsAdded { get; set; }
    }
}

