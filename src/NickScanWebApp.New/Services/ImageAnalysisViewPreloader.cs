using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;
using NickScanWebApp.New.Models;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Orchestrates loading all data needed for an image analysis view dialog.
    /// Handles both consolidated (single container) and non-consolidated (multiple containers) scenarios.
    /// </summary>
    public class ImageAnalysisViewPreloader
    {
        private readonly ContainerViewPreloader _containerPreloader;
        private readonly CargoGroupViewPreloader _cargoGroupPreloader;
        private readonly ViewContextCache _viewContextCache;
        private readonly ILogger<ImageAnalysisViewPreloader> _logger;
        private readonly IConfiguration? _configuration;

        public ImageAnalysisViewPreloader(
            ContainerViewPreloader containerPreloader,
            CargoGroupViewPreloader cargoGroupPreloader,
            ViewContextCache viewContextCache,
            ILogger<ImageAnalysisViewPreloader> logger,
            IConfiguration? configuration = null)
        {
            _containerPreloader = containerPreloader;
            _cargoGroupPreloader = cargoGroupPreloader;
            _viewContextCache = viewContextCache;
            _logger = logger;
            _configuration = configuration;
        }

        private static string GetCacheKey(string groupIdentifier, bool isConsolidated) =>
            $"image_analysis_view:{groupIdentifier}:{(isConsolidated ? "consolidated" : "nonconsolidated")}";

        private bool IsPreloadingEnabled() =>
            _configuration?.GetValue<bool>("ViewContextPreloading:Enabled", true) ?? true;

        /// <summary>
        /// Load (or retrieve from cache) the image analysis view context.
        /// For consolidated: GroupIdentifier is the container number.
        /// For non-consolidated: GroupIdentifier is the BOE/Declaration, and containerNumbers lists all containers.
        /// </summary>
        public async Task<ImageAnalysisViewContext?> LoadAsync(
            string groupIdentifier,
            bool isConsolidated,
            IEnumerable<string>? containerNumbers = null,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(groupIdentifier))
            {
                _logger.LogWarning("Attempted to load image analysis view with empty group identifier");
                return null;
            }

            var key = GetCacheKey(groupIdentifier, isConsolidated);
            var startTime = DateTime.UtcNow;
            var cacheSource = "unknown";

            if (!IsPreloadingEnabled())
            {
                _logger.LogDebug("View context preloading is disabled - returning minimal context for {Identifier}", groupIdentifier);
                return new ImageAnalysisViewContext
                {
                    GroupIdentifier = groupIdentifier,
                    IsConsolidated = isConsolidated
                    // Container contexts will be loaded on-demand
                };
            }

            if (forceRefresh)
            {
                _logger.LogInformation("Force refresh requested for image analysis view {Identifier}", groupIdentifier);
                _viewContextCache.Remove(key);
                cacheSource = "force_refresh";
            }
            else if (_viewContextCache.TryGet<ImageAnalysisViewContext>(key, out var existing) && existing != null)
            {
                var cacheDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation(
                    "ImageAnalysisViewContext cache HIT for {Identifier} (retrieved in {Duration}ms): Containers={ContainerCount}",
                    groupIdentifier,
                    cacheDuration,
                    existing.ContainerContexts.Count);
                return existing;
            }

            cacheSource = "api_load";
            try
            {
                _logger.LogInformation(
                    "ImageAnalysisViewContext cache MISS - Preloading image analysis view for {Identifier} (Consolidated={IsConsolidated})",
                    groupIdentifier,
                    isConsolidated);

                var context = new ImageAnalysisViewContext
                {
                    GroupIdentifier = groupIdentifier,
                    IsConsolidated = isConsolidated
                };

                // Determine which containers to load
                var containersToLoad = new List<string>();
                if (isConsolidated)
                {
                    // For consolidated, GroupIdentifier is the container number
                    containersToLoad.Add(groupIdentifier);
                }
                else
                {
                    // For non-consolidated, use provided container numbers
                    containersToLoad.AddRange(containerNumbers ?? new List<string>());
                }

                // Load cargo group context for summary (in parallel with container contexts)
                var cargoType = isConsolidated
                    ? CargoType.Consolidated
                    : CargoType.NonConsolidated;

                var cargoGroupTask = _cargoGroupPreloader.LoadAsync(groupIdentifier, cargoType, forceRefresh, cancellationToken);

                // Load all container contexts in parallel
                var containerTasks = containersToLoad.Select(containerNumber =>
                    _containerPreloader.LoadAsync(
                        containerNumber,
                        forceRefresh,
                        cancellationToken,
                        groupIdentifier)
                ).ToList();

                // Wait for all loads to complete
                var allTasks = new List<Task> { cargoGroupTask };
                allTasks.AddRange(containerTasks);
                await Task.WhenAll(allTasks);
                cancellationToken.ThrowIfCancellationRequested();

                // Store cargo group context
                context.CargoGroupContext = await cargoGroupTask;

                // Store container contexts
                var loadedCount = 0;
                var failedCount = 0;
                for (int i = 0; i < containersToLoad.Count; i++)
                {
                    var containerNumber = containersToLoad[i];
                    var containerContext = await containerTasks[i];

                    if (containerContext != null)
                    {
                        context.ContainerContexts[containerNumber] = containerContext;
                        loadedCount++;
                    }
                    else
                    {
                        _logger.LogWarning("Failed to load container context for {ContainerNumber} in image analysis view {GroupIdentifier}",
                            containerNumber, groupIdentifier);
                        failedCount++;
                    }
                }

                _viewContextCache.Set(key, context);

                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation(
                    "Preloaded ImageAnalysisViewContext for {Identifier} (Consolidated={IsConsolidated}) in {Duration}ms (source: {Source}): Containers={LoadedCount}/{TotalCount} (Failed: {FailedCount}), CargoGroup={HasCargoGroup}",
                    groupIdentifier,
                    isConsolidated,
                    duration,
                    cacheSource,
                    loadedCount,
                    containersToLoad.Count,
                    failedCount,
                    context.CargoGroupContext != null);

                return context;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Image analysis view preload cancelled for {Identifier}", groupIdentifier);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preloading image analysis view for {Identifier}", groupIdentifier);
                return null;
            }
        }

        /// <summary>
        /// Clear only the per-user view context cache for this image analysis view.
        /// Individual container and cargo group contexts remain unless explicitly cleared.
        /// </summary>
        public void ClearLocalContext(string groupIdentifier, bool isConsolidated)
        {
            var key = GetCacheKey(groupIdentifier, isConsolidated);
            _viewContextCache.Remove(key);
            _logger.LogDebug("Cleared local ImageAnalysisViewContext cache for {Identifier}", groupIdentifier);
        }
    }
}

