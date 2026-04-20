using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageProcessing.FS6000;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    public class FS6000ImagePipeline : IImagePipeline
    {
        private readonly ILogger<FS6000ImagePipeline> _logger;
        private readonly ApplicationDbContext _context;
        private readonly FS6000CompositeProxyClient? _compositeProxy;

        public ScannerType ScannerType => ScannerType.FS6000;

        public FS6000ImagePipeline(
            ILogger<FS6000ImagePipeline> logger,
            ApplicationDbContext context,
            FS6000CompositeProxyClient? compositeProxy = null)
        {
            _logger = logger;
            _context = context;
            _compositeProxy = compositeProxy;
        }

        public async Task<Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber)
        {
            _logger.LogInformation("Processing FS6000 image for container: {ContainerNumber}", containerNumber);

            try
            {
                var scan = await _context.FS6000Scans
                    .Include(s => s.Images)
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (scan == null)
                {
                    return new Core.Models.ImageProcessingResult
                    {
                        ImageId = 0,
                        Status = "Failed",
                        ProcessingType = "FS6000Processing",
                        ProcessedAt = DateTime.UtcNow,
                        ErrorMessage = "FS6000 scan not found for container"
                    };
                }

                var image = scan.Images.FirstOrDefault();
                if (image == null || image.ImageData == null)
                {
                    return new Core.Models.ImageProcessingResult
                    {
                        ImageId = 0,
                        Status = "Failed",
                        ProcessingType = "FS6000Processing",
                        ProcessedAt = DateTime.UtcNow,
                        ErrorMessage = "No image data found in FS6000 scan"
                    };
                }

                // FS6000 images are already in Base64 format, convert to JPEG bytes
                var imageBytes = ConvertBase64ToJpeg(image.ImageData);

                return new Core.Models.ImageProcessingResult
                {
                    ImageId = 0, // Placeholder since we don't have an image ID for this method
                    Status = "Success",
                    ProcessingType = "FS6000Processing",
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTime = 1.0, // Placeholder
                    Result = $"Processed FS6000 image for container {containerNumber}",
                    ErrorMessage = null,
                    AnalysisResults = new Dictionary<string, object>
                    {
                        { "ContainerNumber", containerNumber },
                        { "ScannerType", "FS6000" },
                        { "ImageDataSize", imageBytes.Length },
                        { "ImageData", imageBytes }, // Store actual image bytes
                        { "MimeType", "image/jpeg" },
                        { "ScanTime", scan.ScanTime },
                        { "ScannerId", "FS6000" },
                        { "ImageFormat", "JPEG" },
                        { "ProcessingPipeline", "FS6000-Base64-to-JPEG" },
                        { "Quality", "High" }
                    },
                    QualityScore = null
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing FS6000 image for container: {ContainerNumber}", containerNumber);
                return new Core.Models.ImageProcessingResult
                {
                    ImageId = 0,
                    Status = "Failed",
                    ProcessingType = "FS6000Processing",
                    ProcessedAt = DateTime.UtcNow,
                    ErrorMessage = ex.Message
                };
            }
        }

        public async Task<Core.Interfaces.ImageMetadata> GetImageMetadataAsync(string containerNumber)
        {
            try
            {
                var scan = await _context.FS6000Scans
                    .Include(s => s.Images)
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (scan?.Images?.FirstOrDefault() is var image && image != null)
                {
                    return new Core.Interfaces.ImageMetadata
                    {
                        FileSizeBytes = image.ImageData?.Length ?? 0,
                        ScanTime = scan.ScanTime,
                        ImageFormat = "Base64",
                        ProcessingPipeline = "FS6000-Base64-to-JPEG"
                    };
                }

                return new Core.Interfaces.ImageMetadata();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting FS6000 image metadata for container: {ContainerNumber}", containerNumber);
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
        /// Get complete container data including image and full scanner record.
        ///
        /// v2.9.7 serving rules for the "Main" image (no explicit imageType):
        ///   1. If fs6000images has HighEnergy + LowEnergy + Material blobs for
        ///      this scan, render a 16-bit composite using the native C#
        ///      decoder + compositor (FS6000FormatDecoder + FS6000Compositor),
        ///      re-encode as JPEG at the composite's native dimensions
        ///      (2295x1378 — no resize, matches header dims exactly).
        ///   2. If the native path throws (truncated blob, header mismatch,
        ///      etc.), fall back to the Python inspector over HTTP. This is a
        ///      transitional safety net from v2.9.6 → v2.9.7; retire in v2.9.8
        ///      once the native path has accumulated production miles.
        ///   3. If both composite paths fail, serve the vendor JPEG stored in
        ///      fs6000images (imagetype='Main') at its native dimensions
        ///      (typically 2295x1378).
        /// Explicit imageType requests (Icon, CCR, LPR, Manifest) always bypass
        /// the 16-bit composite path and return the exact stored blob.
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <param name="imageType">Optional: Filter by image type (Main, Icon, CCR, LPR, Manifest). If not provided, returns the canonical "viewer" image (Main with 16-bit composite upgrade when available).</param>
        public async Task<ContainerImageDataResponse?> GetCompleteContainerDataAsync(string containerNumber, string? imageType = null)
        {
            _logger.LogInformation("Getting complete FS6000 data for container: {ContainerNumber}, imageType: {ImageType}", containerNumber, imageType ?? "default");

            try
            {
                var scan = await _context.FS6000Scans
                    .Include(s => s.Images)
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (scan == null)
                {
                    _logger.LogWarning("FS6000 scan not found for container: {ContainerNumber}", containerNumber);
                    return null;
                }

                // ✅ FIX: Filter by image type if provided, otherwise get first available image
                var image = !string.IsNullOrEmpty(imageType)
                    ? scan.Images.FirstOrDefault(i => i.ImageType.Equals(imageType, StringComparison.OrdinalIgnoreCase))
                    : scan.Images.FirstOrDefault(i => i.ImageType == "Main") ?? scan.Images.FirstOrDefault();

                if (image == null || image.ImageData == null)
                {
                    _logger.LogWarning("No image data found for FS6000 container: {ContainerNumber}, imageType: {ImageType}", containerNumber, imageType ?? "default");
                    return null;
                }

                // v2.9.7: if this is the default ("Main") serve path and the scan
                // has all three raw channels, render from 16-bit composite
                // instead of returning the vendor JPEG. No resize — native dims.
                //
                // Order: native C# path → Python HTTP proxy fallback → vendor JPEG.
                byte[]? imageBytes = null;
                string pipelineTag = "FS6000-Base64-to-JPEG";

                bool isMainServe = string.IsNullOrEmpty(imageType)
                    || imageType.Equals("Main", StringComparison.OrdinalIgnoreCase);

                if (isMainServe)
                {
                    var highBlob = scan.Images.FirstOrDefault(i => i.ImageType == "HighEnergy")?.ImageData;
                    var lowBlob = scan.Images.FirstOrDefault(i => i.ImageType == "LowEnergy")?.ImageData;
                    var matBlob = scan.Images.FirstOrDefault(i => i.ImageType == "Material")?.ImageData;

                    if (highBlob != null && lowBlob != null && matBlob != null)
                    {
                        // 1) Primary: native C# decode + composite (no network hop)
                        imageBytes = TryRenderCompositeNative(scan.Id, containerNumber, highBlob, lowBlob, matBlob);
                        if (imageBytes != null)
                        {
                            pipelineTag = "FS6000-Composite16bit-Native";
                        }

                        // 2) Fallback: Python inspector HTTP proxy
                        if (imageBytes == null && _compositeProxy != null)
                        {
                            imageBytes = await TryRenderComposite16BitAsync(scan.Id, containerNumber);
                            if (imageBytes != null)
                            {
                                pipelineTag = "FS6000-Composite16bit-PythonFallback";
                            }
                        }
                    }
                }

                // v2.9.11: raw-channel tabs (HighEnergy / LowEnergy / Material) used to
                // hand the untouched .img blob to the browser tagged as image/jpeg —
                // 7 MB of big-endian pixel data labelled "JPEG" → every browser
                // silently failed the load. Decode + render as a real image here.
                if (imageBytes == null && !isMainServe && image.ImageData != null)
                {
                    bool isRawChannel = imageType!.Equals(FS6000ChannelRenderer.ChannelHighEnergy, StringComparison.OrdinalIgnoreCase)
                                     || imageType.Equals(FS6000ChannelRenderer.ChannelLowEnergy, StringComparison.OrdinalIgnoreCase)
                                     || imageType.Equals(FS6000ChannelRenderer.ChannelMaterial, StringComparison.OrdinalIgnoreCase);
                    if (isRawChannel)
                    {
                        try
                        {
                            var started = DateTime.UtcNow;
                            imageBytes = FS6000ChannelRenderer.RenderChannelJpeg(image.ImageData, imageType);
                            pipelineTag = $"FS6000-RawChannel-{imageType}";
                            _logger.LogInformation(
                                "[FS6000-RAW-CHANNEL] scan {ScanId} ({Container}) channel {Channel}: rendered {OutBytes} bytes JPEG in {ElapsedMs:F0}ms",
                                scan.Id, containerNumber, imageType, imageBytes.Length,
                                (DateTime.UtcNow - started).TotalMilliseconds);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex,
                                "[FS6000-RAW-CHANNEL] scan {ScanId} ({Container}) channel {Channel}: render failed — falling back to raw blob",
                                scan.Id, containerNumber, imageType);
                        }
                    }
                }

                // Fallback (or non-Main request): vendor JPEG / stored bytes as-is
                if (imageBytes == null)
                {
                    imageBytes = ConvertBase64ToJpeg(image.ImageData);
                }

                // ✅ FIX: Get correct MIME type based on image type
                var mimeType = GetMimeTypeFromImageType(image.ImageType);
                // If we served the composite or rendered a raw channel, it's always JPEG
                if (pipelineTag.StartsWith("FS6000-Composite16bit", StringComparison.Ordinal)
                    || pipelineTag.StartsWith("FS6000-RawChannel-", StringComparison.Ordinal))
                    mimeType = "image/jpeg";

                var base64String = Convert.ToBase64String(imageBytes);

                // Build complete response
                var response = new ContainerImageDataResponse
                {
                    ContainerNumber = containerNumber,
                    DetectedScanner = ScannerType.FS6000,
                    ImageBase64 = base64String,
                    ImageBytes = imageBytes,
                    MimeType = mimeType, // ✅ FIX: Use correct MIME type based on image type
                    ScanTime = scan.ScanTime,
                    ProcessingPipeline = pipelineTag,
                    FromCache = false,
                    ImageSizeBytes = imageBytes.Length,
                    Quality = pipelineTag.StartsWith("FS6000-Composite16bit", StringComparison.Ordinal) ? "High-16bit" : "High",

                    // Complete FS6000 scanner data
                    FS6000Data = new FS6000ScanData
                    {
                        Id = 0, // Using 0 as we're using Guid in database but int in DTO
                        ContainerNumber = scan.ContainerNumber,
                        ScanTime = scan.ScanTime,
                        XmlFilePath = scan.FilePath ?? string.Empty,
                        ImageFilePath = scan.FilePath ?? string.Empty,
                        FolderPath = scan.FilePath ?? string.Empty,
                        ProcessedAt = scan.ProcessedAt ?? DateTime.UtcNow,
                        ProcessingStatus = scan.SyncStatus,
                        ErrorMessage = scan.ImageValidationError,
                        ImageCount = scan.Images?.Count ?? 0,
                        Images = scan.Images?.Select(img => new FS6000ImageInfo
                        {
                            Id = 0, // Using 0 as we're using Guid in database but int in DTO
                            ImageType = img.ImageType ?? "Main",
                            ImageSize = img.ImageData?.Length ?? 0,
                            CaptureTime = img.CreatedAt
                        }).ToList()
                    }
                };

                _logger.LogInformation("Successfully retrieved complete FS6000 data for container: {ContainerNumber}", containerNumber);
                return response;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting complete FS6000 data for container: {ContainerNumber}", containerNumber);
                return null;
            }
        }

        private byte[] ConvertBase64ToJpeg(byte[] base64Data)
        {
            try
            {
                // FS6000 ImageData is already byte array from Base64 conversion
                // Just return as is since it's already JPEG bytes
                _logger.LogDebug("Successfully retrieved FS6000 image data. Size: {Size} bytes", base64Data.Length);
                return base64Data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting Base64 to JPEG");
                throw;
            }
        }

        /// <summary>
        /// Get MIME type based on FS6000 image type
        /// </summary>
        private static string GetMimeTypeFromImageType(string imageType)
        {
            return imageType?.ToLowerInvariant() switch
            {
                "main" => "image/jpeg",
                "icon" => "image/png",
                "ccr" => "image/bmp",
                "lpr" => "image/tiff",
                "manifest" => "application/pdf",
                _ => "image/jpeg" // Default to JPEG
            };
        }

        /// <summary>
        /// Attempt to render the FS6000 16-bit composite using the native C#
        /// decoder + compositor (no Python round-trip). Re-encodes PNG → JPEG
        /// at quality 90 for wire compatibility with the existing serving path.
        /// Returns null on any failure so the caller can fall back to the
        /// Python HTTP proxy. See v2.9.7 change notes.
        /// </summary>
        private byte[]? TryRenderCompositeNative(
            Guid scanId,
            string containerNumber,
            byte[] highBlob,
            byte[] lowBlob,
            byte[] materialBlob)
        {
            try
            {
                var started = DateTime.UtcNow;

                var decoded = FS6000FormatDecoder.Decode(highBlob, lowBlob, materialBlob);
                var pngBytes = FS6000Compositor.CompositeRgbPng(decoded);

                // Re-encode PNG → JPEG at native dims (no resize). Quality 90 is
                // a visually-lossless choice for x-ray scans; the composite is
                // already 8-bit RGB so JPEG's lossy chroma subsampling doesn't
                // cost us anything visible.
                using var ms = new MemoryStream(pngBytes);
                using var img = Image.Load(ms);
                using var outMs = new MemoryStream();
                img.SaveAsJpeg(outMs, new JpegEncoder { Quality = 90 });
                var jpeg = outMs.ToArray();

                var elapsed = (DateTime.UtcNow - started).TotalMilliseconds;
                _logger.LogInformation(
                    "[FS6000-COMPOSITE-NATIVE] scan {ScanId} ({Container}): rendered {Width}x{Height} composite in {ElapsedMs:F0}ms, {OutBytes} bytes JPEG",
                    scanId, containerNumber, decoded.Width, decoded.Height, elapsed, jpeg.Length);
                return jpeg;
            }
            catch (InvalidDataException ex)
            {
                // Header/format problem in the stored blob. The Python fallback
                // won't fare any better (it uses the same bytes), but we log
                // loudly so it shows up in triage.
                _logger.LogWarning(ex,
                    "[FS6000-COMPOSITE-NATIVE] scan {ScanId} ({Container}): invalid channel data — falling back to Python proxy",
                    scanId, containerNumber);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "[FS6000-COMPOSITE-NATIVE] scan {ScanId} ({Container}): unexpected native render failure — falling back to Python proxy",
                    scanId, containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Attempt to render the FS6000 16-bit composite via the Python inspector
        /// and re-encode as JPEG. Returns null on any failure so the caller can
        /// fall back to the vendor JPEG. Dimensions are preserved from the
        /// composite output — NO resize. See v2.9.6 change notes.
        /// </summary>
        private async Task<byte[]?> TryRenderComposite16BitAsync(Guid scanId, string containerNumber)
        {
            if (_compositeProxy == null) return null;

            try
            {
                var pngBytes = await _compositeProxy.GetCompositePngAsync(scanId);
                if (pngBytes == null || pngBytes.Length == 0)
                {
                    _logger.LogWarning("[FS6000-COMPOSITE] scan {ScanId}: proxy returned empty payload", scanId);
                    return null;
                }

                // Re-encode PNG → JPEG at native dims (no resize). Quality 90 is a
                // visually-lossless choice for x-ray scans; the composite is already
                // 8-bit BGR so JPEG's lossy chroma subsampling doesn't cost us anything
                // visible.
                using var ms = new MemoryStream(pngBytes);
                using var img = await Image.LoadAsync(ms);
                using var outMs = new MemoryStream();
                await img.SaveAsJpegAsync(outMs, new JpegEncoder { Quality = 90 });
                var jpeg = outMs.ToArray();
                _logger.LogInformation("[FS6000-COMPOSITE] scan {ScanId} ({Container}): rendered {Width}x{Height} composite, {OutBytes} bytes JPEG",
                    scanId, containerNumber, img.Width, img.Height, jpeg.Length);
                return jpeg;
            }
            catch (System.Net.Http.HttpRequestException ex)
            {
                _logger.LogWarning(ex, "[FS6000-COMPOSITE] Python inspector rejected composite request for scan {ScanId} — falling back to vendor JPEG", scanId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-COMPOSITE] Unexpected error rendering composite for scan {ScanId} — falling back to vendor JPEG", scanId);
                return null;
            }
        }
    }
}
