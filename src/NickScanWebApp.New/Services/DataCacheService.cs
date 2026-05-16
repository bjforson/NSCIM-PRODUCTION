using Microsoft.Extensions.Caching.Memory;

namespace NickScanWebApp.New.Services;

/// <summary>
/// Service for caching API responses to improve performance
/// </summary>
public class DataCacheService
{
    private readonly IMemoryCache _cache;
    private readonly ILogger<DataCacheService> _logger;

    private readonly TimeSpan _containerListCacheDuration;
    private readonly TimeSpan _containerDetailsCacheDuration;
    private readonly TimeSpan _dashboardCacheDuration;
    private readonly TimeSpan _staticDataCacheDuration;

    public DataCacheService(IMemoryCache cache, ILogger<DataCacheService> logger, IConfiguration configuration)
    {
        _cache = cache;
        _logger = logger;
        _containerListCacheDuration = TimeSpan.FromMinutes(configuration.GetValue<int>("Cache:ContainerListMinutes", 5));
        _containerDetailsCacheDuration = TimeSpan.FromMinutes(configuration.GetValue<int>("Cache:ContainerDetailsMinutes", 10));
        _dashboardCacheDuration = TimeSpan.FromMinutes(configuration.GetValue<int>("Cache:DashboardMinutes", 1));
        _staticDataCacheDuration = TimeSpan.FromHours(configuration.GetValue<int>("Cache:StaticDataHours", 1));
    }

    /// <summary>
    /// Get or create a cached value
    /// </summary>
    public async Task<T?> GetOrCreateAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? customExpiration = null)
    {
        if (_cache.TryGetValue(key, out T? cachedValue))
        {
            _logger.LogDebug("Cache hit for key: {Key}", key);
            return cachedValue;
        }

        _logger.LogDebug("Cache miss for key: {Key}. Fetching data...", key);

        var value = await factory();

        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = customExpiration ?? GetDefaultExpiration(key),
            SlidingExpiration = TimeSpan.FromMinutes(2),
            Size = 1
        };

        _cache.Set(key, value, cacheOptions);
        _logger.LogDebug("Cached data for key: {Key}", key);

        return value;
    }

    /// <summary>
    /// Get cached value without creating if missing
    /// </summary>
    public T? Get<T>(string key)
    {
        return _cache.TryGetValue(key, out T? value) ? value : default;
    }

    /// <summary>
    /// Set a value in cache
    /// </summary>
    public void Set<T>(string key, T value, TimeSpan? expiration = null)
    {
        var cacheOptions = new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = expiration ?? GetDefaultExpiration(key),
            Size = 1
        };

        _cache.Set(key, value, cacheOptions);
        _logger.LogDebug("Set cache for key: {Key}", key);
    }

    /// <summary>
    /// Remove a specific cache entry
    /// </summary>
    public void Remove(string key)
    {
        _cache.Remove(key);
        _logger.LogDebug("Removed cache for key: {Key}", key);
    }

    /// <summary>
    /// Clear all cache entries matching a pattern
    /// </summary>
    public void ClearPattern(string pattern)
    {
        // Note: MemoryCache doesn't support pattern clearing natively
        // This would require maintaining a separate index or using a different cache implementation
        _logger.LogWarning("Pattern-based cache clearing not implemented for pattern: {Pattern}", pattern);
    }

    /// <summary>
    /// Clear all cache entries
    /// </summary>
    public void ClearAll()
    {
        if (_cache is MemoryCache memoryCache)
        {
            memoryCache.Compact(1.0);
            _logger.LogInformation("Cleared all cache entries");
        }
    }

    // Cache key generators
    public static string GetContainerListKey(string? filter = null, string? scanner = null)
        => $"containers:list:{filter ?? "all"}:{scanner ?? "all"}";

    public static string GetContainerDetailsKey(string containerNumber)
        => $"containers:details:{containerNumber}";

    public static string GetDashboardKey()
        => "dashboard:data";

    public static string GetScannerListKey()
        => "scanners:list";

    public static string GetUserListKey()
        => "users:list";

    public static string GetRoleListKey()
        => "roles:list";

    public static string GetPermissionListKey()
        => "permissions:list";

    public static string GetICUMSStatusKey()
        => "icums:status";

    public static string GetQueueStatusKey(string queueType)
        => $"queue:status:{queueType}";

    private TimeSpan GetDefaultExpiration(string key)
    {
        return key switch
        {
            _ when key.StartsWith("containers:list") => _containerListCacheDuration,
            _ when key.StartsWith("containers:details") => _containerDetailsCacheDuration,
            _ when key.StartsWith("dashboard:") => _dashboardCacheDuration,
            _ when key.StartsWith("permissions:") || key.StartsWith("roles:") => _staticDataCacheDuration,
            _ => TimeSpan.FromMinutes(5) // Default
        };
    }
}

/// <summary>
/// Extension methods for DataCacheService registration
/// </summary>
public static class DataCacheServiceExtensions
{
    public static IServiceCollection AddDataCacheService(this IServiceCollection services)
    {
        services.AddSingleton<DataCacheService>();
        return services;
    }
}

