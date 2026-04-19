using Microsoft.AspNetCore.Components.Authorization;
using System.Security.Claims;

namespace NickScanWebApp.Mobile.Services
{
    /// <summary>
    /// Server-side authentication state provider for Blazor Server with cookie authentication
    /// Reads authentication state from HTTP context (cookie authentication)
    /// </summary>
    public class ServerAuthStateProvider : AuthenticationStateProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly ILogger<ServerAuthStateProvider> _logger;
        private ClaimsPrincipal? _cachedUser;

        public ServerAuthStateProvider(
            IHttpContextAccessor httpContextAccessor,
            ILogger<ServerAuthStateProvider> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _logger = logger;
        }

        public override Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var httpContext = _httpContextAccessor.HttpContext;
            
            // Priority 1: Check cached user first (set by TokenStorageService or login)
            if (_cachedUser?.Identity?.IsAuthenticated == true)
            {
                _logger.LogInformation("✅ User authenticated via cache: {Username}", _cachedUser.Identity.Name);
                return Task.FromResult(new AuthenticationState(_cachedUser));
            }
            
            // Priority 2: Check HTTP context (for cookie auth on initial load)
            if (httpContext?.User?.Identity?.IsAuthenticated == true)
            {
                _logger.LogInformation("✅ User authenticated via HTTP context: {Username}", httpContext.User.Identity.Name);
                _cachedUser = httpContext.User; // Cache for future requests
                return Task.FromResult(new AuthenticationState(httpContext.User));
            }

            // More detailed logging for debugging
            var isAuthenticated = httpContext?.User?.Identity?.IsAuthenticated ?? false;
            var identityName = httpContext?.User?.Identity?.Name ?? "null";
            _logger.LogDebug("ℹ️ User not authenticated - HttpContext exists: {HasContext}, User exists: {HasUser}, IsAuthenticated: {IsAuth}, Identity Name: {Name}", 
                httpContext != null, 
                httpContext?.User != null,
                isAuthenticated,
                identityName);
            
            var anonymous = new ClaimsPrincipal(new ClaimsIdentity());
            return Task.FromResult(new AuthenticationState(anonymous));
        }

        /// <summary>
        /// Set the authenticated user (call after successful login)
        /// </summary>
        public void SetAuthenticatedUser(ClaimsPrincipal user)
        {
            _cachedUser = user;
            _logger.LogInformation("✅ User authenticated and cached: {Username}", user.Identity?.Name);
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(user)));
        }

        /// <summary>
        /// Notify that authentication state has changed (call after login/logout)
        /// </summary>
        public void NotifyAuthChanged()
        {
            // Don't clear cache - just notify to refresh from current state
            // The cache or HttpContext should already have the correct user
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
        }

        /// <summary>
        /// Clear authentication (call on logout)
        /// </summary>
        public void ClearAuth()
        {
            _cachedUser = null;
            _logger.LogInformation("❌ User logged out");
            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()))));
        }
    }
}

