using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    public class FS6000ImagePipeline : IImagePipeline
    {
        private readonly ILogger<FS6000ImagePipeline> _logger;
        private readonly ApplicationDbContext _context;

        public ScannerType ScannerType => ScannerType.FS6000;

        public FS6000ImagePipeline(ILogger<FS6000ImagePipeline> logger, ApplicationDbContext context)
        {
            _logger = logger;
            _context = context;
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
        /// Get complete container data including image and full scanner record
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <param name="imageType">Optional: Filter by image type (Main, Icon, CCR, LPR, Manifest). If not provided, returns first available image.</param>
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
                    : scan.Images.FirstOrDefault();

                if (image == null || image.ImageData == null)
                {
                    _logger.LogWarning("No image data found for FS6000 container: {ContainerNumber}, imageType: {ImageType}", containerNumber, imageType ?? "default");
                    return null;
                }

                // ✅ FIX: Get correct MIME type based on image type
                var mimeType = GetMimeTypeFromImageType(image.ImageType);

                // Convert image to JPEG bytes (or keep original format for non-JPEG images)
                var imageBytes = ConvertBase64ToJpeg(image.ImageData);
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
                    ProcessingPipeline = "FS6000-Base64-to-JPEG",
                    FromCache = false,
                    ImageSizeBytes = imageBytes.Length,
                    Quality = "High",

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
    }
}
