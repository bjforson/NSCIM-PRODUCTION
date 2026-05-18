using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.API.Controllers;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.Caching;
using Xunit;

namespace NickScanCentralImagingPortal.Integration.Tests.Caching;

public sealed class SystemCacheControllerTests
{
    [Fact]
    public void GetStatus_ReturnsActiveImplementationAndOptions()
    {
        var harness = NewHarness(new SystemCacheOptions { UseSystemCacheService = true });
        var controller = harness.NewController(harness.SystemCache);

        var action = controller.GetStatus();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var status = Assert.IsType<SystemCacheStatusSnapshot>(ok.Value);
        Assert.True(status.UseSystemCacheService);
        Assert.True(status.SystemCacheActive);
        Assert.Equal(nameof(SystemCacheService), status.ActiveImplementation);
        Assert.True(status.L1Enabled);
        Assert.True(status.L2Enabled);
        Assert.False(status.WarmupEnabled);
    }

    [Fact]
    public void GetMetrics_ReturnsMetricsSnapshot()
    {
        var harness = NewHarness();
        harness.Metrics.RecordSet();
        var controller = harness.NewController(harness.SystemCache);

        var action = controller.GetMetrics();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var metrics = Assert.IsType<SystemCacheMetricsSnapshot>(ok.Value);
        Assert.Equal(1, metrics.Sets);
    }

    [Fact]
    public async Task InvalidatePrefix_RemovesIndexedKeysAndReportsDelta()
    {
        var harness = NewHarness();
        var controller = harness.NewController(harness.SystemCache);
        var prefix = UniqueKey("controller-prefix") + ":";

        await harness.SystemCache.SetAsync(prefix + "one", new CacheValue("one"));
        await harness.SystemCache.SetAsync(prefix + "two", new CacheValue("two"));

        var action = await controller.InvalidatePrefix(
            new SystemCacheInvalidatePrefixRequest(prefix),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var result = Assert.IsType<SystemCacheInvalidationResult>(ok.Value);
        Assert.Equal("prefix", result.Scope);
        Assert.Equal(2, result.RemovedKeys);
        Assert.False(await harness.SystemCache.ExistsAsync(prefix + "one"));
        Assert.False(await harness.SystemCache.ExistsAsync(prefix + "two"));
    }

    [Fact]
    public async Task InvalidateTag_RemovesTaggedKeysAndReportsDelta()
    {
        var harness = NewHarness();
        var controller = harness.NewController(harness.SystemCache);
        var tag = UniqueKey("controller-tag");
        var firstKey = UniqueKey("controller-tagged-one");
        var secondKey = UniqueKey("controller-tagged-two");

        await harness.SystemCache.SetWithTagsAsync(firstKey, new CacheValue("one"), [tag]);
        await harness.SystemCache.SetWithTagsAsync(secondKey, new CacheValue("two"), [tag]);

        var action = await controller.InvalidateTag(
            new SystemCacheInvalidateTagRequest(tag),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var result = Assert.IsType<SystemCacheInvalidationResult>(ok.Value);
        Assert.Equal("tag", result.Scope);
        Assert.Equal(2, result.RemovedKeys);
        Assert.False(await harness.SystemCache.ExistsAsync(firstKey));
        Assert.False(await harness.SystemCache.ExistsAsync(secondKey));
    }

    [Fact]
    public async Task InvalidateRequests_RejectEmptyValues()
    {
        var harness = NewHarness();
        var controller = harness.NewController(harness.SystemCache);

        var prefix = await controller.InvalidatePrefix(new SystemCacheInvalidatePrefixRequest(" "), CancellationToken.None);
        var tag = await controller.InvalidateTag(new SystemCacheInvalidateTagRequest(" "), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(prefix.Result);
        Assert.IsType<BadRequestObjectResult>(tag.Result);
    }

    [Fact]
    public void GetWarmup_ReturnsWarmupSnapshot()
    {
        var providerName = UniqueKey("warmup-provider");
        var harness = NewHarness(warmupProviders: [new StubWarmupProvider(providerName, 3)]);
        var controller = harness.NewController(harness.SystemCache);

        var action = controller.GetWarmup();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var snapshot = Assert.IsType<SystemCacheWarmupSnapshot>(ok.Value);
        Assert.Contains(providerName, snapshot.RegisteredProviders);
        Assert.False(snapshot.Enabled);
    }

    [Fact]
    public async Task RunWarmup_ExecutesRegisteredProviderWhenForced()
    {
        var providerName = UniqueKey("warmup-provider");
        var harness = NewHarness(warmupProviders: [new StubWarmupProvider(providerName, 3)]);
        var controller = harness.NewController(harness.SystemCache);

        var action = await controller.RunWarmup(new SystemCacheWarmupRunRequest(), CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var result = Assert.IsType<SystemCacheWarmupRunResult>(ok.Value);
        Assert.True(result.Started);
        Assert.Equal(1, result.ProviderCount);
        Assert.Equal(1, result.SuccessCount);
        Assert.Equal(3, result.WarmedKeyCount);
    }

    private static SystemCacheHarness NewHarness(
        SystemCacheOptions? options = null,
        ISystemCacheWarmupProvider[]? warmupProviders = null)
    {
        var distributedCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        var memoryCache = new MemoryCache(new MemoryCacheOptions { SizeLimit = 100 });
        var metrics = new SystemCacheMetrics();
        var effectiveOptions = options ?? new SystemCacheOptions();
        var systemCache = new SystemCacheService(
            distributedCache,
            memoryCache,
            Options.Create(effectiveOptions),
            metrics,
            NullLogger<SystemCacheService>.Instance);
        var warmupService = NewWarmupService(effectiveOptions, warmupProviders ?? []);

        return new SystemCacheHarness(systemCache, metrics, effectiveOptions, memoryCache, warmupService);
    }

    private static SystemCacheWarmupService NewWarmupService(
        SystemCacheOptions options,
        ISystemCacheWarmupProvider[] providers)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.Configure<SystemCacheOptions>(configured =>
        {
            configured.WarmupEnabled = options.WarmupEnabled;
            configured.WarmupStartupDelaySeconds = options.WarmupStartupDelaySeconds;
            configured.WarmupIntervalMinutes = options.WarmupIntervalMinutes;
            configured.WarmupJitterSeconds = options.WarmupJitterSeconds;
            configured.MaxWarmupConcurrency = options.MaxWarmupConcurrency;
        });
        services.AddSingleton<SystemCacheWarmupState>();
        services.AddSingleton<SystemCacheWarmupService>();
        foreach (var provider in providers)
        {
            services.AddSingleton<ISystemCacheWarmupProvider>(provider);
        }

        return services.BuildServiceProvider().GetRequiredService<SystemCacheWarmupService>();
    }

    private static string UniqueKey(string purpose) => $"test:system-cache-controller:{purpose}:{Guid.NewGuid():N}";

    private sealed record CacheValue(string Value);

    private sealed record SystemCacheHarness(
        SystemCacheService SystemCache,
        SystemCacheMetrics Metrics,
        SystemCacheOptions Options,
        MemoryCache MemoryCache,
        SystemCacheWarmupService WarmupService)
    {
        public SystemCacheController NewController(ICacheService activeCache)
        {
            return new SystemCacheController(
                activeCache,
                SystemCache,
                Metrics,
                WarmupService,
                Microsoft.Extensions.Options.Options.Create(Options),
                NullLogger<SystemCacheController>.Instance);
        }
    }

    private sealed class StubWarmupProvider : ISystemCacheWarmupProvider
    {
        private readonly int _warmedKeyCount;

        public StubWarmupProvider(string name, int warmedKeyCount)
        {
            Name = name;
            _warmedKeyCount = warmedKeyCount;
        }

        public string Name { get; }

        public Task<SystemCacheWarmupProviderResult> WarmupAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(SystemCacheWarmupProviderResult.Succeeded(
                Name,
                _warmedKeyCount,
                TimeSpan.FromMilliseconds(1)));
        }
    }
}
