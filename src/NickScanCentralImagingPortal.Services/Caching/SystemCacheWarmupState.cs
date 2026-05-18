namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class SystemCacheWarmupState
{
    private readonly object _sync = new();
    private SystemCacheWarmupRunResult? _lastRun;

    public bool IsRunning { get; private set; }
    public DateTime? LastStartedAtUtc { get; private set; }
    public DateTime? LastFinishedAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public long TotalRuns { get; private set; }
    public long TotalSuccesses { get; private set; }
    public long TotalFailures { get; private set; }
    public long TotalSkipped { get; private set; }

    public void MarkStarted(DateTime startedAtUtc)
    {
        lock (_sync)
        {
            IsRunning = true;
            LastStartedAtUtc = startedAtUtc;
            LastError = null;
        }
    }

    public void MarkCompleted(SystemCacheWarmupRunResult run)
    {
        lock (_sync)
        {
            IsRunning = false;
            LastFinishedAtUtc = run.FinishedAtUtc;
            LastError = run.Providers.FirstOrDefault(p => !p.Success)?.Error;
            _lastRun = run;
            TotalRuns++;

            if (run.Skipped || run.AlreadyRunning)
            {
                TotalSkipped++;
            }
            else if (run.FailureCount > 0)
            {
                TotalFailures++;
            }
            else
            {
                TotalSuccesses++;
            }
        }
    }

    public void MarkFailed(DateTime finishedAtUtc, Exception exception)
    {
        lock (_sync)
        {
            IsRunning = false;
            LastFinishedAtUtc = finishedAtUtc;
            LastError = exception.Message;
            TotalRuns++;
            TotalFailures++;
        }
    }

    public SystemCacheWarmupSnapshot Snapshot(
        SystemCacheOptions options,
        IReadOnlyList<string> registeredProviders)
    {
        lock (_sync)
        {
            return new SystemCacheWarmupSnapshot
            {
                Enabled = options.WarmupEnabled,
                IsRunning = IsRunning,
                LastStartedAtUtc = LastStartedAtUtc,
                LastFinishedAtUtc = LastFinishedAtUtc,
                LastError = LastError,
                TotalRuns = TotalRuns,
                TotalSuccesses = TotalSuccesses,
                TotalFailures = TotalFailures,
                TotalSkipped = TotalSkipped,
                RegisteredProviders = registeredProviders,
                LastRun = _lastRun,
                Config = new SystemCacheWarmupConfigSnapshot
                {
                    StartupDelaySeconds = options.WarmupStartupDelaySeconds,
                    IntervalMinutes = options.WarmupIntervalMinutes,
                    JitterSeconds = options.WarmupJitterSeconds,
                    MaxConcurrency = options.MaxWarmupConcurrency
                }
            };
        }
    }
}
