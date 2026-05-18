namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class SystemCacheOptions
{
    public const string SectionName = "SystemCache";

    public bool UseSystemCacheService { get; set; }
    public bool UseL1MemoryCache { get; set; } = true;
    public bool UseDistributedCache { get; set; } = true;
    public int DefaultExpirationMinutes { get; set; } = 30;
    public int L1ExpirationSeconds { get; set; } = 60;
    public long DefaultL1SizeUnits { get; set; } = 1;
    public bool TrackKeysForPrefixInvalidation { get; set; } = true;
    public int MaxTrackedKeyLength { get; set; } = 512;
    public bool EnableStampedeProtection { get; set; } = true;
    public int StampedeLockTimeoutSeconds { get; set; } = 30;
    public bool UseDistributedInvalidationIndex { get; set; } = true;
    public int InvalidationIndexExpirationMinutes { get; set; } = 120;
    public int MaxInvalidationIndexKeys { get; set; } = 5000;
    public bool WarmupEnabled { get; set; }
    public int WarmupStartupDelaySeconds { get; set; } = 30;
    public int WarmupIntervalMinutes { get; set; } = 15;
    public int WarmupJitterSeconds { get; set; } = 30;
    public int MaxWarmupConcurrency { get; set; } = 2;
}
