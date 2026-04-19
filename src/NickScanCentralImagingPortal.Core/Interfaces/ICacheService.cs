namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Distributed cache service for storing and retrieving cached data
    /// </summary>
    public interface ICacheService
    {
        /// <summary>
        /// Get cached value by key
        /// </summary>
        Task<T?> GetAsync<T>(string key, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Set value in cache with expiration
        /// </summary>
        Task SetAsync<T>(string key, T value, TimeSpan? expiration = null, CancellationToken cancellationToken = default) where T : class;

        /// <summary>
        /// Remove value from cache
        /// </summary>
        Task RemoveAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Remove multiple values from cache by pattern
        /// </summary>
        Task RemoveByPrefixAsync(string prefix, CancellationToken cancellationToken = default);

        /// <summary>
        /// Check if key exists in cache
        /// </summary>
        Task<bool> ExistsAsync(string key, CancellationToken cancellationToken = default);

        /// <summary>
        /// Get or set value in cache (cache-aside pattern)
        /// </summary>
        Task<T> GetOrSetAsync<T>(
            string key,
            Func<Task<T>> factory,
            TimeSpan? expiration = null,
            CancellationToken cancellationToken = default) where T : class;
    }
}

