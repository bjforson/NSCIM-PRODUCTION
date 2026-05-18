using System.Diagnostics;

namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class PredictivePreloadWarmupProvider : ISystemCacheWarmupProvider
{
    private readonly IPredictivePreloadService _predictivePreloadService;

    public PredictivePreloadWarmupProvider(IPredictivePreloadService predictivePreloadService)
    {
        _predictivePreloadService = predictivePreloadService;
    }

    public string Name => "predictive-preload";

    public async Task<SystemCacheWarmupProviderResult> WarmupAsync(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        var result = await _predictivePreloadService.RunOnceAsync(cancellationToken);
        stopwatch.Stop();

        var warmedKeys = result.SuccessCount
            + result.Assignments.Sum(a => a.ContainerPreloadSuccessCount);

        if (!string.IsNullOrWhiteSpace(result.SkippedReason))
        {
            return SystemCacheWarmupProviderResult.SkippedRun(
                Name,
                stopwatch.Elapsed,
                result.SkippedReason);
        }

        return new SystemCacheWarmupProviderResult
        {
            ProviderName = Name,
            Success = result.FailureCount == 0,
            WarmedKeyCount = Math.Max(0, warmedKeys),
            Duration = stopwatch.Elapsed,
            Message = $"Candidates={result.CandidateCount}, Success={result.SuccessCount}, Failed={result.FailureCount}, Containers={result.Assignments.Sum(a => a.ContainerPreloadSuccessCount)}",
            Error = result.FailureCount > 0 ? "One or more predictive preload assignments failed" : null
        };
    }
}
