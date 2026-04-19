using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Service for storing and retrieving JWT tokens
    /// Uses both static storage AND browser session storage for persistence across page refreshes
    /// </summary>
    public class TokenStorageService
    {
        // Static storage to persist across all circuit instances
        private static readonly ConcurrentDictionary<string, TokenInfo> _tokens = new();
        private static string? _defaultCircuitToken; // Fallback for single-user scenarios

        private readonly ILogger<TokenStorageService> _logger;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ProtectedSessionStorage _sessionStorage;
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly string _circuitId;
        private bool _hasAttemptedRestore = false;

        private const string TOKEN_STORAGE_KEY = "nickscan_jwt_token";
        private const string TOKEN_EXPIRY_KEY = "nickscan_jwt_expiry";

        public TokenStorageService(
            ILogger<TokenStorageService> logger,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ProtectedSessionStorage sessionStorage,
            AuthenticationStateProvider authStateProvider)
        {
            _logger = logger;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _sessionStorage = sessionStorage;
            _authStateProvider = authStateProvider;
            _circuitId = Guid.NewGuid().ToString(); // Unique ID for this circuit

            _logger.LogInformation("🆔 TokenStorageService created for circuit {CircuitId}", _circuitId);

            // ✨ FIXED: Delay restoration to avoid prerendering issues
            // Browser storage can only be accessed after the first render
            _ = Task.Run(async () =>
            {
                await Task.Delay(100); // Small delay to ensure browser is ready
                await RestoreTokenFromBrowserAsync();
            });
        }

        private async Task InitializeDefaultTokenAsync()
        {
            try
            {
                var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";
                if (!string.Equals(env, "Development", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                var autoLogin = _configuration.GetValue<bool>("Development:AutoLogin", false);
                if (!autoLogin)
                {
                    _logger.LogInformation("Auto-login disabled");
                    return;
                }

                var defaultUsername = _configuration["Development:DefaultUsername"] ?? "";
                var defaultPassword = _configuration["Development:DefaultPassword"] ?? "";
                if (string.IsNullOrEmpty(defaultUsername) || string.IsNullOrEmpty(defaultPassword))
                {
                    _logger.LogWarning("Auto-login credentials not configured");
                    return;
                }

                _logger.LogInformation("🔧 Development Mode: Attempting auto-login with default credentials...");

                // Create a temporary HTTP client without the authenticated handler
                var client = new HttpClient
                {
                    BaseAddress = new Uri(_configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205"),
                    Timeout = TimeSpan.FromSeconds(10)
                };

                var loginRequest = new
                {
                    username = defaultUsername,
                    password = defaultPassword
                };

                var response = await client.PostAsJsonAsync("/api/Authentication/login", loginRequest);

                if (response.IsSuccessStatusCode)
                {
                    var result = await response.Content.ReadFromJsonAsync<LoginResponse>();
                    if (result != null && !string.IsNullOrEmpty(result.Token))
                    {
                        SetToken(result.Token, DateTime.UtcNow.AddHours(8));
                        _logger.LogInformation("✅ Auto-login successful! Token obtained for user: {Username}", defaultUsername);
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ Auto-login failed: {StatusCode}. API calls may fail without authentication.", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error during auto-login. API calls may fail without authentication.");
            }
        }

        public void SetToken(string token, DateTime? expiration = null)
        {
            var expiryTime = expiration ?? DateTime.UtcNow.AddHours(8);
            var tokenInfo = new TokenInfo
            {
                Token = token,
                Expiration = expiryTime,
                CircuitId = _circuitId
            };

            // Store in static dictionary AND as default
            _tokens[_circuitId] = tokenInfo;
            _defaultCircuitToken = token; // Fallback for cross-circuit access

            // ✨ NEW: Persist to browser session storage for page refresh survival
            _ = Task.Run(async () =>
            {
                try
                {
                    await _sessionStorage.SetAsync(TOKEN_STORAGE_KEY, token);
                    await _sessionStorage.SetAsync(TOKEN_EXPIRY_KEY, expiryTime.ToString("O"));
                    _logger.LogInformation("💾 Token saved to browser session storage");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Could not save token to browser storage (not critical)");
                }
            });

            _logger.LogInformation("✅ TOKEN STORED. Circuit: {CircuitId}, Instance: {Instance}, Token length: {Length}",
                _circuitId, GetHashCode(), token?.Length);
        }

        public string? GetToken()
        {
            _logger.LogInformation("🔍 GET TOKEN. Circuit: {CircuitId}, Instance: {Instance}",
                _circuitId, GetHashCode());

            // Try to get token for this circuit
            if (_tokens.TryGetValue(_circuitId, out var tokenInfo))
            {
                // Check if expired
                if (DateTime.UtcNow > tokenInfo.Expiration)
                {
                    _logger.LogWarning("Token expired for circuit {CircuitId}", _circuitId);
                    _tokens.TryRemove(_circuitId, out _);
                    return null;
                }

                _logger.LogInformation("✅ Token found for circuit {CircuitId}", _circuitId);
                return tokenInfo.Token;
            }

            // Fallback to default token (for cross-circuit scenarios)
            if (!string.IsNullOrEmpty(_defaultCircuitToken))
            {
                _logger.LogInformation("✅ Using default circuit token");
                return _defaultCircuitToken;
            }

            // ✨ FIXED: If no token found and restoration was skipped (prerendering), retry now
            if (!_hasAttemptedRestore)
            {
                _logger.LogInformation("🔄 Retrying token restoration (was skipped during prerendering)");
                _ = RestoreTokenFromBrowserAsync();

                // Check again after restoration attempt
                if (_tokens.TryGetValue(_circuitId, out tokenInfo))
                {
                    return tokenInfo.Token;
                }
                if (!string.IsNullOrEmpty(_defaultCircuitToken))
                {
                    return _defaultCircuitToken;
                }
            }

            _logger.LogWarning("❌ No token found for circuit {CircuitId}", _circuitId);
            return null;
        }

        public bool HasValidToken()
        {
            return !string.IsNullOrEmpty(GetToken());
        }

        public void ClearToken()
        {
            _tokens.TryRemove(_circuitId, out _);
            _defaultCircuitToken = null;

            // ✨ NEW: Also clear from browser storage
            _ = Task.Run(async () =>
            {
                try
                {
                    await _sessionStorage.DeleteAsync(TOKEN_STORAGE_KEY);
                    await _sessionStorage.DeleteAsync(TOKEN_EXPIRY_KEY);
                    _logger.LogInformation("🗑️ Token cleared from browser session storage");
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "⚠️ Could not clear token from browser storage");
                }
            });

            _logger.LogInformation("Token cleared for circuit {CircuitId}", _circuitId);
        }

        /// <summary>
        /// ✨ NEW: Restore token from browser session storage (survives page refresh)
        /// </summary>
        private async Task RestoreTokenFromBrowserAsync()
        {
            if (_hasAttemptedRestore)
            {
                _logger.LogDebug("Token restoration already attempted for circuit {CircuitId}", _circuitId);
                return;
            }

            _hasAttemptedRestore = true;

            try
            {
                var storedToken = await _sessionStorage.GetAsync<string>(TOKEN_STORAGE_KEY);
                var storedExpiry = await _sessionStorage.GetAsync<string>(TOKEN_EXPIRY_KEY);

                if (storedToken.Success && !string.IsNullOrEmpty(storedToken.Value))
                {
                    // Check if token is expired
                    DateTime expiryTime = DateTime.UtcNow.AddHours(8); // Default
                    if (storedExpiry.Success && DateTime.TryParse(storedExpiry.Value, out var parsedExpiry))
                    {
                        expiryTime = parsedExpiry;
                    }

                    if (DateTime.UtcNow < expiryTime)
                    {
                        // Token is still valid - restore it
                        _tokens[_circuitId] = new TokenInfo
                        {
                            Token = storedToken.Value,
                            Expiration = expiryTime,
                            CircuitId = _circuitId
                        };
                        _defaultCircuitToken = storedToken.Value;

                        // ✨ CRITICAL: Also restore authentication state
                        RestoreAuthenticationState(storedToken.Value);

                        _logger.LogInformation("✅ Token restored from browser session storage (Circuit: {CircuitId})", _circuitId);
                    }
                    else
                    {
                        _logger.LogWarning("⚠️ Stored token is expired, clearing it");
                        await _sessionStorage.DeleteAsync(TOKEN_STORAGE_KEY);
                        await _sessionStorage.DeleteAsync(TOKEN_EXPIRY_KEY);
                    }
                }
                else
                {
                    _logger.LogInformation("ℹ️ No token found in browser session storage");

                    // 🔧 DEV MODE: Auto-login with default credentials if no token found
                    await InitializeDefaultTokenAsync();
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop"))
            {
                // ✨ FIXED: Expected during prerendering - browser storage not available yet
                // Will be retried when GetToken() is called after rendering completes
                _logger.LogDebug("Browser storage not available yet (prerendering) - will retry on first API call");
                _hasAttemptedRestore = false; // Allow retry
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Could not restore token from browser storage - will try auto-login");

                // Fallback to auto-login
                await InitializeDefaultTokenAsync();
            }
        }

        /// <summary>
        /// Parse JWT token and restore authentication state
        /// </summary>
        private void RestoreAuthenticationState(string jwtToken)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var token = handler.ReadJwtToken(jwtToken);

                // Extract claims from JWT
                var claims = token.Claims.ToList();
                var identity = new ClaimsIdentity(claims, "jwt");
                var principal = new ClaimsPrincipal(identity);

                // Notify auth state provider
                if (_authStateProvider is ServerAuthStateProvider serverAuthProvider)
                {
                    serverAuthProvider.SetAuthenticatedUser(principal);
                    _logger.LogInformation("✅ Authentication state restored for user: {Username}",
                        principal.Identity?.Name ?? "Unknown");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "⚠️ Could not restore authentication state from token");
            }
        }

        private class TokenInfo
        {
            public string Token { get; set; } = "";
            public DateTime Expiration { get; set; }
            public string CircuitId { get; set; } = "";
        }

        private class LoginResponse
        {
            public string Token { get; set; } = "";
            public string Username { get; set; } = "";
            public string Role { get; set; } = "";
        }
    }
}

