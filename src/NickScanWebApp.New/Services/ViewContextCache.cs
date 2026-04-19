using System.Collections.Concurrent;
using NickScanWebApp.New.Models;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Simple per-user (scoped) cache for view contexts such as containers and cargo groups.
    /// This avoids recomputing the same aggregated context multiple times within a Blazor circuit.
    /// </summary>
    public class ViewContextCache
    {
        private readonly ConcurrentDictionary<string, object> _contexts = new();
        private readonly ConcurrentDictionary<string, CacheEntryMetadata> _metadata = new();

        // ✅ MEMORY FIX: Add size limits to prevent unbounded growth
        private const int MaxCacheEntries = 50; // Maximum number of cached contexts
        private const int MaxAgeMinutes = 30; // Maximum age before eviction
        private readonly object _cleanupLock = new object();

        private class CacheEntryMetadata
        {
            public DateTime CreatedAt { get; set; }
            public DateTime LastAccessedAt { get; set; }
            public int AccessCount { get; set; }
        }

        /// <summary>
        /// Try to get a cached context of the given type.
        /// </summary>
        public bool TryGet<TContext>(string key, out TContext? context) where TContext : class
        {
            if (_contexts.TryGetValue(key, out var raw) && raw is TContext typed)
            {
                context = typed;

                // Update access metadata
                if (_metadata.TryGetValue(key, out var meta))
                {
                    meta.LastAccessedAt = DateTime.UtcNow;
                    meta.AccessCount++;
                }

                return true;
            }

            context = null;
            return false;
        }

        /// <summary>
        /// Store or replace a context for the given key.
        /// </summary>
        public void Set<TContext>(string key, TContext context) where TContext : class
        {
            // ✅ MEMORY FIX: Cleanup old entries before adding new ones
            CleanupOldEntries();

            // ✅ MEMORY FIX: Enforce size limit - remove oldest entries if cache is full
            if (_contexts.Count >= MaxCacheEntries)
            {
                EvictOldestEntries(MaxCacheEntries - 1);
            }

            _contexts[key] = context;

            // Update or create metadata
            _metadata.AddOrUpdate(key,
                new CacheEntryMetadata
                {
                    CreatedAt = DateTime.UtcNow,
                    LastAccessedAt = DateTime.UtcNow,
                    AccessCount = 0
                },
                (k, existing) =>
                {
                    existing.LastAccessedAt = DateTime.UtcNow;
                    return existing;
                });
        }

        /// <summary>
        /// Remove a specific context.
        /// </summary>
        public void Remove(string key)
        {
            _contexts.TryRemove(key, out _);
            _metadata.TryRemove(key, out _);
        }

        /// <summary>
        /// Clear all cached contexts for this user/session.
        /// </summary>
        public void Clear()
        {
            _contexts.Clear();
            _metadata.Clear();
        }

        /// <summary>
        /// Get diagnostic statistics about the current cache state.
        /// </summary>
        public ViewContextCacheStats GetCacheStats()
        {
            var stats = new ViewContextCacheStats
            {
                TotalEntries = _contexts.Count,
                ContainerContexts = 0,
                CargoGroupContexts = 0,
                AuditReviewContexts = 0,
                ImageAnalysisContexts = 0,
                TotalEstimatedSizeKB = 0,
                Entries = new List<ViewContextCacheEntryStats>()
            };

            foreach (var kvp in _contexts)
            {
                var key = kvp.Key;
                var context = kvp.Value;
                var meta = _metadata.TryGetValue(key, out var m) ? m : null;

                var entryStats = new ViewContextCacheEntryStats
                {
                    Key = key,
                    Type = context.GetType().Name,
                    CreatedAt = meta?.CreatedAt ?? DateTime.UtcNow,
                    LastAccessedAt = meta?.LastAccessedAt ?? DateTime.UtcNow,
                    AccessCount = meta?.AccessCount ?? 0,
                    EstimatedSizeKB = 0
                };

                // Calculate size and count by type
                if (context is ContainerViewContext containerCtx)
                {
                    stats.ContainerContexts++;
                    var scannerCount = containerCtx.ScannerData?.TotalCount ?? 0;
                    var icumsCount = containerCtx.ICUMSData?.TotalCount ?? 0;
                    var imageCount = containerCtx.Images?.Count ?? 0;
                    entryStats.EstimatedSizeKB = (1024 + (scannerCount * 500) + (icumsCount * 500) + (imageCount * 2048)) / 1024;
                    entryStats.Details = $"Scanner: {scannerCount}, ICUMS: {icumsCount}, Images: {imageCount}";
                }
                else if (context is CargoGroupViewContext cargoCtx)
                {
                    stats.CargoGroupContexts++;
                    var containerCount = cargoCtx.Group?.TotalContainers ?? 0;
                    var icumsGroupCount = cargoCtx.GroupData?.ICUMSData?.Count ?? 0;
                    var scannerGroupCount = cargoCtx.GroupData?.ScannerData?.Count ?? 0;
                    var imageGroupCount = cargoCtx.GroupData?.ImageData?.Count ?? 0;
                    var totalRecords = (cargoCtx.GroupData?.ICUMSData?.Sum(g => g.Records?.Count ?? 0) ?? 0) +
                                      (cargoCtx.GroupData?.ScannerData?.Sum(g => g.Records?.Count ?? 0) ?? 0) +
                                      (cargoCtx.GroupData?.ImageData?.Sum(g => g.Images?.Count ?? 0) ?? 0);
                    entryStats.EstimatedSizeKB = (5120 + (containerCount * 2048) + (totalRecords * 500)) / 1024;
                    entryStats.Details = $"Containers: {containerCount}, Groups: ICUMS={icumsGroupCount}, Scanner={scannerGroupCount}, Images={imageGroupCount}, TotalRecords: {totalRecords}";
                }
                else if (context is AuditReviewViewContext auditCtx)
                {
                    stats.AuditReviewContexts++;
                    var containerCount = auditCtx.ContainerCount;
                    var totalRecords = auditCtx.ContainerContexts.Values.Sum(c =>
                        (c.ScannerData?.TotalCount ?? 0) +
                        (c.ICUMSData?.TotalCount ?? 0) +
                        (c.Images?.Count ?? 0));
                    entryStats.EstimatedSizeKB = (containerCount * 2048 + totalRecords * 500) / 1024;
                    entryStats.Details = $"Containers: {containerCount}, TotalRecords: {totalRecords}";
                }
                else if (context is ImageAnalysisViewContext imageAnalysisCtx)
                {
                    stats.ImageAnalysisContexts++;
                    var containerCount = imageAnalysisCtx.ContainerContexts.Count;
                    var totalRecords = imageAnalysisCtx.ContainerContexts.Values.Sum(c =>
                        (c.ScannerData?.TotalCount ?? 0) +
                        (c.ICUMSData?.TotalCount ?? 0) +
                        (c.Images?.Count ?? 0));
                    var cargoGroupSize = imageAnalysisCtx.CargoGroupContext != null ? 100 : 0; // Rough estimate
                    entryStats.EstimatedSizeKB = (containerCount * 2048 + totalRecords * 500 + cargoGroupSize) / 1024;
                    entryStats.Details = $"Containers: {containerCount}, TotalRecords: {totalRecords}, Consolidated: {imageAnalysisCtx.IsConsolidated}";
                }

                stats.TotalEstimatedSizeKB += entryStats.EstimatedSizeKB;
                stats.Entries.Add(entryStats);
            }

            return stats;
        }

        /// <summary>
        /// ✅ MEMORY FIX: Clean up entries older than MaxAgeMinutes.
        /// </summary>
        private void CleanupOldEntries()
        {
            lock (_cleanupLock)
            {
                var cutoffTime = DateTime.UtcNow.AddMinutes(-MaxAgeMinutes);
                var keysToRemove = new List<string>();

                foreach (var kvp in _metadata)
                {
                    if (kvp.Value.LastAccessedAt < cutoffTime)
                    {
                        keysToRemove.Add(kvp.Key);
                    }
                }

                foreach (var key in keysToRemove)
                {
                    _contexts.TryRemove(key, out _);
                    _metadata.TryRemove(key, out _);
                }
            }
        }

        /// <summary>
        /// ✅ MEMORY FIX: Evict the oldest entries until cache size is below limit.
        /// </summary>
        private void EvictOldestEntries(int targetSize)
        {
            lock (_cleanupLock)
            {
                if (_contexts.Count <= targetSize)
                    return;

                // Sort by LastAccessedAt (oldest first) and remove excess
                var sortedEntries = _metadata
                    .OrderBy(kvp => kvp.Value.LastAccessedAt)
                    .Take(_contexts.Count - targetSize)
                    .Select(kvp => kvp.Key)
                    .ToList();

                foreach (var key in sortedEntries)
                {
                    _contexts.TryRemove(key, out _);
                    _metadata.TryRemove(key, out _);
                }
            }
        }
    }

    /// <summary>
    /// Diagnostic statistics for the ViewContextCache.
    /// </summary>
    public class ViewContextCacheStats
    {
        public int TotalEntries { get; set; }
        public int ContainerContexts { get; set; }
        public int CargoGroupContexts { get; set; }
        public int AuditReviewContexts { get; set; }
        public int ImageAnalysisContexts { get; set; }
        public double TotalEstimatedSizeKB { get; set; }
        public List<ViewContextCacheEntryStats> Entries { get; set; } = new();
    }

    /// <summary>
    /// Statistics for a single cache entry.
    /// </summary>
    public class ViewContextCacheEntryStats
    {
        public string Key { get; set; } = string.Empty;
        public string Type { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime LastAccessedAt { get; set; }
        public int AccessCount { get; set; }
        public double EstimatedSizeKB { get; set; }
        public string? Details { get; set; }
    }
}


