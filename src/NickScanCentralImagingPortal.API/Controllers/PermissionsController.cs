using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Models.Permissions;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.Permissions;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Permission management controller - Admin only
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class PermissionsController : ControllerBase
    {
        private readonly IPermissionService _permissionService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<PermissionsController> _logger;
        private readonly IPermissionCatalogBuilder _catalogBuilder;
        private readonly IMemoryCache _memoryCache;

        private const string CatalogCacheKey = "permissions:catalog";
        private static readonly MemoryCacheEntryOptions CatalogCacheOptions = new()
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
            Size = 1
        };

        public PermissionsController(
            IPermissionService permissionService,
            ApplicationDbContext context,
            ILogger<PermissionsController> logger,
            IPermissionCatalogBuilder catalogBuilder,
            IMemoryCache memoryCache)
        {
            _permissionService = permissionService;
            _context = context;
            _logger = logger;
            _catalogBuilder = catalogBuilder;
            _memoryCache = memoryCache;
        }

        /// <summary>
        /// Get the permission catalog including categories, metadata, and default role assignments.
        /// </summary>
        [HttpGet("catalog")]
        [Authorize]
        public async Task<ActionResult<PermissionCatalogDto>> GetCatalog(CancellationToken cancellationToken)
        {
            var catalog = await GetOrBuildCatalogAsync(cancellationToken);

            if (Request.Headers.TryGetValue("If-None-Match", out var etags))
            {
                var requested = etags.FirstOrDefault();
                if (!string.IsNullOrEmpty(requested))
                {
                    var normalized = requested.Trim('"');
                    if (string.Equals(normalized, catalog.Version, StringComparison.Ordinal))
                    {
                        Response.Headers.ETag = $"\"{catalog.Version}\"";
                        Response.Headers["Cache-Control"] = "public, max-age=300";
                        return StatusCode(StatusCodes.Status304NotModified);
                    }
                }
            }

            Response.Headers.ETag = $"\"{catalog.Version}\"";
            Response.Headers["Cache-Control"] = "public, max-age=300";
            return Ok(catalog);
        }

        /// <summary>
        /// Get the current permission catalog version hash.
        /// </summary>
        [HttpGet("catalog/version")]
        [Authorize]
        public async Task<ActionResult<object>> GetCatalogVersion(CancellationToken cancellationToken)
        {
            var catalog = await GetOrBuildCatalogAsync(cancellationToken);
            Response.Headers.ETag = $"\"{catalog.Version}\"";
            Response.Headers["Cache-Control"] = "public, max-age=300";

            return Ok(new
            {
                catalog.Version,
                catalog.GeneratedAtUtc
            });
        }

        /// <summary>
        /// Get all available permissions
        /// </summary>
        [HttpGet("all")]
        [HasPermission(Permissions.PermissionsView)]
        public async Task<ActionResult<List<PermissionDto>>> GetAllPermissions()
        {
            try
            {
                var permissions = await _context.Permissions
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Category)
                    .ThenBy(p => p.Name)
                    .Select(p => new PermissionDto
                    {
                        Id = p.Id,
                        Name = p.Name,
                        DisplayName = p.DisplayName,
                        Description = p.Description,
                        Category = p.Category,
                        IsActive = p.IsActive
                    })
                    .ToListAsync();

                return Ok(permissions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSIONS-API] Error getting permissions: {Message}", ex.Message);
                return StatusCode(500, "Error retrieving permissions");
            }
        }

        /// <summary>
        /// Get permissions grouped by category
        /// </summary>
        [HttpGet("by-category")]
        [HasPermission(Permissions.PermissionsView)]
        public async Task<ActionResult<Dictionary<string, List<PermissionDto>>>> GetPermissionsByCategory()
        {
            try
            {
                var permissions = await _context.Permissions
                    .Where(p => p.IsActive)
                    .OrderBy(p => p.Category)
                    .ThenBy(p => p.Name)
                    .ToListAsync();

                var grouped = permissions
                    .GroupBy(p => p.Category ?? "Other")
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(p => new PermissionDto
                        {
                            Id = p.Id,
                            Name = p.Name,
                            DisplayName = p.DisplayName,
                            Description = p.Description,
                            Category = p.Category,
                            IsActive = p.IsActive
                        }).ToList()
                    );

                return Ok(grouped);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSIONS-API] Error getting permissions by category: {Message}", ex.Message);
                return StatusCode(500, "Error retrieving permissions");
            }
        }

        /// <summary>
        /// Check if a user has a specific permission
        /// </summary>
        [HttpGet("check")]
        [HasPermission(Permissions.PermissionsView)]
        public async Task<ActionResult<PermissionCheckResponse>> CheckPermission(
            [FromQuery] int? userId = null,
            [FromQuery] string? username = null,
            [FromQuery] string? permission = null)
        {
            try
            {
                if (string.IsNullOrEmpty(permission))
                {
                    return BadRequest(new PermissionCheckResponse
                    {
                        HasPermission = false,
                        Message = "Permission name is required"
                    });
                }

                bool hasPermission;

                if (userId.HasValue)
                {
                    hasPermission = await _permissionService.HasPermissionAsync(userId.Value, permission);
                }
                else if (!string.IsNullOrEmpty(username))
                {
                    hasPermission = await _permissionService.HasPermissionAsync(username, permission);
                }
                else
                {
                    return BadRequest(new PermissionCheckResponse
                    {
                        HasPermission = false,
                        Message = "Either userId or username is required"
                    });
                }

                return Ok(new PermissionCheckResponse
                {
                    HasPermission = hasPermission,
                    Message = hasPermission ? "Permission granted" : "Permission denied"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSIONS-API] Error checking permission: {Message}", ex.Message);
                return StatusCode(500, new PermissionCheckResponse
                {
                    HasPermission = false,
                    Message = "Error checking permission"
                });
            }
        }

        /// <summary>
        /// Get all permissions for a user (by userId)
        /// </summary>
        [HttpGet("user/{userId}")]
        [HasPermission(Permissions.PermissionsView)]
        public async Task<ActionResult<List<string>>> GetUserPermissions(int userId)
        {
            try
            {
                var permissions = await _permissionService.GetUserPermissionsAsync(userId);
                return Ok(permissions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSIONS-API] Error getting user permissions: {Message}", ex.Message);
                return StatusCode(500, new List<string>());
            }
        }

        /// <summary>
        /// Get all permissions for the current authenticated user
        /// </summary>
        [HttpGet("my-permissions")]
        [Authorize] // Any authenticated user can check their own permissions
        public async Task<ActionResult<List<string>>> GetMyPermissions()
        {
            try
            {
                var username = User.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Unauthorized(new { error = "User not authenticated" });
                }

                // Include whether the user effectively has full access (all permissions)
                var user = await _context.Users
                    .Include(u => u.AssignedRole)
                    .FirstOrDefaultAsync(u => u.Username == username && u.IsActive);

                if (user == null)
                {
                    return Unauthorized(new { error = "User not found" });
                }

                var permissions = await _permissionService.GetUserPermissionsAsync(user.Id);
                var normalizedPermissions = permissions
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var allPermissionSet = Permissions.GetAllPermissions()
                    .Select(p => p.Name)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);

                var userPermissionSet = new HashSet<string>(normalizedPermissions, StringComparer.OrdinalIgnoreCase);
                var hasAllPermissions = allPermissionSet.All(userPermissionSet.Contains);

                return Ok(new
                {
                    Permissions = normalizedPermissions,
                    IsSuperAdmin = hasAllPermissions,
                    HasAllPermissions = hasAllPermissions,
                    RoleName = user?.AssignedRole?.Name ?? "Unknown"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSIONS-API] Error getting current user permissions: {Message}", ex.Message);
                return StatusCode(500, new { error = "Error retrieving permissions" });
            }
        }

        /// <summary>
        /// Check if user has all specified permissions
        /// </summary>
        [HttpPost("check-all")]
        [HasPermission(Permissions.PermissionsView)]
        public async Task<ActionResult<PermissionCheckResponse>> CheckAllPermissions(
            [FromBody] CheckMultiplePermissionsRequest request)
        {
            try
            {
                var hasAll = await _permissionService.HasAllPermissionsAsync(
                    request.UserId,
                    request.Permissions.ToArray());

                return Ok(new PermissionCheckResponse
                {
                    HasPermission = hasAll,
                    Message = hasAll ? "All permissions granted" : "Missing one or more permissions"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSIONS-API] Error checking multiple permissions: {Message}", ex.Message);
                return StatusCode(500, new PermissionCheckResponse
                {
                    HasPermission = false,
                    Message = "Error checking permissions"
                });
            }
        }

        /// <summary>
        /// Check if user has any of the specified permissions
        /// </summary>
        [HttpPost("check-any")]
        [HasPermission(Permissions.PermissionsView)]
        public async Task<ActionResult<PermissionCheckResponse>> CheckAnyPermissions(
            [FromBody] CheckMultiplePermissionsRequest request)
        {
            try
            {
                var hasAny = await _permissionService.HasAnyPermissionAsync(
                    request.UserId,
                    request.Permissions.ToArray());

                return Ok(new PermissionCheckResponse
                {
                    HasPermission = hasAny,
                    Message = hasAny ? "At least one permission granted" : "No permissions granted"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[PERMISSIONS-API] Error checking multiple permissions: {Message}", ex.Message);
                return StatusCode(500, new PermissionCheckResponse
                {
                    HasPermission = false,
                    Message = "Error checking permissions"
                });
            }
        }

        private async Task<PermissionCatalogDto> GetOrBuildCatalogAsync(CancellationToken cancellationToken)
        {
            if (_memoryCache.TryGetValue(CatalogCacheKey, out PermissionCatalogDto? cachedCatalog) && cachedCatalog != null)
            {
                return cachedCatalog;
            }

            var catalog = await _catalogBuilder.BuildAsync(cancellationToken);
            if (catalog == null)
            {
                throw new InvalidOperationException("Failed to build permission catalog");
            }
            _memoryCache.Set(CatalogCacheKey, catalog, CatalogCacheOptions);
            return catalog;
        }
    }

    // Request/Response models
    public class PermissionCheckResponse
    {
        public bool HasPermission { get; set; }
        public string? Message { get; set; }
    }

    public class CheckMultiplePermissionsRequest
    {
        public int UserId { get; set; }
        public List<string> Permissions { get; set; } = new();
    }

    public class PermissionDto
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? Category { get; set; }
        public bool IsActive { get; set; }
    }
}

