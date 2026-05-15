using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanWebApp.New.Models;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Orchestrates loading all data needed for an audit review view.
    /// </summary>
    public class AuditReviewViewPreloader
    {
        private readonly ContainerViewPreloader _containerPreloader;
        private readonly ViewContextCache _viewContextCache;
        private readonly ILogger<AuditReviewViewPreloader> _logger;
        private readonly IConfiguration? _configuration;

        public AuditReviewViewPreloader(
            ContainerViewPreloader containerPreloader,
            ViewContextCache viewContextCache,
            ILogger<AuditReviewViewPreloader> logger,
            IConfiguration? configuration = null)
        {
            _containerPreloader = containerPreloader;
            _viewContextCache = viewContextCache;
            _logger = logger;
            _configuration = configuration;
        }

        private bool IsPreloadingEnabled() =>
            _configuration?.GetValue<bool>("ViewContextPreloading:Enabled", true) ?? true;

        private static string GetCacheKey(string groupIdentifier, IEnumerable<string> containerNumbers)
        {
            var containerSignature = string.Join("|", containerNumbers
                .Where(containerNumber => !string.IsNullOrWhiteSpace(containerNumber))
                .Select(containerNumber => containerNumber.Trim().ToUpperInvariant())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(containerNumber => containerNumber, StringComparer.OrdinalIgnoreCase));

            return $"audit_review_view:{groupIdentifier.Trim().ToUpperInvariant()}:{containerSignature}";
        }

        /// <summary>
        /// Load all container contexts needed by the audit dialog through the shared
        /// container preloader, which itself uses the predictive cache first.
        /// </summary>
        public async Task<AuditReviewViewContext?> LoadAsync(
            string groupIdentifier,
            IEnumerable<string> containerNumbers,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(groupIdentifier))
            {
                _logger.LogWarning("Attempted to load audit review view with empty group identifier");
                return null;
            }

            var containersToLoad = containerNumbers
                .Where(containerNumber => !string.IsNullOrWhiteSpace(containerNumber))
                .Select(containerNumber => containerNumber.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            var context = new AuditReviewViewContext
            {
                GroupIdentifier = groupIdentifier
            };

            if (!IsPreloadingEnabled())
            {
                _logger.LogDebug("View context preloading is disabled - returning minimal audit review context for {GroupIdentifier}", groupIdentifier);
                return context;
            }

            var key = GetCacheKey(groupIdentifier, containersToLoad);
            var startTime = DateTime.UtcNow;

            if (forceRefresh)
            {
                _logger.LogInformation("Force refresh requested for audit review view {GroupIdentifier}", groupIdentifier);
                _viewContextCache.Remove(key);
            }
            else if (_viewContextCache.TryGet<AuditReviewViewContext>(key, out var existing) && existing != null)
            {
                var cacheDuration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation(
                    "AuditReviewViewContext cache HIT for {GroupIdentifier} (retrieved in {Duration}ms): Containers={ContainerCount}",
                    groupIdentifier,
                    cacheDuration,
                    existing.ContainerCount);
                return existing;
            }

            try
            {
                _logger.LogInformation(
                    "AuditReviewViewContext cache MISS - Preloading {ContainerCount} containers for audit group {GroupIdentifier}",
                    containersToLoad.Count,
                    groupIdentifier);

                var loadTasks = containersToLoad
                    .Select(containerNumber => _containerPreloader.LoadAsync(
                        containerNumber,
                        forceRefresh,
                        cancellationToken,
                        groupIdentifier))
                    .ToList();

                await Task.WhenAll(loadTasks);
                cancellationToken.ThrowIfCancellationRequested();

                var loadedCount = 0;
                var failedCount = 0;
                for (var i = 0; i < containersToLoad.Count; i++)
                {
                    var containerNumber = containersToLoad[i];
                    var containerContext = await loadTasks[i];

                    if (containerContext != null)
                    {
                        context.ContainerContexts[containerNumber] = containerContext;
                        loadedCount++;
                    }
                    else
                    {
                        failedCount++;
                        _logger.LogWarning(
                            "Failed to load container context for {ContainerNumber} in audit group {GroupIdentifier}",
                            containerNumber,
                            groupIdentifier);
                    }
                }

                _viewContextCache.Set(key, context);

                var duration = (DateTime.UtcNow - startTime).TotalMilliseconds;
                _logger.LogInformation(
                    "Preloaded AuditReviewViewContext for {GroupIdentifier} in {Duration}ms: Containers={LoadedCount}/{TotalCount}, Failed={FailedCount}",
                    groupIdentifier,
                    duration,
                    loadedCount,
                    containersToLoad.Count,
                    failedCount);

                return context;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning("Audit review preload cancelled for {GroupIdentifier}", groupIdentifier);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preloading audit review view for {GroupIdentifier}", groupIdentifier);
                return null;
            }
        }

        public void ClearLocalContext(string groupIdentifier, IEnumerable<string> containerNumbers)
        {
            var key = GetCacheKey(groupIdentifier, containerNumbers);
            _viewContextCache.Remove(key);
            _logger.LogDebug("Cleared local AuditReviewViewContext cache for {GroupIdentifier}", groupIdentifier);
        }
    }
}

