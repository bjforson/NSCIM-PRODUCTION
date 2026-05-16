using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Services.Caching;
using Xunit;

namespace NickScanCentralImagingPortal.Integration.Tests.Caching;

public class RedisCacheServiceTests
{
    [Fact]
    public async Task RemoveByPrefixAsync_RemovesTrackedMatchingKeysOnly()
    {
        var distributedCache = new MemoryDistributedCache(
            Options.Create(new MemoryDistributedCacheOptions()));
        var cacheService = new RedisCacheService(
            distributedCache,
            NullLogger<RedisCacheService>.Instance);

        await cacheService.SetAsync("ReadyGroups:Analyst:Ready", new CacheValue("analyst"));
        await cacheService.SetAsync("ReadyGroups:Audit:AnalystCompleted", new CacheValue("audit"));
        await cacheService.SetAsync("preload:role:Analyst:assignments", new CacheValue("preload"));

        await cacheService.RemoveByPrefixAsync("ReadyGroups");

        Assert.False(await cacheService.ExistsAsync("ReadyGroups:Analyst:Ready"));
        Assert.False(await cacheService.ExistsAsync("ReadyGroups:Audit:AnalystCompleted"));
        Assert.True(await cacheService.ExistsAsync("preload:role:Analyst:assignments"));
    }

    private sealed record CacheValue(string Value);
}
