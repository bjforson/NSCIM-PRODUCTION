using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
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
        private readonly ConcurrentDictionary<string, byte> _volatileCacheKeys = new(StringComparer.Ordinal);

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
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                var cacheKey = $"container_basic_{normalizedContainerNumber}";

                // Try cache first (5 minute cache)
                if (_cache.TryGetValue(cacheKey, out ContainerBasicInfo? cachedInfo))
                {
                    _logger.LogInformation("Retrieved basic info for container {ContainerNumber} from cache", normalizedContainerNumber);
                    return cachedInfo;
                }

                var predictiveBasicInfo = TryMapPredictiveBasicInfo(
                    await GetPredictiveContainerContextAsync(normalizedContainerNumber));

                if (predictiveBasicInfo != null)
                {
                    _cache.Set(cacheKey, predictiveBasicInfo, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                        Size = 1
                    });
                    _logger.LogInformation("Retrieved basic info for container {ContainerNumber} from predictive cache", normalizedContainerNumber);
                    return predictiveBasicInfo;
                }

                _logger.LogInformation("Fetching basic info for container {ContainerNumber} from API", normalizedContainerNumber);

                var response = await _apiService.GetAsync<ContainerBasicInfo>(
                    ContainerDetailsRoutes.BuildBasicPath(normalizedContainerNumber));

                if (response != null)
                {
                    // ✅ FIX: Cache with Size specified to enforce SizeLimit
                    _cache.Set(cacheKey, response, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                        Size = 1 // Each cache entry counts as 1 unit
                    });
                    _logger.LogInformation("Cached basic info for container {ContainerNumber}", normalizedContainerNumber);
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
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                var cacheKey = $"container_full_{normalizedContainerNumber}";

                if (_cache.TryGetValue(cacheKey, out ContainerFullDetails? cached))
                {
                    _logger.LogInformation("Retrieved full details for container {ContainerNumber} from cache", normalizedContainerNumber);
                    return cached;
                }

                var predictiveFullDetails = TryMapPredictiveFullDetails(
                    await GetPredictiveContainerContextAsync(normalizedContainerNumber));

                if (predictiveFullDetails != null)
                {
                    _cache.Set(cacheKey, predictiveFullDetails, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                        Size = 1
                    });
                    _logger.LogInformation("Retrieved full details for container {ContainerNumber} from predictive cache", normalizedContainerNumber);
                    return predictiveFullDetails;
                }

                _logger.LogInformation("Fetching full details for container {ContainerNumber} from API", normalizedContainerNumber);

                var response = await _apiService.GetAsync<ContainerFullDetails>(
                    ContainerDetailsRoutes.BuildFullPath(normalizedContainerNumber));

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
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                var cacheKey = $"scanner_data_{normalizedContainerNumber}_page_{page}_size_{pageSize}";

                // Try cache first (2 minute cache for paginated data)
                if (_cache.TryGetValue(cacheKey, out PagedResult<ScannerDataRecord>? cachedData))
                {
                    _logger.LogInformation("Retrieved scanner data for container {ContainerNumber} page {Page} from cache", normalizedContainerNumber, page);
                    return cachedData;
                }

                var predictiveScannerData = TryMapPredictiveScannerPage(
                    await GetPredictiveContainerContextAsync(normalizedContainerNumber),
                    page,
                    pageSize);

                if (predictiveScannerData != null)
                {
                    _cache.Set(cacheKey, predictiveScannerData, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                        Size = 1
                    });
                    _logger.LogInformation("Retrieved scanner data for container {ContainerNumber} page {Page} from predictive cache", normalizedContainerNumber, page);
                    return predictiveScannerData;
                }

                _logger.LogInformation("Fetching scanner data for container {ContainerNumber} page {Page} from API", normalizedContainerNumber, page);

                var url = ContainerDetailsRoutes.BuildScannerPagedPath(normalizedContainerNumber, page, pageSize);
                var response = await _apiService.GetAsync<PagedResult<ScannerDataRecord>>(url);

                if (response != null)
                {
                    // ✅ FIX: Cache with Size specified to enforce SizeLimit
                    _cache.Set(cacheKey, response, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                        Size = 1 // Each cache entry counts as 1 unit
                    });
                    _logger.LogInformation("Cached scanner data for container {ContainerNumber} page {Page}", normalizedContainerNumber, page);
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
        /// Resolve the UI's logical container/group tuple to a stable physical
        /// source scan. This is a Phase 2A contract shim: the endpoint is optional
        /// in this fork and callers must retain compatibility fallbacks.
        /// </summary>
        public async Task<ScanAssetResolution?> ResolveScanAssetAsync(
            string containerNumber,
            string? groupIdentifier = null,
            int? analysisRecordId = null,
            Guid? splitJobId = null)
        {
            try
            {
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                if (string.IsNullOrWhiteSpace(normalizedContainerNumber)
                    && !analysisRecordId.HasValue
                    && !splitJobId.HasValue)
                {
                    return null;
                }

                var cacheKey = $"scan_asset_resolution_{normalizedContainerNumber}_{NormalizeCachePart(groupIdentifier)}_{analysisRecordId}_{splitJobId}";
                if (_cache.TryGetValue(cacheKey, out ScanAssetResolution? cachedResolution))
                {
                    return cachedResolution;
                }

                var query = ContainerDetailsRoutes.BuildSourceScanQuery(
                    normalizedContainerNumber,
                    groupIdentifier,
                    analysisRecordId,
                    splitJobId,
                    includeContainerWhenEmpty: false);

                if (string.IsNullOrWhiteSpace(query))
                {
                    return null;
                }

                var resolution = await _apiService.TryGetAsync<ScanAssetResolution>(
                    $"/api/scan-assets/resolve?{query}");

                if (resolution == null)
                {
                    return null;
                }

                if (string.IsNullOrWhiteSpace(resolution.ContainerNumber))
                {
                    resolution.ContainerNumber = normalizedContainerNumber;
                }

                if (string.IsNullOrWhiteSpace(resolution.GroupIdentifier))
                {
                    resolution.GroupIdentifier = groupIdentifier;
                }

                SetVolatileCache(cacheKey, resolution, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                    Size = 1
                });

                return resolution;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex,
                    "Optional scan asset resolution failed for container {ContainerNumber}, group {GroupIdentifier}",
                    containerNumber,
                    groupIdentifier);
                return null;
            }
        }

        /// <summary>
        /// Resolver-aware scanner lookup. Planned API order:
        /// 1. /api/scan-assets/{sourceScanId}/scanner-data
        /// 2. compatibility alias /api/containerdetails/scanner/{container}?sourceScanId=...
        /// 3. legacy container-only route.
        /// </summary>
        public async Task<PagedResult<ScannerDataRecord>?> GetScannerDataForResolvedScanAsync(
            string containerNumber,
            string? groupIdentifier = null,
            int page = 1,
            int pageSize = 50,
            ScanAssetResolution? resolution = null)
        {
            try
            {
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                resolution ??= await ResolveScanAssetAsync(normalizedContainerNumber, groupIdentifier);

                if (resolution?.HasUsableSourceScan == true)
                {
                    var sourceScanId = resolution.EffectiveSourceScanId!;
                    var cacheKey = $"scanner_data_resolved_{normalizedContainerNumber}_{sourceScanId}_{resolution.SplitJobId}_{resolution.SplitResultId}_page_{page}_size_{pageSize}";

                    if (_cache.TryGetValue(cacheKey, out PagedResult<ScannerDataRecord>? cachedData))
                    {
                        _logger.LogInformation(
                            "Retrieved resolver-backed scanner data for container {ContainerNumber} source {SourceScanId} page {Page} from cache",
                            normalizedContainerNumber,
                            sourceScanId,
                            page);
                        return cachedData;
                    }

                    var sourceEndpoint = ScanAssetClient.BuildScannerDataPath(
                        sourceScanId,
                        new ScanAssetScannerDataQuery
                        {
                            ContainerNumber = normalizedContainerNumber,
                            GroupIdentifier = groupIdentifier,
                            AnalysisRecordId = resolution.AnalysisRecordId,
                            SplitJobId = resolution.SplitJobId,
                            SplitResultId = resolution.SplitResultId,
                            Side = resolution.EffectiveSplitSide,
                            Page = page,
                            PageSize = pageSize
                        });
                    var response = await _apiService.TryGetAsync<PagedResult<ScannerDataRecord>>(sourceEndpoint);

                    if (response == null)
                    {
                        var aliasEndpoint = ContainerDetailsRoutes.BuildScannerAliasWithSourceScanQueryPath(
                            normalizedContainerNumber,
                            groupIdentifier,
                            resolution.AnalysisRecordId,
                            resolution.SplitJobId,
                            resolution,
                            page,
                            pageSize);
                        response = await _apiService.TryGetAsync<PagedResult<ScannerDataRecord>>(aliasEndpoint);
                    }

                    if (response != null)
                    {
                        response.Resolution ??= resolution;
                        _cache.Set(cacheKey, response, new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                            Size = 1
                        });
                        return response;
                    }
                }

                return await GetScannerDataAsync(normalizedContainerNumber, page, pageSize);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting resolver-backed scanner data for container {ContainerNumber}", containerNumber);
                return await GetScannerDataAsync(containerNumber, page, pageSize);
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
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                var cacheKey = $"full_scanner_{normalizedContainerNumber}";

                // Try cache first (5 minute cache)
                if (_cache.TryGetValue(cacheKey, out FullScannerDataRecord? cachedData))
                {
                    _logger.LogInformation("Retrieved full scanner data for container {ContainerNumber} from cache", normalizedContainerNumber);
                    return cachedData;
                }

                _logger.LogInformation("Fetching full scanner data for container {ContainerNumber} from API", normalizedContainerNumber);

                // ✅ Phase 2: Use ?full=true parameter to get FullScannerDataRecord directly
                var fullRecord = await _apiService.GetAsync<FullScannerDataRecord>(
                    ContainerDetailsRoutes.BuildScannerFullPath(normalizedContainerNumber));

                if (fullRecord == null)
                {
                    _logger.LogWarning("No scanner data found for container {ContainerNumber}", normalizedContainerNumber);
                    return null;
                }

                // ✅ FIX: Cache with Size specified to enforce SizeLimit
                _cache.Set(cacheKey, fullRecord, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                    Size = 1 // Each cache entry counts as 1 unit
                });
                _logger.LogInformation("Cached full scanner data for container {ContainerNumber}", normalizedContainerNumber);

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
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                var cacheKey = $"icums_data_{normalizedContainerNumber}_page_{page}_size_{pageSize}";

                // Try cache first (2 minute cache for paginated data)
                if (_cache.TryGetValue(cacheKey, out PagedResult<ICUMSDataRecord>? cachedData))
                {
                    _logger.LogInformation("Retrieved ICUMS data for container {ContainerNumber} page {Page} from cache", normalizedContainerNumber, page);
                    return cachedData;
                }

                var predictiveIcumData = TryMapPredictiveIcumPage(
                    await GetPredictiveContainerContextAsync(normalizedContainerNumber),
                    page,
                    pageSize);

                if (predictiveIcumData != null)
                {
                    _cache.Set(cacheKey, predictiveIcumData, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                        Size = 1
                    });
                    _logger.LogInformation("Retrieved ICUMS data for container {ContainerNumber} page {Page} from predictive cache", normalizedContainerNumber, page);
                    return predictiveIcumData;
                }

                _logger.LogInformation("Fetching ICUMS data for container {ContainerNumber} page {Page} from API", normalizedContainerNumber, page);

                var url = ContainerDetailsRoutes.BuildIcumsPagedPath(normalizedContainerNumber, page, pageSize);
                var response = await _apiService.GetAsync<PagedResult<ICUMSDataRecord>>(url);

                if (response != null)
                {
                    // ✅ FIX: Cache with Size specified to enforce SizeLimit
                    _cache.Set(cacheKey, response, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2),
                        Size = 1 // Each cache entry counts as 1 unit
                    });
                    _logger.LogInformation("Cached ICUMS data for container {ContainerNumber} page {Page}", normalizedContainerNumber, page);
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
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                var cacheKey = $"full_boe_{normalizedContainerNumber}";

                // Try cache first (5 minute cache)
                if (_cache.TryGetValue(cacheKey, out FullBOEDataRecord? cachedData))
                {
                    _logger.LogInformation("Retrieved full BOE data for container {ContainerNumber} from cache", normalizedContainerNumber);
                    return cachedData;
                }

                _logger.LogInformation("Fetching full BOE data for container {ContainerNumber} from API", normalizedContainerNumber);

                // ✅ Phase 2: Use ?full=true parameter to get FullBOEDataRecord directly
                var fullRecord = await _apiService.GetAsync<FullBOEDataRecord>(
                    ContainerDetailsRoutes.BuildIcumsFullPath(normalizedContainerNumber));

                if (fullRecord == null)
                {
                    _logger.LogWarning("No ICUMS data found for container {ContainerNumber}", normalizedContainerNumber);
                    return null;
                }

                // ✅ FIX: Cache with Size specified to enforce SizeLimit
                _cache.Set(cacheKey, fullRecord, new Microsoft.Extensions.Caching.Memory.MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                    Size = 1 // Each cache entry counts as 1 unit
                });
                _logger.LogInformation("Cached full BOE data for container {ContainerNumber}", normalizedContainerNumber);

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
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                var cacheKey = $"image_metadata_{normalizedContainerNumber}";

                // Try cache first (1 minute cache for images as they may update)
                if (_cache.TryGetValue(cacheKey, out List<ImageMetadata>? cachedMetadata))
                {
                    _logger.LogInformation("Retrieved image metadata for container {ContainerNumber} from cache", normalizedContainerNumber);
                    return cachedMetadata;
                }

                _logger.LogInformation("Fetching image metadata for container {ContainerNumber} from API", normalizedContainerNumber);

                var response = await _apiService.GetAsync<List<ImageMetadata>>(
                    ContainerDetailsRoutes.BuildImagesPath(normalizedContainerNumber));

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
                    _logger.LogInformation("Cached image metadata for container {ContainerNumber}", normalizedContainerNumber);
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
        /// Resolver-aware image metadata lookup. Planned API order:
        /// 1. /api/scan-assets/{sourceScanId}/images
        /// 2. compatibility alias /api/containerdetails/images/{container}?sourceScanId=...
        /// 3. synthesized /api/scan-assets/{sourceScanId}/image metadata
        /// 4. legacy container-only route.
        /// </summary>
        public async Task<List<ImageMetadata>?> GetImageMetadataForResolvedScanAsync(
            string containerNumber,
            string? groupIdentifier = null,
            ScanAssetResolution? resolution = null)
        {
            try
            {
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                resolution ??= await ResolveScanAssetAsync(normalizedContainerNumber, groupIdentifier);

                if (resolution?.HasUsableSourceScan == true)
                {
                    var sourceScanId = resolution.EffectiveSourceScanId!;
                    var cacheKey = $"image_metadata_resolved_{normalizedContainerNumber}_{sourceScanId}_{resolution.SplitJobId}_{resolution.SplitResultId}";

                    if (_cache.TryGetValue(cacheKey, out List<ImageMetadata>? cachedMetadata))
                    {
                        _logger.LogInformation(
                            "Retrieved resolver-backed image metadata for container {ContainerNumber} source {SourceScanId} from cache",
                            normalizedContainerNumber,
                            sourceScanId);
                        return cachedMetadata;
                    }

                    var response = await _apiService.TryGetAsync<List<ImageMetadata>>(
                        ScanAssetClient.BuildImagesPath(
                            sourceScanId,
                            new ScanAssetImageQuery
                            {
                                ContainerNumber = normalizedContainerNumber,
                                GroupIdentifier = groupIdentifier,
                                AnalysisRecordId = resolution.AnalysisRecordId,
                                SplitJobId = resolution.SplitJobId,
                                SplitResultId = resolution.SplitResultId,
                                Side = resolution.EffectiveSplitSide
                            }));

                    if (response == null)
                    {
                        response = await _apiService.TryGetAsync<List<ImageMetadata>>(
                            ContainerDetailsRoutes.BuildImagesWithQueryPath(
                            normalizedContainerNumber,
                            groupIdentifier,
                            resolution.AnalysisRecordId,
                            resolution.SplitJobId,
                            resolution));
                    }

                    response = response?.Where(image => image != null).ToList();
                    if (response is { Count: > 0 })
                    {
                        ApplyResolutionToImages(response, resolution);
                        NormalizeImageUrls(response);
                        SetVolatileCache(cacheKey, response, new MemoryCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(1),
                            Size = 1
                        });
                        return response;
                    }

                    var synthetic = new List<ImageMetadata>
                    {
                        CreateSyntheticSourceImageMetadata(normalizedContainerNumber, resolution)
                    };
                    SetVolatileCache(cacheKey, synthetic, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30),
                        Size = 1
                    });
                    return synthetic;
                }

                return await GetImageMetadataAsync(normalizedContainerNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting resolver-backed image metadata for container {ContainerNumber}", containerNumber);
                return await GetImageMetadataAsync(containerNumber);
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

                var response = await _apiService.GetAsync<ImageWithTools>(ContainerDetailsRoutes.BuildImageByIdPath(imageId));

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
                var response = await _apiService.PostAsync<object, UnifiedSearchResults>(ContainerDetailsRoutes.BuildSearchPath(), searchRequest);

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
                var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
                // Note: IMemoryCache doesn't support pattern-based removal
                // We'll remove the most common cache keys
                var cacheKeys = new[]
                {
                    $"predictive_container_context_{normalizedContainerNumber}",
                    $"container_basic_{normalizedContainerNumber}",
                    $"container_full_{normalizedContainerNumber}",
                    $"full_scanner_{normalizedContainerNumber}",
                    $"full_boe_{normalizedContainerNumber}",
                    $"image_metadata_{normalizedContainerNumber}",
                    $"scanner_data_{normalizedContainerNumber}_page_1_size_25",
                    $"scanner_data_{normalizedContainerNumber}_page_1_size_50",
                    $"scanner_data_{normalizedContainerNumber}_page_1_size_100",
                    $"scanner_data_{normalizedContainerNumber}_page_1_size_1000",
                    $"icums_data_{normalizedContainerNumber}_page_1_size_25",
                    $"icums_data_{normalizedContainerNumber}_page_1_size_50",
                    $"icums_data_{normalizedContainerNumber}_page_1_size_100",
                    $"icums_data_{normalizedContainerNumber}_page_1_size_1000"
                };

                foreach (var key in cacheKeys)
                {
                    _cache.Remove(key);
                }

                RemoveTrackedCacheKeys($"scan_asset_resolution_{normalizedContainerNumber}_");
                RemoveTrackedCacheKeys($"image_metadata_resolved_{normalizedContainerNumber}_");

                _logger.LogInformation("Cache cleared for container {ContainerNumber}", normalizedContainerNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing cache for container {ContainerNumber}", containerNumber);
            }
        }

        private void SetVolatileCache<T>(string key, T value, MemoryCacheEntryOptions options)
        {
            _cache.Set(key, value, options);
            _volatileCacheKeys[key] = 0;
        }

        private void RemoveTrackedCacheKeys(string prefix)
        {
            foreach (var key in _volatileCacheKeys.Keys)
            {
                if (!key.StartsWith(prefix, StringComparison.Ordinal))
                {
                    continue;
                }

                _cache.Remove(key);
                _volatileCacheKeys.TryRemove(key, out _);
            }
        }

        private static string NormalizeCachePart(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? "_"
                : value.Trim().ToUpperInvariant();
        }

        private void NormalizeImageUrls(IEnumerable<ImageMetadata> images)
        {
            var apiBaseUrl = _configuration?["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
            foreach (var image in images)
            {
                if (!string.IsNullOrEmpty(image.ThumbnailUrl))
                {
                    image.ThumbnailUrl = NormalizeImageUrl(image.ThumbnailUrl, apiBaseUrl);
                }

                if (!string.IsNullOrEmpty(image.FullImageUrl))
                {
                    image.FullImageUrl = NormalizeImageUrl(image.FullImageUrl, apiBaseUrl);
                }
            }
        }

        private static void ApplyResolutionToImages(IEnumerable<ImageMetadata> images, ScanAssetResolution resolution)
        {
            foreach (var image in images)
            {
                image.SourceScanId ??= resolution.SourceScanId;
                image.OriginalScanRecordId ??= resolution.OriginalScanRecordId;
                image.SplitJobId ??= resolution.SplitJobId;
                image.SplitResultId ??= resolution.SplitResultId;
                image.SplitSide ??= resolution.EffectiveSplitSide;
                image.ResolutionReason ??= resolution.ResolutionReason;
                image.Resolution ??= resolution;
            }
        }

        private static ImageMetadata CreateSyntheticSourceImageMetadata(
            string containerNumber,
            ScanAssetResolution resolution)
        {
            var sourceScanId = resolution.EffectiveSourceScanId!;
            var imagePath = BuildSourceScanImagePath(sourceScanId, containerNumber, resolution, "full");
            var thumbnailPath = BuildSourceScanImagePath(sourceScanId, containerNumber, resolution, "thumbnail");

            return new ImageMetadata
            {
                Id = CreateSyntheticImageId(sourceScanId, resolution.SplitJobId, resolution.SplitResultId),
                ImageType = resolution.SourceScannerType ?? "Source",
                FileName = $"{containerNumber}_{sourceScanId}.jpg",
                FileSizeBytes = 0,
                CreatedAt = DateTime.UtcNow,
                ThumbnailUrl = thumbnailPath,
                FullImageUrl = imagePath,
                SourceScanId = resolution.SourceScanId,
                OriginalScanRecordId = resolution.OriginalScanRecordId,
                SplitJobId = resolution.SplitJobId,
                SplitResultId = resolution.SplitResultId,
                SplitSide = resolution.EffectiveSplitSide,
                ResolutionReason = resolution.ResolutionReason,
                Resolution = resolution
            };
        }

        private static int CreateSyntheticImageId(string sourceScanId, Guid? splitJobId, Guid? splitResultId)
        {
            var value = HashCode.Combine(sourceScanId, splitJobId, splitResultId);
            return value == int.MinValue ? int.MaxValue : Math.Abs(value);
        }

        private static string BuildSourceScanImagePath(
            string sourceScanId,
            string containerNumber,
            ScanAssetResolution resolution,
            string size)
        {
            var containerHint = StrictSingleContainerToken(containerNumber);
            return ScanAssetClient.BuildImagePath(
                sourceScanId,
                new ScanAssetImageQuery
                {
                    Size = size,
                    ContainerNumber = containerHint,
                    SplitJobId = resolution.SplitJobId,
                    SplitResultId = resolution.SplitResultId,
                    Side = resolution.EffectiveSplitSide
                });
        }

        private static string? StrictSingleContainerToken(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            var matches = System.Text.RegularExpressions.Regex.Matches(
                value,
                "[A-Z]{4}\\d{7}",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
            if (matches.Count == 1)
            {
                return matches[0].Value.ToUpperInvariant();
            }

            if (matches.Count > 1 || value.IndexOfAny(new[] { ',', ';', '|', '/', '\\', '\t', '\r', '\n' }) >= 0)
            {
                return null;
            }

            return value.Trim().ToUpperInvariant();
        }

        private bool IsPredictiveCacheFirstEnabled()
        {
            if (_configuration == null)
            {
                return true;
            }

            var defaultValue = _configuration.GetValue<bool>("ViewContextPreloading:PredictiveCacheFirst", true);
            return _configuration.GetValue<bool>("PredictivePreloadClient:Enabled", defaultValue);
        }

        private async Task<PredictiveContainerContext?> GetPredictiveContainerContextAsync(string containerNumber)
        {
            if (!IsPredictiveCacheFirstEnabled() || string.IsNullOrWhiteSpace(containerNumber))
            {
                return null;
            }

            var normalizedContainerNumber = NormalizeContainerNumber(containerNumber);
            var cacheKey = $"predictive_container_context_{normalizedContainerNumber}";

            if (_cache.TryGetValue(cacheKey, out PredictiveContainerContext? cachedContext))
            {
                _logger.LogDebug("Predictive container context cache HIT for {ContainerNumber}", normalizedContainerNumber);
                return cachedContext;
            }

            var context = await _apiService.TryGetAsync<PredictiveContainerContext>(
                ContainerDetailsRoutes.BuildPredictiveCacheContainerPath(normalizedContainerNumber));

            if (context == null)
            {
                return null;
            }

            _cache.Set(cacheKey, context, new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = GetPredictiveLocalTtl(context),
                Size = 1
            });

            _logger.LogDebug("Predictive container context cache MISS loaded for {ContainerNumber}", normalizedContainerNumber);
            return context;
        }

        private TimeSpan GetPredictiveLocalTtl(PredictiveContainerContext context)
        {
            var configuredSeconds = _configuration?.GetValue<int>("PredictivePreloadClient:LocalContextSeconds", 30) ?? 30;
            var ttlCap = TimeSpan.FromSeconds(Math.Clamp(configuredSeconds, 5, 300));

            if (context.ExpiresAtUtc == default)
            {
                return ttlCap;
            }

            var remaining = context.ExpiresAtUtc - DateTime.UtcNow;
            if (remaining <= TimeSpan.Zero)
            {
                return TimeSpan.FromSeconds(5);
            }

            return remaining < ttlCap ? remaining : ttlCap;
        }

        private static ContainerBasicInfo? TryMapPredictiveBasicInfo(PredictiveContainerContext? context)
        {
            var summary = context?.Summary;
            if (summary == null)
            {
                return null;
            }

            return new ContainerBasicInfo
            {
                ContainerNumber = summary.ContainerNumber,
                ScannerType = summary.ScannerType ?? string.Empty,
                ScannerRecordCount = summary.ScannerRecordCount,
                ICUMSRecordCount = summary.IcumsRecordCount,
                ImageCount = summary.ImageCount,
                LastUpdated = GetPredictiveTimestamp(summary, context),
                ValidationStatus = summary.HasScannerData && summary.HasIcumsData ? "Complete" : "Partial",
                DataCompletenessScore = summary.CompletenessScore
            };
        }

        private static ContainerFullDetails? TryMapPredictiveFullDetails(PredictiveContainerContext? context)
        {
            var summary = context?.Summary;
            if (summary == null)
            {
                return null;
            }

            return new ContainerFullDetails
            {
                ScannerType = summary.ScannerType ?? string.Empty,
                ScanDate = GetPredictiveTimestamp(summary, context),
                ValidationStatus = "Pending",
                CompletenessScore = summary.CompletenessScore,
                ClearanceType = summary.ClearanceType ?? context?.BoeSummary?.ClearanceType,
                ImageCount = summary.ImageCount,
                HasScannerData = summary.HasScannerData,
                HasICUMSData = summary.HasIcumsData,
                BOENumber = context?.BoeSummary?.DeclarationNumber,
                Consignee = context?.BoeSummary?.ConsigneeName,
                OriginPort = GetFirstFieldValue(context?.IcumsFirstPage?.Data, "Origin Port", "Port of Loading", "Country of Origin"),
                Destination = GetFirstFieldValue(context?.IcumsFirstPage?.Data, "Destination", "Port of Discharge"),
                VesselName = GetFirstFieldValue(context?.ScannerFirstPage?.Data, "Vessel Name", "Vessel"),
                VehicleCount = 0,
                ScanLocation = GetFirstFieldValue(context?.ScannerFirstPage?.Data, "Scan Location", "Location"),
                Operator = GetFirstFieldValue(context?.ScannerFirstPage?.Data, "Operator"),
                ContainerSize = GetFirstFieldValue(context?.IcumsFirstPage?.Data, "Container Size")
            };
        }

        private static PagedResult<ScannerDataRecord>? TryMapPredictiveScannerPage(
            PredictiveContainerContext? context,
            int page,
            int pageSize)
        {
            var scannerPage = context?.ScannerFirstPage;
            if (scannerPage == null || page != 1 || pageSize > scannerPage.PageSize)
            {
                return null;
            }

            return new PagedResult<ScannerDataRecord>
            {
                Data = scannerPage.Data
                    .Take(pageSize)
                    .Select(field => new ScannerDataRecord
                    {
                        Field = field.Field,
                        Value = field.Value ?? string.Empty,
                        Category = field.Category,
                        Timestamp = field.Timestamp
                    })
                    .ToList(),
                TotalCount = scannerPage.TotalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = CalculateTotalPages(scannerPage.TotalCount, pageSize),
                Status = scannerPage.Status
            };
        }

        private static PagedResult<ICUMSDataRecord>? TryMapPredictiveIcumPage(
            PredictiveContainerContext? context,
            int page,
            int pageSize)
        {
            var icumPage = context?.IcumsFirstPage;
            if (icumPage == null || page != 1 || pageSize > icumPage.PageSize)
            {
                return null;
            }

            return new PagedResult<ICUMSDataRecord>
            {
                Data = icumPage.Data
                    .Take(pageSize)
                    .Select(field => new ICUMSDataRecord
                    {
                        Field = field.Field,
                        Value = field.Value ?? string.Empty,
                        Category = field.Category,
                        IsRequired = false
                    })
                    .ToList(),
                TotalCount = icumPage.TotalCount,
                Page = page,
                PageSize = pageSize,
                TotalPages = CalculateTotalPages(icumPage.TotalCount, pageSize),
                Status = icumPage.Status
            };
        }

        private static DateTime GetPredictiveTimestamp(PredictiveContainerSummary summary, PredictiveContainerContext? context)
        {
            return summary.LastUpdatedUtc
                ?? summary.LatestScanDateUtc
                ?? context?.CachedAtUtc
                ?? DateTime.UtcNow;
        }

        private static string? GetFirstFieldValue(IEnumerable<PredictiveFieldValue>? fields, params string[] names)
        {
            if (fields == null)
            {
                return null;
            }

            foreach (var name in names)
            {
                var match = fields.FirstOrDefault(field =>
                    string.Equals(field.Field, name, StringComparison.OrdinalIgnoreCase) ||
                    field.Field.Contains(name, StringComparison.OrdinalIgnoreCase));

                if (!string.IsNullOrWhiteSpace(match?.Value))
                {
                    return match.Value;
                }
            }

            return null;
        }

        private static int CalculateTotalPages(int totalCount, int pageSize)
        {
            if (pageSize <= 0)
            {
                return 0;
            }

            return (int)Math.Ceiling(totalCount / (double)pageSize);
        }

        private static string NormalizeContainerNumber(string containerNumber)
        {
            return string.IsNullOrWhiteSpace(containerNumber)
                ? string.Empty
                : containerNumber.Trim().ToUpperInvariant();
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

