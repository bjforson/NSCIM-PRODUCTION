using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Caching;

public interface ISystemCacheService : ICacheService
{
    Task SetWithTagsAsync<T>(
        string key,
        T value,
        IEnumerable<string> tags,
        TimeSpan? expiration = null,
        CancellationToken cancellationToken = default) where T : class;

    Task RemoveByTagAsync(string tag, CancellationToken cancellationToken = default);
}
