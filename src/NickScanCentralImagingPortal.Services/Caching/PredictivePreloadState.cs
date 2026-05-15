namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class PredictivePreloadState
{
    private readonly object _sync = new();
    private PredictivePreloadRunResult? _lastRun;

    public bool IsRunning { get; private set; }
    public DateTime? LastStartedAtUtc { get; private set; }
    public DateTime? LastFinishedAtUtc { get; private set; }
    public string? LastError { get; private set; }
    public long TotalRuns { get; private set; }
    public long TotalSuccesses { get; private set; }
    public long TotalFailures { get; private set; }
    public long TotalSkipped { get; private set; }

    public void MarkStarted()
    {
        lock (_sync)
        {
            IsRunning = true;
            LastStartedAtUtc = DateTime.UtcNow;
            LastError = null;
        }
    }

    public void MarkCompleted(PredictivePreloadRunResult run)
    {
        lock (_sync)
        {
            IsRunning = false;
            LastFinishedAtUtc = run.FinishedAtUtc;
            _lastRun = run;
            TotalRuns++;
            TotalSuccesses += run.SuccessCount;
            TotalFailures += run.FailureCount;
            TotalSkipped += run.SkippedCount;
        }
    }

    public void MarkFailed(Exception ex)
    {
        lock (_sync)
        {
            IsRunning = false;
            LastFinishedAtUtc = DateTime.UtcNow;
            LastError = ex.Message;
            TotalRuns++;
            TotalFailures++;
        }
    }

    public PredictivePreloadStatusSnapshot Snapshot(PredictivePreloadOptions options)
    {
        lock (_sync)
        {
            return new PredictivePreloadStatusSnapshot
            {
                Enabled = options.Enabled,
                IsRunning = IsRunning,
                LastStartedAtUtc = LastStartedAtUtc,
                LastFinishedAtUtc = LastFinishedAtUtc,
                LastError = LastError,
                TotalRuns = TotalRuns,
                TotalSuccesses = TotalSuccesses,
                TotalFailures = TotalFailures,
                TotalSkipped = TotalSkipped,
                LastRun = _lastRun,
                Config = new PredictivePreloadConfigSnapshot
                {
                    BackgroundEnabled = options.BackgroundEnabled,
                    IntervalSeconds = options.IntervalSeconds,
                    StartupDelaySeconds = options.StartupDelaySeconds,
                    MaxAssignmentsPerRole = options.MaxAssignmentsPerRole,
                    MaxContainersPerGroup = options.MaxContainersPerGroup,
                    MaxConcurrentPreloads = options.MaxConcurrentPreloads,
                    FirstPageSize = options.FirstPageSize,
                    CacheTtlSeconds = options.CacheTtlSeconds,
                    Roles = options.Roles
                }
            };
        }
    }
}

public sealed class PredictivePreloadStatusSnapshot
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
    public PredictivePreloadRunResult? LastRun { get; set; }
    public PredictivePreloadConfigSnapshot Config { get; set; } = new();
}

public sealed class PredictivePreloadConfigSnapshot
{
    public bool BackgroundEnabled { get; set; }
    public int IntervalSeconds { get; set; }
    public int StartupDelaySeconds { get; set; }
    public int MaxAssignmentsPerRole { get; set; }
    public int MaxContainersPerGroup { get; set; }
    public int MaxConcurrentPreloads { get; set; }
    public int FirstPageSize { get; set; }
    public int CacheTtlSeconds { get; set; }
    public string[] Roles { get; set; } = Array.Empty<string>();
}
