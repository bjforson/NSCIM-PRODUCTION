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

    }

    // ScanModeCapabilities lives in Core.Interfaces since it's part of the
    // IImageProcessingService contract. See there for the canonical type.
}
