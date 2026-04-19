using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.ContainerValidation
{
    /// <summary>
    /// Caches ICUMS data responses to reduce API calls and improve performance
    /// </summary>
    public interface IICUMSDataCacheService
    {
        Task<BOEDocument?> GetCachedBOEDocumentAsync(string containerNumber);
        Task SetCachedBOEDocumentAsync(string containerNumber, BOEDocument boeDocument, TimeSpan? expiration = null);
        void InvalidateCache(string containerNumber);
        void ClearCache();
        CacheStatistics GetCacheStatistics();
    }

    /// <summary>
    /// ICUMSDataCacheService backed by ICacheService (distributed cache).
    /// Supports multi-instance deployments via Redis when enabled, falls back to
    /// in-memory distributed cache otherwise.
    /// </summary>
    public class ICUMSDataCacheService : IICUMSDataCacheService
    {
        private readonly ICacheService _cache;
        private readonly ILogger<ICUMSDataCacheService> _logger;
        private int _cacheHits = 0;
        private int _cacheMisses = 0;
        private const string CACHE_KEY_PREFIX = "icums:boe:";

        public ICUMSDataCacheService(ICacheService cache, ILogger<ICUMSDataCacheService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task<BOEDocument?> GetCachedBOEDocumentAsync(string containerNumber)
        {
            var cacheKey = $"{CACHE_KEY_PREFIX}{containerNumber}";

            var cachedDocument = await _cache.GetAsync<BOEDocument>(cacheKey);
            if (cachedDocument != null)
            {
                Interlocked.Increment(ref _cacheHits);
                _logger.LogDebug("[ICUMS-CACHE] Cache HIT for container: {ContainerNumber}", containerNumber);
                return cachedDocument;
            }

            Interlocked.Increment(ref _cacheMisses);
            _logger.LogDebug("[ICUMS-CACHE] Cache MISS for container: {ContainerNumber}", containerNumber);
            return null;
        }

        public async Task SetCachedBOEDocumentAsync(string containerNumber, BOEDocument boeDocument, TimeSpan? expiration = null)
        {
            var cacheKey = $"{CACHE_KEY_PREFIX}{containerNumber}";
            var cacheExpiration = expiration ?? TimeSpan.FromHours(24);

            await _cache.SetAsync(cacheKey, boeDocument, cacheExpiration);
            _logger.LogDebug("[ICUMS-CACHE] Cached BOE document for container: {ContainerNumber}, Expiration: {Expiration}",
                containerNumber, cacheExpiration);
        }

        public void InvalidateCache(string containerNumber)
        {
            var cacheKey = $"{CACHE_KEY_PREFIX}{containerNumber}";
            _ = _cache.RemoveAsync(cacheKey);
            _logger.LogInformation("[ICUMS-CACHE] Invalidated cache for container: {ContainerNumber}", containerNumber);
        }

        public void ClearCache()
        {
            _ = _cache.RemoveByPrefixAsync(CACHE_KEY_PREFIX);
            _logger.LogInformation("[ICUMS-CACHE] Cache clear requested");
            _cacheHits = 0;
            _cacheMisses = 0;
        }

        public CacheStatistics GetCacheStatistics()
        {
            var totalRequests = _cacheHits + _cacheMisses;
            var hitRate = totalRequests > 0 ? (_cacheHits * 100.0 / totalRequests) : 0;

            return new CacheStatistics
            {
                CacheHits = _cacheHits,
                CacheMisses = _cacheMisses,
                TotalRequests = totalRequests,
                HitRatePercentage = hitRate
            };
        }
    }

    public class CacheStatistics
    {
        public int CacheHits { get; set; }
        public int CacheMisses { get; set; }
        public int TotalRequests { get; set; }
        public double HitRatePercentage { get; set; }
    }
}
