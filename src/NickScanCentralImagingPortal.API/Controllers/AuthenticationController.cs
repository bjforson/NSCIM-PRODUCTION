using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NickScanCentralImagingPortal.API.Swagger;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageAnalysis;
using NickScanCentralImagingPortal.Services.Permissions;
using Swashbuckle.AspNetCore.Annotations;
using Swashbuckle.AspNetCore.Filters;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Authentication and JWT Token Management
    /// </summary>
    /// <remarks>
    /// Provides endpoints for user authentication, token generation, and token validation.
    /// All endpoints support JWT Bearer authentication except login which is public.
    /// </remarks>
    [ApiController]
    [Route("api/[controller]")]
    [Route("api/auth")]
    [SwaggerTag("Authentication endpoints for login, logout, and token management")]
    public partial class AuthenticationController : ControllerBase
    {
        private readonly IUserRepository _userRepository;
        private readonly IJwtService _jwtService;
        private readonly ILogger<AuthenticationController> _logger;
        private readonly IPermissionService _permissionService;
        private readonly IConfiguration _configuration;
        private readonly Microsoft.Extensions.Caching.Memory.IMemoryCache? _sessionCache;

        public AuthenticationController(
            IUserRepository userRepository,
            IJwtService jwtService,
            ILogger<AuthenticationController> logger,
            IPermissionService permissionService,
            IConfiguration configuration,
            Microsoft.Extensions.Caching.Memory.IMemoryCache? sessionCache = null)
        {
            _userRepository = userRepository;
            _jwtService = jwtService;
            _logger = logger;
            _permissionService = permissionService;
            _configuration = configuration;
            _sessionCache = sessionCache;
        }

        /// <summary>
        /// Authenticate user and return JWT token
        /// </summary>
        /// <param name="request">Login credentials (username and password)</param>
        /// <returns>JWT access token, refresh token, and user information</returns>
        /// <remarks>
        /// **Authentication Flow:**
        /// 1. Submit username and password
        /// 2. Receive JWT access token (valid for 8 hours)
        /// 3. Receive refresh token (valid for 30 days)
        /// 4. Use access token in Authorization header for all API calls
        /// 
        /// **Rate Limiting:**
        /// - 5 login attempts per minute per IP
        /// - 20 login attempts per hour per IP
        /// 
        /// **Security Features:**
        /// - BCrypt password hashing with automatic migration from legacy SHA256
        /// - Account lockout after failed attempts (if configured)
        /// - Last login time tracking
        /// 
        /// **Example Request:**
        /// ```json
        /// {
        ///   "username": "admin",
        ///   "password": "Admin@123"
        /// }
        /// ```
        /// 
        /// **Example Response:**
        /// ```json
        /// {
        ///   "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
        ///   "refreshToken": "a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6",
        ///   "expiresIn": 28800,
        ///   "tokenType": "Bearer",
        ///   "user": {
        ///     "id": 1,
        ///     "username": "admin",
        ///     "email": "admin@nickscan.com",
        ///     "roleName": "Super Administrator"
        ///   }
        /// }
        /// ```
        /// </remarks>
        /// <response code="200">Successfully authenticated - returns JWT token and user info</response>
        /// <response code="400">Invalid request - missing username or password</response>
        /// <response code="401">Invalid credentials or inactive account</response>
        /// <response code="429">Too many login attempts - rate limit exceeded</response>
        [HttpPost("login")]
        [AllowAnonymous]
        [EnableRateLimiting("login")] // Strict rate limit: 5 attempts per minute
        [SwaggerOperation(
            Summary = "Login and get JWT token",
            Description = "Authenticates user credentials and returns a JWT access token plus refresh token",
            OperationId = "Login",
            Tags = new[] { "Authentication" }
        )]
        [SwaggerRequestExample(typeof(LoginRequest), typeof(LoginRequestExample))]
        [SwaggerResponseExample(200, typeof(LoginResponseExample))]
        [SwaggerResponseExample(400, typeof(ValidationErrorExample))]
        [SwaggerResponseExample(429, typeof(RateLimitExceededExample))]
        [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(object), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(object), StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(typeof(object), StatusCodes.Status429TooManyRequests)]
        public async Task<ActionResult<LoginResponse>> Login([FromBody] LoginRequest request)
        {
            try
            {
                // Validate input
                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    _logger.LogWarning("⚠️ Login attempt with empty username or password");
                    return BadRequest(new { error = "Username and password are required" });
                }

                _logger.LogInformation("🔐 Login attempt for user: {Username}", request.Username);

                // Validate credentials (with automatic BCrypt migration)
                var isValid = await _userRepository.ValidatePasswordAsync(request.Username, request.Password);
                if (!isValid)
                {
                    _logger.LogWarning("❌ Failed login attempt for user: {Username}", request.Username);
                    return Unauthorized(new { error = "Invalid username or password" });
                }

                // Get user details (with role information)
                var user = await _userRepository.GetUserByUsernameAsync(request.Username);
                if (user == null || !user.IsActive)
                {
                    _logger.LogWarning("❌ Login attempt for inactive or non-existent user: {Username}", request.Username);
                    return Unauthorized(new { error = "Account is inactive" });
                }

                var resolvedRoleName = user.AssignedRole?.Name ?? user.Role.ToString();
                var permissions = await _permissionService.GetUserPermissionsAsync(user.Id);

                _logger.LogInformation(
                    "📄 Permission snapshot for {Username} (Role: {Role}) => {PermissionCount} permissions. Sample: {SamplePermissions}",
                    user.Username,
                    resolvedRoleName,
                    permissions.Count,
                    string.Join(", ", permissions.Take(8)));

                if (resolvedRoleName.Equals("Analyst", StringComparison.OrdinalIgnoreCase) ||
                    resolvedRoleName.Equals("Admin", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogInformation(
                        "🔎 Analyst/Admin login diagnostic → User: {Username}, RoleId: {RoleId}, PermissionCount: {PermissionCount}",
                        user.Username,
                        user.RoleId,
                        permissions.Count);
                }

                // Single-session enforcement (2026-04-25): rotate the user's
                // CurrentSessionId BEFORE minting the token, so the new JWT carries
                // the new sid claim and any token issued to a previous device
                // becomes invalid the next time it's validated.
                var rotatedSid = await _userRepository.RotateSessionIdAsync(user.Id);
                if (rotatedSid.HasValue)
                {
                    user.CurrentSessionId = rotatedSid.Value;
                }
                // Invalidate the cached sid (if any) so the validator re-reads from DB
                // and doesn't briefly accept the old value during the cache TTL window.
                _sessionCache?.Remove($"sid:{user.Id}");

                // Generate JWT token
                var token = _jwtService.GenerateToken(user);
                var refreshToken = _jwtService.GenerateRefreshToken();

                // ✨ BLAZOR SERVER FIX: Also set authentication cookie
                // ✅ FIX: Use database Role.Name if available, otherwise use enum for backward compatibility
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                    new Claim(ClaimTypes.Role, resolvedRoleName),
                    // Single-session: cookie-auth carries the same sid claim so the
                    // OnValidatePrincipal hook can reject stale Blazor sessions too.
                    new Claim("sid", user.CurrentSessionId.ToString()),
                    new Claim("FullName", $"{user.FirstName} {user.LastName}".Trim())
                };

                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    claimsPrincipal,
                    new AuthenticationProperties
                    {
                        IsPersistent = true, // Persist across browser sessions
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
                        AllowRefresh = true
                    });

                // Update last login time
                await _userRepository.UpdateLastLoginAsync(user.Id);

                _logger.LogInformation("✅ Successful login for user: {Username} (ID: {UserId}) - Cookie + JWT issued",
                    user.Username, user.Id);

                return Ok(new LoginResponse
                {
                    Token = token,
                    RefreshToken = refreshToken,
                    ExpiresIn = _configuration.GetValue<int>("Jwt:ExpiresInSeconds", 28800),
                    TokenType = "Bearer",
                    User = new UserInfo
                    {
                        Id = user.Id,
                        Username = user.Username,
                        Email = user.Email ?? string.Empty,
                        FullName = $"{user.FirstName} {user.LastName}".Trim(),
                        RoleId = user.RoleId ?? 0,
                        RoleName = user.Role.ToString(),
                        IsActive = user.IsActive
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during login for user: {Username}", request.Username);
                return StatusCode(500, new { error = "An error occurred during login" });
            }
        }

        /// <summary>
        /// Refresh JWT token using refresh token
        /// </summary>
        /// <param name="request">Refresh token</param>
        /// <returns>New JWT token</returns>
        /// <response code="200">Returns new JWT token</response>
        /// <response code="400">Invalid request</response>
        /// <response code="401">Invalid refresh token</response>
        [HttpPost("refresh")]
        [AllowAnonymous]
        [EnableRateLimiting("login")]
        [ProducesResponseType(typeof(RefreshResponse), 200)]
        [ProducesResponseType(400)]
        [ProducesResponseType(401)]
        public async Task<ActionResult<RefreshResponse>> RefreshToken([FromBody] RefreshRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.RefreshToken))
                {
                    return BadRequest(new { error = "Refresh token is required" });
                }

                // M6: Refresh token storage is not yet implemented (see JwtService.RefreshTokenAsync
                // and DEFERRED_ACTIONS.md). Return 501 Not Implemented so clients can distinguish
                // "feature missing" from "your token is invalid" (which 401 would imply, leading to
                // confused log analysis and potentially incorrect client retry logic).
                var newToken = await _jwtService.RefreshTokenAsync(request.RefreshToken);
                if (newToken == null)
                {
                    _logger.LogWarning("⚠️ /auth/refresh called but refresh-token storage is not implemented; returning 501");
                    return StatusCode(501, new { error = "Refresh token endpoint is not yet implemented. Please re-authenticate via /auth/login." });
                }

                return Ok(new RefreshResponse
                {
                    Token = newToken,
                    ExpiresIn = _configuration.GetValue<int>("Jwt:ExpiresInSeconds", 28800),
                    TokenType = "Bearer"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error refreshing token");
                return StatusCode(500, new { error = "An error occurred during token refresh" });
            }
        }

        /// <summary>
        /// Validate current JWT token and return user info
        /// </summary>
        /// <returns>Token validation result and user info</returns>
        /// <response code="200">Token is valid</response>
        /// <response code="401">Token is invalid or expired</response>
        [HttpGet("validate")]
        [Authorize]
        [ProducesResponseType(typeof(ValidateResponse), 200)]
        [ProducesResponseType(401)]
        public ActionResult<ValidateResponse> ValidateToken()
        {
            var username = User.Identity?.Name;
            var userId = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            var role = User.FindFirst(ClaimTypes.Role)?.Value;
            var email = User.FindFirst(ClaimTypes.Email)?.Value;

            _logger.LogDebug("✅ Token validation successful for user: {Username}", username);

            return Ok(new ValidateResponse
            {
                IsValid = true,
                Username = username ?? string.Empty,
                UserId = int.TryParse(userId, out var id) ? id : 0,
                Role = role ?? string.Empty,
                Email = email ?? string.Empty
            });
        }

        /// <summary>
        /// Logout (client-side - invalidates token)
        /// </summary>
        /// <returns>Success message</returns>
        /// <response code="200">Logout successful</response>
        /// <response code="401">Not authenticated</response>
        [HttpPost("logout")]
        [Authorize]
        [ProducesResponseType(200)]
        [ProducesResponseType(401)]
        public async Task<ActionResult> Logout()
        {
            var username = User.Identity?.Name;
            _logger.LogInformation("👋 User logged out: {Username}", username);

            // ✅ FIX: Clear UserReadiness records on logout to prevent stale assignments
            // This ensures no assignments are created for users who have logged out
            try
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Clear all UserReadiness records for this user (all roles)
                var userReadinessRecords = await db.UserReadiness
                    .Where(r => r.Username == username)
                    .ToListAsync();

                if (userReadinessRecords.Any())
                {
                    foreach (var record in userReadinessRecords)
                    {
                        record.IsReady = false;
                        record.LastChangedAt = DateTime.UtcNow;
                        record.ChangedBy = username;
                        _logger.LogInformation("[LOGOUT] Marked user {Username} ({Role}) as not ready", record.Username, record.Role);
                    }

                    await db.SaveChangesAsync();
                    _logger.LogInformation("[LOGOUT] Cleared {Count} UserReadiness record(s) for user {Username}",
                        userReadinessRecords.Count, username);
                }

                // Also clear from SignalR state provider (in-memory)
                if (!string.IsNullOrEmpty(username))
                {
                    UserReadinessStateProvider.ClearUserReadiness(username);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[LOGOUT] Error clearing UserReadiness for user {Username}", username);
                // Don't fail logout if clearing readiness fails - still proceed with logout
            }

            // ✨ BLAZOR SERVER FIX: Clear authentication cookie
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

            // Note: With JWT, logout is typically handled client-side by deleting the token
            // Server-side token blacklisting would require a token revocation list (future enhancement)

            return Ok(new { message = "Logged out successfully. Cookie cleared and UserReadiness cleared." });
        }

        /// <summary>
        /// Get current authenticated user information
        /// </summary>
        /// <returns>Current user details</returns>
        /// <response code="200">Returns current user</response>
        /// <response code="401">Not authenticated</response>
        [HttpGet("me")]
        [Authorize]
        [ProducesResponseType(typeof(UserInfo), 200)]
        [ProducesResponseType(401)]
        public async Task<ActionResult<UserInfo>> GetCurrentUser()
        {
            try
            {
                var username = User.Identity?.Name;
                if (string.IsNullOrEmpty(username))
                {
                    return Unauthorized(new { error = "User identity not found" });
                }

                var user = await _userRepository.GetUserByUsernameAsync(username);
                if (user == null)
                {
                    return NotFound(new { error = "User not found" });
                }

                return Ok(new UserInfo
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email ?? string.Empty,
                    FullName = $"{user.FirstName} {user.LastName}".Trim(),
                    RoleId = user.RoleId ?? 0,
                    RoleName = user.Role.ToString(),
                    IsActive = user.IsActive
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting current user info");
                return StatusCode(500, new { error = "An error occurred" });
            }
        }

        /// <summary>
        /// Service-to-service credential validation (for NickHR central auth)
        /// </summary>
        [HttpPost("validate-credentials")]
        [AllowAnonymous]
        [EnableRateLimiting("login")]
        public async Task<IActionResult> ValidateCredentials([FromBody] ValidateCredentialsRequest request)
        {
            try
            {
                var providedServiceKey = ServiceApiKeyValidator.GetProvidedKey(Request, request.ServiceApiKey);
                if (!ServiceApiKeyValidator.IsValid(_configuration, providedServiceKey))
                {
                    _logger.LogWarning("🔒 validate-credentials: Invalid service API key from {IP}",
                        HttpContext.Connection.RemoteIpAddress);
                    return Unauthorized(new ValidateCredentialsResponse { IsValid = false });
                }

                if (string.IsNullOrWhiteSpace(request.Username) || string.IsNullOrWhiteSpace(request.Password))
                {
                    return Ok(new ValidateCredentialsResponse { IsValid = false });
                }

                // Accept either username or email as the identifier
                // First, resolve to a username (if an email was provided)
                var identifier = request.Username.Trim();
                string? actualUsername = identifier;

                if (identifier.Contains('@'))
                {
                    // Email provided — look up the user to get the username
                    var userByEmail = await _userRepository.GetUserByEmailAsync(identifier);
                    if (userByEmail == null)
                    {
                        _logger.LogInformation("🔒 validate-credentials: No user for email {Email}", identifier);
                        return Ok(new ValidateCredentialsResponse { IsValid = false });
                    }
                    actualUsername = userByEmail.Username;
                }

                // Use the same password validation as the login endpoint
                var isValid = await _userRepository.ValidatePasswordAsync(actualUsername, request.Password);

                if (!isValid)
                {
                    _logger.LogInformation("🔒 validate-credentials: Failed for {Identifier}", identifier);
                    return Ok(new ValidateCredentialsResponse { IsValid = false });
                }

                // Get user details
                var user = await _userRepository.GetUserByUsernameAsync(actualUsername);
                if (user == null || !user.IsActive)
                {
                    return Ok(new ValidateCredentialsResponse { IsValid = false });
                }

                _logger.LogInformation("✅ validate-credentials: Success for user {Username} from service", user.Username);

                return Ok(new ValidateCredentialsResponse
                {
                    IsValid = true,
                    Username = user.Username,
                    Email = user.Email ?? string.Empty,
                    FullName = $"{user.FirstName} {user.LastName}".Trim(),
                    NscisUserId = user.Id
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error in validate-credentials for user {Username}", request.Username);
                return StatusCode(500, new ValidateCredentialsResponse { IsValid = false });
            }
        }
    }

    #region Request/Response Models

    /// <summary>
    /// Service-to-service credential validation request
    /// </summary>
    public class ValidateCredentialsRequest
    {
        [Required]
        public string Username { get; set; } = string.Empty;
        [Required]
        public string Password { get; set; } = string.Empty;
        [Required]
        public string ServiceApiKey { get; set; } = string.Empty;
    }

    /// <summary>
    /// Service-to-service credential validation response
    /// </summary>
    public class ValidateCredentialsResponse
    {
        public bool IsValid { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int NscisUserId { get; set; }
    }

    /// <summary>
    /// Login request model
    /// </summary>
    public class LoginRequest
    {
        /// <summary>
        /// Username
        /// </summary>
        [Required(ErrorMessage = "Username is required")]
        public string Username { get; set; } = string.Empty;

        /// <summary>
        /// Password
        /// </summary>
        [Required(ErrorMessage = "Password is required")]
        public string Password { get; set; } = string.Empty;
    }

    /// <summary>
    /// Login response with JWT token
    /// </summary>
    public class LoginResponse
    {
        /// <summary>
        /// JWT access token
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Refresh token for renewing access token
        /// </summary>
        public string RefreshToken { get; set; } = string.Empty;

        /// <summary>
        /// Token expiration in seconds
        /// </summary>
        public int ExpiresIn { get; set; }

        /// <summary>
        /// Token type (always "Bearer")
        /// </summary>
        public string TokenType { get; set; } = "Bearer";

        /// <summary>
        /// Authenticated user information
        /// </summary>
        public UserInfo User { get; set; } = new();
    }

    /// <summary>
    /// User information model
    /// </summary>
    public class UserInfo
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public int RoleId { get; set; }
        public string RoleName { get; set; } = string.Empty;
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Refresh token request
    /// </summary>
    public class RefreshRequest
    {
        /// <summary>
        /// Refresh token
        /// </summary>
        [Required]
        public string RefreshToken { get; set; } = string.Empty;
    }

    /// <summary>
    /// Refresh token response
    /// </summary>
    public class RefreshResponse
    {
        /// <summary>
        /// New JWT access token
        /// </summary>
        public string Token { get; set; } = string.Empty;

        /// <summary>
        /// Token expiration in seconds
        /// </summary>
        public int ExpiresIn { get; set; }

        /// <summary>
        /// Token type (always "Bearer")
        /// </summary>
        public string TokenType { get; set; } = "Bearer";
    }

    /// <summary>
    /// Token validation response
    /// </summary>
    public class ValidateResponse
    {
        public bool IsValid { get; set; }
        public string Username { get; set; } = string.Empty;
        public int UserId { get; set; }
        public string Role { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
    }

    #endregion

    internal static class ServiceApiKeyValidator
    {
        public const string HeaderName = "X-Service-Key";

        public static string? GetProvidedKey(
            Microsoft.AspNetCore.Http.HttpRequest request,
            string? bodyKey = null,
            bool allowQueryStringFallback = false)
        {
            if (request.Headers.TryGetValue(HeaderName, out var headerValues))
            {
                var headerValue = headerValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(headerValue))
                {
                    return headerValue;
                }
            }

            if (!string.IsNullOrWhiteSpace(bodyKey))
            {
                return bodyKey;
            }

            if (allowQueryStringFallback && request.Query.TryGetValue("apiKey", out var queryValues))
            {
                var queryValue = queryValues.FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(queryValue))
                {
                    return queryValue;
                }
            }

            return null;
        }

        public static bool IsValid(IConfiguration configuration, string? providedKey)
        {
            var expectedKey = Environment.GetEnvironmentVariable("NICKSCAN_SERVICE_API_KEY")
                ?? configuration["ServiceAuth:ApiKey"];

            return FixedTimeEquals(expectedKey, providedKey);
        }

        private static bool FixedTimeEquals(string? expectedKey, string? providedKey)
        {
            if (string.IsNullOrEmpty(expectedKey) || string.IsNullOrEmpty(providedKey))
            {
                return false;
            }

            var expectedHash = SHA256.HashData(Encoding.UTF8.GetBytes(expectedKey));
            var providedHash = SHA256.HashData(Encoding.UTF8.GetBytes(providedKey));

            return CryptographicOperations.FixedTimeEquals(expectedHash, providedHash);
        }
    }
}

