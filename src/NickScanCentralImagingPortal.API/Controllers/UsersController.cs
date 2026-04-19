using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Permissions;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// User management controller - requires authentication
    /// </summary>
    [Authorize] // All endpoints require authentication
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IRoleService _roleService;
        private readonly ILogger<UsersController> _logger;
        private readonly IConfiguration _configuration;

        public UsersController(
            IUserRepository userRepository,
            IRoleService roleService,
            ILogger<UsersController> logger,
            IConfiguration configuration)
        {
            _userRepository = userRepository;
            _roleService = roleService;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Validates the service API key used by other services (e.g., NickHR) for S2S calls.
        /// </summary>
        private bool IsValidServiceApiKey(string? providedKey)
        {
            var expected = Environment.GetEnvironmentVariable("NICKSCAN_SERVICE_API_KEY")
                ?? _configuration["ServiceAuth:ApiKey"]
                ?? string.Empty;
            return !string.IsNullOrEmpty(expected) && providedKey == expected;
        }

        [HttpGet]
        public async Task<ActionResult<List<object>>> GetAllUsers()
        {
            try
            {
                var users = await _userRepository.GetAllUsersAsync();

                // Map to DTO to avoid circular reference issues with navigation properties
                var userDtos = users.Select(u => new
                {
                    u.Id,
                    UserId = u.Username, // Map Username to UserId for frontend compatibility
                    u.Username,
                    u.UserNumber, // ✅ Include UserNumber for anonymized reporting
                    u.Email,
                    u.FirstName,
                    u.LastName,
                    u.Department,
                    u.PhoneNumber,
                    RoleName = u.AssignedRole?.Name ?? u.Role.ToString(),
                    RoleDisplayName = u.AssignedRole?.DisplayName ?? u.Role.ToString(),
                    u.RoleId, // ✅ Include RoleId for new role system
                    u.IsActive,
                    LastLogin = u.LastLoginAt,
                    u.CreatedAt,
                    u.CreatedBy,
                    u.UpdatedAt,
                    u.UpdatedBy
                }).ToList();

                return Ok(userDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching all users");
                return StatusCode(500, new { error = "Internal server error", message = ex.Message });
            }
        }

        [HttpGet("active")]
        public async Task<ActionResult<List<User>>> GetActiveUsers()
        {
            try
            {
                var users = await _userRepository.GetActiveUsersAsync();
                return Ok(users);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching active users");
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<User>> GetUserById(int id)
        {
            try
            {
                var user = await _userRepository.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound();
                }
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user {UserId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("username/{username}")]
        public async Task<ActionResult<User>> GetUserByUsername(string username)
        {
            try
            {
                var user = await _userRepository.GetUserByUsernameAsync(username);
                if (user == null)
                {
                    return NotFound();
                }
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user {Username}", username);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("email/{email}")]
        public async Task<ActionResult<User>> GetUserByEmail(string email)
        {
            try
            {
                var user = await _userRepository.GetUserByEmailAsync(email);
                if (user == null)
                {
                    return NotFound();
                }
                return Ok(user);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching user {Email}", email);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Create new user (Admin only)
        /// </summary>
        [HttpPost]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<User>> CreateUser([FromBody] CreateUserRequest request)
        {
            try
            {
                // Validate request
                if (string.IsNullOrEmpty(request.Username) || string.IsNullOrEmpty(request.Email))
                {
                    return BadRequest("Username and email are required");
                }

                // Check if username already exists
                if (await _userRepository.UsernameExistsAsync(request.Username))
                {
                    return Conflict("Username already exists");
                }

                // Check if email already exists
                if (await _userRepository.EmailExistsAsync(request.Email))
                {
                    return Conflict("Email already exists");
                }
                var user = await _userRepository.CreateUserAsync(request);
                return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, user);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error creating user {Username}", request.Username);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating user {Username}", request.Username);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Update user (Admin or self)
        /// </summary>
        [HttpPut("{id}")]
        public async Task<ActionResult<User>> UpdateUser(int id, [FromBody] UpdateUserRequest request)
        {
            try
            {
                _logger.LogInformation("🎯 UsersController.UpdateUser called for user ID: {UserId}", id);
                _logger.LogInformation("   Request Email: {Email}", request.Email);
                _logger.LogInformation("   Request Role: {Role}", request.Role ?? "NULL");
                _logger.LogInformation("   Request IsActive: {IsActive}", request.IsActive);

                // Validate request
                if (string.IsNullOrEmpty(request.Email))
                {
                    _logger.LogWarning("   ❌ Email is empty - returning BadRequest");
                    return BadRequest("Email is required");
                }

                // Check if email already exists (excluding current user)
                var existingUser = await _userRepository.GetUserByIdAsync(id);
                if (existingUser == null)
                {
                    _logger.LogWarning("   ❌ User {UserId} not found", id);
                    return NotFound();
                }

                _logger.LogInformation("   Found existing user: {Username}, Current Role: {CurrentRole}", existingUser.Username, existingUser.Role);

                if (existingUser.Email != request.Email && await _userRepository.EmailExistsAsync(request.Email))
                {
                    _logger.LogWarning("   ❌ Email conflict detected");
                    return Conflict("Email already exists");
                }

                _logger.LogInformation("   ✅ Validation passed - calling repository UpdateUserAsync");
                var user = await _userRepository.UpdateUserAsync(id, request);
                _logger.LogInformation("   ✅ Repository returned updated user with Role: {Role}", user.Role);
                return Ok(user);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Validation error updating user {UserId}", id);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ EXCEPTION in UpdateUser for user {UserId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Delete user (Admin only)
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult> DeleteUser(int id)
        {
            try
            {
                var success = await _userRepository.DeleteUserAsync(id);
                if (!success)
                {
                    return NotFound();
                }
                return NoContent();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user {UserId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("validate-password")]
        public async Task<ActionResult<bool>> ValidatePassword([FromBody] ValidatePasswordRequest request)
        {
            try
            {
                var isValid = await _userRepository.ValidatePasswordAsync(request.Username, request.Password);
                return Ok(isValid);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating password for user {Username}", request.Username);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpPost("{id}/update-last-login")]
        public async Task<ActionResult> UpdateLastLogin(int id)
        {
            try
            {
                await _userRepository.UpdateLastLoginAsync(id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating last login for user {UserId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("username-exists/{username}")]
        public async Task<ActionResult<bool>> UsernameExists(string username)
        {
            try
            {
                var exists = await _userRepository.UsernameExistsAsync(username);
                return Ok(exists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking username existence {Username}", username);
                return StatusCode(500, "Internal server error");
            }
        }

        [HttpGet("email-exists/{email}")]
        public async Task<ActionResult<bool>> EmailExists(string email)
        {
            try
            {
                var exists = await _userRepository.EmailExistsAsync(email);
                return Ok(exists);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking email existence {Email}", email);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Reset user password to a temporary password (Admin only)
        /// </summary>
        [HttpPost("{id}/reset-password")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult<object>> ResetPassword(int id)
        {
            try
            {
                var user = await _userRepository.GetUserByIdAsync(id);
                if (user == null)
                {
                    return NotFound($"User with ID {id} not found");
                }

                // Generate a temporary password
                var tempPassword = GenerateTemporaryPassword();

                // Update the user's password
                await _userRepository.UpdatePasswordAsync(id, tempPassword);

                _logger.LogInformation("✅ Password reset for user {Username} (ID: {UserId})", user.Username, id);

                // In a real system, you would send an email here
                // For now, return the temporary password in the response
                return Ok(new
                {
                    message = $"Password reset successfully for {user.Username}",
                    temporaryPassword = tempPassword,
                    warning = "User should change this password immediately after logging in"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting password for user {UserId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Assign a role to a user (Admin only)
        /// </summary>
        /// <param name="id">User ID</param>
        /// <param name="request">Role assignment request</param>
        /// <returns>Success message</returns>
        [HttpPut("{id}/role")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult> AssignRole(int id, [FromBody] AssignRoleRequest request)
        {
            try
            {
                // Validate user exists
                var user = await _userRepository.GetUserByIdAsync(id);
                if (user == null)
                {
                    _logger.LogWarning("Attempted to assign role to non-existent user {UserId}", id);
                    return NotFound($"User with ID {id} not found");
                }

                // Validate role exists
                var role = await _roleService.GetRoleByIdAsync(request.RoleId);
                if (role == null)
                {
                    _logger.LogWarning("Attempted to assign non-existent role {RoleId} to user {UserId}", request.RoleId, id);
                    return NotFound($"Role with ID {request.RoleId} not found");
                }

                // Assign role
                var assignedBy = User.Identity?.Name ?? "System";
                await _roleService.AssignRoleToUserAsync(id, request.RoleId, assignedBy);

                _logger.LogInformation("✅ Role {RoleName} (ID: {RoleId}) assigned to user {Username} (ID: {UserId}) by {AssignedBy}",
                    role.Name, request.RoleId, user.Username, id, assignedBy);

                return Ok(new
                {
                    message = $"Role '{role.DisplayName}' assigned successfully to user '{user.Username}'",
                    userId = id,
                    roleId = request.RoleId,
                    roleName = role.Name
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid argument when assigning role to user {UserId}", id);
                return BadRequest(ex.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assigning role to user {UserId}", id);
                return StatusCode(500, "Error assigning role");
            }
        }

        /// <summary>
        /// Generate a secure temporary password
        /// </summary>
        private string GenerateTemporaryPassword()
        {
            const string validChars = "abcdefghjkmnpqrstuvwxyzABCDEFGHJKMNPQRSTUVWXYZ23456789!@#$%";
            var random = new Random();
            var chars = new char[12];

            for (int i = 0; i < 12; i++)
            {
                chars[i] = validChars[random.Next(validChars.Length)];
            }

            return new string(chars);
        }

        // ===== SERVICE-TO-SERVICE ENDPOINTS (Central Provisioning) =====
        // These endpoints allow other services (e.g., NickHR) to provision users
        // without needing an Admin JWT. Protected by NICKSCAN_SERVICE_API_KEY.

        /// <summary>
        /// Service-to-service: Provision (create or update) a user.
        /// Idempotent by username — updates existing user if it exists.
        /// </summary>
        [HttpPost("service/provision")]
        [AllowAnonymous]
        public async Task<IActionResult> ServiceProvisionUser([FromBody] ServiceProvisionRequest request)
        {
            if (!IsValidServiceApiKey(request.ServiceApiKey))
            {
                _logger.LogWarning("🔒 service/provision: Invalid service API key from {IP}",
                    HttpContext.Connection.RemoteIpAddress);
                return Unauthorized(new { error = "Invalid service API key" });
            }

            try
            {
                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Email))
                {
                    return BadRequest(new { error = "Username and email are required" });
                }

                var existing = await _userRepository.GetUserByUsernameAsync(request.Username);

                if (existing != null)
                {
                    // Update existing user (idempotent)
                    var updateReq = new UpdateUserRequest
                    {
                        Email = request.Email,
                        FirstName = request.FirstName,
                        LastName = request.LastName,
                        RoleId = request.RoleId,
                        IsActive = request.IsActive,
                        UpdatedBy = "NickHR-Service"
                    };
                    var updated = await _userRepository.UpdateUserAsync(existing.Id, updateReq);
                    _logger.LogInformation("✅ service/provision: Updated user {Username} (ID {Id})", request.Username, updated.Id);
                    return Ok(new ServiceProvisionResponse
                    {
                        Success = true,
                        NscisUserId = updated.Id,
                        Username = updated.Username,
                        Email = updated.Email,
                        IsActive = updated.IsActive,
                        Created = false
                    });
                }

                // Create new user with random password (user will reset via NSCIS admin flow)
                var randomPassword = GenerateTemporaryPassword();
                var createReq = new CreateUserRequest
                {
                    Username = request.Username,
                    Email = request.Email,
                    FirstName = request.FirstName,
                    LastName = request.LastName,
                    Password = randomPassword,
                    RoleId = request.RoleId,
                    Role = request.RoleId.HasValue ? "" : "Viewer", // fallback to Viewer for legacy field
                    IsActive = request.IsActive,
                    CreatedBy = "NickHR-Service"
                };

                var created = await _userRepository.CreateUserAsync(createReq);
                _logger.LogInformation("✅ service/provision: Created user {Username} (ID {Id})", request.Username, created.Id);

                return Ok(new ServiceProvisionResponse
                {
                    Success = true,
                    NscisUserId = created.Id,
                    Username = created.Username,
                    Email = created.Email,
                    IsActive = created.IsActive,
                    Created = true,
                    TemporaryPassword = randomPassword // Returned so NickHR can show it or email it
                });
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "service/provision: Validation error for {Username}", request.Username);
                return BadRequest(new { error = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "service/provision: Error provisioning {Username}", request.Username);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }

        /// <summary>
        /// Service-to-service: Deactivate a user (soft delete via IsActive=false).
        /// </summary>
        [HttpPut("service/{username}/deactivate")]
        [AllowAnonymous]
        public async Task<IActionResult> ServiceDeactivateUser(string username, [FromBody] ServiceApiKeyRequest request)
        {
            if (!IsValidServiceApiKey(request.ServiceApiKey))
            {
                return Unauthorized(new { error = "Invalid service API key" });
            }

            try
            {
                var user = await _userRepository.GetUserByUsernameAsync(username);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                await _userRepository.UpdateUserAsync(user.Id, new UpdateUserRequest
                {
                    Email = user.Email,
                    IsActive = false,
                    UpdatedBy = "NickHR-Service"
                });

                _logger.LogInformation("✅ service/deactivate: Deactivated user {Username}", username);
                return Ok(new { success = true, username });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "service/deactivate: Error for {Username}", username);
                return StatusCode(500, new { error = "Internal server error" });
            }
        }
    }

    public class ServiceProvisionRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public int? RoleId { get; set; }
        public bool IsActive { get; set; } = true;
        public string ServiceApiKey { get; set; } = string.Empty;
    }

    public class ServiceProvisionResponse
    {
        public bool Success { get; set; }
        public int NscisUserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public bool Created { get; set; }
        public string? TemporaryPassword { get; set; }
    }

    public class ServiceApiKeyRequest
    {
        public string ServiceApiKey { get; set; } = string.Empty;
    }

    public class ValidatePasswordRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class AssignRoleRequest
    {
        /// <summary>
        /// Role ID to assign to the user
        /// </summary>
        public int RoleId { get; set; }
    }
}
