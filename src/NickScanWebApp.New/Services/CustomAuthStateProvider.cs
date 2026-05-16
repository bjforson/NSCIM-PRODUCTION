using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanWebApp.New.Services.Permissions;
using NickScanWebApp.Shared.Services;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Authentication state provider that keeps a local permission cache and provides helper methods for RBAC checks.
    /// </summary>
    public class CustomAuthStateProvider : AuthenticationStateProvider, IPermissionProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly SimpleAuthStateProvider _simpleAuthProvider;
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CustomAuthStateProvider> _logger;

        private readonly object _permissionLock = new();
        private ClaimsPrincipal? _currentUser;
        private HashSet<string> _permissionClaims = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

        public CustomAuthStateProvider(
            IHttpContextAccessor httpContextAccessor,
            SimpleAuthStateProvider simpleAuthProvider,
            IServiceProvider serviceProvider,
            ILogger<CustomAuthStateProvider> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _simpleAuthProvider = simpleAuthProvider;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public event Action? PermissionsChanged;

        public bool IsAuthenticated => GetCurrentUser().Identity?.IsAuthenticated == true;

        public string? CurrentUsername => GetCurrentUser().Identity?.Name;

        public string? CurrentUserRole => GetCurrentUser().FindFirst(ClaimTypes.Role)?.Value;

        public void SetAuthenticatedUser(string username, string role, string email, string firstName, string lastName)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, username),
                new(ClaimTypes.Email, email),
                new(ClaimTypes.GivenName, firstName),
                new(ClaimTypes.Surname, lastName),
                new(ClaimTypes.Role, role)
            };

            UpdatePrincipal(new ClaimsPrincipal(new ClaimsIdentity(claims, "CustomAuth")));
        }

        public void SetAuthenticatedUserWithPermissions(string username, string role, string email, List<string> permissions)
        {
            // Only update if we have permissions - don't clear if permissions list is empty during refresh
            if (permissions == null || permissions.Count == 0)
            {
                _logger.LogWarning("[AuthStateProvider:{Instance}] Attempted to set permissions with empty list for {Username}. Preserving existing permissions.", _instanceId, username);
                return;
            }

            var claims = new List<Claim>
            {
                new(ClaimTypes.Name, username),
                new(ClaimTypes.Email, email),
                new(ClaimTypes.Role, role)
            };

            foreach (var permission in permissions.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                claims.Add(new Claim("Permission", permission));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "CustomAuth"));
            UpdatePrincipal(principal, notify: false);
            SetPermissionCache(permissions);
            _logger.LogInformation("[AuthStateProvider:{Instance}] Cached {PermissionCount} permissions for {Username}", _instanceId, permissions.Count, username);

            NotifyAuthenticationStateChanged(Task.FromResult(new AuthenticationState(principal)));
            PermissionsChanged?.Invoke();
        }

        public void ClearAuthentication()
        {
            _logger.LogInformation("[AuthStateProvider:{Instance}] Clearing authentication state", _instanceId);
            _currentUser = null;
            SetPermissionCache(Array.Empty<string>());
            NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
            PermissionsChanged?.Invoke();
        }

        public bool HasPermission(string permission)
        {
            var user = GetCurrentUser();
            if (user.Identity?.IsAuthenticated != true)
            {
                _logger.LogDebug("[AuthStateProvider:{Instance}] HasPermission('{Permission}') called but user is not authenticated", _instanceId, permission);
                return false;
            }

            var hasPermission = HasPermissionCore(user, permission);
            if (!hasPermission)
            {
                LogPermissionDiagnostics(permission, user);
            }

            return hasPermission;
        }

        public bool HasAnyPermission(params string[] permissions)
        {
            var user = GetCurrentUser();
            if (user.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            return permissions.Any(p => HasPermissionCore(user, p));
        }

        public bool HasAllPermissions(params string[] permissions)
        {
            var user = GetCurrentUser();
            if (user.Identity?.IsAuthenticated != true)
            {
                return false;
            }

            return permissions.All(p => HasPermissionCore(user, p));
        }

        public override async Task<AuthenticationState> GetAuthenticationStateAsync()
        {
            var wasUnauthenticated = _currentUser?.Identity?.IsAuthenticated != true;

            try
            {
                var authState = await _simpleAuthProvider.GetAuthenticationStateAsync();
                var newUser = authState?.User;

                if (_currentUser?.Identity?.IsAuthenticated != true)
                {
                    _currentUser = newUser;
                }
                else if (newUser?.Identity?.IsAuthenticated != true)
                {
                    // User logged out - clear current user
                    _currentUser = newUser;
                    // Clear permission cache on logout
                    lock (_permissionLock)
                    {
                        _permissionClaims.Clear();
                    }
                }
                else
                {
                    // Both are authenticated - check if it's the same user
                    var currentUsername = _currentUser.Identity?.Name;
                    var newUsername = newUser.Identity?.Name;

                    if (currentUsername != newUsername)
                    {
                        // Different user - update (user switch case)
                        _currentUser = newUser;
                        // Clear permission cache on user switch
                        lock (_permissionLock)
                        {
                            _permissionClaims.Clear();
                        }
                    }
                    else
                    {
                        // ✅ CRITICAL FIX: Same user - NEVER overwrite _currentUser if we have cached permissions
                        // This is the key fix: preserve _currentUser completely when we have cached permissions
                        // API failures may cause newUser to have no permission claims, but we must preserve
                        // the existing _currentUser which has the permission claims merged in

                        List<string> cachedPermissions;
                        lock (_permissionLock)
                        {
                            cachedPermissions = _permissionClaims.ToList();
                        }

                        // ✅ CRITICAL: If we have cached permissions, NEVER overwrite _currentUser
                        // Even if newUser has permission claims, preserve _currentUser to maintain stability
                        if (cachedPermissions.Count > 0)
                        {
                            // We have cached permissions - preserve _currentUser completely
                            // Only update non-permission claims if needed (e.g., role changes)
                            var currentPermissionClaims = _currentUser.Claims
                                .Where(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase))
                                .Select(c => c.Value)
                                .ToHashSet(StringComparer.OrdinalIgnoreCase);

                            // Ensure _currentUser has all cached permissions as claims
                            if (currentPermissionClaims.Count != cachedPermissions.Count ||
                                !cachedPermissions.All(p => currentPermissionClaims.Contains(p)))
                            {
                                // Rebuild _currentUser with all cached permissions
                                var existingClaims = _currentUser.Claims
                                    .Where(c => !c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                foreach (var permission in cachedPermissions)
                                {
                                    existingClaims.Add(new Claim("Permission", permission));
                                }

                                _currentUser = new ClaimsPrincipal(new ClaimsIdentity(existingClaims, _currentUser.Identity?.AuthenticationType ?? "CustomAuth"));
                                _logger.LogDebug("[AuthStateProvider:{Instance}] Rebuilt _currentUser with {Count} cached permissions (API failure protection)", _instanceId, cachedPermissions.Count);
                            }

                            // DO NOT overwrite _currentUser with newUser - preserve it completely
                            _logger.LogDebug("[AuthStateProvider:{Instance}] Preserving _currentUser with {Count} cached permissions (ignoring newUser from API)", _instanceId, cachedPermissions.Count);
                        }
                        else
                        {
                            // No cached permissions - safe to update from newUser
                            // But still merge permission claims if newUser has them
                            var newPermissionClaims = newUser.Claims
                                .Where(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase))
                                .Select(c => c.Value)
                                .ToList();

                            if (newPermissionClaims.Count > 0)
                            {
                                // newUser has permission claims - update cache and merge into _currentUser
                                SetPermissionCache(newPermissionClaims);

                                var existingClaims = _currentUser.Claims
                                    .Where(c => !c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                foreach (var permission in newPermissionClaims)
                                {
                                    existingClaims.Add(new Claim("Permission", permission));
                                }

                                _currentUser = new ClaimsPrincipal(new ClaimsIdentity(existingClaims, _currentUser.Identity?.AuthenticationType ?? "CustomAuth"));
                                _logger.LogDebug("[AuthStateProvider:{Instance}] Updated _currentUser with {Count} permissions from newUser", _instanceId, newPermissionClaims.Count);
                            }
                            else
                            {
                                // No cached permissions and newUser has no permissions - preserve _currentUser
                                // Don't overwrite with a user that has no permissions
                                _logger.LogDebug("[AuthStateProvider:{Instance}] Preserving _currentUser (no cached permissions, newUser has no permissions)", _instanceId);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // ✅ CRITICAL FIX: On any exception (API failure, network error, etc.), preserve _currentUser
                // Never clear permissions on exceptions - they should persist through failures
                _logger.LogWarning(ex, "[AuthStateProvider:{Instance}] Exception in GetAuthenticationStateAsync - preserving _currentUser and cached permissions", _instanceId);

                // If we have an authenticated user with cached permissions, preserve it
                if (_currentUser?.Identity?.IsAuthenticated == true)
                {
                    List<string> cachedPermissions;
                    lock (_permissionLock)
                    {
                        cachedPermissions = _permissionClaims.ToList();
                    }

                    if (cachedPermissions.Count > 0)
                    {
                        _logger.LogInformation("[AuthStateProvider:{Instance}] Preserving {Count} cached permissions through API failure", _instanceId, cachedPermissions.Count);
                    }
                }
            }

            EnsurePermissionCache(_currentUser);

            // After a forceLoad navigation the Blazor circuit is brand-new, so _currentUser
            // starts null. Once SimpleAuthStateProvider restores the JWT from session storage
            // and we populate the cache above, we must notify subscribers (NavMenu, TopBar,
            // etc.) so they re-render with the correct permissions.
            if (wasUnauthenticated && _currentUser?.Identity?.IsAuthenticated == true)
            {
                _logger.LogInformation("[AuthStateProvider:{Instance}] Auth state transitioned to authenticated for {User} — notifying subscribers",
                    _instanceId, _currentUser.Identity?.Name);
                PermissionsChanged?.Invoke();
            }

            return new AuthenticationState(_currentUser ?? new ClaimsPrincipal(new ClaimsIdentity()));
        }

        public Task LoginAsync(string username)
        {
            SetAuthenticatedUser(username, "Admin", $"{username}@ghana.gov.gh", username, "User");
            return Task.CompletedTask;
        }

        public Task LogoutAsync()
        {
            ClearAuthentication();
            return Task.CompletedTask;
        }

        public void InvalidatePermissionCache()
        {
            lock (_permissionLock)
            {
                _permissionClaims.Clear();
            }
        }

        private ClaimsPrincipal GetCurrentUser()
        {
            // Prefer cached principal if we already have an authenticated user.
            // The cache is populated either from SimpleAuthStateProvider in GetAuthenticationStateAsync
            // or via SetAuthenticatedUserWithPermissions after login/refresh.
            if (_currentUser != null && _currentUser.Identity?.IsAuthenticated == true)
            {
                return _currentUser;
            }

            // If we don't yet have an authenticated principal cached, treat as unauthenticated.
            // We intentionally avoid blocking calls here (no sync-over-async) to prevent deadlocks
            // in Blazor Server circuits.
            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        private void UpdatePrincipal(ClaimsPrincipal principal, bool notify = true)
        {
            // ✅ CRITICAL FIX: Never clear permission cache unless we're explicitly setting new permissions
            // If principal has no permission claims but we have cached permissions, preserve the cache
            var hasNewPermissions = principal.Claims.Any(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase));

            List<string> cachedPermissions;
            lock (_permissionLock)
            {
                cachedPermissions = _permissionClaims.ToList();
            }

            if (!hasNewPermissions && principal.Identity?.IsAuthenticated == true && cachedPermissions.Count > 0)
            {
                // ✅ CRITICAL FIX: New principal has no permission claims but we have cached permissions
                // Merge cached permissions into the principal to preserve them
                var claims = principal.Claims.ToList();
                foreach (var permission in cachedPermissions)
                {
                    // Only add if not already present (avoid duplicates)
                    if (!claims.Any(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase) &&
                                       c.Value.Equals(permission, StringComparison.OrdinalIgnoreCase)))
                    {
                        claims.Add(new Claim("Permission", permission));
                    }
                }

                // Update the principal with merged claims
                principal = new ClaimsPrincipal(new ClaimsIdentity(claims, principal.Identity?.AuthenticationType ?? "CustomAuth"));
                _logger.LogDebug("[AuthStateProvider:{Instance}] Merged {Count} cached permissions into principal without permission claims", _instanceId, cachedPermissions.Count);

                // ✅ CRITICAL: DO NOT invalidate cache - we're preserving it
            }
            else if (hasNewPermissions)
            {
                // ✅ CRITICAL FIX: Principal has new permission claims - update cache, don't clear it
                // Extract permissions from principal and update cache
                var newPermissions = principal.Claims
                    .Where(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Value)
                    .ToList();

                if (newPermissions.Count > 0)
                {
                    SetPermissionCache(newPermissions);
                    _logger.LogDebug("[AuthStateProvider:{Instance}] Updated permission cache with {Count} permissions from principal", _instanceId, newPermissions.Count);
                }
            }
            // ✅ CRITICAL: If principal has no permissions and we have no cached permissions, preserve empty cache
            // Don't clear anything - just update _currentUser

            _currentUser = principal;

            if (notify)
            {
                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
                PermissionsChanged?.Invoke();
            }
        }

        private void EnsurePermissionCache(ClaimsPrincipal? user)
        {
            if (user?.Identity?.IsAuthenticated != true)
            {
                return;
            }

            lock (_permissionLock)
            {
                // ✅ PERSISTENCE FIX: Only rebuild cache if it's empty AND user has permission claims
                // If cache has permissions, preserve them even if user temporarily has no claims (API failure)
                if (_permissionClaims.Count == 0)
                {
                    // Cache is empty - check if user has permission claims to rebuild from
                    var permissionClaims = user.Claims
                        .Where(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase))
                        .Select(c => c.Value)
                        .ToList();

                    if (permissionClaims.Count > 0)
                    {
                        // User has permission claims - rebuild cache
                        SetPermissionCache(permissionClaims);
                        _logger.LogDebug("[AuthStateProvider:{Instance}] Rebuilt permission cache from user claims: {Count} permissions", _instanceId, permissionClaims.Count);
                    }
                    else
                    {
                        // ✅ FIX 1: Cache is empty and user has no permission claims - fetch from API as fallback
                        // This provides recovery mechanism when cache is lost and JWT has no permissions
                        _logger.LogDebug("[AuthStateProvider:{Instance}] Cache is empty and user has no permission claims. Attempting API fallback...", _instanceId);

                        // Fetch permissions from API asynchronously (don't block)
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                await FetchPermissionsFromApiAsync();
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "[AuthStateProvider:{Instance}] API fallback failed to fetch permissions", _instanceId);
                            }
                        });
                    }
                }
                else
                {
                    // ✅ PERSISTENCE FIX: Cache has permissions - preserve them even if user has no claims
                    // This handles the case where API calls fail and user temporarily has no permission claims
                    // Permissions should persist through API failures until logout/inactivity timeout/restart
                    var hasPermissionClaims = user.Claims.Any(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase));
                    if (!hasPermissionClaims)
                    {
                        _logger.LogDebug("[AuthStateProvider:{Instance}] User has no permission claims but cache has {Count} permissions. Preserving cache (API failure scenario).", _instanceId, _permissionClaims.Count);
                    }
                }
            }
        }

        private void SetPermissionCache(IEnumerable<string> permissions)
        {
            lock (_permissionLock)
            {
                var refreshed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var permission in permissions)
                {
                    AddPermissionVariants(refreshed, permission);
                }

                _permissionClaims = refreshed;
                _logger.LogDebug("[AuthStateProvider:{Instance}] Permission cache populated with {Count} entries", _instanceId, _permissionClaims.Count);
            }
        }

        private bool HasPermissionCore(ClaimsPrincipal user, string permission)
        {
            var normalized = NormalizePermission(permission);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return false;
            }

            EnsurePermissionCache(user);

            if (HasCachedPermission(normalized))
            {
                return true;
            }

            if (HasPermissionByClaim(user, normalized))
            {
                CachePermission(normalized);
                return true;
            }

            return false;
        }

        private void LogPermissionDiagnostics(string permission, ClaimsPrincipal user)
        {
            try
            {
                var normalized = NormalizePermission(permission);
                var allClaimPerms = user.Claims
                    .Where(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase))
                    .Select(c => NormalizePermission(c.Value))
                    .ToList();

                int cachedCount;
                bool cachedContains;
                lock (_permissionLock)
                {
                    cachedCount = _permissionClaims.Count;
                    cachedContains = _permissionClaims.Contains(normalized);
                }

                _logger.LogWarning(
                    "[AuthStateProvider:{Instance}] Permission denied for '{Permission}' (normalized: '{Normalized}'). " +
                    "User={User}, Auth={IsAuth}, ClaimPerms={ClaimCount}, CacheCount={CacheCount}, CacheHasNormalized={CacheHasNormalized}. " +
                    "SampleClaimPerms=[{Sample}]",
                    _instanceId,
                    permission,
                    normalized,
                    user.Identity?.Name ?? "(unknown)",
                    user.Identity?.IsAuthenticated == true,
                    allClaimPerms.Count,
                    cachedCount,
                    cachedContains,
                    string.Join(", ", allClaimPerms.Take(5)));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AuthStateProvider:{Instance}] Error logging permission diagnostics for '{Permission}'", _instanceId, permission);
            }
        }

        private bool HasCachedPermission(string permission)
        {
            lock (_permissionLock)
            {
                if (_permissionClaims.Contains(permission))
                {
                    return true;
                }

                if (permission.StartsWith("pages.", StringComparison.OrdinalIgnoreCase))
                {
                    var trimmed = permission.Substring("pages.".Length);
                    return _permissionClaims.Contains(trimmed);
                }

                return _permissionClaims.Contains($"pages.{permission}");
            }
        }

        private bool HasPermissionByClaim(ClaimsPrincipal user, string permission)
        {
            var claimPermissions = new HashSet<string>(
                user.Claims
                    .Where(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase))
                    .Select(c => NormalizePermission(c.Value)),
                StringComparer.OrdinalIgnoreCase);

            if (claimPermissions.Contains(permission))
            {
                return true;
            }

            if (permission.StartsWith("pages.", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = permission.Substring("pages.".Length);
                return !string.IsNullOrWhiteSpace(trimmed) && claimPermissions.Contains(trimmed);
            }

            return claimPermissions.Contains($"pages.{permission}");
        }

        private void CachePermission(string permission)
        {
            lock (_permissionLock)
            {
                AddPermissionVariants(_permissionClaims, permission);
            }
        }

        private static void AddPermissionVariants(HashSet<string> target, string permission)
        {
            var normalized = NormalizePermission(permission);
            if (string.IsNullOrWhiteSpace(normalized))
            {
                return;
            }

            target.Add(normalized);

            if (normalized.StartsWith("pages.", StringComparison.OrdinalIgnoreCase))
            {
                var trimmed = normalized.Substring("pages.".Length);
                if (!string.IsNullOrWhiteSpace(trimmed))
                {
                    target.Add(trimmed);
                }
            }
            else
            {
                target.Add($"pages.{normalized}");
            }
        }

        private static string NormalizePermission(string permission) =>
            permission?.Trim() ?? string.Empty;

        /// <summary>
        /// Get JWT token from the underlying SimpleAuthStateProvider
        /// This allows ApiService (using reflection) to retrieve the token
        /// </summary>
        public async Task<string?> GetTokenAsync()
        {
            return await _simpleAuthProvider.GetTokenAsync();
        }

        /// <summary>
        /// Fetches permissions from API as fallback when cache is empty and JWT has no permissions
        /// </summary>
        private async Task FetchPermissionsFromApiAsync()
        {
            try
            {
                // Create a scope to avoid circular dependency (ApiService depends on AuthenticationStateProvider)
                using var scope = _serviceProvider.CreateScope();
                var apiService = scope.ServiceProvider.GetRequiredService<ApiService>();

                var permissions = await apiService.GetAsync<List<string>>(AuthenticationRoutes.MyPermissionsPath);
                if (permissions != null && permissions.Count > 0)
                {
                    lock (_permissionLock)
                    {
                        // Double-check cache is still empty (another thread might have populated it)
                        if (_permissionClaims.Count == 0)
                        {
                            SetPermissionCache(permissions);
                            _logger.LogInformation("[AuthStateProvider:{Instance}] ✅ API fallback successful - loaded {Count} permissions", _instanceId, permissions.Count);

                            // Rebuild _currentUser with permissions
                            if (_currentUser?.Identity?.IsAuthenticated == true)
                            {
                                var existingClaims = _currentUser.Claims
                                    .Where(c => !c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase))
                                    .ToList();

                                foreach (var permission in permissions)
                                {
                                    existingClaims.Add(new Claim("Permission", permission));
                                }

                                _currentUser = new ClaimsPrincipal(new ClaimsIdentity(existingClaims, _currentUser.Identity?.AuthenticationType ?? "CustomAuth"));

                                // Notify authentication state changed
                                NotifyAuthenticationStateChanged(GetAuthenticationStateAsync());
                                PermissionsChanged?.Invoke();
                            }
                        }
                        else
                        {
                            _logger.LogDebug("[AuthStateProvider:{Instance}] Cache was populated by another thread, skipping API fallback", _instanceId);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("[AuthStateProvider:{Instance}] API fallback returned empty permissions list", _instanceId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[AuthStateProvider:{Instance}] API fallback failed to fetch permissions", _instanceId);
                // Don't throw - this is a fallback mechanism, failures are acceptable
            }
        }
    }
}

