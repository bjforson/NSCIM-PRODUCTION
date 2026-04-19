using Microsoft.Extensions.Configuration;
using NickScanWebApp.New.Models;
using NickScanWebApp.Shared.Models;
using NickScanWebApp.Shared.Services;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Orchestrates loading all data needed for the container details experience.
    /// Uses the shared IContainerDetailsService (with its own IMemoryCache) plus a per-user ViewContextCache.
    /// </summary>
    public class ContainerViewPreloader
    {
        private readonly NickScanWebApp.Shared.Services.IContainerDetailsService _detailsService;
        private readonly ViewContextCache _viewContextCache;
        private readonly ILogger<ContainerViewPreloader> _logger;
        private readonly IConfiguration? _configuration;

        public ContainerViewPreloader(
            NickScanWebApp.Shared.Services.IContainerDetailsService detailsService,
            ViewContextCache viewContextCache,
            ILogger<ContainerViewPreloader> logger,
            IConfiguration? configuration = null)
        {
            _detailsService = detailsService;
            _viewContextCache = viewContextCache;
            _logger = logger;
            _configuration = configuration;
        }

        private bool IsPreloadingEnabled()
        {
            return _configuration?.GetValue<bool>("ViewContextPreloading:Enabled", true) ?? true;
        }

        private static string GetCacheKey(string containerNumber) =>
            $"container_view:{containerNumber}";

        /// <summary>
        /// Load (or retrieve from cache) the full container view context.
        /// When forceRefresh is true, underlying service cache entries and the per-user context cache are cleared first.
        /// </summary>
        public async Task<ContainerViewContext?> LoadAsync(
            string containerNumber,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(containerNumber))
            {
                _logger.LogWarning("Attempted to load container view with empty container number");
                return null;
            }

            // Check if preloading is disabled via feature flag
            if (!IsPreloadingEnabled())
            {
                _logger.LogDebug("View context preloading is disabled - loading basic info only for {ContainerNumber}", containerNumber);
                var basicInfo = await _detailsService.GetBasicInfoAsync(containerNumber);
                if (basicInfo == null) return null;

                return new ContainerViewContext
                {
                    ContainerNumber = containerNumber,
                    BasicInfo = basicInfo
                    // ScannerData, ICUMSData, Images will be loaded on-demand by tabs
                };
            }

            var key = GetCacheKey(containerNumber);
            var startTime = DateTime.UtcNow;
            var cacheSource = "unknown";

            if (forceRefresh)
            {
                _logger.LogInformation("Force refresh requested for container view {ContainerNumber}", containerNumber);
                _detailsService.ClearContainerCache(containerNumber);
                _viewContextCache.Remove(key);
                cacheSource = "force_refresh";
            }
            else if (_viewContextCache.TryGet<ContainerViewContext>(key, out var existing))
            {
                var cacheDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation(
                    "ContainerViewContext cache HIT for {ContainerNumber} (retrieved in {Duration}ms): Scanner={ScannerCount}, ICUMS={ICUMSCount}, Images={ImageCount}",
                    containerNumber,
                    cacheDuration,
                    existing.ScannerData?.TotalCount ?? 0,
                    existing.ICUMSData?.TotalCount ?? 0,
                    existing.Images?.Count ?? 0);
                return existing;
            }

            cacheSource = "api_load";
            try
            {
                _logger.LogInformation("ContainerViewContext cache MISS - Preloading container view data for {ContainerNumber}", containerNumber);

                // Step 1: always load basic info first (small, needed to validate container and compute counts)
                var basicInfoStart = DateTime.UtcNow;
                var basicInfo = await _detailsService.GetBasicInfoAsync(containerNumber);
                var basicInfoDuration = (DateTime.UtcNow - basicInfoStart).TotalMilliseconds;
                cancellationToken.ThrowIfCancellationRequested();

                if (basicInfo == null)
                {
                    _logger.LogWarning("No basic container info found for {ContainerNumber}", containerNumber);
                    return null;
                }

                _logger.LogDebug("Loaded basic info for {ContainerNumber} in {Duration}ms", containerNumber, basicInfoDuration);

                // Step 2: based on counts, fire off the heavier requests in parallel
                var parallelLoadStart = DateTime.UtcNow;
                var tasks = new List<Task>();

                Task<NickScanWebApp.Shared.Models.PagedResult<NickScanWebApp.Shared.Models.ScannerDataRecord>?>? scannerTask = null;
                Task<NickScanWebApp.Shared.Models.PagedResult<NickScanWebApp.Shared.Models.ICUMSDataRecord>?>? icumsTask = null;
                Task<List<NickScanWebApp.Shared.Models.ImageMetadata>?>? imagesTask = null;

                if (basicInfo.ScannerRecordCount > 0)
                {
                    scannerTask = _detailsService.GetScannerDataAsync(containerNumber, page: 1, pageSize: 1000);
                    tasks.Add(scannerTask);
                }

                if (basicInfo.ICUMSRecordCount > 0)
                {
                    icumsTask = _detailsService.GetICUMSDataAsync(containerNumber, page: 1, pageSize: 1000);
                    tasks.Add(icumsTask);
                }

                if (basicInfo.ImageCount > 0)
                {
                    imagesTask = _detailsService.GetImageMetadataAsync(containerNumber);
                    tasks.Add(imagesTask);
                }

                if (tasks.Count > 0)
                {
                    await Task.WhenAll(tasks);
                    cancellationToken.ThrowIfCancellationRequested();
                }
                var parallelLoadDuration = (DateTime.UtcNow - parallelLoadStart).TotalMilliseconds;

                var context = new ContainerViewContext
                {
                    ContainerNumber = containerNumber,
                    BasicInfo = basicInfo,
                    ScannerData = scannerTask?.Result,
                    ICUMSData = icumsTask?.Result,
                    Images = imagesTask?.Result
                };

                // Calculate record counts and size estimate
                var scannerCount = context.ScannerData?.TotalCount ?? 0;
                var icumsCount = context.ICUMSData?.TotalCount ?? 0;
                var imageCount = context.Images?.Count ?? 0;

                // Rough size estimate (bytes): basic info ~1KB, each record ~500 bytes, each image metadata ~2KB
                var sizeEstimate = 1024 + (scannerCount * 500) + (icumsCount * 500) + (imageCount * 2048);
                var totalDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;

                // Check soft limits and log warnings
                if (scannerCount > 5000 || icumsCount > 5000 || imageCount > 100)
                {
                    _logger.LogWarning(
                        "ContainerViewContext for {ContainerNumber} exceeds soft limits: Scanner={ScannerCount} (limit: 5000), ICUMS={ICUMSCount} (limit: 5000), Images={ImageCount} (limit: 100). Estimated size: {SizeEstimate}KB",
                        containerNumber, scannerCount, icumsCount, imageCount, sizeEstimate / 1024);
                }

                _viewContextCache.Set(key, context);
                _logger.LogInformation(
                    "Preloaded ContainerViewContext for {ContainerNumber} in {TotalDuration}ms (parallel load: {ParallelDuration}ms, source: {Source}): Scanner={ScannerCount}, ICUMS={ICUMSCount}, Images={ImageCount}, EstimatedSize={SizeEstimate}KB",
                    containerNumber,
                    totalDuration,
                    parallelLoadDuration,
                    cacheSource,
                    scannerCount,
                    icumsCount,
                    imageCount,
                    sizeEstimate / 1024);

                return context;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Container view preload cancelled for {ContainerNumber}", containerNumber);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preloading container view for {ContainerNumber}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Clear only the per-user view context cache for this container.
        /// Underlying IMemoryCache entries remain unless forceRefresh is used.
        /// </summary>
        public void ClearLocalContext(string containerNumber)
        {
            var key = GetCacheKey(containerNumber);
            _viewContextCache.Remove(key);
            _logger.LogDebug("Cleared local ContainerViewContext cache for {ContainerNumber}", containerNumber);
        }
    }
}


