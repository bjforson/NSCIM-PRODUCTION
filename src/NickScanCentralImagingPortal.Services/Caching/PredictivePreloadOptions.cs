namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class PredictivePreloadOptions
{
    public const string SectionName = "PredictivePreload";

    public bool Enabled { get; set; } = true;
    public bool BackgroundEnabled { get; set; } = true;
    public int IntervalSeconds { get; set; } = 20;
    public int StartupDelaySeconds { get; set; } = 20;
    public int MaxAssignmentsPerRole { get; set; } = 5;
    public int MaxContainersPerGroup { get; set; } = 20;
    public int MaxConcurrentPreloads { get; set; } = 4;
    public int FirstPageSize { get; set; } = 25;
    public int CacheTtlSeconds { get; set; } = 300;
    public int FailureBackoffSeconds { get; set; } = 120;
    public bool PreloadContainerSummary { get; set; } = true;
    public bool PreloadCargoGroupSummary { get; set; } = true;
    public bool PreloadScannerFirstPage { get; set; } = true;
    public bool PreloadIcumsFirstPage { get; set; } = true;
    public bool PreloadBoeSummary { get; set; } = true;
    public bool PreloadImageMetadata { get; set; } = true;
    public bool PreloadFullImages { get; set; }
    public int FullImageMaxCount { get; set; }
    public int SkipWhenQueueDepthBelow { get; set; }
    public int SkipWhenCpuAbovePercent { get; set; } = 85;
    public int SkipWhenDbLatencyAboveMs { get; set; } = 1500;
    public string[] Roles { get; set; } = ["Analyst", "Audit"];
}
