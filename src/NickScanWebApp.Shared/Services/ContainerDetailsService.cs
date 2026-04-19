using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanWebApp.Shared.Models;

namespace NickScanWebApp.Shared.Services
{
    /// <summary>
    /// Service for fetching and caching container details from the API
    /// Implements smart caching strategy with different TTLs based on data volatility
    /// ✅ Unified service for both desktop and mobile applications
    /// </summary>
    public class ContainerDetailsService : IContainerDetailsService
    {
        private readonly ApiService _apiService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ContainerDetailsService> _logger;
        private readonly IConfiguration? _configuration;

        public ContainerDetailsService(
            ApiService apiService,
            IMemoryCache cache,
            ILogger<ContainerDetailsService> logger,
            IConfiguration? configuration = null)
        {
            _apiService = apiService;
            _cache = cache;
            _logger = logger;
            _configuration = configuration;
        }

        /// <summary>
        /// Get basic container information (cached for 5 minutes)
        /// </summary>
        public async Task<ContainerBasicInfo?> GetBasicInfoAsync(string containerNumber)
        {
            try
            {
                var cacheKey = $"container_basic_{containerNumber}";

                // Try cache first (5 minute cache)
                if (_cache.TryGetValue(cacheKey, out ContainerBasicInfo? cachedInfo))
                {
                    _logger.LogInformation("Retrieved basic info for container {ContainerNumber} from cache", containerNumber);
                    return cachedInfo;
                }

                _logger.LogInformation("Fetching basic info for container {ContainerNumber} from API", containerNumber);

                var response = await _apiService.GetAsync<ContainerBasicInfo>(
                    $"/api/containerdetails/basic/{Uri.EscapeDataString(containerNumber)}");

                if (response != null)
                {
                    // ✅ FIX: Cache with Size specified to enforce SizeLimit
                    _cache.Set(cacheKey, response, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                        Size = 1 // Each cache entry counts as 1 unit
                    });
                    _logger.LogInformation("Cached basic info for container {ContainerNumber}", containerNumber);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting basic info for container {ContainerNumber}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Full container summary (cached for 5 minutes)
        /// </summary>
        public async Task<ContainerFullDetails?> GetFullDetailsAsync(string containerNumber)
        {
            try
            {
                var cacheKey = $"container_full_{containerNumber}";

                if (_cache.TryGetValue(cacheKey, out ContainerFullDetails? cached))
                {
                    _logger.LogInformation("Retrieved full details for container {ContainerNumber} from cache", containerNumber);
                    return cached;
                }

                _logger.LogInformation("Fetching full details for container {ContainerNumber} from API", containerNumber);

                var response = await _apiService.GetAsync<ContainerFullDetails>(
                    $"/api/containerdetails/full/{Uri.EscapeDataString(containerNumber)}");

                if (response != null)
                {
                    _cache.Set(cacheKey, response, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                        Size = 1
                    });
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting full details for container {ContainerNumber}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Get paginated scanner data (cached for 2 minutes)
        /// </summary>
        public async Task<PagedResult<ScannerDataRecord>?> GetScannerDataAsync(string containerNumber, int page = 1, int pageSize = 50)
        {
            try
            {
                var cacheKey = $"scanner_data_{containerNumber}_page_{page}_size_{pageSize}";

                // Try cache first (2 minute cache for paginated data)
                if (_cache.TryGetValue(cacheKey, out PagedResult<ScannerDataRecord>? cachedData))
                {
                    _logger.LogInformation("Retrieved scanner data for container {ContainerNumber} page {Page} from cache", containerNumber, page);
                    return cachedData;
                }

                _logger.LogInformation("Fetching scanner data for container {ContainerNumber} page {Page} from API", containerNumber, page);

                var url = $"/api/containerdetails/scanner/{Uri.EscapeDataString(containerNumber)}?page={page}&pageSize={pageSize}";
                var response = await _apiService.GetAsync<PagedResult<ScannerDataRecord>>(url);

                if (response != null)
                {
                    // ✅ FIX: Cache with Size specified to enforce SizeLimit
                    _cache.Set(cacheKey, response, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                        Size = 1 // Each cache entry counts as 1 unit
                    });
                    _logger.LogInformation("Cached scanner data for container {ContainerNumber} page {Page}", containerNumber, page);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scanner data for container {ContainerNumber}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Get full scanner data record (cached for 5 minutes)
        /// ✅ Phase 2: Uses ?full=true parameter to get FullScannerDataRecord directly from API
        /// </summary>
        public async Task<FullScannerDataRecord?> GetFullScannerDataAsync(string containerNumber)
        {
            try
            {
                var cacheKey = $"full_scanner_{containerNumber}";

                // Try cache first (5 minute cache)
                if (_cache.TryGetValue(cacheKey, out FullScannerDataRecord? cachedData))
                {
                    _logger.LogInformation("Retrieved full scanner data for container {ContainerNumber} from cache", containerNumber);
                    return cachedData;
                }

                _logger.LogInformation("Fetching full scanner data for container {ContainerNumber} from API", containerNumber);

                // ✅ Phase 2: Use ?full=true parameter to get FullScannerDataRecord directly
                var fullRecord = await _apiService.GetAsync<FullScannerDataRecord>(
                    $"/api/containerdetails/scanner/{Uri.EscapeDataString(containerNumber)}?full=true");

                if (fullRecord == null)
                {
                    _logger.LogWarning("No scanner data found for container {ContainerNumber}", containerNumber);
                    return null;
                }

                // ✅ FIX: Cache with Size specified to enforce SizeLimit
                _cache.Set(cacheKey, fullRecord, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                    Size = 1 // Each cache entry counts as 1 unit
                });
                _logger.LogInformation("Cached full scanner data for container {ContainerNumber}", containerNumber);

                return fullRecord;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting full scanner data for container {ContainerNumber}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Get paginated ICUMS data (cached for 2 minutes)
        /// </summary>
        public async Task<PagedResult<ICUMSDataRecord>?> GetICUMSDataAsync(string containerNumber, int page = 1, int pageSize = 50)
        {
            try
            {
                var cacheKey = $"icums_data_{containerNumber}_page_{page}_size_{pageSize}";

                // Try cache first (2 minute cache for paginated data)
                if (_cache.TryGetValue(cacheKey, out PagedResult<ICUMSDataRecord>? cachedData))
                {
                    _logger.LogInformation("Retrieved ICUMS data for container {ContainerNumber} page {Page} from cache", containerNumber, page);
                    return cachedData;
                }

                _logger.LogInformation("Fetching ICUMS data for container {ContainerNumber} page {Page} from API", containerNumber, page);

                var url = $"/api/containerdetails/icums/{Uri.EscapeDataString(containerNumber)}?page={page}&pageSize={pageSize}";
                var response = await _apiService.GetAsync<PagedResult<ICUMSDataRecord>>(url);

                if (response != null)
                {
                    // ✅ FIX: Cache with Size specified to enforce SizeLimit
                    _cache.Set(cacheKey, response, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                        Size = 1 // Each cache entry counts as 1 unit
                    });
                    _logger.LogInformation("Cached ICUMS data for container {ContainerNumber} page {Page}", containerNumber, page);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ICUMS data for container {ContainerNumber}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Get full BOE data (cached for 5 minutes)
        /// ✅ Phase 2: Uses ?full=true parameter to get FullBOEDataRecord directly from API
        /// </summary>
        public async Task<FullBOEDataRecord?> GetFullBOEDataAsync(string containerNumber)
        {
            try
            {
                var cacheKey = $"full_boe_{containerNumber}";

                // Try cache first (5 minute cache)
                if (_cache.TryGetValue(cacheKey, out FullBOEDataRecord? cachedData))
                {
                    _logger.LogInformation("Retrieved full BOE data for container {ContainerNumber} from cache", containerNumber);
                    return cachedData;
                }

                _logger.LogInformation("Fetching full BOE data for container {ContainerNumber} from API", containerNumber);

                // ✅ Phase 2: Use ?full=true parameter to get FullBOEDataRecord directly
                var fullRecord = await _apiService.GetAsync<FullBOEDataRecord>(
                    $"/api/containerdetails/icums/{Uri.EscapeDataString(containerNumber)}?full=true");

                if (fullRecord == null)
                {
                    _logger.LogWarning("No ICUMS data found for container {ContainerNumber}", containerNumber);
                    return null;
                }

                // ✅ FIX: Cache with Size specified to enforce SizeLimit
                _cache.Set(cacheKey, fullRecord, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                    Size = 1 // Each cache entry counts as 1 unit
                });
                _logger.LogInformation("Cached full BOE data for container {ContainerNumber}", containerNumber);

                return fullRecord;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting full BOE data for container {ContainerNumber}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Get image metadata (cached for 1 minute, with URL normalization)
        /// </summary>
        public async Task<List<ImageMetadata>?> GetImageMetadataAsync(string containerNumber)
        {
            try
            {
                var cacheKey = $"image_metadata_{containerNumber}";

                // Try cache first (1 minute cache for images as they may update)
                if (_cache.TryGetValue(cacheKey, out List<ImageMetadata>? cachedMetadata))
                {
                    _logger.LogInformation("Retrieved image metadata for container {ContainerNumber} from cache", containerNumber);
                    return cachedMetadata;
                }

                _logger.LogInformation("Fetching image metadata for container {ContainerNumber} from API", containerNumber);

                var response = await _apiService.GetAsync<List<ImageMetadata>>(
                    $"/api/containerdetails/images/{Uri.EscapeDataString(containerNumber)}");

                if (response != null)
                {
                    // Normalize image URLs to use the correct API base URL
                    var apiBaseUrl = _configuration?["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
                    foreach (var image in response)
                    {
                        // Fix thumbnail URLs that might use localhost or relative paths
                        if (!string.IsNullOrEmpty(image.ThumbnailUrl))
                        {
                            image.ThumbnailUrl = NormalizeImageUrl(image.ThumbnailUrl, apiBaseUrl);
                        }
                        if (!string.IsNullOrEmpty(image.FullImageUrl))
                        {
                            image.FullImageUrl = NormalizeImageUrl(image.FullImageUrl, apiBaseUrl);
                        }
                    }

                    // ✅ FIX: Cache with Size specified to enforce SizeLimit
                    _cache.Set(cacheKey, response, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                        Size = 1 // Each cache entry counts as 1 unit
                    });
                    _logger.LogInformation("Cached image metadata for container {ContainerNumber}", containerNumber);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image metadata for container {ContainerNumber}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Get full image with tools (cached for 10 minutes - largest data)
        /// </summary>
        public async Task<ImageWithTools?> GetFullImageAsync(int imageId)
        {
            try
            {
                var cacheKey = $"full_image_{imageId}";

                // Try cache first (10 minute cache for full images)
                if (_cache.TryGetValue(cacheKey, out ImageWithTools? cachedImage))
                {
                    _logger.LogInformation("Retrieved full image {ImageId} from cache", imageId);
                    return cachedImage;
                }

                _logger.LogInformation("Fetching full image {ImageId} from API", imageId);

                var response = await _apiService.GetAsync<ImageWithTools>($"/api/containerdetails/image/{imageId}");

                if (response != null)
                {
                    // ✅ FIX: Cache with Size specified to enforce SizeLimit
                    _cache.Set(cacheKey, response, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10),
                        Size = 1 // Each cache entry counts as 1 unit
                    });
                    _logger.LogInformation("Cached full image {ImageId}", imageId);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting full image {ImageId}", imageId);
                return null;
            }
        }

        /// <summary>
        /// Search container data (cached for 1 minute - most volatile)
        /// </summary>
        public async Task<UnifiedSearchResults?> SearchContainerDataAsync(string containerNumber, string query)
        {
            try
            {
                var cacheKey = $"search_{containerNumber}_{query}";

                // Try cache first (1 minute cache for search results)
                if (_cache.TryGetValue(cacheKey, out UnifiedSearchResults? cachedResults))
                {
                    _logger.LogInformation("Retrieved search results for container {ContainerNumber} query '{Query}' from cache", containerNumber, query);
                    return cachedResults;
                }

                _logger.LogInformation("Searching container {ContainerNumber} for query '{Query}'", containerNumber, query);

                var searchRequest = new { ContainerNumber = containerNumber, Query = query };
                var response = await _apiService.PostAsync<object, UnifiedSearchResults>("/api/containerdetails/search", searchRequest);

                if (response != null)
                {
                    // ✅ FIX: Cache with Size specified to enforce SizeLimit
                    _cache.Set(cacheKey, response, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                        Size = 1 // Each cache entry counts as 1 unit
                    });
                    _logger.LogInformation("Cached search results for container {ContainerNumber} query '{Query}'", containerNumber, query);
                }

                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error searching container {ContainerNumber} for query '{Query}'", containerNumber, query);
                return null;
            }
        }

        /// <summary>
        /// Clear all cached data for a specific container
        /// </summary>
        public void ClearContainerCache(string containerNumber)
        {
            try
            {
                // Note: IMemoryCache doesn't support pattern-based removal
                // We'll remove the most common cache keys
                var cacheKeys = new[]
                {
                    $"container_basic_{containerNumber}",
                    $"container_full_{containerNumber}",
                    $"full_scanner_{containerNumber}",
                    $"full_boe_{containerNumber}",
                    $"image_metadata_{containerNumber}"
                };

                foreach (var key in cacheKeys)
                {
                    _cache.Remove(key);
                }

                _logger.LogInformation("Cache cleared for container {ContainerNumber}", containerNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing cache for container {ContainerNumber}", containerNumber);
            }
        }

        /// <summary>
        /// Normalize image URLs to use the correct API base URL
        /// Fixes URLs that use localhost or relative paths
        /// ✅ CRITICAL: Preserves query parameters (imageType, v, etc.) when normalizing
        /// </summary>
        private string NormalizeImageUrl(string url, string apiBaseUrl)
        {
            if (string.IsNullOrEmpty(url))
                return url;

            // If URL is already absolute and uses the correct base, return as-is
            if (Uri.TryCreate(url, UriKind.Absolute, out var uri))
            {
                // Check if it's using localhost - replace with configured API base URL
                if (uri.Host == "localhost" || uri.Host == "127.0.0.1")
                {
                    // ✅ CRITICAL: Extract path AND query from the original URL (preserves imageType parameter)
                    var pathAndQuery = uri.PathAndQuery; // Includes query string
                    _logger.LogDebug("Normalizing URL: {OriginalUrl} -> {BaseUrl}{PathAndQuery}", url, apiBaseUrl, pathAndQuery);
                    return $"{apiBaseUrl.TrimEnd('/')}{pathAndQuery}";
                }

                // If it's already using the correct base URL, return as-is (preserves query params)
                if (url.StartsWith(apiBaseUrl, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogDebug("URL already normalized: {Url}", url);
                    return url;
                }
            }

            // If it's a relative path, make it absolute (preserves query params)
            if (url.StartsWith("/"))
            {
                _logger.LogDebug("Normalizing relative URL: {Url} -> {BaseUrl}{Url}", url, apiBaseUrl, url);
                return $"{apiBaseUrl.TrimEnd('/')}{url}"; // ✅ Preserves query string if present
            }

            // If it starts with http:// or https:// but uses wrong host, try to fix it
            if (url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                url.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                // Extract path and query (preserves query parameters)
                if (Uri.TryCreate(url, UriKind.Absolute, out var originalUri))
                {
                    var pathAndQuery = originalUri.PathAndQuery; // ✅ Includes query string
                    // Use the configured API base URL's scheme
                    var scheme = apiBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ? "https" : "http";
                    var apiUri = new Uri(apiBaseUrl);
                    var normalized = $"{scheme}://{apiUri.Host}:{apiUri.Port}{pathAndQuery}";
                    _logger.LogDebug("Normalizing absolute URL: {OriginalUrl} -> {Normalized}", url, normalized);
                    return normalized;
                }
            }

            // Return as-is if we can't normalize it
            _logger.LogWarning("Could not normalize URL: {Url}, returning as-is", url);
            return url;
        }
    }
}

