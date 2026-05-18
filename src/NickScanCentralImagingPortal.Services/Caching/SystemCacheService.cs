using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class SystemCacheService : ICacheService
{
    private const string LocalKeyPrefix = "SystemCache:L1:";
    private static readonly ConcurrentDictionary<string, byte> KnownKeys = new(StringComparer.Ordinal);

    private readonly IDistributedCache _distributedCache;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<SystemCacheService> _logger;
    private readonly SystemCacheOptions _options;
    private readonly TimeSpan _defaultExpiration;

    public SystemCacheService(
        IDistributedCache distributedCache,
        IMemoryCache memoryCache,
        IOptions<SystemCacheOptions> options,
        ILogger<SystemCacheService> logger)
    {
        _distributedCache = distributedCache;
        _memoryCache = memoryCache;
        _logger = logger;
        _options = options.Value;
        _defaultExpiration = TimeSpan.FromMinutes(Math.Max(1, _options.DefaultExpirationMinutes));
    }

    public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            if (_options.UseL1MemoryCache &&
                _memoryCache.TryGetValue(GetLocalKey(key), out string? localPayload) &&
                !string.IsNullOrWhiteSpace(localPayload))
            {
                _logger.LogDebug("System cache L1 hit for key: {Key}", key);
                return Deserialize<T>(localPayload, key);
            }

            if (!_options.UseDistributedCache)
            {
                _logger.LogDebug("System cache miss for key: {Key}", key);
                return null;
            }

            var distributedPayload = await _distributedCache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrWhiteSpace(distributedPayload))
            {
                _logger.LogDebug("System cache miss for key: {Key}", key);
                return null;
            }

            TrackKey(key);
            SetLocalPayload(key, distributedPayload, _defaultExpiration);
            _logger.LogDebug("System cache L2 hit for key: {Key}", key);

            return Deserialize<T>(distributedPayload, key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting system cache value for key: {Key}", key);
            return null;
        }
    }

    public async Task SetAsync<T>(
        string key,
        T value,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            if (value is null)
            {
                return;
            }

            var effectiveExpiration = NormalizeExpiration(expiration);
            var payload = JsonSerializer.Serialize(value);

            if (_options.UseDistributedCache)
            {
                await _distributedCache.SetStringAsync(
                    key,
                    payload,
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = effectiveExpiration
                    },
                    cancellationToken);
            }

            SetLocalPayload(key, payload, effectiveExpiration);
            TrackKey(key);

            _logger.LogDebug(
                "Stored system cache value for key: {Key} with expiration: {Expiration}",
                key,
                effectiveExpiration);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error setting system cache value for key: {Key}", key);
        }
    }

    public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            _memoryCache.Remove(GetLocalKey(key));

            if (_options.UseDistributedCache)
            {
                await _distributedCache.RemoveAsync(key, cancellationToken);
            }

            KnownKeys.TryRemove(key, out _);
            _logger.LogDebug("Removed system cache value for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing system cache value for key: {Key}", key);
        }
    }

    public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(prefix))
            {
                return;
            }

            var matchingKeys = KnownKeys.Keys
                .Where(key => key.StartsWith(prefix, StringComparison.Ordinal))
                .ToList();

            foreach (var key in matchingKeys)
            {
                _memoryCache.Remove(GetLocalKey(key));

                if (_options.UseDistributedCache)
                {
                    await _distributedCache.RemoveAsync(key, cancellationToken);
                }

                KnownKeys.TryRemove(key, out _);
            }

            _logger.LogInformation(
                "Removed {Count} system cache value(s) by prefix: {Prefix}",
                matchingKeys.Count,
                prefix);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing system cache values by prefix: {Prefix}", prefix);
        }
    }

    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        try
        {
            if (_options.UseL1MemoryCache && _memoryCache.TryGetValue(GetLocalKey(key), out string? localPayload))
            {
                return !string.IsNullOrWhiteSpace(localPayload);
            }

            if (!_options.UseDistributedCache)
            {
                return false;
            }

            var distributedPayload = await _distributedCache.GetStringAsync(key, cancellationToken);
            return !string.IsNullOrWhiteSpace(distributedPayload);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking if system cache key exists: {Key}", key);
            return false;
        }
    }

    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        try
        {
            var cachedData = await GetAsync<T>(key, cancellationToken);
            if (cachedData is not null)
            {
                return cachedData;
            }

            var data = await factory();
            if (data is not null)
            {
                await SetAsync(key, data, expiration, cancellationToken);
            }

            return data;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in system cache GetOrSetAsync for key: {Key}", key);
            return await factory();
        }
    }

    private void SetLocalPayload(string key, string payload, TimeSpan expiration)
    {
        if (!_options.UseL1MemoryCache)
        {
            return;
        }

        var localExpiration = GetLocalExpiration(expiration);
        var sizeUnits = Math.Max(1, _options.DefaultL1SizeUnits);

        _memoryCache.Set(
            GetLocalKey(key),
            payload,
            new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = localExpiration,
                Size = sizeUnits
            });
    }

    private void TrackKey(string key)
    {
        if (!_options.TrackKeysForPrefixInvalidation)
        {
            return;
        }

        if (key.Length > Math.Max(1, _options.MaxTrackedKeyLength))
        {
            return;
        }

        KnownKeys[key] = 0;
    }

    private T? Deserialize<T>(string payload, string key) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(payload);
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Invalid system cache payload for key: {Key}", key);
            _memoryCache.Remove(GetLocalKey(key));
            return null;
        }
    }

    private TimeSpan NormalizeExpiration(TimeSpan? expiration)
    {
        if (expiration is { } value && value > TimeSpan.Zero)
        {
            return value;
        }

        return _defaultExpiration;
    }

    private TimeSpan GetLocalExpiration(TimeSpan distributedExpiration)
    {
        var configuredSeconds = Math.Max(1, _options.L1ExpirationSeconds);
        var configuredExpiration = TimeSpan.FromSeconds(configuredSeconds);
        return configuredExpiration < distributedExpiration
            ? configuredExpiration
            : distributedExpiration;
    }

    private static string GetLocalKey(string key) => LocalKeyPrefix + key;
}
