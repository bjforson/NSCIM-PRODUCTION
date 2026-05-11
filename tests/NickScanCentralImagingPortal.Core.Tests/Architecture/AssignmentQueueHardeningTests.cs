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
