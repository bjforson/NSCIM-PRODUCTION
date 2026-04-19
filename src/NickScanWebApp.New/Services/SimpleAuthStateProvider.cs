using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.ProtectedBrowserStorage;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Simplified authentication state provider that uses JWT from session storage
    /// </summary>
    public class SimpleAuthStateProvider : AuthenticationStateProvider
    {
        private readonly ProtectedSessionStorage _sessionStorage;
        private readonly ILogger<SimpleAuthStateProvider> _logger;
        private ClaimsPrincipal _currentUser = new(new ClaimsIdentity());
        private bool _isInitialized = false;
        private readonly SemaphoreSlim _initLock = new(1, 1);
        private string? _cachedToken = null; // Cache token in memory as fallback

        private const string TOKEN_KEY = "auth_token";

        public SimpleAuthStateProvider(
            ProtectedSessionStorage sessionStorage,
            ILogger<SimpleAuthStateProvider> logger)
        {
            _sessionStorage = sessionStorage;
            _logger = logger;
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            // Ensure we only initialize once
            if (!_isInitialized)
            {
                await _initLock.WaitAsync();
                try
                {
                    if (!_isInitialized) // Double-check after acquiring lock
                    {
                        await InitializeAsync();
                        _isInitialized = true;
                    }
                }
                finally
                {
                    _initLock.Release();
                }
            }

            // Check if token has expired and auto-logout
            if (_currentUser.Identity?.IsAuthenticated == true)
            {
                var expClaim = _currentUser.FindFirst("exp");
                if (expClaim != null && long.TryParse(expClaim.Value, out var exp))
                {
                    var expiryTime = DateTimeOffset.FromUnixTimeSeconds(exp).UtcDateTime;
                    if (expiryTime < DateTime.UtcNow)
                    {
                        _logger.LogWarning("⏰ Token has expired - auto-logout");
                        await LogoutAsync();
                    }
                }
            }

            return new AuthenticationState(_currentUser);
        }

        private async Task InitializeAsync()
        {
            try
            {
                var tokenResult = await _sessionStorage.GetAsync<string>(TOKEN_KEY);

                if (tokenResult.Success && !string.IsNullOrEmpty(tokenResult.Value))
                {
                    var user = GetUserFromToken(tokenResult.Value);
                    if (user != null)
                    {
                        _currentUser = user;
                        _cachedToken = tokenResult.Value; // Cache token in memory
                        _logger.LogInformation("✅ User restored from token: {Username}", _currentUser.Identity?.Name);
                    }
                }
                else
                {
                    _logger.LogInformation("ℹ️ No authentication token found");
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop"))
            {
                _logger.LogDebug("Browser storage not available yet - using anonymous user");
            }
            catch (Exception ex) when (IsCircuitDisconnectedException(ex))
            {
                _logger.LogDebug("Circuit disconnected during initialization - using anonymous user");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error loading authentication token");
            }
        }

        public async Task LoginAsync(string token)
        {
            var user = GetUserFromToken(token);
            if (user == null)
            {
                _logger.LogWarning("Invalid token provided for login");
                return;
            }

            _currentUser = user;
            _isInitialized = true;

            try
            {
                await _sessionStorage.SetAsync(TOKEN_KEY, token);
                _cachedToken = token; // Cache token in memory
                _logger.LogInformation("✅ User logged in: {Username}", _currentUser.Identity?.Name);
                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
            }
            catch (Exception ex) when (IsCircuitDisconnectedException(ex))
            {
                // Circuit disconnected - cache token in memory as fallback
                _cachedToken = token;
                _logger.LogDebug("Circuit disconnected during login - token cached in memory");
                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error saving authentication token");
            }
        }

        public async Task LogoutAsync()
        {
            _currentUser = new ClaimsPrincipal(new ClaimsIdentity());
            _isInitialized = true;

            try
            {
                await _sessionStorage.DeleteAsync(TOKEN_KEY);
                _cachedToken = null; // Clear cached token
                _logger.LogInformation("👋 User logged out");
                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
            }
            catch (Exception ex) when (IsCircuitDisconnectedException(ex))
            {
                // Circuit disconnected - user is logged out locally anyway
                _cachedToken = null; // Clear cached token
                _logger.LogDebug("Circuit disconnected during logout - user logged out locally");
                NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(_currentUser)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing authentication token");
            }
        }

        private ClaimsPrincipal? GetUserFromToken(string token)
        {
            try
            {
                var handler = new JwtSecurityTokenHandler();
                var jwtToken = handler.ReadJwtToken(token);

                // Check if token is expired
                if (jwtToken.ValidTo < DateTime.UtcNow)
                {
                    _logger.LogWarning("Token has expired");
                    return null;
                }

                var claims = jwtToken.Claims.ToList();
                var identity = new ClaimsIdentity(claims, "jwt");
                return new ClaimsPrincipal(identity);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing JWT token");
                return null;
            }
        }

        public async Task<string?> GetTokenAsync()
        {
            try
            {
                var result = await _sessionStorage.GetAsync<string>(TOKEN_KEY);
                if (result.Success && !string.IsNullOrEmpty(result.Value))
                {
                    _cachedToken = result.Value; // Update cache on successful retrieval
                    _logger.LogDebug("✅ Token retrieved from session storage (length: {Length})", result.Value.Length);
                    return result.Value;
                }
                else
                {
                    _logger.LogDebug("⚠️ Token not found in session storage (Success: {Success})", result.Success);
                    // Try cached token as fallback
                    return GetValidatedCachedToken();
                }
            }
            catch (InvalidOperationException ex) when (ex.Message.Contains("JavaScript interop"))
            {
                _logger.LogDebug("⚠️ Browser storage not available yet (JavaScript interop not ready)");
                // Use cached token as fallback when storage isn't available
                return GetValidatedCachedToken();
            }
            catch (TaskCanceledException)
            {
                // Task was canceled (timeout or component disposal) - use cached token as fallback
                // This is expected during component disposal or timeout scenarios, so log at Debug level
                _logger.LogDebug("Task canceled during token retrieval - using cached token as fallback");
                return GetValidatedCachedToken();
            }
            catch (Exception ex) when (IsCircuitDisconnectedException(ex))
            {
                // Circuit has disconnected - use cached token as fallback
                // This is expected during component disposal, so log at Debug level
                _logger.LogDebug("Circuit disconnected - using cached token as fallback");
                return GetValidatedCachedToken();
            }
            catch (Exception ex)
            {
                // Only log as warning if it's not a circuit disconnection or cancellation issue
                if (!IsCircuitDisconnectedException(ex) && !(ex is TaskCanceledException))
                {
                    _logger.LogWarning(ex, "❌ Error retrieving token from session storage");
                }
                // Try cached token as fallback
                return GetValidatedCachedToken();
            }
        }

        /// <summary>
        /// Checks if an exception is related to circuit disconnection (JSDisconnectedException)
        /// </summary>
        private static bool IsCircuitDisconnectedException(Exception ex)
        {
            // Check by type name (JSDisconnectedException may not be directly accessible)
            return ex.GetType().Name.Contains("JSDisconnected", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("circuit has disconnected", StringComparison.OrdinalIgnoreCase) ||
                   ex.Message.Contains("circuit disconnected", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Get cached token if available and valid, otherwise return null
        /// </summary>
        private string? GetValidatedCachedToken()
        {
            if (string.IsNullOrEmpty(_cachedToken))
            {
                return null;
            }

            try
            {
                // Validate that the cached token is still valid (not expired)
                var user = GetUserFromToken(_cachedToken);
                if (user != null && user.Identity?.IsAuthenticated == true)
                {
                    _logger.LogDebug("✅ Using cached token (session storage unavailable)");
                    return _cachedToken;
                }
                else
                {
                    // Cached token is invalid/expired, clear it
                    _cachedToken = null;
                    _logger.LogDebug("⚠️ Cached token is invalid or expired - cleared");
                    return null;
                }
            }
            catch (Exception ex)
            {
                // Token parsing failed, clear cache
                _cachedToken = null;
                _logger.LogDebug(ex, "⚠️ Error validating cached token - cleared");
                return null;
            }
        }
    }
}

