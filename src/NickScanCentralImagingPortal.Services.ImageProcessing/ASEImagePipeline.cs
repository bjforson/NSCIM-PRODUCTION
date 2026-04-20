using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageProcessing.ASE;
using NickScanCentralImagingPortal.Services.ImageProcessing.FS6000;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    public class ASEImagePipeline : IImagePipeline
    {
        private readonly ILogger<ASEImagePipeline> _logger;
        private readonly ApplicationDbContext _context;
        private readonly IImageCacheService _cacheService;
        private readonly IASEImageConverterService _converterService;
        private readonly IMemoryCache? _cache;

        public ScannerType ScannerType => ScannerType.ASE;

        public ASEImagePipeline(
            ILogger<ASEImagePipeline> logger,
            ApplicationDbContext context,
            IImageCacheService cacheService,
            IASEImageConverterService converterService,
            IMemoryCache? cache = null)
        {
            _logger = logger;
            _context = context;
            _cacheService = cacheService;
            _converterService = converterService;
            _cache = cache;
        }

        public async Task<Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber)
        {
            _logger.LogInformation("Processing ASE image for container: {ContainerNumber}", containerNumber);

            try
            {
                // Check cache first
                var cachedImage = await _cacheService.GetCachedImageAsync(containerNumber, ScannerType.ASE);
                if (cachedImage != null)
                {
                    _logger.LogDebug("Returning cached ASE image for container: {ContainerNumber}", containerNumber);
                    return new Core.Models.ImageProcessingResult
                    {
                        ImageId = 0, // Placeholder since we don't have an image ID for this method
                        Status = "Success",
                        ProcessingType = "ASEProcessing",
                        ProcessedAt = DateTime.UtcNow,
                        ProcessingTime = 1.0, // Placeholder
                        Result = $"Processed ASE image for container {containerNumber} (from cache)",
                        ErrorMessage = null,
                        AnalysisResults = new Dictionary<string, object>
                        {
                            { "ContainerNumber", containerNumber },
                            { "ScannerType", "ASE" },
                            { "ImageDataSize", cachedImage.ImageData.Length },
                            { "ImageData", cachedImage.ImageData }, // Store actual image bytes
                            { "MimeType", cachedImage.MimeType },
                            { "Width", cachedImage.Width },
                            { "Height", cachedImage.Height },
                            { "FileSizeBytes", cachedImage.FileSizeBytes },
                            { "ScanTime", cachedImage.ScanTime },
                            { "ScannerId", containerNumber },
                            { "ImageFormat", "JPEG" },
                            { "ProcessingPipeline", cachedImage.ProcessingPipeline },
                            { "Quality", cachedImage.Quality }
                        },
                        QualityScore = null
                    };
                }

                // Get ASE scan data
                var scan = await _context.AseScans
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (scan == null)
                {
                    return new Core.Models.ImageProcessingResult
                    {
                        ImageId = 0,
                        Status = "Failed",
                        ProcessingType = "ASEProcessing",
                        ProcessedAt = DateTime.UtcNow,
                        ErrorMessage = "ASE scan not found for container"
                    };
                }

                if (scan.ScanImage == null || scan.ScanImage.Length == 0)
                {
                    return new Core.Models.ImageProcessingResult
                    {
                        ImageId = 0,
                        Status = "Failed",
                        ProcessingType = "ASEProcessing",
                        ProcessedAt = DateTime.UtcNow,
                        ErrorMessage = "No image data found in ASE scan"
                    };
                }

                // Convert proprietary format to JPEG using ASE converter
                var conversionResult = await _converterService.ConvertAseImageToJpegAsync(scan.ScanImage);

                _logger.LogDebug("ASE decode path: {Decoder} for container {Container}",
                    conversionResult.DecoderUsed, containerNumber);

                if (!conversionResult.Success)
                {
                    return new Core.Models.ImageProcessingResult
                    {
                        ImageId = 0,
                        Status = "Failed",
                        ProcessingType = "ASEProcessing",
                        ProcessedAt = DateTime.UtcNow,
                        ErrorMessage = $"Failed to convert ASE image: {conversionResult.ErrorMessage}"
                    };
                }

                var jpegBytes = conversionResult.ImageData;
                var metadata = conversionResult.Metadata;

                // Provenance-aware ProcessingPipeline tag that rides with the
                // cache row and the AnalysisResults dictionary. Ops can GROUP BY
                // this column in ImageCache to see DLL vs fallback adoption.
                var provenancePipeline = conversionResult.DecoderUsed switch
                {
                    "DLL"      => "ASE-Proprietary-to-JPEG-DLL",
                    "Fallback" => "ASE-Proprietary-to-JPEG-Fallback",
                    _          => "ASE-Proprietary-to-JPEG"
                };

                // ✅ ENHANCEMENT: Validate image before caching to prevent placeholder caching
                const int MIN_REAL_IMAGE_SIZE = 10000; // 10KB minimum for real images
                const int MIN_IMAGE_DIMENSION = 50; // Minimum width/height

                if (jpegBytes.Length < MIN_REAL_IMAGE_SIZE || metadata.Width < MIN_IMAGE_DIMENSION || metadata.Height < MIN_IMAGE_DIMENSION)
                {
                    _logger.LogWarning(
                        "⚠️ ASE conversion produced suspiciously small image for {Container}: {Size} bytes ({Width}x{Height}). NOT caching to prevent placeholder pollution.",
                        containerNumber, jpegBytes.Length, metadata.Width, metadata.Height);

                    // Return success but DON'T cache the placeholder
                    return new Core.Models.ImageProcessingResult
                    {
                        ImageId = 0,
                        Status = "Success",
                        ProcessingType = "ASEProcessing",
                        ProcessedAt = DateTime.UtcNow,
                        ProcessingTime = 1.0,
                        Result = $"⚠️ ASE image converted but NOT cached (too small: {jpegBytes.Length} bytes) - may be placeholder",
                        ErrorMessage = null,
                        AnalysisResults = new Dictionary<string, object>
                        {
                            { "ContainerNumber", containerNumber },
                            { "ScannerType", "ASE" },
                            { "ImageDataSize", jpegBytes.Length },
                            { "ImageData", jpegBytes },
                            { "MimeType", "image/jpeg" },
                            { "Width", metadata.Width },
                            { "Height", metadata.Height },
                            { "FileSizeBytes", jpegBytes.Length },
                            { "ScanTime", scan.ScanTime },
                            { "ScannerId", scan.InspectionId.ToString() },
                            { "ImageFormat", "JPEG" },
                            { "ProcessingPipeline", "ASE-Proprietary-to-JPEG-NOT-CACHED" },
                            { "Quality", "Low-Placeholder" },
                            { "CacheSkipped", true }
                        },
                        QualityScore = null
                    };
                }

                // Cache the converted image (only if it passes validation)
                _logger.LogInformation("✅ Caching valid ASE image for {Container}: {Size} bytes ({Width}x{Height})",
                    containerNumber, jpegBytes.Length, metadata.Width, metadata.Height);

                var imageCache = new ImageCache
                {
                    ContainerNumber = containerNumber,
                    ScannerType = ScannerType.ASE.ToString(),
                    ImageData = jpegBytes,
                    MimeType = "image/jpeg",
                    Width = metadata.Width,
                    Height = metadata.Height,
                    FileSizeBytes = jpegBytes.Length,
                    ScanTime = scan.ScanTime,
                    ProcessingPipeline = provenancePipeline,
                    Quality = "High"
                };

                await _cacheService.CacheImageAsync(imageCache);

                return new Core.Models.ImageProcessingResult
                {
                    ImageId = 0, // Placeholder since we don't have an image ID for this method
                    Status = "Success",
                    ProcessingType = "ASEProcessing",
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTime = 1.0, // Placeholder
                    Result = $"Processed ASE image for container {containerNumber}",
                    ErrorMessage = null,
                    AnalysisResults = new Dictionary<string, object>
                    {
                        { "ContainerNumber", containerNumber },
                        { "ScannerType", "ASE" },
                        { "ImageDataSize", jpegBytes.Length },
                        { "ImageData", jpegBytes }, // Store actual image bytes
                        { "MimeType", "image/jpeg" },
                        { "Width", metadata.Width },
                        { "Height", metadata.Height },
                        { "FileSizeBytes", jpegBytes.Length },
                        { "ScanTime", scan.ScanTime },
                        { "ScannerId", scan.InspectionId.ToString() },
                        { "ImageFormat", "JPEG" },
                        { "ProcessingPipeline", provenancePipeline },
                        { "Quality", "High" }
                    },
                    QualityScore = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ASE image for container: {ContainerNumber}", containerNumber);
                return new Core.Models.ImageProcessingResult
                {
                    ImageId = 0,
                    Status = "Failed",
                    ProcessingType = "ASEProcessing",
                    ProcessedAt = DateTime.UtcNow,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<Core.Interfaces.ImageMetadata> GetImageMetadataAsync(string containerNumber)
        {
            try
            {
                var scan = await _context.AseScans
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (scan != null && scan.ScanImage != null)
                {
                    var conversionResult = await _converterService.ConvertAseImageToJpegAsync(scan.ScanImage);
                    return new Core.Interfaces.ImageMetadata
                    {
                        Width = conversionResult.Metadata.Width,
                        Height = conversionResult.Metadata.Height,
                        FileSizeBytes = conversionResult.Metadata.FileSizeBytes,
                        ScanTime = conversionResult.Metadata.ProcessedAt, // Use ProcessedAt as ScanTime
                        ScannerId = conversionResult.Metadata.ScannerType, // Use ScannerType as ScannerId
                        ImageFormat = conversionResult.Metadata.ImageFormat,
                        ProcessingPipeline = conversionResult.Metadata.ProcessingPipeline,
                        AdditionalProperties = new Dictionary<string, object>
                        {
                            { "Quality", conversionResult.Metadata.Quality },
                            { "EnhancementApplied", conversionResult.Metadata.EnhancementApplied },
                            { "OriginalFileSizeBytes", conversionResult.Metadata.OriginalFileSizeBytes },
                            { "CompressionRatio", conversionResult.Metadata.CompressionRatio },
                            { "EnhancementType", conversionResult.Metadata.EnhancementType },
                            { "SharpeningFactor", conversionResult.Metadata.SharpeningFactor },
                            { "ContrastFactor", conversionResult.Metadata.ContrastFactor }
                        }
                    };
                }

                return new Core.Interfaces.ImageMetadata();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ASE image metadata for container: {ContainerNumber}", containerNumber);
                return new Core.Interfaces.ImageMetadata();
            }
        }

        public async Task<string> GetImageAsBase64Async(string containerNumber)
        {
            try
            {
                var result = await ProcessImageAsync(containerNumber);
                if (result.Status == "Success")
                {
                    // Get actual image data from AnalysisResults
                    if (result.AnalysisResults.ContainsKey("ImageData"))
                    {
                        var imageBytes = result.AnalysisResults["ImageData"] as byte[];
                        if (imageBytes != null && imageBytes.Length > 0)
                        {
                            // Convert byte array to Base64 string with data URI prefix
                            var base64String = Convert.ToBase64String(imageBytes);
                            return $"data:image/jpeg;base64,{base64String}";
                        }
                    }
                }
                _logger.LogWarning("GetImageAsBase64Async: No image data available for container {ContainerNumber}. Status: {Status}, Error: {Error}",
                    containerNumber, result.Status, result.ErrorMessage);
                return string.Empty;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in GetImageAsBase64Async for container {ContainerNumber}", containerNumber);
                return string.Empty; // ✅ Return empty string instead of throwing - allows detection services to handle gracefully
            }
        }

        /// <summary>
        /// Get complete container data including image and full scanner record
        /// </summary>
        public async Task<ContainerImageDataResponse?> GetCompleteContainerDataAsync(string containerNumber)
        {
            _logger.LogInformation("Getting complete ASE data for container: {ContainerNumber}", containerNumber);

            try
            {
                const int MIN_CACHE_IMAGE_SIZE = 10000; // 10KB — below this is likely a placeholder/thumbnail
                const int MIN_CACHE_DIMENSION = 100;   // Real scan images are much larger than 100px

                // Check cache first
                var cachedImage = await _cacheService.GetCachedImageAsync(containerNumber, ScannerType.ASE);
                var fromCache = cachedImage != null;

                // Validate cached image quality — stale thumbnails from older conversions must be evicted
                if (fromCache && (cachedImage!.ImageData.Length < MIN_CACHE_IMAGE_SIZE
                    || cachedImage.Width < MIN_CACHE_DIMENSION
                    || cachedImage.Height < MIN_CACHE_DIMENSION))
                {
                    _logger.LogWarning(
                        "⚠️ Stale ASE cache detected for {Container}: {Size} bytes ({Width}x{Height}). Evicting and reconverting.",
                        containerNumber, cachedImage.ImageData.Length, cachedImage.Width, cachedImage.Height);
                    await _cacheService.RemoveCachedImageAsync(containerNumber, ScannerType.ASE);
                    cachedImage = null;
                    fromCache = false;
                }

                // Get ASE scan data
                var scan = await _context.AseScans
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (scan == null)
                {
                    _logger.LogWarning("ASE scan not found for container: {ContainerNumber}", containerNumber);
                    return null;
                }

                byte[] imageBytes;
                int? width = null;
                int? height = null;
                string quality = "Unknown";
                string provenancePipeline = "ASE-Proprietary-to-JPEG";

                if (fromCache)
                {
                    imageBytes = cachedImage!.ImageData;
                    width = cachedImage.Width;
                    height = cachedImage.Height;
                    quality = cachedImage.Quality ?? "High";
                    provenancePipeline = cachedImage.ProcessingPipeline ?? "ASE-Proprietary-to-JPEG";
                    _logger.LogDebug("Using cached ASE image for container: {ContainerNumber}", containerNumber);
                }
                else
                {
                    if (scan.ScanImage == null || scan.ScanImage.Length == 0)
                    {
                        _logger.LogWarning("No image data found for ASE container: {ContainerNumber}", containerNumber);
                        return null;
                    }

                    var conversionResult = await _converterService.ConvertAseImageToJpegAsync(scan.ScanImage);

                    _logger.LogDebug("ASE decode path: {Decoder} for container {Container} (complete-data flow)",
                        conversionResult.DecoderUsed, containerNumber);

                    if (!conversionResult.Success)
                    {
                        _logger.LogError("Failed to convert ASE image for container {ContainerNumber}: {Error}",
                            containerNumber, conversionResult.ErrorMessage);
                        return null;
                    }

                    imageBytes = conversionResult.ImageData;
                    width = conversionResult.Metadata.Width;
                    height = conversionResult.Metadata.Height;
                    quality = conversionResult.Metadata.Quality ?? "High";

                    provenancePipeline = conversionResult.DecoderUsed switch
                    {
                        "DLL"      => "ASE-Proprietary-to-JPEG-DLL",
                        "Fallback" => "ASE-Proprietary-to-JPEG-Fallback",
                        _          => "ASE-Proprietary-to-JPEG"
                    };

                    // Only cache if the conversion produced a real image (not a placeholder/thumbnail)
                    if (imageBytes.Length >= MIN_CACHE_IMAGE_SIZE
                        && (width ?? 0) >= MIN_CACHE_DIMENSION
                        && (height ?? 0) >= MIN_CACHE_DIMENSION)
                    {
                        var imageCache = new ImageCache
                        {
                            ContainerNumber = containerNumber,
                            ScannerType = ScannerType.ASE.ToString(),
                            ImageData = imageBytes,
                            MimeType = "image/jpeg",
                            Width = width ?? 0,
                            Height = height ?? 0,
                            FileSizeBytes = imageBytes.Length,
                            ScanTime = scan.ScanTime,
                            ProcessingPipeline = provenancePipeline,
                            Quality = quality
                        };
                        await _cacheService.CacheImageAsync(imageCache);
                    }
                    else
                    {
                        _logger.LogWarning(
                            "⚠️ ASE conversion produced small image for {Container}: {Size} bytes ({Width}x{Height}). Serving but NOT caching.",
                            containerNumber, imageBytes.Length, width, height);
                        quality = "Low-Placeholder";
                    }
                }

                var base64String = Convert.ToBase64String(imageBytes);

                // Build complete response
                var response = new ContainerImageDataResponse
                {
                    ContainerNumber = containerNumber,
                    DetectedScanner = ScannerType.ASE,
                    ImageBase64 = base64String,
                    ImageBytes = imageBytes,
                    MimeType = "image/jpeg",
                    ScanTime = scan.ScanTime,
                    ProcessingPipeline = provenancePipeline,
                    FromCache = fromCache,
                    ImageSizeBytes = imageBytes.Length,
                    Width = width,
                    Height = height,
                    Quality = quality,

                    // Complete ASE scanner data
                    ASEData = new ASEScanData
                    {
                        Id = 0, // Using 0 as we're using Guid in database but int in DTO
                        ContainerNumber = scan.ContainerNumber ?? string.Empty,
                        ScanTime = scan.ScanTime,
                        ScanId = scan.InspectionId.ToString(),
                        OperatorId = null, // Property not in simplified model
                        Location = null, // Property not in simplified model
                        ScanMode = null, // Property not in simplified model
                        EnergyLevel = null, // Property not in simplified model
                        DoseRate = null, // Property not in simplified model
                        ProcessingStatus = "Completed", // Default value
                        ProcessedAt = scan.UpdatedAt,
                        ImageSizeBytes = scan.ScanImage?.Length ?? 0,
                        ImageWidth = width ?? 0,
                        ImageHeight = height ?? 0,
                        ImageFormat = "Proprietary-ASE",
                        ThreatDetected = null, // Property not in simplified model
                        ThreatConfidence = null, // Property not in simplified model
                        DetectionNotes = null // Property not in simplified model
                    }
                };

                _logger.LogInformation("Successfully retrieved complete ASE data for container: {ContainerNumber} (FromCache: {FromCache})",
                    containerNumber, fromCache);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting complete ASE data for container: {ContainerNumber}", containerNumber);
                return null;
            }
        }

        // ══════════════════════════════════════════════════════════════════════
        //  v2.10.0 — Mode Catalog + ROI Inspector for ASE
        //
        //  The catalog is capability-gated on lineDataType:
        //
        //    • lineDataType == 3 (ParcelDualEnergyBitmap / tri-panel, ~8% of
        //      production scans) — the 3 panels give us separable LowEnergy,
        //      HighEnergy, and Material, so all 9 FS6000 modes translate cleanly.
        //      Tri-panel blobs are split via AseTriPanelDecoder into a
        //      DecodedFs6000-shaped struct and passed to the existing
        //      FS6000ModeRenderer (code reuse; one renderer for both scanners
        //      once the tri-panel is split).
        //
        //    • lineDataType == 2 (DualEnergyBitmap / single-view, ~92% of
        //      production) — single 16-bit grayscale channel only. No separable
        //      energies, no material layer. We support only the 3 grayscale-
        //      friendly modes: bw, inverse, edge. Unsupported modes return
        //      null so the controller can fall back / 404 cleanly.
        //
        //  Rotation: the current AsePercentileRenderer post-rotates 90° CCW to
        //  match the vendor DLL's output. For mode renders we preserve the
        //  decode-native orientation (which matches FS6000's) — operators
        //  viewing modes are effectively viewing a "dual-energy inspection"
        //  screen, not the legacy vendor view; consistency with FS6000 matters
        //  more than consistency with the legacy single-view.
        // ══════════════════════════════════════════════════════════════════════

        private const string AseDecodedCacheKeyPrefix = "ase.decoded.";
        private static readonly TimeSpan AseDecodedCacheTtl = TimeSpan.FromSeconds(30);

        /// <summary>
        /// Render an ASE scan in the named operator mode. Returns JPEG bytes
        /// or null when the mode isn't supported by the scan variant (e.g.
        /// requesting <c>composite</c> on a single-view scan).
        /// </summary>
        public async Task<byte[]?> RenderImageInModeAsync(
            string containerNumber,
            string mode,
            float loPct = 1.0f,
            float hiPct = 99.5f,
            float gamma = 1.0f,
            CancellationToken ct = default)
        {
            var parsedMode = FS6000ModeRenderer.TryParseMode(mode);
            if (parsedMode == null)
            {
                _logger.LogWarning("[ASE-MODE] Unknown mode '{Mode}' for {Container}", mode, containerNumber);
                return null;
            }

            var (ase, timestamp) = await LoadDecodedAseCachedAsync(containerNumber, ct);
            if (ase == null) return null;
            var decoded = ase.Value;

            // Capability gating for single-view ASE. The mode catalog's six
            // material/dual-energy modes physically can't be built from a
            // single grayscale channel — we bail cleanly rather than return
            // garbage that happens to decode as a JPEG.
            if (!decoded.IsMultiPanel && !IsSingleViewSupportedMode(parsedMode.Value))
            {
                _logger.LogInformation(
                    "[ASE-MODE] {Container} single-view ASE (ldt=2) doesn't support mode={Mode}; returning null — UI should hide this mode button for this scan",
                    containerNumber, parsedMode);
                return null;
            }

            try
            {
                var started = DateTime.UtcNow;
                byte[] jpeg;
                if (decoded.IsMultiPanel)
                {
                    // Tri-panel: split into LowEnergy / HighEnergy / Material and
                    // hand to the same renderer FS6000 uses. This shares the
                    // percentile math, material LUT, and JPEG encoder paths.
                    var fs6000Shaped = AseTriPanelDecoder.SplitToDualEnergyShape(decoded, timestamp);
                    jpeg = FS6000ModeRenderer.RenderJpeg(fs6000Shaped, parsedMode.Value, loPct, hiPct, gamma);
                }
                else
                {
                    // Single-view: build a synthetic DecodedFs6000 with the
                    // single channel as HighEnergy (Low and Material stay zero).
                    // Only bw/inverse/edge are allowed here; each uses only the
                    // High channel in the FS6000 renderer.
                    var synthetic = new FS6000FormatDecoder.DecodedFs6000(
                        width: decoded.Width,
                        height: decoded.Height,
                        high: decoded.Pixels,
                        low: new ushort[decoded.Pixels.Length],       // unused for allowed modes
                        material: new byte[decoded.Pixels.Length],    // unused for allowed modes
                        timestamp: timestamp);
                    jpeg = FS6000ModeRenderer.RenderJpeg(synthetic, parsedMode.Value, loPct, hiPct, gamma);
                }

                _logger.LogInformation(
                    "[ASE-MODE] {Container} variant={Variant} mode={Mode} loPct={LoPct} hiPct={HiPct} gamma={Gamma}: {OutBytes}B in {ElapsedMs:F0}ms",
                    containerNumber, decoded.IsMultiPanel ? "tri-panel" : "single-view",
                    parsedMode, loPct, hiPct, gamma, jpeg.Length,
                    (DateTime.UtcNow - started).TotalMilliseconds);
                return jpeg;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ASE-MODE] Render failed for {Container} mode={Mode}", containerNumber, mode);
                return null;
            }
        }

        /// <summary>
        /// Build an ROI inspector payload for an ASE scan.
        /// <list type="bullet">
        ///   <item>Tri-panel: same shape as FS6000 (3 channels + material stats).</item>
        ///   <item>Single-view: single-channel stats only; material block is populated with a "not applicable" placeholder so the UI can render a simplified view without null-checking every field.</item>
        /// </list>
        /// </summary>
        public async Task<RoiInspectorResult?> BuildRoiInspectorAsync(
            string containerNumber,
            int x, int y, int w, int h,
            CancellationToken ct = default)
        {
            var (ase, timestamp) = await LoadDecodedAseCachedAsync(containerNumber, ct);
            if (ase == null) return null;

            if (ase.Value.IsMultiPanel)
            {
                var fs6000Shaped = AseTriPanelDecoder.SplitToDualEnergyShape(ase.Value, timestamp);
                // Reuse FS6000's ROI logic by shape-matching — but we can't call
                // the FS6000 pipeline's private helper directly from here. Duplicate
                // the tiny bit we need. Cropping + stats are pure functions.
                return BuildRoiFromDecodedDualEnergy(containerNumber, fs6000Shaped, x, y, w, h, variant: "tri-panel");
            }
            else
            {
                // Single-view ROI — single-channel stats. We return the decoded
                // channel as both HE and LE so the existing UI stats rows don't
                // need a null-check, and Material carries a synthetic "background"
                // class distribution so the category chip can render "N/A".
                return BuildRoiSingleChannel(containerNumber, ase.Value, x, y, w, h, timestamp);
            }
        }

        /// <summary>
        /// Report the scan-mode capability manifest for a container. The
        /// single-canvas viewer calls this once on open so it can gate its
        /// mode-toolbar buttons to only the modes that make sense for the
        /// scan at hand.
        /// </summary>
        public async Task<Core.Interfaces.ScanModeCapabilities?> GetModeCapabilitiesAsync(string containerNumber, CancellationToken ct = default)
        {
            var (ase, _) = await LoadDecodedAseCachedAsync(containerNumber, ct);
            if (ase == null) return null;
            return new Core.Interfaces.ScanModeCapabilities
            {
                Scanner = "ASE",
                Variant = ase.Value.IsMultiPanel ? "tri-panel" : "single-view",
                SupportedModes = ase.Value.IsMultiPanel
                    ? AllNineModeNames()
                    : SingleViewModeNames(),
            };
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static bool IsSingleViewSupportedMode(Fs6000RenderMode mode)
            => mode == Fs6000RenderMode.BlackWhite
            || mode == Fs6000RenderMode.Inverse
            || mode == Fs6000RenderMode.Edge;

        private static string[] SingleViewModeNames() => new[] { "bw", "inverse", "edge" };

        private static string[] AllNineModeNames() => new[]
        {
            "composite", "bw", "inverse", "high-pen", "low-pen",
            "organic-strip", "metal-strip", "edge", "diff",
        };

        /// <summary>
        /// Load the ASE scan's decoded pixel struct, memoised for 30 s. Returns
        /// null for "not found" or "decode failed" — both surface as "no mode
        /// render available" upstream.
        /// </summary>
        private async Task<(AseFormatDecoder.DecodedAse? Decoded, DateTime? ScanTime)> LoadDecodedAseCachedAsync(
            string containerNumber,
            CancellationToken ct)
        {
            string cacheKey = AseDecodedCacheKeyPrefix + containerNumber;
            if (_cache != null && _cache.TryGetValue(cacheKey, out CachedAse cached))
            {
                return (cached.Decoded, cached.ScanTime);
            }

            var scan = await _context.AseScans
                .AsNoTracking()
                .Where(s => s.ContainerNumber == containerNumber
                        && s.ScanImage != null
                        && s.ScanImage.Length > 16)
                .OrderByDescending(s => s.ScanTime)
                .Select(s => new { s.Id, s.ScanImage, s.ScanTime })
                .FirstOrDefaultAsync(ct);
            if (scan == null)
            {
                _logger.LogWarning("[ASE-MODE] No ASE scan with usable blob for container {Container}", containerNumber);
                return (null, null);
            }

            AseFormatDecoder.DecodedAse decoded;
            try
            {
                decoded = AseFormatDecoder.Decode(scan.ScanImage!);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ASE-MODE] Decode failed for ASE scan {ScanId} ({Container})", scan.Id, containerNumber);
                return (null, null);
            }

            if (_cache != null)
            {
                _cache.Set(cacheKey, new CachedAse(decoded, scan.ScanTime), new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = AseDecodedCacheTtl,
                    Priority = CacheItemPriority.Low,
                    // ASE scans are smaller than FS6000 (one blob vs three) —
                    // weight 5 vs FS6000's 20 in the same IMemoryCache SizeLimit
                    // bucket (1000).
                    Size = 5,
                });
            }
            return (decoded, scan.ScanTime);
        }

        private readonly record struct CachedAse(AseFormatDecoder.DecodedAse Decoded, DateTime ScanTime);

        // ── ROI builders ─────────────────────────────────────────────────

        private static RoiInspectorResult BuildRoiFromDecodedDualEnergy(
            string containerNumber,
            FS6000FormatDecoder.DecodedFs6000 d,
            int x, int y, int w, int h,
            string variant)
        {
            var started = DateTime.UtcNow;
            int x0 = Math.Max(0, Math.Min(x, d.Width - 1));
            int y0 = Math.Max(0, Math.Min(y, d.Height - 1));
            int x1 = Math.Max(x0 + 1, Math.Min(x + w, d.Width));
            int y1 = Math.Max(y0 + 1, Math.Min(y + h, d.Height));
            int cw = x1 - x0;
            int ch = y1 - y0;
            int n = cw * ch;

            var heCrop = new ushort[n];
            var leCrop = new ushort[n];
            var matCrop = new byte[n];
            for (int r = 0; r < ch; r++)
            {
                int srcRow = (y0 + r) * d.Width + x0;
                int dstRow = r * cw;
                d.High.AsSpan(srcRow, cw).CopyTo(heCrop.AsSpan(dstRow, cw));
                d.Low.AsSpan(srcRow, cw).CopyTo(leCrop.AsSpan(dstRow, cw));
                d.Material.AsSpan(srcRow, cw).CopyTo(matCrop.AsSpan(dstRow, cw));
            }

            return new RoiInspectorResult
            {
                ContainerNumber = containerNumber,
                X = x0, Y = y0, Width = cw, Height = ch,
                ImageWidth = d.Width, ImageHeight = d.Height,
                HighEnergy = RoiStatsUtil.ChannelStats16(heCrop),
                LowEnergy = RoiStatsUtil.ChannelStats16(leCrop),
                Material = RoiStatsUtil.MaterialStats(matCrop),
                HighEnergyPreviewB64 = Convert.ToBase64String(RoiPreviewUtil.EnergyPreview(heCrop, cw, ch)),
                LowEnergyPreviewB64 = Convert.ToBase64String(RoiPreviewUtil.EnergyPreview(leCrop, cw, ch)),
                MaterialPreviewB64 = Convert.ToBase64String(RoiPreviewUtil.MaterialPreview(matCrop, cw, ch)),
                ElapsedMs = (long)(DateTime.UtcNow - started).TotalMilliseconds,
            };
        }

        private static RoiInspectorResult BuildRoiSingleChannel(
            string containerNumber,
            AseFormatDecoder.DecodedAse ase,
            int x, int y, int w, int h,
            DateTime? timestamp)
        {
            var started = DateTime.UtcNow;
            int x0 = Math.Max(0, Math.Min(x, ase.Width - 1));
            int y0 = Math.Max(0, Math.Min(y, ase.Height - 1));
            int x1 = Math.Max(x0 + 1, Math.Min(x + w, ase.Width));
            int y1 = Math.Max(y0 + 1, Math.Min(y + h, ase.Height));
            int cw = x1 - x0;
            int ch = y1 - y0;
            int n = cw * ch;

            var crop = new ushort[n];
            for (int r = 0; r < ch; r++)
            {
                int srcRow = (y0 + r) * ase.Width + x0;
                int dstRow = r * cw;
                ase.Pixels.AsSpan(srcRow, cw).CopyTo(crop.AsSpan(dstRow, cw));
            }

            var stats = RoiStatsUtil.ChannelStats16(crop);
            // Single-view has no second energy, so both HE and LE return the
            // same stats (the UI can show one chart or collapse the row).
            // Material block reports "not applicable" via a synthetic
            // distribution — saves the UI from branching on variant.
            return new RoiInspectorResult
            {
                ContainerNumber = containerNumber,
                X = x0, Y = y0, Width = cw, Height = ch,
                ImageWidth = ase.Width, ImageHeight = ase.Height,
                HighEnergy = stats,
                LowEnergy = stats,
                Material = new MaterialStats
                {
                    DominantCategory = "n/a (single-view)",
                    DominantPercent = 0,
                    CategoryDistribution = new Dictionary<string, double>
                    {
                        ["background"] = 0,
                        ["noise"] = 0,
                        ["organic"] = 0,
                        ["metal"] = 0,
                    },
                },
                HighEnergyPreviewB64 = Convert.ToBase64String(RoiPreviewUtil.EnergyPreview(crop, cw, ch)),
                LowEnergyPreviewB64 = string.Empty,
                MaterialPreviewB64 = string.Empty,
                ElapsedMs = (long)(DateTime.UtcNow - started).TotalMilliseconds,
            };
        }
    }

    // ScanModeCapabilities lives in Core.Interfaces since it's part of the
    // IImageProcessingService contract. See there for the canonical type.
}
