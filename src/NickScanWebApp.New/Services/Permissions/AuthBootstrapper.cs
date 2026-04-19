using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Models;
using NickScanWebApp.New.Services;
using NickScanWebApp.Shared.Services;

namespace NickScanWebApp.New.Services.Permissions
{
    /// <summary>
    /// Coordinates initial hydration of the authenticated user profile and permission catalog.
    /// </summary>
    public class AuthBootstrapper : IDisposable
    {
        private readonly ApiService _apiService;
        private readonly PermissionCatalogClient _catalogClient;
        private readonly CustomAuthStateProvider _authStateProvider;
        private readonly ILogger<AuthBootstrapper> _logger;
        private readonly SemaphoreSlim _sync = new(1, 1);
        private CancellationTokenSource? _refreshCts;
        private Task? _refreshTask;
        private bool _initialized;

        public AuthBootstrapper(
            ApiService apiService,
            PermissionCatalogClient catalogClient,
            CustomAuthStateProvider authStateProvider,
            ILogger<AuthBootstrapper> logger)
        {
            _apiService = apiService;
            _catalogClient = catalogClient;
            _authStateProvider = authStateProvider;
            _logger = logger;
        }

        public UserProfileDto? Profile { get; private set; }
        public event Action<UserProfileDto?>? ProfileUpdated;

        public async Task EnsureInitializedAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            // Force CustomAuthStateProvider to sync with SimpleAuthStateProvider.
            // After a forceLoad navigation (full page reload), the CustomAuthStateProvider
            // is a fresh instance with no cached user. This call populates it from the
            // JWT stored in session storage via SimpleAuthStateProvider.
            try
            {
                await _authStateProvider.GetAuthenticationStateAsync();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Auth state sync failed during bootstrap — may be too early in circuit lifecycle.");
            }

            if (!_authStateProvider.IsAuthenticated)
            {
                _logger.LogDebug("Auth bootstrap skipped - user not authenticated yet.");
                return;
            }

            if (!forceRefresh && _initialized)
            {
                return;
            }

            await _sync.WaitAsync(cancellationToken);
            try
            {
                if (!forceRefresh && _initialized)
                {
                    return;
                }

                await LoadProfileAsync(cancellationToken);
                await _catalogClient.EnsureLoadedAsync(forceRefresh, cancellationToken);

                _initialized = true;
            }
            finally
            {
                _sync.Release();
            }
        }

        public async Task RefreshAsync(CancellationToken cancellationToken = default)
        {
            await EnsureInitializedAsync(forceRefresh: true, cancellationToken);
        }

        public void StartPeriodicRefresh(TimeSpan interval)
        {
            StopPeriodicRefresh();
            _refreshCts = new CancellationTokenSource();
            _refreshTask = RunRefreshLoopAsync(interval, _refreshCts.Token);
        }

        public void StopPeriodicRefresh()
        {
            _refreshCts?.Cancel();
            _refreshCts?.Dispose();
            _refreshCts = null;
        }

        private async Task LoadProfileAsync(CancellationToken cancellationToken, bool silent = false)
        {
            try
            {
                if (!_authStateProvider.IsAuthenticated)
                {
                    if (!silent)
                    {
                        _logger.LogDebug("Skipping profile load - user not authenticated.");
                    }
                    return;
                }

                var profile = await _apiService.GetAsync<UserProfileDto>("api/auth/profile");
                if (profile == null)
                {
                    if (!silent)
                    {
                        _logger.LogWarning("User profile response was null.");
                    }
                    // Don't clear existing permissions if profile is null - keep current state
                    return;
                }

                Profile = profile;
                ProfileUpdated?.Invoke(profile);

                // NOTE: We intentionally do NOT call SetAuthenticatedUserWithPermissions here.
                // Permissions for the session are sourced from the JWT at login time and remain
                // stable until logout/token renewal. Profile refresh updates metadata (name/role),
                // but should not shrink/replace the cached permission set mid-session.
                if (!silent)
                {
                    _logger.LogInformation("User profile loaded for {Username} (Role: {Role})", profile.Username, profile.PrimaryRole);
                }
            }
            catch (ApiException ex)
            {
                if (ex.InnerException is HttpRequestException httpEx &&
                    httpEx.StatusCode == System.Net.HttpStatusCode.Unauthorized)
                {
                    _logger.LogDebug("Profile request returned 401 - user not authenticated yet.");
                    // Don't clear permissions on 401 - might be temporary network issue
                    return;
                }

                if (!silent)
                {
                    _logger.LogWarning(ex, "Unable to load user profile. Permissions may be stale until next login.");
                }
                else
                {
                    _logger.LogDebug(ex, "Silent profile refresh failed. Keeping existing permissions.");
                }
                // IMPORTANT: Don't clear permissions on failure - keep existing cached permissions
            }
            catch (Exception ex)
            {
                if (!silent)
                {
                    _logger.LogError(ex, "Unexpected error loading user profile.");
                }
                else
                {
                    _logger.LogDebug(ex, "Silent profile refresh encountered an unexpected error. Keeping existing permissions.");
                }
                // IMPORTANT: Don't clear permissions on failure - keep existing cached permissions
            }
        }

        private async Task RunRefreshLoopAsync(TimeSpan interval, CancellationToken token)
        {
            using var timer = new PeriodicTimer(interval);
            try
            {
                while (await timer.WaitForNextTickAsync(token))
                {
                    await LoadProfileAsync(token, silent: true);
                    await _catalogClient.EnsureLoadedAsync(forceRefresh: true, token);
                }
            }
            catch (OperationCanceledException)
            {
                // expected on disposal
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Periodic permission refresh loop encountered an unexpected error.");
            }
            finally
            {
                _refreshTask = null;
            }
        }

        public void Dispose()
        {
            StopPeriodicRefresh();
        }
    }
}

