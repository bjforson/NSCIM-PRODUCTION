using Microsoft.Extensions.Caching.Memory;

namespace NickScanCentralImagingPortal.Infrastructure.Caching
{
    /// <summary>
    /// Extension methods for IMemoryCache to enforce proper memory management
    /// </summary>
    public static class CacheExtensions
    {
        /// <summary>
        /// Sets a cache entry with MANDATORY size parameter to prevent unbounded memory growth
        /// </summary>
        /// <typeparam name="TItem">Type of the item to cache</typeparam>
        /// <param name="cache">The memory cache</param>
        /// <param name="key">Cache key</param>
        /// <param name="value">Value to cache</param>
        /// <param name="sizeInBytes">Estimated size in bytes (REQUIRED)</param>
        /// <param name="absoluteExpirationMinutes">Absolute expiration in minutes (default: 60)</param>
        /// <param name="slidingExpirationMinutes">Sliding expiration in minutes (default: null)</param>
        /// <returns>The cached value</returns>
        public static TItem SetWithSize<TItem>(
            this IMemoryCache cache,
            object key,
            TItem value,
            long sizeInBytes,
            int absoluteExpirationMinutes = 60,
            int? slidingExpirationMinutes = null)
        {
            if (sizeInBytes <= 0)
            {
                throw new ArgumentException("Cache size must be greater than 0", nameof(sizeInBytes));
            }

            var cacheEntryOptions = new MemoryCacheEntryOptions()
                .SetSize(sizeInBytes)
                .SetAbsoluteExpiration(TimeSpan.FromMinutes(absoluteExpirationMinutes));

            if (slidingExpirationMinutes.HasValue)
            {
                cacheEntryOptions.SetSlidingExpiration(TimeSpan.FromMinutes(slidingExpirationMinutes.Value));
            }

            return cache.Set(key, value, cacheEntryOptions);
        }

        /// <summary>
        /// Sets a cache entry for a string with automatic size calculation
        /// </summary>
        public static string SetString(
            this IMemoryCache cache,
            object key,
            string value,
            int absoluteExpirationMinutes = 60,
            int? slidingExpirationMinutes = null)
        {
            // Estimate: 2 bytes per character (Unicode)
            var estimatedSize = (value?.Length ?? 0) * 2;
            return cache.SetWithSize(key, value ?? string.Empty, estimatedSize, absoluteExpirationMinutes, slidingExpirationMinutes);
        }

        /// <summary>
        /// Sets a cache entry for a collection with automatic size calculation
        /// </summary>
        public static List<T> SetCollection<T>(
            this IMemoryCache cache,
            object key,
            List<T> value,
            long estimatedItemSize = 1024,
            int absoluteExpirationMinutes = 60,
            int? slidingExpirationMinutes = null)
        {
            // Estimate: base object overhead + (item count * estimated item size)
            var estimatedSize = 64 + ((value?.Count ?? 0) * estimatedItemSize);
            return cache.SetWithSize(key, value ?? new List<T>(), estimatedSize, absoluteExpirationMinutes, slidingExpirationMinutes);
        }

        /// <summary>
        /// Estimates the size of an object for caching purposes
        /// </summary>
        public static long EstimateSize<T>(T item)
        {
            if (item == null) return 0;

            var type = typeof(T);

            // String: 2 bytes per character
            if (type == typeof(string))
            {
                return ((string)(object)item).Length * 2;
            }

            // Collections
            if (item is System.Collections.ICollection collection)
            {
                // Base overhead + (count * average item size)
                return 64 + (collection.Count * 512);
            }

            // Primitives
            if (type.IsPrimitive)
            {
                return type == typeof(long) || type == typeof(ulong) || type == typeof(double) ? 8 : 4;
            }

            // Complex objects: default estimate
            return 1024;
        }

        /// <summary>
        /// Removes an item from cache by key
        /// </summary>
        public static void RemoveSafe(this IMemoryCache cache, object key)
        {
            try
            {
                cache.Remove(key);
            }
            catch (Exception)
            {
                // Swallow exceptions to prevent cache removal from crashing the application
            }
        }

        /// <summary>
        /// Gets or creates a cache entry with mandatory size
        /// </summary>
        public static async Task<TItem> GetOrCreateWithSizeAsync<TItem>(
            this IMemoryCache cache,
            object key,
            Func<Task<TItem>> factory,
            long sizeInBytes,
            int absoluteExpirationMinutes = 60,
            int? slidingExpirationMinutes = null)
        {
            if (!cache.TryGetValue(key, out TItem? cachedItem))
            {
                cachedItem = await factory();
                cache.SetWithSize(key, cachedItem, sizeInBytes, absoluteExpirationMinutes, slidingExpirationMinutes);
            }

            return cachedItem!;
        }
    }
}

