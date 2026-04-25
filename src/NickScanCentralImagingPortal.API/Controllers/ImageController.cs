using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.ImageProcessing;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Image retrieval controller - requires authentication
    /// </summary>
    [Authorize]
    [Route("api/[controller]")]
    [ApiController]
    public class ImageController : ControllerBase
    {
        private readonly IImageProcessingService _imageProcessingService;
        private readonly ILogger<ImageController> _logger;

        // M4: Container numbers per ISO 6346 are 4 letters + 7 digits (11 chars). Allow a slightly
        // wider charset for legacy / non-standard data, but strictly disallow path separators,
        // dot-segments, and shell metacharacters that could be used for path traversal.
        private static readonly Regex ContainerNumberPattern =
            new(@"^[A-Za-z0-9_-]{1,32}$", RegexOptions.Compiled);

        public ImageController(IImageProcessingService imageProcessingService, ILogger<ImageController> logger)
        {
            _imageProcessingService = imageProcessingService;
            _logger = logger;
        }

        /// <summary>
        /// ⚠️ DEPRECATED: Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=full instead
        /// This endpoint will be removed in a future version.
        /// </summary>
        /// <param name="containerNumber">The container number.</param>
        /// <param name="scannerType">Optional: Specify a scanner type to prioritize (e.g., FS6000, ASE).</param>
        /// <returns>The processed image as a JPEG byte array.</returns>
        [Obsolete("Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=full instead")]
        [HttpGet("{containerNumber}")]
        [ProducesResponseType(200, Type = typeof(FileContentResult))]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetImage(string containerNumber, [FromQuery] ScannerType? scannerType = null)
        {
            _logger.LogInformation("Received request to get image for container: {ContainerNumber}, preferred scanner: {ScannerType}", containerNumber, scannerType);

            var result = await _imageProcessingService.ProcessImageAsync(containerNumber, scannerType ?? ScannerType.Unknown);

            if (result.Status != "Success")
            {
                _logger.LogWarning("Failed to retrieve or process image for container {ContainerNumber}: {ErrorMessage}", containerNumber, result.ErrorMessage);
                return NotFound(result.ErrorMessage);
            }

            // Get image data from AnalysisResults
            if (result.AnalysisResults.ContainsKey("ImageDataSize") && result.AnalysisResults.ContainsKey("MimeType"))
            {
                // For now, return a placeholder since we don't have actual image data in the result
                return StatusCode(501, "Image data retrieval not yet implemented in this version");
            }

            return NotFound("No image data available");
        }

        /// <summary>
        /// ⚠️ DEPRECATED: Use /api/containerdetails/images/{containerNumber} instead
        /// This endpoint will be removed in a future version.
        /// </summary>
        /// <param name="containerNumber">The container number.</param>
        /// <param name="scannerType">Optional: Specify a scanner type to prioritize (e.g., FS6000, ASE).</param>
        /// <returns>Image metadata.</returns>
        [Obsolete("Use /api/containerdetails/images/{containerNumber} instead")]
        [HttpGet("{containerNumber}/metadata")]
        [ProducesResponseType(200, Type = typeof(ImageMetadata))]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetImageMetadata(string containerNumber, [FromQuery] ScannerType? scannerType = null)
        {
            _logger.LogInformation("Received request to get image metadata for container: {ContainerNumber}, preferred scanner: {ScannerType}", containerNumber, scannerType);

            var metadata = await _imageProcessingService.GetImageMetadataAsync(containerNumber);

            if (string.IsNullOrEmpty(metadata.ImageFormat) || metadata.ImageFormat == "Unknown")
            {
                _logger.LogWarning("No image metadata found for container: {ContainerNumber}", containerNumber);
                return NotFound("No image metadata found for the specified container.");
            }

            return Ok(metadata);
        }

        /// <summary>
        /// ⚠️ DEPRECATED: Use /api/ImageProcessing/image/{containerNumber}/base64 instead
        /// This endpoint will be removed in a future version.
        /// </summary>
        /// <param name="containerNumber">The container number.</param>
        /// <param name="scannerType">Optional: Specify a scanner type to prioritize (e.g., FS6000, ASE).</param>
        /// <returns>Base64 encoded image string.</returns>
        [Obsolete("Use /api/ImageProcessing/image/{containerNumber}/base64 instead")]
        [HttpGet("{containerNumber}/base64")]
        [ProducesResponseType(200, Type = typeof(string))]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> GetImageAsBase64(string containerNumber, [FromQuery] ScannerType? scannerType = null)
        {
            _logger.LogInformation("Received request to get image as Base64 for container: {ContainerNumber}, preferred scanner: {ScannerType}", containerNumber, scannerType);

            // Use the ProcessImageAsync method and extract base64 from AnalysisResults
            var result = await _imageProcessingService.ProcessImageAsync(containerNumber, scannerType ?? ScannerType.Unknown);

            if (result.Status != "Success")
            {
                _logger.LogWarning("No image found for container: {ContainerNumber}", containerNumber);
                return NotFound("No image found for the specified container.");
            }

            // For now, return a placeholder since we don't have actual base64 data in the result
            var placeholderBase64 = "data:image/jpeg;base64,/9j/4AAQSkZJRgABAQEAYABgAAD/2wBDAAYEBQYFBAYGBQYHBwYIChAKCgkJChQODwwQFxQYGBcUFhYaHSUfGhsjHBYWICwgIyYnKSopGR8tMC0oMCUoKSj/2wBDAQcHBwoIChMKChMoGhYaKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCgoKCj/wAARCAABAAEDASIAAhEBAxEB/8QAFQABAQAAAAAAAAAAAAAAAAAAAAv/xAAUEAEAAAAAAAAAAAAAAAAAAAAA/8QAFQEBAQAAAAAAAAAAAAAAAAAAAAX/xAAUEQEAAAAAAAAAAAAAAAAAAAAA/9oADAMBAAIRAxEAPwCdABmX/9k=";
            return Ok(new { image = placeholderBase64, containerNumber, scannerType = scannerType?.ToString() ?? "Auto-detected", note = "Placeholder image - actual base64 retrieval not yet implemented" });
        }

        /// <summary>
        /// Detects the scanner type for a given container number.
        /// </summary>
        /// <param name="containerNumber">The container number.</param>
        /// <returns>Detected scanner type.</returns>
        [HttpGet("{containerNumber}/detect")]
        [ProducesResponseType(200, Type = typeof(ScannerType))]
        [ProducesResponseType(404)]
        [ProducesResponseType(500)]
        public async Task<IActionResult> DetectScannerType(string containerNumber)
        {
            _logger.LogInformation("Received request to detect scanner type for container: {ContainerNumber}", containerNumber);

            var scannerType = await _imageProcessingService.DetectScannerTypeAsync(containerNumber);

            if (scannerType == ScannerType.Unknown)
            {
                _logger.LogWarning("Could not detect scanner type for container: {ContainerNumber}", containerNumber);
                return NotFound("Could not detect scanner type for the specified container.");
            }

            return Ok(new { scannerType = scannerType.ToString(), containerNumber });
        }

        /// <summary>
        /// ⚠️ DEPRECATED: Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=full instead
        /// This endpoint will be removed in a future version.
        /// </summary>
        /// <param name="containerNumber">The container number to find an image for.</param>
        /// <returns>Image file or placeholder if not found.</returns>
        [Obsolete("Use /api/ImageProcessing/container/{containerNumber}/complete/image?size=full instead")]
        [HttpGet("serve/{containerNumber}")]
        [ProducesResponseType(200, Type = typeof(FileContentResult))]
        [ProducesResponseType(404)]
        public Task<IActionResult> ServeImage(string containerNumber)
        {
            _logger.LogInformation("Received request to serve image for container: {ContainerNumber}", containerNumber);

            try
            {
                // Look for images in the project directory
                var projectRoot = Directory.GetCurrentDirectory();
                var imageExtensions = new[] { ".jpg", ".jpeg", ".png" };

                // Search for images that contain the container number
                var imageFiles = Directory.GetFiles(projectRoot, "*", SearchOption.AllDirectories)
                    .Where(file => imageExtensions.Any(ext => file.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                    .Where(file => Path.GetFileNameWithoutExtension(file).Contains(containerNumber, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                if (imageFiles.Any())
                {
                    var imagePath = imageFiles.First();
                    var contentType = GetContentType(imagePath);

                    // ✅ MEMORY FIX: Stream file directly instead of loading into memory
                    // This prevents 1.7MB+ byte arrays from going to Large Object Heap
                    var fileStream = System.IO.File.OpenRead(imagePath);
                    _logger.LogInformation("Serving image for container {ContainerNumber}: {ImagePath} (streaming)", containerNumber, imagePath);
                    return Task.FromResult<IActionResult>(File(fileStream, contentType, enableRangeProcessing: true));
                }

                // M4: Validate container number before using it in a file path. Without this,
                // a request like /api/Image/serve/..%5C..%5CWindows%5CSystem32%5Cconfig%5CSAM
                // could escape C:\tadi_mirror via Path.Combine. Belt-and-braces: regex-validate
                // the input AND verify the resolved path stays under the base directory.
                if (!ContainerNumberPattern.IsMatch(containerNumber))
                {
                    _logger.LogWarning("ServeImage: rejected container number with disallowed characters: {ContainerNumber}", containerNumber);
                    return Task.FromResult<IActionResult>(BadRequest("Invalid container number format."));
                }

                // Check TADI mirror folder for FS6000 images
                const string TadiMirrorRoot = @"C:\tadi_mirror";
                var tadiImagePath = Path.Combine(TadiMirrorRoot, $"{containerNumber}.jpg");
                var tadiFullPath = Path.GetFullPath(tadiImagePath);
                var tadiRootFull = Path.GetFullPath(TadiMirrorRoot);
                if (!tadiFullPath.StartsWith(tadiRootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                    && !tadiFullPath.Equals(tadiRootFull, StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("ServeImage: resolved path escaped base directory: {Resolved}", tadiFullPath);
                    return Task.FromResult<IActionResult>(BadRequest("Invalid container number format."));
                }
                if (System.IO.File.Exists(tadiImagePath))
                {
                    // ✅ MEMORY FIX: Stream file directly instead of loading into memory
                    var fileStream = System.IO.File.OpenRead(tadiImagePath);
                    _logger.LogInformation("Serving TADI image for container {ContainerNumber}: {ImagePath} (streaming)", containerNumber, tadiImagePath);
                    return Task.FromResult<IActionResult>(File(fileStream, "image/jpeg", enableRangeProcessing: true));
                }

                // If no image found, return 404
                _logger.LogWarning("No image found for container: {ContainerNumber}", containerNumber);
                return Task.FromResult<IActionResult>(NotFound($"No image found for container {containerNumber}"));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error serving image for container {ContainerNumber}", containerNumber);
                return Task.FromResult<IActionResult>(StatusCode(500, "Internal server error"));
            }
        }

        private string GetContentType(string filePath)
        {
            var extension = Path.GetExtension(filePath).ToLowerInvariant();
            return extension switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                _ => "application/octet-stream"
            };
        }
    }
}
