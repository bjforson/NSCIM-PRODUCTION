using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.Permissions;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Access Review Controller - ISO 27001 compliance requirement
    /// Manages quarterly access reviews for user access rights
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminOnly")] // Only admins can manage access reviews
    public class AccessReviewController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IRoleService _roleService;
        private readonly IPermissionService _permissionService;
        private readonly ILogger<AccessReviewController> _logger;

        public AccessReviewController(
            IUserRepository userRepository,
            IRoleService roleService,
            IPermissionService permissionService,
            ILogger<AccessReviewController> logger)
        {
            _userRepository = userRepository;
            _roleService = roleService;
            _permissionService = permissionService;
            _logger = logger;
        }

        /// <summary>
        /// Get all users with their access rights for review
        /// </summary>
        [HttpGet("users")]
        public async Task<ActionResult<object>> GetUsersForReview(
            [FromQuery] bool includeInactive = false,
            [FromQuery] string? department = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                _logger.LogInformation("Getting users for access review - IncludeInactive: {IncludeInactive}, Department: {Department}, Page: {Page}",
                    includeInactive, department, page);

                var users = includeInactive
                    ? await _userRepository.GetAllUsersAsync()
                    : await _userRepository.GetActiveUsersAsync();

                // Filter by department if specified
                if (!string.IsNullOrEmpty(department))
                {
                    users = users.Where(u => u.Department == department).ToList();
                }

                // Calculate pagination
                var totalCount = users.Count;
                var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);
                var pagedUsers = users.Skip((page - 1) * pageSize).Take(pageSize).ToList();

                // Get access details for each user
                var reviewData = new List<object>();
                foreach (var user in pagedUsers)
                {
                    // Get user roles - users have a single AssignedRole
                    var roles = new List<NickScanCentralImagingPortal.Core.Entities.Role>();
                    if (user.AssignedRole != null)
                    {
                        roles.Add(user.AssignedRole);
                    }
                    else if (user.RoleId.HasValue)
                    {
                        var role = await _roleService.GetRoleByIdAsync(user.RoleId.Value);
                        if (role != null)
                        {
                            roles.Add(role);
                        }
                    }

                    var permissions = await _permissionService.GetUserPermissionsAsync(user.Id);

                    reviewData.Add(new
                    {
                        user.Id,
                        user.Username,
                        user.UserNumber,
                        user.Email,
                        user.FirstName,
                        user.LastName,
                        user.Department,
                        user.RoleId,
                        RoleName = user.Role.ToString(),
                        user.IsActive,
                        LastLogin = user.LastLoginAt,
                        Roles = roles.Select(r => new { r.Id, r.Name }).ToList(),
                        Permissions = permissions.ToList(),
                        PermissionCount = permissions.Count,
                        DaysSinceLastLogin = user.LastLoginAt.HasValue
                            ? (DateTime.UtcNow - user.LastLoginAt.Value).Days
                            : (int?)null,
                        ReviewStatus = "Pending", // TODO: Implement review status tracking
                        ReviewDate = (DateTime?)null, // TODO: Implement review date tracking
                        ReviewerId = (int?)null // TODO: Implement reviewer tracking
                    });
                }

                return Ok(new
                {
                    Users = reviewData,
                    Pagination = new
                    {
                        Page = page,
                        PageSize = pageSize,
                        TotalCount = totalCount,
                        TotalPages = totalPages
                    },
                    Summary = new
                    {
                        TotalUsers = totalCount,
                        ActiveUsers = users.Count(u => u.IsActive),
                        InactiveUsers = users.Count(u => !u.IsActive),
                        UsersWithNoRecentLogin = users.Count(u => !u.LastLoginAt.HasValue || (DateTime.UtcNow - u.LastLoginAt.Value).Days > 90),
                        UsersWithExcessivePermissions = reviewData.Count(u =>
                        {
                            var prop = u.GetType().GetProperty("PermissionCount");
                            var value = prop?.GetValue(u);
                            return value != null && (int)value > 50;
                        })
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting users for access review");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        /// <summary>
        /// Get access review summary statistics
        /// </summary>
        [HttpGet("summary")]
        public async Task<ActionResult<object>> GetAccessReviewSummary()
        {
            try
            {
                _logger.LogInformation("Getting access review summary");

                var users = await _userRepository.GetAllUsersAsync();
                var activeUsers = users.Where(u => u.IsActive).ToList();

                var summary = new
                {
                    TotalUsers = users.Count,
                    ActiveUsers = activeUsers.Count,
                    InactiveUsers = users.Count - activeUsers.Count,
                    UsersWithNoLogin = users.Count(u => !u.LastLoginAt.HasValue),
                    UsersWithStaleLogin = users.Count(u => u.LastLoginAt.HasValue && (DateTime.UtcNow - u.LastLoginAt.Value).Days > 90),
                    UsersByRole = users.GroupBy(u => u.Role.ToString())
                        .Select(g => new { Role = g.Key, Count = g.Count() })
                        .ToList(),
                    UsersByDepartment = users.Where(u => !string.IsNullOrEmpty(u.Department))
                        .GroupBy(u => u.Department)
                        .Select(g => new { Department = g.Key, Count = g.Count() })
                        .ToList(),
                    LastReviewDate = (DateTime?)null, // TODO: Implement review date tracking
                    NextReviewDate = (DateTime?)null, // TODO: Calculate next review date
                    ReviewStatus = "Pending" // TODO: Implement review status
                };

                return Ok(summary);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting access review summary");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        /// <summary>
        /// Get user access details for review
        /// </summary>
        [HttpGet("users/{userId}")]
        public async Task<ActionResult<object>> GetUserAccessDetails(int userId)
        {
            try
            {
                _logger.LogInformation("Getting access details for user {UserId}", userId);

                var user = await _userRepository.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // Get user roles - users have a single AssignedRole
                var roles = new List<NickScanCentralImagingPortal.Core.Entities.Role>();
                if (user.AssignedRole != null)
                {
                    roles.Add(user.AssignedRole);
                }
                else if (user.RoleId.HasValue)
                {
                    var role = await _roleService.GetRoleByIdAsync(user.RoleId.Value);
                    if (role != null)
                    {
                        roles.Add(role);
                    }
                }

                var permissionNames = await _permissionService.GetUserPermissionsAsync(userId);

                var accessDetails = new
                {
                    User = new
                    {
                        user.Id,
                        user.Username,
                        user.UserNumber,
                        user.Email,
                        user.FirstName,
                        user.LastName,
                        user.Department,
                        user.RoleId,
                        RoleName = user.Role.ToString(),
                        user.IsActive,
                        user.CreatedAt,
                        LastLogin = user.LastLoginAt,
                        DaysSinceLastLogin = user.LastLoginAt.HasValue
                            ? (DateTime.UtcNow - user.LastLoginAt.Value).Days
                            : (int?)null
                    },
                    Roles = roles.Select(r => new
                    {
                        r.Id,
                        r.Name,
                        r.Description
                    }).ToList(),
                    Permissions = permissionNames.Select(p => new
                    {
                        Name = p
                    }).ToList(),
                    PermissionCount = permissionNames.Count,
                    ReviewStatus = "Pending", // TODO: Implement review status
                    ReviewDate = (DateTime?)null, // TODO: Implement review date
                    ReviewerId = (int?)null // TODO: Implement reviewer
                };

                return Ok(accessDetails);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting access details for user {UserId}", userId);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        /// <summary>
        /// Approve user access (mark as reviewed and approved)
        /// </summary>
        [HttpPost("users/{userId}/approve")]
        public async Task<ActionResult> ApproveUserAccess(int userId, [FromBody] AccessReviewApprovalDto approval)
        {
            try
            {
                var currentUser = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                _logger.LogInformation("User {CurrentUser} approving access for user {UserId}", currentUser, userId);

                var user = await _userRepository.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // TODO: Implement review tracking in database
                // For now, just log the approval
                _logger.LogInformation("✅ Access approved for user {UserId} by {Reviewer} - Notes: {Notes}",
                    userId, approval.ReviewerId, approval.Notes);

                return Ok(new { message = "Access approved successfully", userId, reviewDate = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving access for user {UserId}", userId);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        /// <summary>
        /// Revoke user access (mark for access removal)
        /// </summary>
        [HttpPost("users/{userId}/revoke")]
        public async Task<ActionResult> RevokeUserAccess(int userId, [FromBody] AccessReviewRevocationDto revocation)
        {
            try
            {
                var currentUser = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                _logger.LogInformation("User {CurrentUser} revoking access for user {UserId}", currentUser, userId);

                var user = await _userRepository.GetUserByIdAsync(userId);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                // TODO: Implement access revocation
                // For now, just log the revocation
                _logger.LogWarning("⚠️ Access revoked for user {UserId} by {Reviewer} - Reason: {Reason}",
                    userId, revocation.ReviewerId, revocation.Reason);

                return Ok(new { message = "Access revocation logged", userId, reviewDate = DateTime.UtcNow });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error revoking access for user {UserId}", userId);
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        /// <summary>
        /// Export access review report
        /// </summary>
        [HttpGet("export")]
        public async Task<ActionResult> ExportAccessReviewReport(
            [FromQuery] string format = "json",
            [FromQuery] bool includeInactive = false)
        {
            try
            {
                _logger.LogInformation("Exporting access review report - Format: {Format}, IncludeInactive: {IncludeInactive}",
                    format, includeInactive);

                var users = includeInactive
                    ? await _userRepository.GetAllUsersAsync()
                    : await _userRepository.GetActiveUsersAsync();

                var reportData = new List<object>();
                foreach (var user in users)
                {
                    // Get user roles - users have a single AssignedRole
                    var roles = new List<NickScanCentralImagingPortal.Core.Entities.Role>();
                    if (user.AssignedRole != null)
                    {
                        roles.Add(user.AssignedRole);
                    }
                    else if (user.RoleId.HasValue)
                    {
                        var role = await _roleService.GetRoleByIdAsync(user.RoleId.Value);
                        if (role != null)
                        {
                            roles.Add(role);
                        }
                    }

                    var permissions = await _permissionService.GetUserPermissionsAsync(user.Id);

                    reportData.Add(new
                    {
                        user.Id,
                        user.Username,
                        user.UserNumber,
                        user.Email,
                        user.FirstName,
                        user.LastName,
                        user.Department,
                        RoleName = user.Role.ToString(),
                        user.IsActive,
                        LastLogin = user.LastLoginAt,
                        DaysSinceLastLogin = user.LastLoginAt.HasValue
                            ? (DateTime.UtcNow - user.LastLoginAt.Value).Days
                            : (int?)null,
                        Roles = roles.Select(r => r.Name).ToList(),
                        PermissionCount = permissions.Count,
                        Permissions = permissions.ToList()
                    });
                }

                if (format.ToLower() == "csv")
                {
                    // TODO: Implement CSV export
                    return Ok(reportData);
                }

                return Ok(new
                {
                    ExportDate = DateTime.UtcNow,
                    TotalUsers = reportData.Count,
                    Users = reportData
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting access review report");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }
    }

    // DTOs for access review operations
    public class AccessReviewApprovalDto
    {
        public int ReviewerId { get; set; }
        public string? Notes { get; set; }
        public DateTime ReviewDate { get; set; } = DateTime.UtcNow;
    }

    public class AccessReviewRevocationDto
    {
        public int ReviewerId { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime ReviewDate { get; set; } = DateTime.UtcNow;
    }
}

