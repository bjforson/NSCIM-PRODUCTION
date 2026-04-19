using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Processing.Processors.Convolution;
using SixLabors.ImageSharp.Processing.Processors.Filters;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    /// <summary>
    /// Advanced image processing service using ImageSharp for enhancement operations
    /// </summary>
    public class AdvancedImageProcessingService : IAdvancedImageProcessingService
    {
        private readonly ILogger<AdvancedImageProcessingService> _logger;
        private readonly IImageProcessingService _imageProcessingService;

        public AdvancedImageProcessingService(
            ILogger<AdvancedImageProcessingService> logger,
            IImageProcessingService imageProcessingService)
        {
            _logger = logger;
            _imageProcessingService = imageProcessingService;
        }

        /// <summary>
        /// Get enhanced image bytes for a container
        /// Applies automatic enhancement: brightness, contrast, noise reduction, sharpening, histogram equalization
        /// </summary>
        public async Task<byte[]?> GetEnhancedImageAsync(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Enhancing image for container: {ContainerNumber}", containerNumber);

                // Get original image as base64
                var base64Image = await _imageProcessingService.GetImageAsBase64Async(containerNumber);
                if (string.IsNullOrEmpty(base64Image))
                {
                    _logger.LogWarning("No image found for container: {ContainerNumber}", containerNumber);
                    return null;
                }

                // ✅ FIX: Strip data URI prefix if present (e.g., "data:image/jpeg;base64,")
                var base64Data = base64Image;
                if (base64Image.Contains(","))
                {
                    base64Data = base64Image.Substring(base64Image.IndexOf(",") + 1);
                }

                // Convert base64 to bytes
                var imageBytes = Convert.FromBase64String(base64Data);

                // Load image using ImageSharp
                using var image = await Image.LoadAsync(new MemoryStream(imageBytes));

                // Apply enhancement operations
                image.Mutate(x => x
                    // Resize to max 1920x1080 while maintaining aspect ratio
                    .Resize(new ResizeOptions
                    {
                        Size = new Size(1920, 1080),
                        Mode = ResizeMode.Max
                    })
                    // Slight brightness boost (15%)
                    .Brightness(1.15f)
                    // Improve contrast (10%)
                    .Contrast(1.1f)
                    // Reduce noise with slight blur
                    .GaussianBlur(0.3f)
                    // Apply unsharp mask for sharpening (alternative to Sharpen())
                    .GaussianSharpen(0.5f)
                    // Histogram equalization for better overall visibility
                    .HistogramEqualization()
                );

                // Save enhanced image to memory stream
                using var outputStream = new MemoryStream();
                await image.SaveAsJpegAsync(outputStream, new JpegEncoder
                {
                    Quality = 95
                });

                _logger.LogInformation("Successfully enhanced image for container: {ContainerNumber}, Size: {Size} bytes",
                    containerNumber, outputStream.Length);

                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enhancing image for container: {ContainerNumber}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Get enhanced image with custom parameters
        /// </summary>
        public async Task<byte[]?> GetEnhancedImageAsync(
            string containerNumber,
            float brightness = 1.15f,
            float contrast = 1.1f,
            float blurAmount = 0.3f,
            bool applyHistogramEqualization = true)
        {
            try
            {
                _logger.LogInformation("Enhancing image for container: {ContainerNumber} with custom parameters", containerNumber);

                var base64Image = await _imageProcessingService.GetImageAsBase64Async(containerNumber);
                if (string.IsNullOrEmpty(base64Image))
                {
                    return null;
                }

                // ✅ FIX: Strip data URI prefix if present (e.g., "data:image/jpeg;base64,")
                var base64Data = base64Image;
                if (base64Image.Contains(","))
                {
                    base64Data = base64Image.Substring(base64Image.IndexOf(",") + 1);
                }

                var imageBytes = Convert.FromBase64String(base64Data);
                using var image = await Image.LoadAsync(new MemoryStream(imageBytes));

                image.Mutate(x =>
                {
                    x.Resize(new ResizeOptions
                    {
                        Size = new Size(1920, 1080),
                        Mode = ResizeMode.Max
                    });

                    if (brightness != 1.0f)
                        x.Brightness(brightness);

                    if (contrast != 1.0f)
                        x.Contrast(contrast);

                    if (blurAmount > 0)
                    {
                        x.GaussianBlur(blurAmount);
                        x.GaussianSharpen(0.5f);
                    }

                    if (applyHistogramEqualization)
                        x.HistogramEqualization();
                });

                using var outputStream = new MemoryStream();
                await image.SaveAsJpegAsync(outputStream, new JpegEncoder { Quality = 95 });

                return outputStream.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enhancing image with custom parameters for container: {ContainerNumber}", containerNumber);
                return null;
            }
        }
    }
}

