using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Models.Permissions;
using NickScanWebApp.New.Services;
using NickScanWebApp.Shared.Services;

namespace NickScanWebApp.New.Services.Permissions
{
    /// <summary>
    /// Client for retrieving and caching the permission catalog from the backend.
    /// </summary>
    public class PermissionCatalogClient
    {
        private readonly ApiService _apiService;
        private readonly IPermissionProvider _permissionProvider;
        private readonly ILogger<PermissionCatalogClient> _logger;
        private readonly SemaphoreSlim _syncLock = new(1, 1);

        private PermissionCatalogDto? _catalog;
        private ConcurrentDictionary<string, PermissionSummaryDto>? _lookup;

        public PermissionCatalogClient(
            ApiService apiService,
            IPermissionProvider permissionProvider,
            ILogger<PermissionCatalogClient> logger)
        {
            _apiService = apiService;
            _permissionProvider = permissionProvider;
            _logger = logger;
        }

        public PermissionCatalogDto? Catalog => _catalog;
        public string? Version => _catalog?.Version;

        public async Task<PermissionCatalogDto?> EnsureLoadedAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            // ✅ FIX: Check authentication before attempting to load catalog
            if (!_permissionProvider.IsAuthenticated)
            {
                _logger.LogDebug("Skipping permission catalog load - user not authenticated.");
                return _catalog; // Return existing catalog if available, null otherwise
            }

            if (!forceRefresh && _catalog != null)
            {
                return _catalog;
            }

            await _syncLock.WaitAsync(cancellationToken);
            try
            {
                // Double-check authentication after acquiring lock
                if (!_permissionProvider.IsAuthenticated)
                {
                    _logger.LogDebug("Skipping permission catalog load - user not authenticated (checked after lock).");
                    return _catalog;
                }

                if (!forceRefresh && _catalog != null)
                {
                    return _catalog;
                }

                var catalog = await _apiService.GetAsync<PermissionCatalogDto>(AuthenticationRoutes.PermissionCatalogPath);
                if (catalog == null)
                {
                    _logger.LogWarning("Permission catalog returned null response from API.");
                    return _catalog;
                }

                _catalog = catalog;
                _lookup = new ConcurrentDictionary<string, PermissionSummaryDto>(
                    catalog.Categories
                        .SelectMany(c => c.Permissions)
                        .ToDictionary(p => p.Name, p => p, StringComparer.OrdinalIgnoreCase),
                    StringComparer.OrdinalIgnoreCase);

                _logger.LogInformation("Permission catalog loaded. Version: {Version}, Permissions: {Count}",
                    catalog.Version,
                    catalog.Categories.Sum(c => c.Permissions.Count));

                return _catalog;
            }
            catch (ApiException ex)
            {
                // ✅ FIX: Suppress 401 errors when not authenticated (expected behavior)
                if (ex.Message.Contains("401") || ex.Message.Contains("Unauthorized"))
                {
                    _logger.LogDebug("Permission catalog load skipped - user not authenticated (401 response).");
                }
                else
                {
                    _logger.LogWarning(ex, "Failed to load permission catalog from API.");
                }
                return _catalog;
            }
            finally
            {
                _syncLock.Release();
            }
        }

        public PermissionSummaryDto? Find(string permissionName)
        {
            if (string.IsNullOrWhiteSpace(permissionName) || _lookup == null)
            {
                return null;
            }

            return _lookup.TryGetValue(permissionName, out var summary)
                ? summary
                : null;
        }

        public IReadOnlyList<PermissionSummaryDto> AllPermissions =>
            _catalog == null
                ? Array.Empty<PermissionSummaryDto>()
                : _catalog.Categories.SelectMany(c => c.Permissions).ToList();
    }
}

