using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

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
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<AuthController> _logger;

        public AuthController(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<AuthController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
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

                // Create HTTP client to call backend API
                var client = _httpClientFactory.CreateClient();
                client.BaseAddress = new Uri(_configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205");

                // Call backend API to validate credentials
                var loginData = new { Username = request.Username, Password = request.Password };
                var response = await client.PostAsJsonAsync("/api/Authentication/login", loginData);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("❌ API login failed for user: {Username}", request.Username);
                    return Unauthorized(new { error = "Invalid credentials" });
                }

                var apiResponse = await response.Content.ReadFromJsonAsync<ApiLoginResponse>();
                if (apiResponse == null || string.IsNullOrEmpty(apiResponse.Token))
                {
                    _logger.LogWarning("❌ Invalid API response for user: {Username}", request.Username);
                    return Unauthorized(new { error = "Invalid response from authentication service" });
                }

                // Parse JWT token to extract claims
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(apiResponse.Token);

                // Create claims from JWT
                var claims = new List<Claim>();
                foreach (var claim in jwtToken.Claims)
                {
                    claims.Add(new Claim(claim.Type, claim.Value));
                }

                // Ensure username claim is present
                if (!claims.Any(c => c.Type == ClaimTypes.Name))
                {
                    claims.Add(new Claim(ClaimTypes.Name, request.Username));
                }

                // Create authentication cookie
                var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
                var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

                await HttpContext.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    claimsPrincipal,
                    new AuthenticationProperties
                    {
                        IsPersistent = request.RememberMe, // Persist across browser sessions if "Remember Me"
                        ExpiresUtc = DateTimeOffset.UtcNow.AddHours(8),
                        AllowRefresh = true
                    });

                _logger.LogInformation("✅ Server-side login successful for user: {Username} - Cookie set", request.Username);

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
        /// Server-side logout endpoint that clears authentication cookie
        /// </summary>
        [HttpPost("logout")]
        [Authorize]
        public async Task<ActionResult> Logout()
        {
            var username = User.Identity?.Name;
            _logger.LogInformation("👋 Server-side logout for user: {Username}", username);

            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);

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

