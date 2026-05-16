using NickScanWebApp.Shared.Services;

namespace NickScanWebApp.New.Services
{
    public class RoleLookupService
    {
        private readonly RoleAdminClient _roleAdminClient;
        private readonly ILogger<RoleLookupService> _logger;
        private readonly SemaphoreSlim _syncLock = new(1, 1);
        private IReadOnlyList<RoleOption> _cachedRoles = Array.Empty<RoleOption>();
        private DateTime _lastFetchUtc = DateTime.MinValue;
        private static readonly TimeSpan CacheDuration = TimeSpan.FromMinutes(5);

        public RoleLookupService(RoleAdminClient roleAdminClient, ILogger<RoleLookupService> logger)
        {
            _roleAdminClient = roleAdminClient;
            _logger = logger;
        }

        public async Task<IReadOnlyList<RoleOption>> GetRolesAsync(bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            if (!forceRefresh && _cachedRoles.Count > 0 && DateTime.UtcNow - _lastFetchUtc < CacheDuration)
            {
                return _cachedRoles;
            }

            await _syncLock.WaitAsync(cancellationToken);
            try
            {
                if (!forceRefresh && _cachedRoles.Count > 0 && DateTime.UtcNow - _lastFetchUtc < CacheDuration)
                {
                    return _cachedRoles;
                }

                _logger.LogInformation("[RoleLookup] Fetching roles from API...");
                var rolesFromApi = await _roleAdminClient.GetRolesAsync<List<RoleDto>>();

                if (rolesFromApi == null)
                {
                    _logger.LogWarning("[RoleLookup] API returned null role list. Keeping existing cache of {Count} roles.", _cachedRoles.Count);
                    return _cachedRoles;
                }

                _cachedRoles = rolesFromApi
                    .OrderBy(r => r.DisplayName ?? r.Name)
                    .Select(r => new RoleOption
                    {
                        Id = r.Id,
                        Name = r.Name ?? string.Empty,
                        DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? (r.Name ?? string.Empty) : r.DisplayName!,
                        Description = r.Description ?? string.Empty,
                        BaseRole = r.BaseRole,
                        IsSystemRole = r.IsSystemRole,
                        IsActive = r.IsActive
                    })
                    .ToList();

                _lastFetchUtc = DateTime.UtcNow;

                _logger.LogInformation("[RoleLookup] Cached {Count} roles from API.", _cachedRoles.Count);
                return _cachedRoles;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RoleLookup] Failed to retrieve roles from API.");
                return _cachedRoles;
            }
            finally
            {
                _syncLock.Release();
            }
        }

        public void ClearCache()
        {
            _cachedRoles = Array.Empty<RoleOption>();
            _lastFetchUtc = DateTime.MinValue;
        }

        public record RoleOption
        {
            public int Id { get; init; }
            public string Name { get; init; } = string.Empty;
            public string DisplayName { get; init; } = string.Empty;
            public string Description { get; init; } = string.Empty;
            public string? BaseRole { get; init; }
            public bool IsSystemRole { get; init; }
            public bool IsActive { get; init; }
        }

        private class RoleDto
        {
            public int Id { get; set; }
            public string? Name { get; set; }
            public string? DisplayName { get; set; }
            public string? Description { get; set; }
            public string? BaseRole { get; set; }
            public bool IsSystemRole { get; set; }
            public bool IsActive { get; set; }
        }
    }
}

