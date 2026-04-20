using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel.Abstractions;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.Kernel
{
    /// <summary>
    /// v2.11.0 — single resolution point for "container → <see cref="DecodedScan"/>".
    /// Encapsulates scanner detection, retriever + adapter lookup, decode
    /// orchestration, and in-memory caching. Every kernel operation goes
    /// through this; no other class needs to know which scanner produced a
    /// given scan.
    ///
    /// Cache: 30 seconds, weighted size 20 (matches the pre-refactor FS6000
    /// pipeline's budget for MemoryCache SizeLimit buckets). A hover-heavy
    /// viewer session hits the cache on every mouse-move request; a slider
    /// drag during windowing hits it across every tick.
    /// </summary>
    public sealed class ScanRouter
    {
        private readonly ILogger<ScanRouter> _logger;
        private readonly IScannerTypeDetector _detector;
        private readonly IReadOnlyDictionary<ScannerType, IScanSourceRetriever> _retrievers;
        private readonly IReadOnlyDictionary<string, IScanFormatAdapter> _adapters;
        private readonly IMemoryCache? _cache;

        private const string CacheKeyPrefix = "kernel.decoded.";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

        public ScanRouter(
            ILogger<ScanRouter> logger,
            IScannerTypeDetector detector,
            IEnumerable<IScanSourceRetriever> retrievers,
            IEnumerable<IScanFormatAdapter> adapters,
            IMemoryCache? cache = null)
        {
            _logger = logger;
            _detector = detector;

            // Index retrievers by scanner type — one per scanner. A second
            // retriever registered for the same scanner would silently win,
            // so fail loud if that happens.
            var retrieverMap = new Dictionary<ScannerType, IScanSourceRetriever>();
            foreach (var r in retrievers)
            {
                if (!retrieverMap.TryAdd(r.ScannerType, r))
                {
                    throw new InvalidOperationException(
                        $"Multiple IScanSourceRetriever registered for {r.ScannerType} — only one allowed.");
                }
            }
            _retrievers = retrieverMap;

            // Index adapters by format tag. Multiple adapters CAN exist for one
            // scanner (e.g. a future FS6000-v2 wire format) but each must declare
            // a unique SourceFormatTag.
            var adapterMap = new Dictionary<string, IScanFormatAdapter>();
            foreach (var a in adapters)
            {
                if (!adapterMap.TryAdd(a.SourceFormatTag, a))
                {
                    throw new InvalidOperationException(
                        $"Multiple IScanFormatAdapter registered for format '{a.SourceFormatTag}' — each tag must be unique.");
                }
            }
            _adapters = adapterMap;

            _cache = cache;
        }

        /// <summary>
        /// Resolve a container to its decoded IR. Returns null when no scan
        /// exists, no adapter can decode the retrieved bytes, or decoding
        /// fails. Callers that need to distinguish "no scan" from "scan
        /// exists but partial channels" use <see cref="InventoryAsync"/>.
        /// </summary>
        public async Task<DecodedScan?> GetDecodedAsync(string containerNumber, CancellationToken ct = default)
        {
            if (_cache != null && _cache.TryGetValue(CacheKeyPrefix + containerNumber, out DecodedScan? cached) && cached != null)
            {
                return cached;
            }

            var scannerType = await _detector.DetectAsync(containerNumber, ct);
            if (scannerType == ScannerType.Unknown)
            {
                _logger.LogDebug("[ScanRouter] No scanner detected for {Container}", containerNumber);
                return null;
            }

            if (!_retrievers.TryGetValue(scannerType, out var retriever))
            {
                _logger.LogWarning("[ScanRouter] No IScanSourceRetriever registered for {Scanner}", scannerType);
                return null;
            }

            var bytes = await retriever.LoadAsync(containerNumber, ct);
            if (bytes == null)
            {
                _logger.LogDebug("[ScanRouter] Retriever returned null for {Container} (scanner={Scanner})", containerNumber, scannerType);
                return null;
            }

            if (!_adapters.TryGetValue(bytes.SourceFormatTag, out var adapter))
            {
                _logger.LogError("[ScanRouter] Retriever produced format tag '{Tag}' for {Container} but no adapter is registered for it", bytes.SourceFormatTag, containerNumber);
                return null;
            }

            var decoded = await adapter.DecodeAsync(bytes, ct);
            if (decoded == null)
            {
                _logger.LogDebug("[ScanRouter] Adapter '{Tag}' returned null for {Container} — partial channels or decode failure", bytes.SourceFormatTag, containerNumber);
                return null;
            }

            if (_cache != null)
            {
                _cache.Set(CacheKeyPrefix + containerNumber, decoded, new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = CacheTtl,
                    Priority = CacheItemPriority.Low,
                    Size = 20,
                });
            }
            return decoded;
        }

        /// <summary>
        /// Cheap "what raw blobs does this container have?" check without
        /// decoding. Capabilities uses this to distinguish "no scan" (return
        /// null) from "scan exists but missing Material" (return Variant +
        /// missing-blobs hint + empty mode list).
        /// </summary>
        public async Task<(ScannerType Scanner, BlobInventory? Inventory)> InventoryAsync(string containerNumber, CancellationToken ct = default)
        {
            var scannerType = await _detector.DetectAsync(containerNumber, ct);
            if (scannerType == ScannerType.Unknown) return (scannerType, null);

            if (!_retrievers.TryGetValue(scannerType, out var retriever))
            {
                _logger.LogWarning("[ScanRouter] No IScanSourceRetriever registered for {Scanner} (inventory path)", scannerType);
                return (scannerType, null);
            }

            var inv = await retriever.InventoryAsync(containerNumber, ct);
            return (scannerType, inv);
        }
    }

    /// <summary>
    /// Abstraction over "which scanner produced this container's scan?"
    /// Exists so the kernel doesn't reach into <see cref="IImageProcessingService.DetectScannerTypeAsync"/>
    /// (which would create a circular-ish dependency since that service now
    /// delegates into the kernel). Implementation is a thin EF query.
    /// </summary>
    public interface IScannerTypeDetector
    {
        Task<ScannerType> DetectAsync(string containerNumber, CancellationToken ct = default);
    }
}
