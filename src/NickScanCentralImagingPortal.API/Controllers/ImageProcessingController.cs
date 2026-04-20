using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ImageProcessingController : ControllerBase
    {
        private readonly IImageProcessingService _imageProcessingService;
        private readonly IContainerImageRepository _containerImageRepository;
        private readonly ILogger<ImageProcessingController> _logger;
        private static readonly SemaphoreSlim _imageRequestSemaphore = new(15, 15);

        public ImageProcessingController(
            IImageProcessingService imageProcessingService,
            IContainerImageRepository containerImageRepository,
            ILogger<ImageProcessingController> logger)
        {
            _imageProcessingService = imageProcessingService;
            _containerImageRepository = containerImageRepository;
            _logger = logger;
        }

        /// <summary>
        /// Get images with filtering and pagination
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<ImageSearchResult>> GetImages(
            [FromQuery] string? containerNumber = null,
            [FromQuery] string? scannerType = null,
            [FromQuery] string? imageType = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 20,
            [FromQuery] string? sortBy = "CreatedAt",
            [FromQuery] string? sortOrder = "desc")
        {
            try
            {
                var searchCriteria = new ImageSearchCriteria
                {
                    ContainerNumber = containerNumber,
                    ScannerType = scannerType,
                    ImageType = imageType,
                    FromDate = fromDate,
                    ToDate = toDate,
                    Page = page,
                    PageSize = pageSize,
                    SortBy = sortBy ?? "CreatedAt",
                    SortOrder = sortOrder ?? "desc"
                };

                var result = await _containerImageRepository.SearchImagesAsync(searchCriteria);
                _logger.LogInformation("Retrieved {Count} images for search criteria", result.Images.Count());
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving images");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get image by ID with full details
        /// </summary>
        [HttpGet("{id}")]
        public async Task<ActionResult<ImageDetails>> GetImage(int id)
        {
            try
            {
                var image = await _containerImageRepository.GetImageByIdAsync(id);
                if (image == null)
                {
                    return NotFound($"Image with ID {id} not found");
                }
                return Ok(image);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving image {ImageId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Process image for analysis
        /// </summary>
        [HttpPost("{id}/process")]
        public async Task<ActionResult<Core.Models.ImageProcessingResult>> ProcessImage(int id, [FromBody] ImageProcessingRequest request)
        {
            try
            {
                var image = await _containerImageRepository.GetImageByIdAsync(id);
                if (image == null)
                {
                    return NotFound($"Image with ID {id} not found");
                }

                var result = await _imageProcessingService.ProcessImageAsync(image, request);
                _logger.LogInformation("Processed image {ImageId} with result {Status}", id, result.Status);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing image {ImageId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Batch process multiple images
        /// </summary>
        [HttpPost("batch-process")]
        public async Task<ActionResult<BatchProcessingResult>> BatchProcessImages([FromBody] BatchProcessingRequest request)
        {
            try
            {
                var result = await _imageProcessingService.BatchProcessImagesAsync(request);
                _logger.LogInformation("Batch processed {Count} images", request.ImageIds.Count);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error batch processing images");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get image processing statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<ActionResult<ImageProcessingStatistics>> GetImageProcessingStatistics(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                var statistics = await _containerImageRepository.GetImageProcessingStatisticsAsync(fromDate, toDate);
                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving image processing statistics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get image quality metrics
        /// </summary>
        [HttpGet("quality-metrics")]
        public async Task<ActionResult<ImageQualityMetrics>> GetImageQualityMetrics()
        {
            try
            {
                var metrics = await _containerImageRepository.GetImageQualityMetricsAsync();
                return Ok(metrics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving image quality metrics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Retry failed image processing
        /// </summary>
        [HttpPost("{id}/retry")]
        public async Task<ActionResult> RetryImageProcessing(int id)
        {
            try
            {
                var image = await _containerImageRepository.GetImageByIdAsync(id);
                if (image == null)
                {
                    return NotFound($"Image with ID {id} not found");
                }

                await _imageProcessingService.RetryImageProcessingAsync(id);
                _logger.LogInformation("Retried processing for image {ImageId}", id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying image processing for {ImageId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get image processing history
        /// </summary>
        [HttpGet("{id}/processing-history")]
        public async Task<ActionResult<IEnumerable<ImageProcessingHistory>>> GetImageProcessingHistory(int id)
        {
            try
            {
                var image = await _containerImageRepository.GetImageByIdAsync(id);
                if (image == null)
                {
                    return NotFound($"Image with ID {id} not found");
                }

                var history = await _containerImageRepository.GetImageProcessingHistoryAsync(id);
                return Ok(history);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving processing history for image {ImageId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Download image file
        /// </summary>
        [HttpGet("{id}/download")]
        public async Task<ActionResult> DownloadImage(int id)
        {
            try
            {
                var image = await _containerImageRepository.GetImageByIdAsync(id);
                if (image == null)
                {
                    return NotFound($"Image with ID {id} not found");
                }

                var imageBytes = await _containerImageRepository.GetImageBytesAsync(id);
                if (imageBytes == null)
                {
                    return NotFound("Image file not found");
                }

                return File(imageBytes, "image/jpeg", $"{image.FileName}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading image {ImageId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get images by container
        /// </summary>
        [HttpGet("by-container/{containerNumber}")]
        public async Task<ActionResult<IEnumerable<ImageDetails>>> GetImagesByContainer(string containerNumber)
        {
            try
            {
                var images = await _containerImageRepository.GetImagesByContainerAsync(containerNumber);
                _logger.LogInformation("Retrieved {Count} images for container {ContainerNumber}", images.Count(), containerNumber);
                return Ok(images);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving images for container {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get processing queue status
        /// </summary>
        [HttpGet("queue-status")]
        public async Task<ActionResult<ImageProcessingQueueStatus>> GetProcessingQueueStatus()
        {
            try
            {
                var queueStatus = await _containerImageRepository.GetProcessingQueueStatusAsync();
                return Ok(queueStatus);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving processing queue status");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Update image metadata
        /// </summary>
        [HttpPut("{id}/metadata")]
        public async Task<ActionResult> UpdateImageMetadata(int id, [FromBody] ImageMetadataUpdate metadataUpdate)
        {
            try
            {
                var image = await _containerImageRepository.GetImageByIdAsync(id);
                if (image == null)
                {
                    return NotFound($"Image with ID {id} not found");
                }

                await _containerImageRepository.UpdateImageMetadataAsync(id, metadataUpdate);
                _logger.LogInformation("Updated metadata for image {ImageId}", id);
                return Ok();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating metadata for image {ImageId}", id);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get FS6000 image as base64 string
        /// </summary>
        [HttpGet("fs6000/{scanId}/base64")]
        public async Task<ActionResult<object>> GetFS6000ImageAsBase64(Guid scanId)
        {
            try
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var scan = await dbContext.FS6000Scans
                    .Include(s => s.Images)
                    .FirstOrDefaultAsync(s => s.Id == scanId);

                if (scan == null)
                {
                    return NotFound($"FS6000 scan with ID {scanId} not found");
                }

                var image = scan.Images.FirstOrDefault();
                if (image == null || image.ImageData == null)
                {
                    return NotFound("No image data found for this scan");
                }

                var base64String = Convert.ToBase64String(image.ImageData);

                _logger.LogInformation("Retrieved FS6000 image as base64 for scan {ScanId}, size: {Size} bytes",
                    scanId, image.ImageData.Length);

                return Ok(new
                {
                    ScanId = scanId,
                    ContainerNumber = scan.ContainerNumber,
                    ImageType = image.ImageType,
                    FileName = image.FileName,
                    FileSizeBytes = image.FileSizeBytes,
                    Base64Data = base64String,
                    MimeType = GetMimeTypeFromImageType(image.ImageType),
                    RetrievedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving FS6000 image as base64 for scan {ScanId}", scanId);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get FS6000 images by container number as base64
        /// </summary>
        [HttpGet("fs6000/container/{containerNumber}/base64")]
        public async Task<ActionResult<object>> GetFS6000ImagesByContainerAsBase64(string containerNumber)
        {
            try
            {
                using var scope = HttpContext.RequestServices.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var scans = await dbContext.FS6000Scans
                    .Include(s => s.Images)
                    .Where(s => s.ContainerNumber == containerNumber)
                    .ToListAsync();

                if (!scans.Any())
                {
                    return NotFound($"No FS6000 scans found for container {containerNumber}");
                }

                var result = new List<object>();

                foreach (var scan in scans)
                {
                    foreach (var image in scan.Images.Where(i => i.ImageData != null))
                    {
                        var base64String = Convert.ToBase64String(image.ImageData!);

                        result.Add(new
                        {
                            ScanId = scan.Id,
                            ContainerNumber = scan.ContainerNumber,
                            ImageId = image.Id,
                            ImageType = image.ImageType,
                            FileName = image.FileName,
                            FileSizeBytes = image.FileSizeBytes,
                            Base64Data = base64String,
                            MimeType = GetMimeTypeFromImageType(image.ImageType),
                            ScanTime = scan.ScanTime,
                            RetrievedAt = DateTime.UtcNow
                        });
                    }
                }

                _logger.LogInformation("Retrieved {Count} FS6000 images as base64 for container {ContainerNumber}",
                    result.Count, containerNumber);

                return Ok(new
                {
                    ContainerNumber = containerNumber,
                    ImageCount = result.Count,
                    Images = result
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving FS6000 images as base64 for container {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// ⚠️ DEPRECATED: Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=thumbnail instead
        /// This endpoint will be removed in a future version.
        /// </summary>
        [Obsolete("Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=thumbnail instead")]
        [HttpGet("image/{containerNumber}/thumbnail")]
        public async Task<ActionResult> GetContainerImageThumbnail(string containerNumber)
        {
            try
            {
                _logger.LogInformation("🔍 Getting thumbnail for container {ContainerNumber} via Image Processing Pipeline", containerNumber);

                // Process the image using the pipeline (handles scanner detection & conversion)
                var result = await _imageProcessingService.ProcessImageAsync(containerNumber);

                if (result.Status != "Success" || result.AnalysisResults == null)
                {
                    _logger.LogWarning("No image found or processing failed for container {ContainerNumber}", containerNumber);
                    return NotFound($"No image found for container {containerNumber}");
                }

                // Extract image data from processing result
                if (result.AnalysisResults.TryGetValue("ImageData", out var imageDataObj) && imageDataObj is byte[] imageBytes)
                {
                    var mimeType = result.AnalysisResults.TryGetValue("MimeType", out var mimeObj)
                        ? mimeObj?.ToString() ?? "image/jpeg"
                        : "image/jpeg";

                    _logger.LogInformation("✅ Returning thumbnail ({Size} bytes, {MimeType}) for container {ContainerNumber}",
                        imageBytes.Length, mimeType, containerNumber);

                    return File(imageBytes, mimeType);
                }

                return NotFound("Image data not available");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting thumbnail for container {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// ⚠️ DEPRECATED: Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=full instead
        /// This endpoint will be removed in a future version.
        /// </summary>
        [Obsolete("Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=full instead")]
        [HttpGet("image/{containerNumber}/full")]
        public async Task<ActionResult> GetContainerImageFull(string containerNumber)
        {
            try
            {
                _logger.LogInformation("🔍 Getting full image for container {ContainerNumber} via Image Processing Pipeline", containerNumber);

                // Process the image using the pipeline (handles scanner detection & conversion)
                var result = await _imageProcessingService.ProcessImageAsync(containerNumber);

                if (result.Status != "Success" || result.AnalysisResults == null)
                {
                    _logger.LogWarning("No image found or processing failed for container {ContainerNumber}", containerNumber);
                    return NotFound($"No image found for container {containerNumber}");
                }

                // Extract image data from processing result
                if (result.AnalysisResults.TryGetValue("ImageData", out var imageDataObj) && imageDataObj is byte[] imageBytes)
                {
                    var mimeType = result.AnalysisResults.TryGetValue("MimeType", out var mimeObj)
                        ? mimeObj?.ToString() ?? "image/jpeg"
                        : "image/jpeg";

                    _logger.LogInformation("✅ Returning full image ({Size} bytes, {MimeType}) for container {ContainerNumber}",
                        imageBytes.Length, mimeType, containerNumber);

                    return File(imageBytes, mimeType);
                }

                return NotFound("Image data not available");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting full image for container {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get image as base64 for any container (uses Image Processing Pipeline)
        /// </summary>
        [HttpGet("image/{containerNumber}/base64")]
        public async Task<ActionResult<object>> GetContainerImageAsBase64(string containerNumber)
        {
            try
            {
                _logger.LogInformation("🔍 Getting base64 image for container {ContainerNumber} via Image Processing Pipeline", containerNumber);

                // Use the Image Processing Service to get base64
                var base64String = await _imageProcessingService.GetImageAsBase64Async(containerNumber);

                if (string.IsNullOrEmpty(base64String))
                {
                    return NotFound($"No image found for container {containerNumber}");
                }

                // Get scanner type and metadata for additional info
                var scannerType = await _imageProcessingService.DetectScannerTypeAsync(containerNumber);
                var metadata = await _imageProcessingService.GetImageMetadataAsync(containerNumber);

                _logger.LogInformation("✅ Returning base64 image for container {ContainerNumber}", containerNumber);

                return Ok(new
                {
                    ContainerNumber = containerNumber,
                    Base64Data = base64String,
                    ScannerType = scannerType.ToString(),
                    FileName = metadata?.ScannerId ?? $"{scannerType}_Scan_{containerNumber}.jpg",
                    FileSizeBytes = metadata?.FileSizeBytes ?? 0,
                    ScanTime = metadata?.ScanTime,
                    MimeType = "image/jpeg",
                    RetrievedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting base64 image for container {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// ⚠️ DEPRECATED: Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=thumbnail instead
        /// This endpoint will be removed in a future version.
        /// </summary>
        [Obsolete("Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=thumbnail instead")]
        [AllowAnonymous]
        [HttpGet("container/{containerNumber}/thumbnail")]
        public async Task<ActionResult> GetContainerThumbnail(string containerNumber)
        {
            try
            {
                _logger.LogInformation("🔍 Getting thumbnail JPEG for container {ContainerNumber}", containerNumber);

                var base64String = await _imageProcessingService.GetImageAsBase64Async(containerNumber);

                if (string.IsNullOrEmpty(base64String))
                {
                    _logger.LogWarning("No image data returned for container {ContainerNumber}", containerNumber);
                    return NotFound($"No image found for container {containerNumber}");
                }

                // ✅ FIX: Strip data URI prefix if present (pipelines return "data:image/jpeg;base64,...")
                if (base64String.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var commaIndex = base64String.IndexOf(',');
                    if (commaIndex >= 0 && commaIndex < base64String.Length - 1)
                    {
                        base64String = base64String.Substring(commaIndex + 1);
                    }
                }

                // ✅ FIX: Trim whitespace and newlines that might cause base64 conversion to fail
                base64String = base64String.Trim();

                if (string.IsNullOrEmpty(base64String))
                {
                    _logger.LogWarning("Base64 string is empty after processing for container {ContainerNumber}", containerNumber);
                    return NotFound($"No image data available for container {containerNumber}");
                }

                // ✅ MEMORY FIX: Use ArrayPool for base64 conversion to reduce LOH pressure
                // Base64 strings are already in memory, but we can use ArrayPool for the byte array
                // This reduces LOH fragmentation and allows GC to reclaim memory faster
                byte[]? imageBytes = null;
                try
                {
                    // Estimate byte array size (base64 is ~4/3 of original size)
                    var estimatedSize = (base64String.Length * 3) / 4;
                    var rentedArray = System.Buffers.ArrayPool<byte>.Shared.Rent(estimatedSize);
                    try
                    {
                        // Convert base64 to bytes
                        if (!Convert.TryFromBase64String(base64String, rentedArray, out var bytesWritten))
                        {
                            // Fallback to standard conversion if TryFromBase64String fails
                            imageBytes = Convert.FromBase64String(base64String);
                        }
                        else
                        {
                            // Copy only the bytes we need
                            imageBytes = new byte[bytesWritten];
                            Array.Copy(rentedArray, imageBytes, bytesWritten);
                        }
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(rentedArray, clearArray: true);
                    }
                }
                catch (FormatException fex)
                {
                    _logger.LogError(fex, "Invalid base64 string for container {ContainerNumber}. Base64 length: {Length}, First 100 chars: {Preview}",
                        containerNumber, base64String?.Length ?? 0,
                        base64String?.Length > 100 ? base64String.Substring(0, 100) : base64String);
                    return StatusCode(500, "Invalid image data format");
                }

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    _logger.LogWarning("Empty image bytes for container {ContainerNumber}", containerNumber);
                    return NotFound($"No image data available for container {containerNumber}");
                }

                _logger.LogInformation("✅ Returning thumbnail JPEG ({Size} KB) for container {ContainerNumber}",
                    imageBytes.Length / 1024, containerNumber);

                return File(imageBytes, "image/jpeg");
            }
            catch (FormatException fex)
            {
                _logger.LogError(fex, "Format error getting thumbnail for container {ContainerNumber}", containerNumber);
                return StatusCode(500, "Invalid image data format");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting thumbnail for container {ContainerNumber}: {Message}",
                    containerNumber, ex.Message);
                return StatusCode(500, $"Internal server error: {ex.Message}");
            }
        }

        /// <summary>
        /// ⚠️ DEPRECATED: Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=full instead
        /// This endpoint will be removed in a future version.
        /// </summary>
        [Obsolete("Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=full instead")]
        [AllowAnonymous]
        [HttpGet("container/{containerNumber}/full")]
        public async Task<ActionResult> GetContainerFull(string containerNumber)
        {
            try
            {
                _logger.LogInformation("🔍 Getting full JPEG for container {ContainerNumber}", containerNumber);

                var base64String = await _imageProcessingService.GetImageAsBase64Async(containerNumber);

                if (string.IsNullOrEmpty(base64String))
                {
                    return NotFound($"No image found for container {containerNumber}");
                }

                // ✅ FIX: Strip data URI prefix if present (pipelines return "data:image/jpeg;base64,...")
                if (base64String.StartsWith("data:", StringComparison.OrdinalIgnoreCase))
                {
                    var commaIndex = base64String.IndexOf(',');
                    if (commaIndex >= 0 && commaIndex < base64String.Length - 1)
                    {
                        base64String = base64String.Substring(commaIndex + 1);
                    }
                }

                // ✅ MEMORY FIX: Use ArrayPool for base64 conversion to reduce LOH pressure
                byte[]? imageBytes = null;
                try
                {
                    // Estimate byte array size (base64 is ~4/3 of original size)
                    var estimatedSize = (base64String.Length * 3) / 4;
                    var rentedArray = System.Buffers.ArrayPool<byte>.Shared.Rent(estimatedSize);
                    try
                    {
                        // Convert base64 to bytes
                        if (!Convert.TryFromBase64String(base64String, rentedArray, out var bytesWritten))
                        {
                            // Fallback to standard conversion if TryFromBase64String fails
                            imageBytes = Convert.FromBase64String(base64String);
                        }
                        else
                        {
                            // Copy only the bytes we need
                            imageBytes = new byte[bytesWritten];
                            Array.Copy(rentedArray, imageBytes, bytesWritten);
                        }
                    }
                    finally
                    {
                        System.Buffers.ArrayPool<byte>.Shared.Return(rentedArray, clearArray: true);
                    }
                }
                catch (FormatException fex)
                {
                    _logger.LogError(fex, "Invalid base64 string for container {ContainerNumber}", containerNumber);
                    return StatusCode(500, "Invalid image data format");
                }

                if (imageBytes == null || imageBytes.Length == 0)
                {
                    return NotFound($"No image data available for container {containerNumber}");
                }

                _logger.LogInformation("✅ Returning full JPEG ({Size} MB) for container {ContainerNumber}",
                    Math.Round(imageBytes.Length / 1024.0 / 1024.0, 2), containerNumber);

                return File(imageBytes, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting full image for container {ContainerNumber}", containerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get complete container data including image and full scanner records - UNIFIED ENDPOINT
        /// Returns both the image and all scanner-specific metadata in a single call
        /// </summary>
        [AllowAnonymous] // Allow unauthenticated access
        [HttpGet("container/{containerNumber}/complete")]
        [ProducesResponseType(200, Type = typeof(NickScanCentralImagingPortal.Core.Models.ContainerImageDataResponse))]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<ActionResult<NickScanCentralImagingPortal.Core.Models.ContainerImageDataResponse>> GetCompleteContainerData(string containerNumber)
        {
            try
            {
                _logger.LogInformation("🔍 Getting complete data (image + scanner records) for container {ContainerNumber}", containerNumber);

                var completeData = await _imageProcessingService.GetCompleteContainerDataAsync(containerNumber);

                if (completeData == null)
                {
                    _logger.LogWarning("❌ No data found for container {ContainerNumber}", containerNumber);
                    return NotFound(new { message = $"No data found for container {containerNumber}" });
                }

                _logger.LogInformation("✅ Returning complete data for container {ContainerNumber}: Scanner={Scanner}, ImageSize={Size}KB, FromCache={FromCache}",
                    containerNumber, completeData.DetectedScanner, completeData.ImageSizeBytes / 1024, completeData.FromCache);

                return Ok(completeData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error getting complete data for container {ContainerNumber}", containerNumber);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// ✅ UNIFIED IMAGE ENDPOINT - Serves images for all container types via unified pipeline
        /// Supports FS6000 multiple image types (Main, Icon, CCR, LPR) and size options
        /// This is the PRIMARY endpoint for all image serving - use this instead of legacy endpoints
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <param name="imageType">Optional: Image type filter for FS6000 (Main, Icon, CCR, LPR, Manifest). If not provided, returns first available image.</param>
        /// <param name="size">Optional: Image size preference (thumbnail, full, original). Default: full. Note: Currently all sizes return full resolution - resizing to be implemented.</param>
        [AllowAnonymous]
        [HttpGet("container/{containerNumber}/complete/image")]
        public async Task<ActionResult> GetCompleteContainerDataImage(
            string containerNumber,
            [FromQuery] string? imageType = null,
            [FromQuery] string size = "full",
            [FromQuery] bool annotations = false,
            [FromQuery] string? mode = null,
            [FromQuery] float? loPct = null,
            [FromQuery] float? hiPct = null,
            [FromQuery] float? gamma = null)
        {
            await _imageRequestSemaphore.WaitAsync();
            try
            {
                _logger.LogInformation("Getting image for container {ContainerNumber} via unified pipeline, imageType: {ImageType}, size: {Size}, mode: {Mode}",
                    containerNumber, imageType ?? "default", size, mode ?? "-");

                // ── Split intercept: if this container has a chosen split, serve the cropped image ──
                var splitImage = await TryGetSplitCropImageAsync(containerNumber, size);
                if (splitImage != null)
                {
                    _logger.LogInformation("Serving split crop image for container {ContainerNumber}", containerNumber);
                    Response.Headers.CacheControl = "private, max-age=300";
                    return File(splitImage, "image/jpeg");
                }

                // ── v2.10.0 Mode Catalog: if ?mode= is set, route through the mode
                //    pipeline (composite / bw / organic-strip / metal-strip / high-pen /
                //    inverse / edge / diff). Supported for FS6000 + ASE. Unsupported
                //    modes for the scan variant return a clear 422 so the UI knows
                //    to grey out that button (it should also have called
                //    /mode-capabilities on open to pre-filter, but defence in depth).
                if (!string.IsNullOrWhiteSpace(mode))
                {
                    var modeBytes = await _imageProcessingService.GetRenderedImageBytesAsync(
                        containerNumber,
                        mode,
                        loPct ?? 1.0f,
                        hiPct ?? 99.5f,
                        gamma ?? 1.0f);
                    if (modeBytes != null)
                    {
                        // Server-side thumbnail if requested.
                        if (string.Equals(size, "thumbnail", StringComparison.OrdinalIgnoreCase))
                        {
                            modeBytes = await ResizeToThumbnailAsync(modeBytes) ?? modeBytes;
                        }
                        Response.Headers.CacheControl = "private, max-age=3600";
                        _logger.LogInformation("[MODE] Serving {Container} mode={Mode} size={Bytes} bytes", containerNumber, mode, modeBytes.Length);
                        return File(modeBytes, "image/jpeg");
                    }

                    // modeBytes == null when mode is explicitly requested. Two sub-cases:
                    //   (a) scan exists but the mode isn't supported for the scan variant
                    //       (e.g. ?mode=composite on a single-view ASE) — return 422 with
                    //       a message pointing the caller at /mode-capabilities.
                    //   (b) no scan at all, or unknown scanner — return 404.
                    //   (c) mode IS supported per capabilities but the pipeline
                    //       still failed — capabilities-bug or transient decode
                    //       failure. 500 so the client doesn't grey out a legit
                    //       button.
                    // We detect (b) by asking for capabilities; if that too comes back
                    // null, the scan isn't known.
                    var caps = await _imageProcessingService.GetScanModeCapabilitiesAsync(containerNumber);
                    if (caps == null)
                    {
                        return NotFound(new { error = "No scan found for container", container = containerNumber });
                    }

                    // v2.10.5 — distinguish cases (a) and (c). If the mode IS listed
                    // as supported in capabilities, the render failure is server-side,
                    // not a client-bad-request.
                    var normalized = mode.Trim().ToLowerInvariant();
                    bool claimedSupported = caps.SupportedModes != null
                        && Array.Exists(caps.SupportedModes,
                            m => string.Equals(m, normalized, StringComparison.OrdinalIgnoreCase));
                    if (claimedSupported)
                    {
                        _logger.LogError("[MODE] {Container} claimed to support mode '{Mode}' but pipeline returned null — investigate raw-channel state", containerNumber, mode);
                        return StatusCode(500, new
                        {
                            error = $"Render failed for mode '{mode}' even though capabilities claim support — check server logs.",
                            container = containerNumber,
                            scanner = caps.Scanner,
                            variant = caps.Variant,
                        });
                    }

                    _logger.LogInformation("[MODE] Mode '{Mode}' not supported for {Container} ({Variant}); supported={Supported}",
                        mode, containerNumber, caps.Variant, string.Join(",", caps.SupportedModes ?? Array.Empty<string>()));
                    return UnprocessableEntity(new
                    {
                        error = $"Mode '{mode}' not supported for this scan variant",
                        container = containerNumber,
                        scanner = caps.Scanner,
                        variant = caps.Variant,
                        supportedModes = caps.SupportedModes,
                    });
                }

                var completeData = await _imageProcessingService.GetCompleteContainerDataAsync(containerNumber, imageType);

                // ✅ FIX: When imageType-specific request fails (e.g. ASE has ImageDisplayName but no ScanImage),
                // try fallback without imageType to get first available image from any scanner (e.g. FS6000)
                if ((completeData == null || completeData.ImageBytes == null) && !string.IsNullOrEmpty(imageType))
                {
                    _logger.LogWarning("No image for container {Container} with imageType={Type}, trying fallback (any scanner)", containerNumber, imageType);
                    completeData = await _imageProcessingService.GetCompleteContainerDataAsync(containerNumber, null);
                }

                if (completeData == null || completeData.ImageBytes == null)
                {
                    _logger.LogWarning("No image found for container {ContainerNumber}, imageType: {ImageType} — returning placeholder", containerNumber, imageType ?? "default");
                    var placeholder = GenerateNoImagePlaceholder(containerNumber);
                    Response.Headers.CacheControl = "public, max-age=60";
                    return File(placeholder, "image/jpeg");
                }

                byte[] outputBytes = completeData.ImageBytes;
                var mimeType = completeData.MimeType ?? "image/jpeg";

                // Server-side thumbnail: resize when size=thumbnail to reduce bandwidth and DB/CPU load
                if (string.Equals(size, "thumbnail", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        const int thumbMaxWidth = 240;
                        const int thumbMaxHeight = 240;
                        using var inputStream = new MemoryStream(completeData.ImageBytes);
                        using var image = await Image.LoadAsync(inputStream);
                        var (newW, newH) = ComputeThumbnailSize(image.Width, image.Height, thumbMaxWidth, thumbMaxHeight);
                        if (newW < image.Width || newH < image.Height)
                        {
                            image.Mutate(x => x.Resize(newW, newH, KnownResamplers.Lanczos3));
                            using var outputStream = new MemoryStream();
                            await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = 85 });
                            outputBytes = outputStream.ToArray();
                            mimeType = "image/jpeg";
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Thumbnail resize failed for {ContainerNumber}, returning full image", containerNumber);
                    }
                }

                // Burn annotations onto image when requested (for ICUMS preview)
                if (annotations)
                {
                    try
                    {
                        var renderer = HttpContext.RequestServices.GetService<NickScanCentralImagingPortal.Services.ImageProcessing.IImageAnnotationRenderer>();
                        var dbContext = HttpContext.RequestServices.GetService<NickScanCentralImagingPortal.Infrastructure.Data.ApplicationDbContext>();
                        if (renderer != null && dbContext != null)
                        {
                            var decision = await dbContext.ImageAnalysisDecisions
                                .FirstOrDefaultAsync(d => d.ContainerNumber == containerNumber);
                            if (decision != null && (!string.IsNullOrWhiteSpace(decision.SuspiciousAreas) || !string.IsNullOrWhiteSpace(decision.Tags)))
                            {
                                outputBytes = await renderer.RenderAnnotationsAsync(outputBytes, decision.SuspiciousAreas, decision.Tags);
                                mimeType = "image/jpeg";
                            }
                        }
                    }
                    catch (Exception annotEx)
                    {
                        _logger.LogWarning(annotEx, "Failed to render annotations for {ContainerNumber}, returning raw image", containerNumber);
                    }
                }

                _logger.LogInformation("Returning image via unified pipeline: {ContainerNumber}, Size: {Size}KB, MimeType: {MimeType}, RequestedSize: {RequestedSize}, Annotations: {Annotations}",
                    containerNumber, outputBytes.Length / 1024, mimeType, size, annotations);

                // HTTP caching: use `private` so only the end-user's browser caches the image,
                // NOT the in-process ResponseCachingMiddleware. v2.9.11: we hit a bug where the
                // middleware was keying its cache poorly across imageType query params, so a
                // request for HighEnergy poisoned subsequent LowEnergy/Material/Main hits — all
                // four tabs returned the same bytes. Client-side caching is still beneficial
                // (browser revalidates in-session scroll-back) without the memory pressure of
                // holding full JPEG bodies server-side. The app pipeline will gain its own
                // content-aware byte cache in a later release if cold-render cost becomes an issue.
                Response.Headers.CacheControl = annotations ? "private, max-age=60" : "private, max-age=3600";
                return File(outputBytes, mimeType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting complete data image for container {ContainerNumber}, imageType: {ImageType}", containerNumber, imageType ?? "default");
                return StatusCode(500, "Internal server error");
            }
            finally
            {
                _imageRequestSemaphore.Release();
            }
        }

        // NOTE: Alternative path /container/{containerNumber}/image removed to avoid route conflicts
        // Use /container/{containerNumber}/complete/image instead

        /// <summary>
        /// v2.10.0 — scan-mode capability manifest. The single-canvas viewer
        /// fetches this once on open so it knows which mode-toolbar buttons
        /// to render. FS6000 always returns all 9 modes. ASE splits on
        /// lineDataType: tri-panel = 9 modes, single-view = 3 modes
        /// (bw / inverse / edge).
        /// </summary>
        [AllowAnonymous]
        [HttpGet("container/{containerNumber}/mode-capabilities")]
        [ProducesResponseType(200, Type = typeof(ScanModeCapabilities))]
        [ProducesResponseType(404)]
        public async Task<ActionResult<ScanModeCapabilities>> GetScanModeCapabilities(string containerNumber)
        {
            try
            {
                var caps = await _imageProcessingService.GetScanModeCapabilitiesAsync(containerNumber);
                if (caps == null)
                {
                    return NotFound(new { error = "No scan found or scanner type not resolvable", container = containerNumber });
                }
                Response.Headers.CacheControl = "private, max-age=60";
                return Ok(caps);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load mode capabilities for {Container}", containerNumber);
                return StatusCode(500, new { error = "mode-capabilities lookup failed", message = ex.Message });
            }
        }

        /// <summary>
        /// v2.10.0 — ROI Inspector. Crop a rectangle from the 3 FS6000 raw
        /// channels (or ASE tri-panel channels), return per-channel stats,
        /// material-class distribution, and small preview JPEGs (base64).
        /// Used by the single-canvas viewer's side panel when the operator
        /// draws an inspection rectangle.
        /// Single-view ASE scans degenerate to a one-channel histogram with
        /// a "not applicable" material block so the UI can render a
        /// simplified view without null-checking every field.
        /// </summary>
        [AllowAnonymous]
        [HttpGet("container/{containerNumber}/roi")]
        [ProducesResponseType(200, Type = typeof(RoiInspectorResult))]
        [ProducesResponseType(404)]
        public async Task<ActionResult<RoiInspectorResult>> GetRoiInspector(
            string containerNumber,
            [FromQuery] int x = 0,
            [FromQuery] int y = 0,
            [FromQuery] int w = 100,
            [FromQuery] int h = 100)
        {
            if (w <= 0 || h <= 0)
            {
                return BadRequest(new { error = "w and h must be positive" });
            }

            try
            {
                var result = await _imageProcessingService.GetRoiInspectorAsync(containerNumber, x, y, w, h);
                if (result == null)
                {
                    return NotFound(new { error = "ROI inspector unavailable — container not found, not FS6000, or lacking raw channels" });
                }
                // ROI payload includes ~3 small base64 thumbnails (~6–20 KB each).
                // private-cache for 5 min so a static rectangle doesn't re-crop on every scroll.
                Response.Headers.CacheControl = "private, max-age=300";
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ROI inspector failed for {Container} rect=({X},{Y},{W},{H})", containerNumber, x, y, w, h);
                return StatusCode(500, new { error = "ROI inspector failed", message = ex.Message });
            }
        }

        /// <summary>
        /// Downscale a JPEG byte buffer to a thumbnail (max 240 px on the long
        /// edge). Shared helper between the mode-render path and the legacy
        /// size=thumbnail branch. Returns null on decode failure so the caller
        /// can fall back to the full-size bytes.
        /// </summary>
        private async Task<byte[]?> ResizeToThumbnailAsync(byte[] input)
        {
            try
            {
                const int thumbMaxWidth = 240;
                const int thumbMaxHeight = 240;
                using var inputStream = new MemoryStream(input);
                using var image = await Image.LoadAsync(inputStream);
                var (newW, newH) = ComputeThumbnailSize(image.Width, image.Height, thumbMaxWidth, thumbMaxHeight);
                if (newW >= image.Width && newH >= image.Height)
                {
                    // Already smaller than thumbnail bounds — don't upscale.
                    return null;
                }
                image.Mutate(x => x.Resize(newW, newH, KnownResamplers.Lanczos3));
                using var outputStream = new MemoryStream();
                await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = 85 });
                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Thumbnail resize failed — serving full-size bytes");
                return null;
            }
        }

        /// <summary>
        /// Checks if this container has a chosen split crop and returns the cropped image bytes.
        /// Returns null if no split applies (single-container scan, pending, skipped, etc.)
        /// </summary>
        private async Task<byte[]?> TryGetSplitCropImageAsync(string containerNumber, string size)
        {
            try
            {
                var dbContext = HttpContext.RequestServices.GetService<ApplicationDbContext>();
                if (dbContext == null) return null;

                var record = await dbContext.AnalysisRecords
                    .Where(r => r.ContainerNumber == containerNumber &&
                                r.IsMultiContainerScan &&
                                r.SplitStatus == "Chosen" &&
                                r.SplitResultId != null &&
                                r.SplitJobId != null)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .FirstOrDefaultAsync();

                if (record == null) return null;

                var side = record.SplitPosition ?? "left";
                var client = HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>()
                    .CreateClient("RawImageEngine");
                var response = await client.GetAsync(
                    $"/api/split/{record.SplitJobId}/results/{record.SplitResultId}/image/{side}");

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning(
                        "Split crop fetch failed for container {Container}, job {Job}, result {Result}: {Status}",
                        containerNumber, record.SplitJobId, record.SplitResultId, response.StatusCode);
                    return null;
                }

                var imageBytes = await response.Content.ReadAsByteArrayAsync();

                // Apply thumbnail resize if requested
                if (string.Equals(size, "thumbnail", StringComparison.OrdinalIgnoreCase) && imageBytes.Length > 0)
                {
                    try
                    {
                        using var inputStream = new MemoryStream(imageBytes);
                        using var image = await Image.LoadAsync(inputStream);
                        var (newW, newH) = ComputeThumbnailSize(image.Width, image.Height, 240, 240);
                        if (newW < image.Width || newH < image.Height)
                        {
                            image.Mutate(x => x.Resize(newW, newH, KnownResamplers.Lanczos3));
                            using var outputStream = new MemoryStream();
                            await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = 85 });
                            return outputStream.ToArray();
                        }
                    }
                    catch { /* fall through to full-size crop */ }
                }

                return imageBytes;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Split crop intercept failed for container {Container}, falling through to original", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Generates a lightweight placeholder JPEG when the actual image data is missing.
        /// Returns a dark gray image with a subtle cross pattern so it's clearly "no image."
        /// </summary>
        private static byte[] GenerateNoImagePlaceholder(string containerNumber)
        {
            const int w = 400, h = 300;
            using var img = new Image<SixLabors.ImageSharp.PixelFormats.Rgba32>(w, h);
            var bg = new SixLabors.ImageSharp.PixelFormats.Rgba32(45, 45, 48, 255);
            var line = new SixLabors.ImageSharp.PixelFormats.Rgba32(70, 70, 75, 255);

            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < h; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < w; x++)
                    {
                        bool onCross = (Math.Abs(x * h - y * w) < w) || (Math.Abs(x * h - (h - y) * w) < w);
                        row[x] = onCross ? line : bg;
                    }
                }
            });

            using var ms = new MemoryStream();
            img.SaveAsJpeg(ms, new JpegEncoder { Quality = 60 });
            return ms.ToArray();
        }

        /// <summary>
        /// Compute thumbnail dimensions preserving aspect ratio
        /// </summary>
        private static (int width, int height) ComputeThumbnailSize(int origW, int origH, int maxW, int maxH)
        {
            if (origW <= maxW && origH <= maxH) return (origW, origH);
            var ratio = Math.Min((double)maxW / origW, (double)maxH / origH);
            return ((int)Math.Round(origW * ratio), (int)Math.Round(origH * ratio));
        }

        /// <summary>
        /// Get MIME type based on image type
        /// </summary>
        private static string GetMimeTypeFromImageType(string imageType)
        {
            return imageType.ToLowerInvariant() switch
            {
                "main" => "image/jpeg",
                "icon" => "image/png",
                "ccr" => "image/bmp",
                "lpr" => "image/tiff",
                "manifest" => "application/pdf",
                _ => "application/octet-stream"
            };
        }

        // ────────────────────────────────────────────────────────────────────────
        //  FS6000 raw-channel backfill
        //  2026-04-19: replaces the broken inline .img loop that lived in
        //  Services.FS6000.IngestionService.cs. See FS6000RawChannelIngester
        //  for the full rationale. The backfill walks fs6000scans in a given
        //  date window and ingests HighEnergy/LowEnergy/Material rows from
        //  Archive/ folders. Idempotent; re-runnable.
        // ────────────────────────────────────────────────────────────────────────

        [HttpPost("backfill/fs6000-raw-channels")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public IActionResult StartFs6000RawChannelBackfill(
            [FromServices] NickScanCentralImagingPortal.Services.ImageProcessing.FS6000.FS6000BackfillJobTracker tracker,
            [FromServices] IServiceScopeFactory scopeFactory,
            [FromQuery] DateTime? startDate = null,
            [FromQuery] DateTime? endDate = null)
        {
            var from = DateTime.SpecifyKind(startDate ?? DateTime.UtcNow.Date.AddDays(-30), DateTimeKind.Utc);
            var to   = DateTime.SpecifyKind(endDate   ?? DateTime.UtcNow.Date.AddDays(1),   DateTimeKind.Utc);
            if (to <= from)
            {
                return BadRequest(new { error = "endDate must be greater than startDate" });
            }

            int total;
            using (var scope = scopeFactory.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                total = db.FS6000Scans.Count(s => s.ScanTime >= from && s.ScanTime < to);
            }

            if (!tracker.TryStart(from, to, total, out var jobId))
            {
                return Conflict(new
                {
                    error = "A backfill job is already in progress",
                    jobId = tracker.JobId,
                    startedAtUtc = tracker.StartedAtUtc,
                    processed = tracker.Processed,
                    totalScans = tracker.TotalScans,
                });
            }

            _ = Task.Run(async () => await RunFs6000BackfillAsync(scopeFactory, tracker, from, to));

            return Accepted(new
            {
                jobId,
                status = "started",
                totalScans = total,
                fromDate = from,
                toDate = to,
                statusEndpoint = "/api/imageprocessing/backfill/fs6000-raw-channels/status",
            });
        }

        [HttpGet("backfill/fs6000-raw-channels/status")]
        [Authorize(Roles = "Admin,SuperAdmin")]
        public IActionResult GetFs6000RawChannelBackfillStatus(
            [FromServices] NickScanCentralImagingPortal.Services.ImageProcessing.FS6000.FS6000BackfillJobTracker tracker)
        {
            return Ok(new
            {
                inProgress            = tracker.IsRunning,
                jobId                 = tracker.JobId,
                startedAtUtc          = tracker.StartedAtUtc,
                finishedAtUtc         = tracker.FinishedAtUtc,
                fromDate              = tracker.FromDate,
                toDate                = tracker.ToDate,
                totalScans            = tracker.TotalScans,
                processed             = tracker.Processed,
                scansWithNewChannels  = tracker.ScansWithNewChannels,
                scansAlreadyComplete  = tracker.ScansAlreadyComplete,
                scansFailed           = tracker.ScansFailed,
                channelsIngested      = tracker.ChannelsIngested,
                bytesIngested         = tracker.BytesIngested,
                lastError             = tracker.LastError,
            });
        }

        private async Task RunFs6000BackfillAsync(
            IServiceScopeFactory scopeFactory,
            NickScanCentralImagingPortal.Services.ImageProcessing.FS6000.FS6000BackfillJobTracker tracker,
            DateTime from,
            DateTime to)
        {
            try
            {
                using var scope = scopeFactory.CreateScope();
                var db  = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var svc = scope.ServiceProvider.GetRequiredService<IImageProcessingService>();

                var scans = await db.FS6000Scans
                    .AsNoTracking()
                    .Where(s => s.ScanTime >= from && s.ScanTime < to)
                    .OrderBy(s => s.ScanTime)
                    .Select(s => new { s.Id, s.FilePath, s.ContainerNumber })
                    .ToListAsync();

                _logger.LogInformation("[FS6000-BACKFILL] Starting for {Count} scans between {From:O} and {To:O}",
                    scans.Count, from, to);

                foreach (var scan in scans)
                {
                    try
                    {
                        var folder = ResolveStableFolderPath(scan.FilePath);
                        var report = await svc.IngestFS6000RawChannelsAsync(scan.Id, folder);
                        tracker.RecordScan(new NickScanCentralImagingPortal.Services.ImageProcessing.FS6000.RawChannelIngestionResult
                        {
                            ScanId           = report.ScanId,
                            FolderPath       = report.FolderPath,
                            IngestedChannels = report.IngestedChannels,
                            IngestedBytes    = report.IngestedBytes,
                            AlreadyPresent   = report.AlreadyPresent,
                            MissingFiles     = report.MissingFiles,
                            FailedChannels   = report.FailedChannels,
                            ErrorMessage     = report.ErrorMessage,
                            LastError        = report.LastError,
                        });

                        if (tracker.Processed % 50 == 0)
                        {
                            _logger.LogInformation(
                                "[FS6000-BACKFILL] Progress {Done}/{Total} | new={New} already={Already} failed={Failed} bytes={Bytes}",
                                tracker.Processed, tracker.TotalScans,
                                tracker.ScansWithNewChannels, tracker.ScansAlreadyComplete, tracker.ScansFailed, tracker.BytesIngested);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "[FS6000-BACKFILL] Scan {ScanId} ({Container}) failed", scan.Id, scan.ContainerNumber);
                        tracker.RecordScanException(ex.Message);
                    }
                }

                _logger.LogInformation(
                    "[FS6000-BACKFILL] Completed. Total={Total} New={New} Already={Already} Failed={Failed} Bytes={Bytes}",
                    tracker.TotalScans, tracker.ScansWithNewChannels,
                    tracker.ScansAlreadyComplete, tracker.ScansFailed, tracker.BytesIngested);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-BACKFILL] Fatal error in backfill job");
                tracker.RecordScanException("fatal: " + ex.Message);
            }
            finally
            {
                tracker.Finish();
            }
        }

        private static string ResolveStableFolderPath(string? recordedPath)
        {
            if (string.IsNullOrWhiteSpace(recordedPath)) return string.Empty;
            return recordedPath.Replace(@"\Staging\", @"\Archive\", StringComparison.OrdinalIgnoreCase);
        }
    }
}
