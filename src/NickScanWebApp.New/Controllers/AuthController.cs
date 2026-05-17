using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanWebApp.Shared.Services;

namespace NickScanWebApp.New.Controllers
{
    /// <summary>
    /// Server-side authentication controller for Blazor Server
    /// Sets authentication cookies after validating credentials with the API
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    public class AuthController : ControllerBase
    {
        private readonly AuthenticationClient _authenticationClient;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            AuthenticationClient authenticationClient,
            ILogger<AuthController> logger)
        {
            _authenticationClient = authenticationClient;
            _logger = logger;
        }

        /// <summary>
        /// Server-side login endpoint that validates credentials with API and sets authentication cookie
        /// </summary>
        [HttpPost("login")]
        [AllowAnonymous]
        public async Task<ActionResult> Login([FromBody] LoginRequest request)
        {
            try
            {
                _logger.LogInformation("🔐 Server-side login attempt for user: {Username}", request.Username);

                // Call backend API to validate credentials
                var loginData = new { Username = request.Username, Password = request.Password };
                var response = await _authenticationClient.LoginAsync<object, ApiLoginResponse>(loginData);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("❌ API login failed for user: {Username}", request.Username);
                    return Unauthorized(new { error = "Invalid credentials" });
                }

                var apiResponse = response.Payload;
                if (apiResponse == null || string.IsNullOrEmpty(apiResponse.Token))
                {
                    _logger.LogWarning("❌ Invalid API response for user: {Username}", request.Username);
                    return Unauthorized(new { error = "Invalid response from authentication service" });
                }

                // The WebApp auth model is JWT session storage, not server cookies.
                // Keep this local endpoint as a compatibility/BFF login proxy without
                // introducing a second authentication scheme.
                _logger.LogInformation("✅ Server-side login proxy successful for user: {Username} - JWT returned", request.Username);

                return Ok(new
                {
                    success = true,
                    token = apiResponse.Token, // Also return JWT for API calls
                    user = apiResponse.User
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during server-side login for user: {Username}", request.Username);
                return StatusCode(500, new { error = "An error occurred during login" });
            }
        }

        /// <summary>
        /// Server-side logout compatibility endpoint for JWT-only auth.
        /// </summary>
        [HttpPost("logout")]
        [AllowAnonymous]
        public ActionResult Logout()
        {
            var username = User.Identity?.Name;
            _logger.LogInformation("👋 Server-side logout compatibility endpoint called for user: {Username}", username ?? "anonymous");

            return Ok(new { success = true, message = "Logged out successfully" });
        }
    }

    public class LoginRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool RememberMe { get; set; } = false;
    }

    public class ApiLoginResponse
    {
        public string Token { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public int ExpiresIn { get; set; }
        public string TokenType { get; set; } = string.Empty;
        public UserInfo? User { get; set; }
    }

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
}

