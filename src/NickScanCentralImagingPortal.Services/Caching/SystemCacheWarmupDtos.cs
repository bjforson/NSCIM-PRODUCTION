namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class SystemCacheWarmupRunResult
{
    public bool Enabled { get; set; }
    public bool Force { get; set; }
    public string Trigger { get; set; } = string.Empty;
    public bool Started { get; set; }
    public bool Skipped { get; set; }
    public bool AlreadyRunning { get; set; }
    public string? SkippedReason { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime? FinishedAtUtc { get; set; }
    public int ProviderCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int SkippedCount { get; set; }
    public int WarmedKeyCount { get; set; }
    public List<SystemCacheWarmupProviderResult> Providers { get; set; } = new();
}

public sealed class SystemCacheWarmupProviderResult
{
    public string ProviderName { get; set; } = string.Empty;
    public bool Success { get; set; }
    public bool Skipped { get; set; }
    public int WarmedKeyCount { get; set; }
    public TimeSpan Duration { get; set; }
    public string? Message { get; set; }
    public string? Error { get; set; }

    public static SystemCacheWarmupProviderResult Succeeded(
        string providerName,
        int warmedKeyCount,
        TimeSpan duration,
        string? message = null)
    {
        return new SystemCacheWarmupProviderResult
        {
            ProviderName = providerName,
            Success = true,
            WarmedKeyCount = Math.Max(0, warmedKeyCount),
            Duration = duration,
            Message = message
        };
    }

    public static SystemCacheWarmupProviderResult SkippedRun(
        string providerName,
        TimeSpan duration,
        string message)
    {
        return new SystemCacheWarmupProviderResult
        {
            ProviderName = providerName,
            Success = true,
            Skipped = true,
            Duration = duration,
            Message = message
        };
    }

    public static SystemCacheWarmupProviderResult Failed(
        string providerName,
        TimeSpan duration,
        Exception exception)
    {
        return new SystemCacheWarmupProviderResult
        {
            ProviderName = providerName,
            Success = false,
            Duration = duration,
            Error = exception.Message
        };
    }
}

public sealed class SystemCacheWarmupSnapshot
{
    public bool Enabled { get; set; }
    public bool IsRunning { get; set; }
    public DateTime? LastStartedAtUtc { get; set; }
    public DateTime? LastFinishedAtUtc { get; set; }
    public string? LastError { get; set; }
    public long TotalRuns { get; set; }
    public long TotalSuccesses { get; set; }
    public long TotalFailures { get; set; }
    public long TotalSkipped { get; set; }
    public IReadOnlyList<string> RegisteredProviders { get; set; } = Array.Empty<string>();
    public SystemCacheWarmupRunResult? LastRun { get; set; }
    public SystemCacheWarmupConfigSnapshot Config { get; set; } = new();
}

public sealed class SystemCacheWarmupConfigSnapshot
{
    public int StartupDelaySeconds { get; set; }
    public int IntervalMinutes { get; set; }
    public int JitterSeconds { get; set; }
    public int MaxConcurrency { get; set; }
}

public sealed record SystemCacheWarmupRunRequest(bool Force = true);
