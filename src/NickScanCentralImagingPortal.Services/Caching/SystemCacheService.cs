using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class SystemCacheService : ISystemCacheService
{
    private const string LocalKeyPrefix = "SystemCache:L1:";
    private const string PrefixIndexKeyPrefix = "SystemCache:Index:Prefix:";
    private const string TagIndexKeyPrefix = "SystemCache:Index:Tag:";
    private static readonly ConcurrentDictionary<string, byte> KnownKeys = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> KeyLocks = new(StringComparer.Ordinal);

    private readonly IDistributedCache _distributedCache;
    private readonly IMemoryCache _memoryCache;
    private readonly ILogger<SystemCacheService> _logger;
    private readonly SystemCacheMetrics _metrics;
    private readonly SystemCacheOptions _options;
    private readonly TimeSpan _defaultExpiration;

    public SystemCacheService(
        IDistributedCache distributedCache,
        IMemoryCache memoryCache,
        IOptions<SystemCacheOptions> options,
        SystemCacheMetrics metrics,
        ILogger<SystemCacheService> logger)
    {
        _distributedCache = distributedCache;
        _memoryCache = memoryCache;
        _logger = logger;
        _metrics = metrics;
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
                _metrics.RecordL1Hit();
                _logger.LogDebug("System cache L1 hit for key: {Key}", key);
                return Deserialize<T>(localPayload, key);
            }

            if (!_options.UseDistributedCache)
            {
                _metrics.RecordMiss();
                _logger.LogDebug("System cache miss for key: {Key}", key);
                return null;
            }

            var distributedPayload = await _distributedCache.GetStringAsync(key, cancellationToken);
            if (string.IsNullOrWhiteSpace(distributedPayload))
            {
                _metrics.RecordMiss();
                _logger.LogDebug("System cache miss for key: {Key}", key);
                return null;
            }

            TrackLocalKey(key);
            SetLocalPayload(key, distributedPayload, _defaultExpiration);
            _metrics.RecordL2Hit();
            _logger.LogDebug("System cache L2 hit for key: {Key}", key);

            return Deserialize<T>(distributedPayload, key);
        }
        catch (Exception ex)
        {
            _metrics.RecordCacheError();
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
            await TrackKeyAsync(key, cancellationToken);
            _metrics.RecordSet();

            _logger.LogDebug(
                "Stored system cache value for key: {Key} with expiration: {Expiration}",
                key,
                effectiveExpiration);
        }
        catch (Exception ex)
        {
            _metrics.RecordCacheError();
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
            _metrics.RecordRemove();
            _logger.LogDebug("Removed system cache value for key: {Key}", key);
        }
        catch (Exception ex)
        {
            _metrics.RecordCacheError();
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
                .ToHashSet(StringComparer.Ordinal);

            foreach (var indexedKey in await ReadIndexedKeysAsync(GetPrefixIndexKey(prefix), cancellationToken))
            {
                if (indexedKey.StartsWith(prefix, StringComparison.Ordinal))
                {
                    matchingKeys.Add(indexedKey);
                }
            }

            foreach (var key in matchingKeys)
            {
                _memoryCache.Remove(GetLocalKey(key));

                if (_options.UseDistributedCache)
                {
                    await _distributedCache.RemoveAsync(key, cancellationToken);
                }

                KnownKeys.TryRemove(key, out _);
            }

            await RemoveIndexAsync(GetPrefixIndexKey(prefix), cancellationToken);
            _metrics.RecordPrefixInvalidation(matchingKeys.Count);
            _logger.LogInformation(
                "Removed {Count} system cache value(s) by prefix: {Prefix}",
                matchingKeys.Count,
                prefix);
        }
        catch (Exception ex)
        {
            _metrics.RecordCacheError();
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
            _metrics.RecordCacheError();
            _logger.LogError(ex, "Error checking if system cache key exists: {Key}", key);
            return false;
        }
    }

    public async Task SetWithTagsAsync<T>(
        string key,
        T value,
        IEnumerable<string> tags,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        await SetAsync(key, value, expiration, cancellationToken);
        await TrackTagsAsync(key, tags, cancellationToken);
    }

    public async Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(tag))
            {
                return;
            }

            var tagIndexKey = GetTagIndexKey(tag);
            var indexedKeys = await ReadIndexedKeysAsync(tagIndexKey, cancellationToken);
            var removedCount = 0;

            foreach (var key in indexedKeys)
            {
                _memoryCache.Remove(GetLocalKey(key));

                if (_options.UseDistributedCache)
                {
                    await _distributedCache.RemoveAsync(key, cancellationToken);
                }

                KnownKeys.TryRemove(key, out _);
                removedCount++;
            }

            await RemoveIndexAsync(tagIndexKey, cancellationToken);
            _metrics.RecordTagInvalidation(removedCount);

            _logger.LogInformation(
                "Removed {Count} system cache value(s) by tag: {Tag}",
                removedCount,
                tag);
        }
        catch (Exception ex)
        {
            _metrics.RecordCacheError();
            _logger.LogError(ex, "Error removing system cache values by tag: {Tag}", tag);
        }
    }

    public async Task<T> GetOrSetAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class
    {
        var cachedData = await GetAsync<T>(key, cancellationToken);
        if (cachedData is not null)
        {
            return cachedData;
        }

        if (!_options.EnableStampedeProtection)
        {
            return await CreateAndCacheAsync(key, factory, expiration, cancellationToken);
        }

        var keyLock = KeyLocks.GetOrAdd(key, _ => new SemaphoreSlim(1, 1));
        var acquired = false;
        var factoryStarted = false;

        try
        {
            var timeout = TimeSpan.FromSeconds(Math.Max(1, _options.StampedeLockTimeoutSeconds));
            _metrics.RecordStampedeWait();
            acquired = await keyLock.WaitAsync(timeout, cancellationToken);
            if (!acquired)
            {
                _metrics.RecordStampedeTimeout();
                _logger.LogWarning(
                    "Timed out waiting for system cache stampede lock for key: {Key}",
                    key);
                factoryStarted = true;
                return await CreateAndCacheAsync(key, factory, expiration, cancellationToken);
            }

            cachedData = await GetAsync<T>(key, cancellationToken);
            if (cachedData is not null)
            {
                _metrics.RecordStampedePrevented();
                return cachedData;
            }

            factoryStarted = true;
            return await CreateAndCacheAsync(key, factory, expiration, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex) when (!factoryStarted)
        {
            _metrics.RecordCacheError();
            _logger.LogError(ex, "Error in system cache GetOrSetAsync for key: {Key}", key);
            return await factory();
        }
        finally
        {
            if (acquired)
            {
                keyLock.Release();
                TryReleaseKeyLock(key, keyLock);
            }
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

    private void TrackLocalKey(string key)
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

    private async Task TrackKeyAsync(string key, CancellationToken cancellationToken)
    {
        TrackLocalKey(key);

        if (!ShouldUseDistributedIndex())
        {
            return;
        }

        foreach (var prefix in EnumeratePrefixIndexes(key))
        {
            await AddIndexKeyAsync(GetPrefixIndexKey(prefix), key, cancellationToken);
        }
    }

    private async Task TrackTagsAsync(
        string key,
        IEnumerable<string> tags,
        CancellationToken cancellationToken)
    {
        if (!ShouldUseDistributedIndex())
        {
            return;
        }

        foreach (var tag in tags.Where(tag => !string.IsNullOrWhiteSpace(tag)).Distinct(StringComparer.OrdinalIgnoreCase))
        {
            await AddIndexKeyAsync(GetTagIndexKey(tag), key, cancellationToken);
        }
    }

    private async Task<T> CreateAndCacheAsync<T>(
        string key,
        Func<Task<T>> factory,
        TimeSpan? expiration,
        CancellationToken cancellationToken) where T : class
    {
        T data;
        try
        {
            data = await factory();
        }
        catch
        {
            _metrics.RecordFactoryFailure();
            throw;
        }

        if (data is not null)
        {
            await SetAsync(key, data, expiration, cancellationToken);
        }

        return data;
    }

    private static void TryReleaseKeyLock(string key, SemaphoreSlim keyLock)
    {
        if (keyLock.CurrentCount != 1)
        {
            return;
        }

        KeyLocks.TryRemove(new KeyValuePair<string, SemaphoreSlim>(key, keyLock));
    }

    private async Task AddIndexKeyAsync(
        string indexKey,
        string key,
        CancellationToken cancellationToken)
    {
        var indexLock = KeyLocks.GetOrAdd(indexKey, _ => new SemaphoreSlim(1, 1));
        var acquired = false;

        try
        {
            await indexLock.WaitAsync(cancellationToken);
            acquired = true;
            var document = await ReadIndexDocumentAsync(indexKey, cancellationToken);

            if (!document.Keys.Contains(key, StringComparer.Ordinal))
            {
                document.Keys.Add(key);
            }

            var maxKeys = Math.Max(1, _options.MaxInvalidationIndexKeys);
            if (document.Keys.Count > maxKeys)
            {
                document.Keys = document.Keys
                    .Skip(document.Keys.Count - maxKeys)
                    .ToList();
            }

            document.UpdatedAtUtc = DateTimeOffset.UtcNow;
            await WriteIndexDocumentAsync(indexKey, document, cancellationToken);
        }
        catch (Exception ex)
        {
            _metrics.RecordCacheError();
            _logger.LogWarning(ex, "Error tracking system cache index key: {IndexKey}", indexKey);
        }
        finally
        {
            if (acquired)
            {
                indexLock.Release();
                TryReleaseKeyLock(indexKey, indexLock);
            }
        }
    }

    private async Task<IReadOnlyCollection<string>> ReadIndexedKeysAsync(
        string indexKey,
        CancellationToken cancellationToken)
    {
        if (!ShouldUseDistributedIndex())
        {
            return Array.Empty<string>();
        }

        var document = await ReadIndexDocumentAsync(indexKey, cancellationToken);
        return document.Keys
            .Where(key => !string.IsNullOrWhiteSpace(key))
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    private async Task<SystemCacheIndexDocument> ReadIndexDocumentAsync(
        string indexKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var payload = await _distributedCache.GetStringAsync(indexKey, cancellationToken);
            if (string.IsNullOrWhiteSpace(payload))
            {
                return new SystemCacheIndexDocument();
            }

            return JsonSerializer.Deserialize<SystemCacheIndexDocument>(payload)
                ?? new SystemCacheIndexDocument();
        }
        catch (Exception ex)
        {
            _metrics.RecordCacheError();
            _logger.LogWarning(ex, "Error reading system cache index: {IndexKey}", indexKey);
            return new SystemCacheIndexDocument();
        }
    }

    private Task WriteIndexDocumentAsync(
        string indexKey,
        SystemCacheIndexDocument document,
        CancellationToken cancellationToken)
    {
        var expiration = TimeSpan.FromMinutes(Math.Max(1, _options.InvalidationIndexExpirationMinutes));
        var payload = JsonSerializer.Serialize(document);

        return _distributedCache.SetStringAsync(
            indexKey,
            payload,
            new DistributedCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration
            },
            cancellationToken);
    }

    private Task RemoveIndexAsync(string indexKey, CancellationToken cancellationToken)
    {
        return ShouldUseDistributedIndex()
            ? _distributedCache.RemoveAsync(indexKey, cancellationToken)
            : Task.CompletedTask;
    }

    private bool ShouldUseDistributedIndex() =>
        _options.UseDistributedCache && _options.UseDistributedInvalidationIndex;

    private static IEnumerable<string> EnumeratePrefixIndexes(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            yield break;
        }

        yield return key;

        var segments = key.Split(':', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (segments.Length == 0)
        {
            yield break;
        }

        var current = segments[0];
        yield return current;

        for (var i = 1; i < segments.Length; i++)
        {
            yield return current + ":";
            current += ":" + segments[i];
            yield return current;
        }
    }

    private static string GetPrefixIndexKey(string prefix) =>
        PrefixIndexKeyPrefix + HashIndexName(prefix);

    private static string GetTagIndexKey(string tag) =>
        TagIndexKeyPrefix + HashIndexName(tag.Trim().ToUpperInvariant());

    private static string HashIndexName(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

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
