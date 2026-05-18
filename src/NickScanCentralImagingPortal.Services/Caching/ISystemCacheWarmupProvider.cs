namespace NickScanCentralImagingPortal.Services.Caching;

public interface ISystemCacheWarmupProvider
{
    string Name { get; }

    Task<SystemCacheWarmupProviderResult> WarmupAsync(CancellationToken cancellationToken = default);
}
