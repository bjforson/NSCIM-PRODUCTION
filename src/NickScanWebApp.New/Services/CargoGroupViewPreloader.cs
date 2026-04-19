using Microsoft.Extensions.Configuration;
using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;
using NickScanWebApp.New.Models;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Orchestrates loading all data needed for the cargo group view (summary + optional full data).
    /// </summary>
    public class CargoGroupViewPreloader
    {
        private readonly CargoGroupService _cargoGroupService;
        private readonly ViewContextCache _viewContextCache;
        private readonly ILogger<CargoGroupViewPreloader> _logger;
        private readonly IConfiguration? _configuration;

        public CargoGroupViewPreloader(
            CargoGroupService cargoGroupService,
            ViewContextCache viewContextCache,
            ILogger<CargoGroupViewPreloader> logger,
            IConfiguration? configuration = null)
        {
            _cargoGroupService = cargoGroupService;
            _viewContextCache = viewContextCache;
            _logger = logger;
            _configuration = configuration;
        }

        private bool IsPreloadingEnabled()
        {
            return _configuration?.GetValue<bool>("ViewContextPreloading:Enabled", true) ?? true;
        }

        private static string GetCacheKey(string groupIdentifier, CargoType? type) =>
            $"cargo_group_view:{groupIdentifier}:{type?.ToString() ?? "any"}";

        /// <summary>
        /// Load (or retrieve from cache) the cargo group view context.
        /// Currently relies on CargoGroupService.GetCargoGroupAsync, which already returns a CargoGroupDto
        /// with the aggregated CargoGroupDataDto (ICUMS, scanner, images) populated.
        /// </summary>
        public async Task<CargoGroupViewContext?> LoadAsync(
            string groupIdentifier,
            CargoType? type,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(groupIdentifier))
            {
                _logger.LogWarning("Attempted to load cargo group view with empty identifier");
                return null;
            }

            // Check if preloading is disabled via feature flag
            if (!IsPreloadingEnabled())
            {
                _logger.LogDebug("View context preloading is disabled - loading cargo group directly for {Identifier}", groupIdentifier);
                var group = await _cargoGroupService.GetCargoGroupAsync(groupIdentifier, type);
                if (group == null) return null;

                return new CargoGroupViewContext
                {
                    GroupIdentifier = groupIdentifier,
                    Type = type,
                    Group = group,
                    GroupData = group.Data
                };
            }

            var startTime = DateTime.UtcNow;
            var cacheSource = "unknown";

            try
            {
                var key = GetCacheKey(groupIdentifier, type);

                if (forceRefresh)
                {
                    _logger.LogInformation("Force refresh requested for cargo group view {Identifier}", groupIdentifier);
                    _viewContextCache.Remove(key);
                    cacheSource = "force_refresh";
                }
                else if (_viewContextCache.TryGet<CargoGroupViewContext>(key, out var existing) && existing != null)
                {
                    var cacheDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                    var cachedContainerCount = existing.Group?.TotalContainers ?? 0;
                    var cachedIcumsGroupCount = existing.GroupData?.ICUMSData?.Count ?? 0;
                    var cachedScannerGroupCount = existing.GroupData?.ScannerData?.Count ?? 0;
                    var cachedImageGroupCount = existing.GroupData?.ImageData?.Count ?? 0;

                    _logger.LogInformation(
                        "CargoGroupViewContext cache HIT for {Identifier} (retrieved in {Duration}ms): Containers={ContainerCount}, ICUMSGroups={ICUMSCount}, ScannerGroups={ScannerCount}, ImageGroups={ImageCount}",
                        groupIdentifier,
                        cacheDuration,
                        cachedContainerCount,
                        cachedIcumsGroupCount,
                        cachedScannerGroupCount,
                        cachedImageGroupCount);
                    return existing;
                }

                cacheSource = "api_load";
                _logger.LogInformation("CargoGroupViewContext cache MISS - Preloading cargo group view for {Identifier} (type={Type})", groupIdentifier, type);

                // Load the core CargoGroupDto (includes CargoGroupDataDto for ICUMS/Scanner/Images)
                var group = await _cargoGroupService.GetCargoGroupAsync(groupIdentifier, type);
                cancellationToken.ThrowIfCancellationRequested();

                if (group == null)
                {
                    _logger.LogWarning("No cargo group found for identifier {Identifier}", groupIdentifier);
                    return null;
                }

                var context = new CargoGroupViewContext
                {
                    GroupIdentifier = groupIdentifier,
                    Type = type,
                    Group = group,
                    GroupData = group.Data
                };

                // Calculate counts and size estimate
                var containerCount = group.TotalContainers;
                var icumsGroupCount = group.Data?.ICUMSData?.Count ?? 0;
                var scannerGroupCount = group.Data?.ScannerData?.Count ?? 0;
                var imageGroupCount = group.Data?.ImageData?.Count ?? 0;

                // Count total records across all groups
                var totalICUMSCount = group.Data?.ICUMSData?.Sum(g => g.Records?.Count ?? 0) ?? 0;
                var totalScannerCount = group.Data?.ScannerData?.Sum(g => g.Records?.Count ?? 0) ?? 0;
                var totalImageCount = group.Data?.ImageData?.Sum(g => g.Images?.Count ?? 0) ?? 0;
                var totalRecordCount = totalICUMSCount + totalScannerCount + totalImageCount;

                // Rough size estimate: base ~5KB, each container ~2KB, each record ~500 bytes
                var sizeEstimate = 5120 + (containerCount * 2048) + (totalRecordCount * 500);
                var totalDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;

                // Check soft limits and log warnings
                if (containerCount > 50 || totalRecordCount > 10000)
                {
                    _logger.LogWarning(
                        "CargoGroupViewContext for {Identifier} exceeds soft limits: Containers={ContainerCount} (limit: 50), TotalRecords={TotalRecords} (limit: 10000). Estimated size: {SizeEstimate}KB",
                        groupIdentifier, containerCount, totalRecordCount, sizeEstimate / 1024);
                }

                _viewContextCache.Set(key, context);
                _logger.LogInformation(
                    "Preloaded CargoGroupViewContext for {Identifier} (type={Type}) in {TotalDuration}ms (source: {Source}): Containers={ContainerCount}, ICUMSGroups={ICUMSCount} ({TotalICUMSCount} records), ScannerGroups={ScannerCount} ({TotalScannerCount} records), ImageGroups={ImageCount} ({TotalImageCount} images), TotalRecords={TotalRecords}, EstimatedSize={SizeEstimate}KB",
                    groupIdentifier,
                    type,
                    totalDuration,
                    cacheSource,
                    containerCount,
                    icumsGroupCount,
                    totalICUMSCount,
                    scannerGroupCount,
                    totalScannerCount,
                    imageGroupCount,
                    totalImageCount,
                    totalRecordCount,
                    sizeEstimate / 1024);

                return context;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Cargo group view preload cancelled for {Identifier}", groupIdentifier);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preloading cargo group view for {Identifier}", groupIdentifier);
                return null;
            }
        }
    }
}


