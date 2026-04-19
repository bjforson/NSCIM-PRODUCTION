using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Permissions;

namespace NickScanCentralImagingPortal.Services.Authentication
{
    /// <summary>
    /// JWT token generation and validation service
    /// </summary>
    public class JwtService : IJwtService
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<JwtService> _logger;
        private readonly ISettingsProvider _settingsProvider;
        private readonly IPermissionService _permissionService;
        private readonly string _secretKey;
        private readonly string _issuer;
        private readonly string _audience;
        private readonly int _expirationHours;

        public JwtService(
            IConfiguration configuration,
            ILogger<JwtService> logger,
            ISettingsProvider settingsProvider,
            IPermissionService permissionService)
        {
            _configuration = configuration;
            _logger = logger;
            _settingsProvider = settingsProvider;
            _permissionService = permissionService;

            // Load JWT secret from environment variable (most secure)
            _secretKey = Environment.GetEnvironmentVariable("NICKSCAN_JWT_SECRET_KEY")
                         ?? _configuration["Jwt:SecretKey"]
                         ?? throw new InvalidOperationException("JWT Secret Key not configured. Set NICKSCAN_JWT_SECRET_KEY environment variable.");

            // Read JWT configuration from database settings (synchronous call acceptable for startup initialization)
            // Note: These settings require API restart to apply (RequiresRestart=true in database)
            _issuer = _settingsProvider.GetStringAsync("Authentication", "JWT.Issuer", "NickScanCentralImagingPortal").GetAwaiter().GetResult();
            _audience = _settingsProvider.GetStringAsync("Authentication", "JWT.Audience", "NickScanPortalUsers").GetAwaiter().GetResult();
            _expirationHours = _settingsProvider.GetIntAsync("Authentication", "JWT.ExpirationHours", 8).GetAwaiter().GetResult();

            // Validate secret key length (minimum 256 bits / 32 bytes for HS256)
            if (_secretKey.Length < 32)
            {
                throw new InvalidOperationException("JWT Secret Key must be at least 32 characters for security. Current length: " + _secretKey.Length);
            }

            _logger.LogInformation("✅ JWT Service initialized (Issuer: {Issuer}, Expiration: {Hours}h) [from database settings]", _issuer, _expirationHours);
        }

        /// <summary>
        /// Generate JWT token for authenticated user
        /// </summary>
        public string GenerateToken(User user)
        {
            try
            {
                var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));
                var credentials = new SigningCredentials(securityKey, SecurityAlgorithms.HmacSha256);

                // Build claims for the token
                var claims = new List<Claim>
                {
                    new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
                    new Claim(ClaimTypes.Name, user.Username),
                    new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
                    new Claim("FullName", $"{user.FirstName} {user.LastName}".Trim()),
                    // NICKSCAN ERP Phase 1 — multi-tenancy claim. Defaults to tenant 1
                    // (Nick TC-Scan Operations) until per-tenant user provisioning is
                    // wired up in Phase 10. SuperAdmin gets the platform_admin claim
                    // so they can impersonate other tenants via X-NickERP-Tenant header.
                    new Claim("tenant_id", "1")
                };

                if (user.Username == "superadmin")
                {
                    claims.Add(new Claim("platform_admin", "true"));
                }

                // Add RoleId if available (nullable)
                if (user.RoleId.HasValue)
                {
                    claims.Add(new Claim("RoleId", user.RoleId.Value.ToString()));
                }

                // ✅ FIX: Use database Role.Name if available, otherwise fallback to enum
                // This ensures controllers checking for "Lead", "Analyst", "Audit" will work
                if (user.AssignedRole != null && !string.IsNullOrEmpty(user.AssignedRole.Name))
                {
                    // Use database role name (e.g., "Lead", "Analyst", "Audit", "CustomsOfficer")
                    claims.Add(new Claim(ClaimTypes.Role, user.AssignedRole.Name));
                    _logger.LogDebug("Added role claim from database: {RoleName}", user.AssignedRole.Name);
                }
                else
                {
                    // Fallback to enum value (for backward compatibility with users without RoleId)
                    claims.Add(new Claim(ClaimTypes.Role, user.Role.ToString()));
                    _logger.LogDebug("Added role claim from enum: {Role}", user.Role);
                }

                // Determine permission claims
                List<string> permissionClaims = new();
                try
                {
                    // ✅ FIX 2: Load permissions with timeout to prevent hanging on slow database queries
                    var permissionTask = _permissionService.GetUserPermissionsAsync(user.Id);
                    var timeoutTask = Task.Delay(TimeSpan.FromSeconds(5)); // 5 second timeout

                    var completedTask = Task.WhenAny(permissionTask, timeoutTask).GetAwaiter().GetResult();
                    if (completedTask == permissionTask)
                    {
                        permissionClaims = permissionTask.GetAwaiter().GetResult();
                        _logger.LogDebug(
                            "[JWT] User {Username} (Role: {RoleName}) assigned {PermissionCount} permissions",
                            user.Username,
                            user.AssignedRole?.Name ?? user.Role.ToString(),
                            permissionClaims.Count);

                        // Debug: Log first few permissions
                        var samplePermissions = permissionClaims.Take(5).ToList();
                        _logger.LogInformation(
                            "[JWT] Sample permissions for {Username}: {Permissions}",
                            user.Username,
                            string.Join(", ", samplePermissions));
                    }
                    else
                    {
                        // ✅ FIX 2: Timeout occurred - log critical warning but continue with empty permissions
                        _logger.LogWarning(
                            "[JWT] ⚠️ CRITICAL: Permission loading timed out for user {Username} ({UserId}) after 5 seconds - token generated WITHOUT permissions. User may experience permission issues.",
                            user.Username,
                            user.Id);
                        permissionClaims = new List<string>(); // Empty permissions list
                    }
                }
                catch (Exception permEx)
                {
                    // ✅ FIX 2: Log critical warning if permission loading fails
                    _logger.LogError(
                        permEx,
                        "[JWT] ⚠️ CRITICAL: Failed to load permissions for user {Username} ({UserId}) - token will be generated WITHOUT permissions. User may experience permission issues.",
                        user.Username,
                        user.Id);
                    // Continue with empty permissions list - don't fail token generation
                    permissionClaims = new List<string>();
                }

                foreach (var permission in permissionClaims)
                {
                    claims.Add(new Claim("Permission", permission));
                }

                // ✅ FIX 2: Log warning if token is generated without permissions
                if (permissionClaims.Count == 0)
                {
                    _logger.LogWarning(
                        "[JWT] ⚠️ WARNING: Token generated for {Username} with NO permissions ({PermissionCount}). User may experience permission issues until next login.",
                        user.Username,
                        permissionClaims.Count);
                }

                _logger.LogInformation(
                    "[JWT] Final token claims for {Username}: Role={Role}, Permissions={PermissionCount}",
                    user.Username,
                    user.AssignedRole?.Name ?? user.Role.ToString(),
                    permissionClaims.Count);

                var token = new JwtSecurityToken(
                    issuer: _issuer,
                    audience: _audience,
                    claims: claims,
                    expires: DateTime.UtcNow.AddHours(_expirationHours),
                    notBefore: DateTime.UtcNow,
                    signingCredentials: credentials
                );

                var tokenString = new JwtSecurityTokenHandler().WriteToken(token);

                _logger.LogInformation(
                    "🔑 Generated JWT token for user {Username} (ID: {UserId}), expires at {ExpiresAt}",
                    user.Username,
                    user.Id,
                    token.ValidTo);

                return tokenString;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error generating JWT token for user {Username}", user.Username);
                throw;
            }
        }
        /// <summary>
        /// Generate a cryptographically secure refresh token
        /// </summary>
        public string GenerateRefreshToken()
        {
            var randomNumber = new byte[64];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(randomNumber);
            var refreshToken = Convert.ToBase64String(randomNumber);

            _logger.LogDebug("Generated refresh token (length: {Length})", refreshToken.Length);

            return refreshToken;
        }

        /// <summary>
        /// Validate JWT token and extract claims
        /// </summary>
        public ClaimsPrincipal? ValidateToken(string token)
        {
            if (string.IsNullOrWhiteSpace(token))
            {
                _logger.LogWarning("⚠️ Token validation failed: Empty token");
                return null;
            }

            try
            {
                var tokenHandler = new JwtSecurityTokenHandler();
                var securityKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secretKey));

                var validationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = _issuer,
                    ValidAudience = _audience,
                    IssuerSigningKey = securityKey,
                    ClockSkew = TimeSpan.Zero // No tolerance for expired tokens
                };

                var principal = tokenHandler.ValidateToken(token, validationParameters, out var validatedToken);

                // Verify the token is JWT and uses HS256
                if (validatedToken is JwtSecurityToken jwtToken &&
                    jwtToken.Header.Alg.Equals(SecurityAlgorithms.HmacSha256, StringComparison.InvariantCultureIgnoreCase))
                {
                    var username = principal.Identity?.Name;
                    _logger.LogDebug("✅ Token validated for user: {Username}", username);
                    return principal;
                }

                _logger.LogWarning("⚠️ Token validation failed: Invalid algorithm or token type");
                return null;
            }
            catch (SecurityTokenExpiredException)
            {
                _logger.LogWarning("⚠️ Token validation failed: Token expired");
                return null;
            }
            catch (SecurityTokenInvalidSignatureException)
            {
                _logger.LogWarning("⚠️ Token validation failed: Invalid signature");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Token validation failed: {Message}", ex.Message);
                return null;
            }
        }

        /// <summary>
        /// Refresh a JWT token using a refresh token
        /// Note: This requires refresh token storage in database (not implemented yet)
        /// </summary>
        public async Task<string?> RefreshTokenAsync(string refreshToken)
        {
            // TODO: Implement refresh token logic with database storage
            // This would require:
            // 1. RefreshToken entity in database
            // 2. Link to User
            // 3. Expiration tracking
            // 4. Token rotation on use

            _logger.LogWarning("⚠️ Refresh token functionality not yet implemented");
            await Task.CompletedTask;
            return null;
        }
    }
}

