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
}
