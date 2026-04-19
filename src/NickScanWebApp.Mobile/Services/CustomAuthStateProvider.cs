using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NickScanWebApp.Mobile.Services.Permissions;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;

namespace NickScanWebApp.Mobile.Services
{
    /// <summary>
    /// Authentication state provider that keeps a local permission cache and provides helper methods for RBAC checks.
    /// </summary>
    public class CustomAuthStateProvider : AuthenticationStateProvider, IPermissionProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly SimpleAuthStateProvider _simpleAuthProvider;
        private readonly ILogger<CustomAuthStateProvider> _logger;

        private readonly object _permissionLock = new();
        private ClaimsPrincipal? _currentUser;
        private HashSet<string> _permissionClaims = new(StringComparer.OrdinalIgnoreCase);
        private readonly string _instanceId = Guid.NewGuid().ToString("N")[..8];

        public CustomAuthStateProvider(
            IHttpContextAccessor httpContextAccessor,
            SimpleAuthStateProvider simpleAuthProvider,
            ILogger<CustomAuthStateProvider> logger)
        {
            _httpContextAccessor = httpContextAccessor;
            _simpleAuthProvider = simpleAuthProvider;
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
                return false;
            }

            var hasPermission = HasPermissionCore(user, permission);
            if (!hasPermission)
            {
                _logger.LogWarning("[AuthStateProvider:{Instance}] Permission denied for {Permission} (User: {User})", _instanceId, permission, user.Identity?.Name);
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
            var authState = await _simpleAuthProvider.GetAuthenticationStateAsync();
            _currentUser = authState?.User;
            EnsurePermissionCache(_currentUser);
            return authState ?? new AuthenticationState(new ClaimsPrincipal(new ClaimsIdentity()));
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
            if (_currentUser != null && _currentUser.Identity?.IsAuthenticated == true)
            {
                return _currentUser;
            }

            var httpUser = _httpContextAccessor.HttpContext?.User;
            if (httpUser?.Identity?.IsAuthenticated == true)
            {
                return httpUser;
            }

            return new ClaimsPrincipal(new ClaimsIdentity());
        }

        private void UpdatePrincipal(ClaimsPrincipal principal, bool notify = true)
        {
            _currentUser = principal;
            // Only invalidate cache if we have new permission claims, otherwise preserve existing cache
            var hasNewPermissions = principal.Claims.Any(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase));
            if (hasNewPermissions)
            {
                InvalidatePermissionCache();
            }

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

            var shouldRefresh = false;
            lock (_permissionLock)
            {
                if (_permissionClaims.Count == 0)
                {
                    shouldRefresh = true;
                }
            }

            if (shouldRefresh)
            {
                var claims = user.Claims
                    .Where(c => c.Type.Equals("Permission", StringComparison.OrdinalIgnoreCase))
                    .Select(c => c.Value)
                    .ToList();

                SetPermissionCache(claims);
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
    }
}

