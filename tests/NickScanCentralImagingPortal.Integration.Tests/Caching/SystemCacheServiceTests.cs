using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Services.Caching;
using Xunit;

namespace NickScanCentralImagingPortal.Integration.Tests.Caching;

public sealed class SystemCacheServiceTests
{
    [Fact]
    public async Task GetAsync_HydratesL1FromDistributedCache()
    {
        var distributedCache = NewDistributedCache();
        using var firstMemoryCache = NewMemoryCache();
        using var secondMemoryCache = NewMemoryCache();

        var firstService = NewService(distributedCache, firstMemoryCache);
        var secondService = NewService(distributedCache, secondMemoryCache);
        var key = UniqueKey("hydrate");

        await firstService.SetAsync(key, new CacheValue("from-l2"), TimeSpan.FromMinutes(5));

        var fromDistributed = await secondService.GetAsync<CacheValue>(key);
        await distributedCache.RemoveAsync(key);
        var fromLocal = await secondService.GetAsync<CacheValue>(key);

        Assert.NotNull(fromDistributed);
        Assert.Equal("from-l2", fromDistributed.Value);
        Assert.NotNull(fromLocal);
        Assert.Equal("from-l2", fromLocal.Value);
    }

    [Fact]
    public async Task RemoveByPrefixAsync_RemovesTrackedKeysFromBothLayers()
    {
        var distributedCache = NewDistributedCache();
        using var memoryCache = NewMemoryCache();
        var service = NewService(distributedCache, memoryCache);
        var prefix = UniqueKey("prefix") + ":";
        var otherKey = UniqueKey("other");

        await service.SetAsync(prefix + "one", new CacheValue("one"));
        await service.SetAsync(prefix + "two", new CacheValue("two"));
        await service.SetAsync(otherKey, new CacheValue("other"));

        await service.RemoveByPrefixAsync(prefix);

        Assert.False(await service.ExistsAsync(prefix + "one"));
        Assert.False(await service.ExistsAsync(prefix + "two"));
        Assert.True(await service.ExistsAsync(otherKey));
    }

    [Fact]
    public async Task UseDistributedCacheFalse_StoresOnlyInLocalMemory()
    {
        var distributedCache = NewDistributedCache();
        using var firstMemoryCache = NewMemoryCache();
        using var secondMemoryCache = NewMemoryCache();
        var options = new SystemCacheOptions
        {
            UseDistributedCache = false,
            UseL1MemoryCache = true
        };
        var firstService = NewService(distributedCache, firstMemoryCache, options);
        var secondService = NewService(distributedCache, secondMemoryCache, options);
        var key = UniqueKey("local-only");

        await firstService.SetAsync(key, new CacheValue("local"));

        Assert.NotNull(await firstService.GetAsync<CacheValue>(key));
        Assert.Null(await secondService.GetAsync<CacheValue>(key));
        Assert.Null(await distributedCache.GetStringAsync(key));
    }

    [Fact]
    public async Task GetOrSetAsync_CoalescesConcurrentMissesForSameKey()
    {
        var distributedCache = NewDistributedCache();
        using var memoryCache = NewMemoryCache();
        var service = NewService(distributedCache, memoryCache);
        var key = UniqueKey("stampede");
        var factoryCalls = 0;

        async Task<CacheValue> Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Delay(50);
            return new CacheValue("coalesced");
        }

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => service.GetOrSetAsync(key, Factory, TimeSpan.FromMinutes(5)))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var metrics = service.Metrics.Snapshot();

        Assert.All(results, result => Assert.Equal("coalesced", result.Value));
        Assert.Equal(1, factoryCalls);
        Assert.True(metrics.StampedePrevented > 0);
    }

    [Fact]
    public async Task GetOrSetAsync_DoesNotCoalesceWhenStampedeProtectionDisabled()
    {
        var distributedCache = NewDistributedCache();
        using var memoryCache = NewMemoryCache();
        var service = NewService(
            distributedCache,
            memoryCache,
            new SystemCacheOptions { EnableStampedeProtection = false });
        var key = UniqueKey("stampede-disabled");
        var factoryCalls = 0;

        async Task<CacheValue> Factory()
        {
            Interlocked.Increment(ref factoryCalls);
            await Task.Delay(50);
            return new CacheValue("not-coalesced");
        }

        var tasks = Enumerable.Range(0, 20)
            .Select(_ => service.GetOrSetAsync(key, Factory, TimeSpan.FromMinutes(5)))
            .ToArray();

        var results = await Task.WhenAll(tasks);
        var metrics = service.Metrics.Snapshot();

        Assert.All(results, result => Assert.Equal("not-coalesced", result.Value));
        Assert.True(factoryCalls > 1);
        Assert.Equal(0, metrics.StampedePrevented);
    }

    [Fact]
    public async Task GetOrSetAsync_FactoryExceptionDoesNotPoisonKeyLock()
    {
        var distributedCache = NewDistributedCache();
        using var memoryCache = NewMemoryCache();
        var service = NewService(distributedCache, memoryCache);
        var key = UniqueKey("factory-failure");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.GetOrSetAsync<CacheValue>(
                key,
                () => throw new InvalidOperationException("source failed")));
        var afterFailure = service.Metrics.Snapshot();

        var recovered = await service.GetOrSetAsync(
            key,
            () => Task.FromResult(new CacheValue("recovered")));

        Assert.Equal("recovered", recovered.Value);
        Assert.Equal(1, afterFailure.FactoryFailures);
    }

    [Fact]
    public void SystemCacheOptions_DefaultsToDisabledRegistrationFlag()
    {
        var options = new SystemCacheOptions();

        Assert.False(options.UseSystemCacheService);
        Assert.True(options.EnableStampedeProtection);
    }

    [Fact]
    public void SystemCacheKeyRegistry_BuildsNormalizedKeys()
    {
        var key = SystemCacheKeyRegistry.Container(" mscu1234567 ", "summary");

        Assert.Equal("container-details:MSCU1234567:summary", key);
    }

    private static MemoryDistributedCache NewDistributedCache() =>
        new(Options.Create(new MemoryDistributedCacheOptions()));

    private static MemoryCache NewMemoryCache() =>
        new(new MemoryCacheOptions { SizeLimit = 100 });

    private static TestSystemCacheService NewService(
        IDistributedCache distributedCache,
        IMemoryCache memoryCache,
        SystemCacheOptions? options = null)
    {
        var metrics = new SystemCacheMetrics();
        var service = new SystemCacheService(
            distributedCache,
            memoryCache,
            Options.Create(options ?? new SystemCacheOptions()),
            metrics,
            NullLogger<SystemCacheService>.Instance);

        return new TestSystemCacheService(service, metrics);
    }

    private static string UniqueKey(string purpose) => $"test:system-cache:{purpose}:{Guid.NewGuid():N}";

    private sealed record CacheValue(string Value);

    private sealed record TestSystemCacheService(SystemCacheService Service, SystemCacheMetrics Metrics)
    {
        public Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class =>
            Service.GetAsync<T>(key, cancellationToken);

        public Task SetAsync<T>(
            string key,
            T value,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default) where T : class =>
            Service.SetAsync(key, value, expiration, cancellationToken);

        public Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default) =>
            Service.RemoveByPrefixAsync(prefix, cancellationToken);

        public Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default) =>
            Service.ExistsAsync(key, cancellationToken);

        public Task<T> GetOrSetAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default) where T : class =>
            Service.GetOrSetAsync(key, factory, expiration, cancellationToken);
    }
}
