using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Caching
{
    /// <summary>
    /// Redis-based distributed cache service implementation
    /// </summary>
    public class RedisCacheService : ICacheService
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger<RedisCacheService> _logger;
        private readonly TimeSpan _defaultExpiration = TimeSpan.FromMinutes(30);

        public RedisCacheService(
            IDistributedCache cache,
            ILogger<RedisCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class
        {
            try
            {
                var cachedData = await _cache.GetStringAsync(key, cancellationToken);

                if (string.IsNullOrEmpty(cachedData))
                {
                    _logger.LogDebug("Cache miss for key: {Key}", key);
                    return null;
                }

                _logger.LogDebug("Cache hit for key: {Key}", key);
                return JsonSerializer.Deserialize<T>(cachedData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cached value for key: {Key}", key);
                return null; // Fail gracefully - don't break the application
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
                var serializedData = JsonSerializer.Serialize(value);
                var options = new DistributedCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = expiration ?? _defaultExpiration
                };

                await _cache.SetStringAsync(key, serializedData, options, cancellationToken);
                _logger.LogDebug("Cached value for key: {Key} with expiration: {Expiration}",
                    key, expiration ?? _defaultExpiration);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting cached value for key: {Key}", key);
                // Fail gracefully - caching is not critical
            }
        }

        public async Task RemoveAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                await _cache.RemoveAsync(key, cancellationToken);
                _logger.LogDebug("Removed cached value for key: {Key}", key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cached value for key: {Key}", key);
            }
        }

        public async Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default)
        {
            try
            {
                // Note: This requires StackExchange.Redis directly for pattern-based deletion
                // For now, log a warning - implement when needed with StackExchange.Redis IConnectionMultiplexer
                _logger.LogWarning("RemoveByPrefixAsync not fully implemented. Prefix: {Prefix}", prefix);
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cached values by prefix: {Prefix}", prefix);
            }
        }

        public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default)
        {
            try
            {
                var data = await _cache.GetStringAsync(key, cancellationToken);
                return !string.IsNullOrEmpty(data);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if key exists: {Key}", key);
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
                // Try to get from cache first
                var cachedData = await GetAsync<T>(key, cancellationToken);
                if (cachedData != null)
                {
                    return cachedData;
                }

                // Cache miss - get from factory
                _logger.LogDebug("Cache miss for key: {Key}, fetching from source", key);
                var data = await factory();

                // Cache the result
                if (data != null)
                {
                    await SetAsync(key, data, expiration, cancellationToken);
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in GetOrSetAsync for key: {Key}", key);
                // On error, fall back to factory
                return await factory();
            }
        }
    }
}

