using System.Runtime.CompilerServices;
using Xunit;

namespace NickScanCentralImagingPortal.Core.Tests.Architecture;

public class AssignmentQueueHardeningTests
{
    [Fact]
    public void ImageAnalysisController_LastAccessKeepAliveUsesParameterizedUtcUpdates()
    {
        var controller = ReadRepoFile("src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisController.cs");

        Assert.False(
            controller.Contains("LastAccessedAtUtc = now() AT TIME ZONE 'UTC'", StringComparison.OrdinalIgnoreCase),
            "LastAccessedAtUtc updates must bind a UTC value or use a timestamptz-safe expression, not now() AT TIME ZONE 'UTC'.");

        var keepAlive = Slice(
            NormalizeLineEndings(controller),
            "private async Task UpdateLastAccessedForCachedAssignments",
            "/// <summary>\n        /// Get available groups");

        Assert.Contains("ExecuteUpdateAsync", keepAlive);
        Assert.Contains("DateTime.UtcNow", keepAlive);
        Assert.DoesNotContain("ExecuteSqlRawAsync", keepAlive);
        Assert.DoesNotContain("username.Replace", keepAlive);
        Assert.DoesNotContain("userRole.Replace", keepAlive);
    }

    [Fact]
    public void ReadyGroupsCacheService_SyncInvalidationWrappersDoNotBlockOnAsync()
    {
        var cacheService = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/ReadyGroupsCacheService.cs");

        Assert.DoesNotContain(".GetAwaiter().GetResult()", cacheService);
        Assert.Contains("InvalidateCacheBestEffortAsync", cacheService);
        Assert.Contains("InvalidateAllCachesBestEffortAsync", cacheService);
    }

    [Fact]
    public void ReadyGroupsCacheService_DoesNotCacheEmptyQueuesByDefault()
    {
        var cacheService = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/ReadyGroupsCacheService.cs");
        var managementController = ReadRepoFile("src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisManagementController.cs");

        Assert.Contains("_cacheEmptyResults", cacheService);
        Assert.Contains("ReadyGroupsCache:CacheEmptyResults", cacheService);
        Assert.Contains("readyGroups.Count > 0 || _cacheEmptyResults", cacheService);
        Assert.Contains("[CACHE-SKIP] Empty ready groups", cacheService);
        Assert.DoesNotContain("Cache empty result too", cacheService);

        Assert.Contains("TimeSpan.FromSeconds(30)", managementController);
        Assert.Contains("result.Count > 0", managementController);
        Assert.Contains("[CACHE SKIP] Ready groups returned empty", managementController);
        Assert.DoesNotContain("TimeSpan.FromMinutes(2)", managementController);
    }

    [Fact]
    public void AssignmentStateChanges_AwaitReadyGroupsCacheInvalidation()
    {
        var orchestrator = ReadRepoFile("src/NickScanCentralImagingPortal.Services/ImageAnalysis/ImageAnalysisOrchestratorService.cs");
        var decisionController = ReadRepoFile("src/NickScanCentralImagingPortal.API/Controllers/ImageAnalysisDecisionController.cs");

        Assert.DoesNotContain("_readyGroupsCache.InvalidateCache(roleName, eligibleStatus);", orchestrator);
        Assert.Contains("await _readyGroupsCache.InvalidateCacheAsync(roleName, eligibleStatus, stoppingToken);", orchestrator);
        Assert.Contains("AnalysisStatuses.Ready", orchestrator);
        Assert.Contains("AnalysisStatuses.AnalystCompleted", orchestrator);

        Assert.DoesNotContain("_readyGroupsCache?.InvalidateCache(\"Audit\", \"AnalystCompleted\")", decisionController);
        Assert.Contains("await _readyGroupsCache.InvalidateCacheAsync(", decisionController);
        Assert.Contains("AnalysisStatuses.AnalystCompleted", decisionController);
    }

    [Fact]
    public void RedisCacheService_PrefixInvalidationRemovesTrackedKeys()
    {
        var cacheService = ReadRepoFile("src/NickScanCentralImagingPortal.Services/Caching/RedisCacheService.cs");

        Assert.DoesNotContain("not fully implemented", cacheService);
        Assert.Contains("ConcurrentDictionary<string, byte>", cacheService);
        Assert.Contains("_knownKeys[key] = 0", cacheService);
        Assert.Contains("StartsWith(prefix, StringComparison.Ordinal)", cacheService);
        Assert.Contains("_knownKeys.TryRemove(key, out _)", cacheService);
    }

    private static string ReadRepoFile(string relativePath, [CallerFilePath] string callerPath = "")
    {
        return File.ReadAllText(Path.Combine(ResolveRepoRoot(callerPath), relativePath));
    }

    private static string ResolveRepoRoot(string callerPath)
    {
        var dir = Path.GetDirectoryName(callerPath);
        for (var i = 0; i < 8 && dir != null; i++)
        {
            if (Directory.Exists(Path.Combine(dir, "src")) && Directory.Exists(Path.Combine(dir, "tests")))
            {
                return dir;
            }

            dir = Path.GetDirectoryName(dir);
        }

        throw new DirectoryNotFoundException($"Could not resolve repository root from caller path {callerPath}.");
    }

    private static string NormalizeLineEndings(string value)
    {
        return value.Replace("\r\n", "\n");
    }

    private static string Slice(string value, string startMarker, string endMarker)
    {
        var start = value.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0, $"Start marker not found: {startMarker}");

        var end = value.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start, $"End marker not found after {startMarker}: {endMarker}");

        return value[start..end];
    }
}
