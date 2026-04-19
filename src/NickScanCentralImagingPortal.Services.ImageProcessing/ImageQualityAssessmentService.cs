using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.ImageProcessing;
using NickScanCentralImagingPortal.Core.Interfaces;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    /// <summary>
    /// Image quality assessment service
    /// Calculates quality metrics: sharpness, brightness, contrast, noise
    /// </summary>
    public class ImageQualityAssessmentService : IImageQualityAssessmentService
    {
        private readonly ILogger<ImageQualityAssessmentService> _logger;
        private readonly IImageProcessingService _imageProcessingService;

        public ImageQualityAssessmentService(
            ILogger<ImageQualityAssessmentService> logger,
            IImageProcessingService imageProcessingService)
        {
            _logger = logger;
            _imageProcessingService = imageProcessingService;
        }

        /// <summary>
        /// Assess image quality and provide recommendations
        /// </summary>
        public async Task<QualityAssessment> AssessQualityAsync(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Assessing image quality for container: {ContainerNumber}", containerNumber);

                // Get image as base64
                var base64Image = await _imageProcessingService.GetImageAsBase64Async(containerNumber);
                if (string.IsNullOrEmpty(base64Image))
                {
                    _logger.LogWarning("No image found for container: {ContainerNumber}", containerNumber);
                    return new QualityAssessment
                    {
                        Success = false,
                        ErrorMessage = "No image available for quality assessment"
                    };
                }

                // ✅ FIX: Strip data URI prefix if present (e.g., "data:image/jpeg;base64,")
                var base64Data = base64Image;
                if (base64Image.Contains(","))
                {
                    base64Data = base64Image.Substring(base64Image.IndexOf(",") + 1);
                }

                // Convert base64 to bytes
                var imageBytes = Convert.FromBase64String(base64Data);

                // Load image with ImageSharp
                using var image = await Image.LoadAsync<Rgb24>(new MemoryStream(imageBytes));

                // Calculate quality metrics
                var sharpness = CalculateSharpness(image);
                var brightness = CalculateBrightness(image);
                var contrast = CalculateContrast(image);
                var noise = EstimateNoise(image);

                // Overall score (weighted average)
                var overallScore = (sharpness * 0.3f) + (brightness * 0.25f) + (contrast * 0.25f) + ((1 - noise) * 0.2f);

                // Generate recommendations
                var recommendations = GenerateRecommendations(sharpness, brightness, contrast, noise);

                _logger.LogInformation("Quality assessment completed for {ContainerNumber}. Overall: {Score:F2}",
                    containerNumber, overallScore);

                return new QualityAssessment
                {
                    Success = true,
                    OverallScore = overallScore,
                    Sharpness = sharpness,
                    Brightness = brightness,
                    Contrast = contrast,
                    NoiseLevel = noise,
                    IsAcceptable = overallScore > 0.7f,
                    Recommendations = recommendations
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error assessing image quality for container: {ContainerNumber}", containerNumber);
                return new QualityAssessment
                {
                    Success = false,
                    ErrorMessage = $"Quality assessment failed: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// Calculate image sharpness using Laplacian variance
        /// Higher variance = sharper image
        /// </summary>
        private float CalculateSharpness(Image<Rgb24> image)
        {
            try
            {
                // Convert to grayscale for sharpness calculation
                var variance = 0.0;
                var pixelCount = 0;

                // Sample pixels for performance (every 10th pixel)
                for (int y = 1; y < image.Height - 1; y += 10)
                {
                    for (int x = 1; x < image.Width - 1; x += 10)
                    {
                        var center = image[x, y];
                        var centerGray = (center.R + center.G + center.B) / 3.0;

                        // Calculate Laplacian (second derivative approximation)
                        var top = image[x, y - 1];
                        var bottom = image[x, y + 1];
                        var left = image[x - 1, y];
                        var right = image[x + 1, y];

                        var topGray = (top.R + top.G + top.B) / 3.0;
                        var bottomGray = (bottom.R + bottom.G + bottom.B) / 3.0;
                        var leftGray = (left.R + left.G + left.B) / 3.0;
                        var rightGray = (right.R + right.G + right.B) / 3.0;

                        var laplacian = Math.Abs((4 * centerGray) - topGray - bottomGray - leftGray - rightGray);
                        variance += laplacian * laplacian;
                        pixelCount++;
                    }
                }

                var avgVariance = variance / pixelCount;

                // Normalize to 0-1 range (typical sharp images have variance > 100)
                var sharpness = Math.Min(1.0f, (float)(avgVariance / 500.0));

                return sharpness;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error calculating sharpness");
                return 0.5f; // Default moderate sharpness
            }
        }

        /// <summary>
        /// Calculate average brightness (0 = black, 1 = white)
        /// </summary>
        private float CalculateBrightness(Image<Rgb24> image)
        {
            try
            {
                long totalBrightness = 0;
                var pixelCount = 0;

                // Sample pixels for performance
                for (int y = 0; y < image.Height; y += 10)
                {
                    for (int x = 0; x < image.Width; x += 10)
                    {
                        var pixel = image[x, y];
                        // Calculate luminance (weighted average)
                        var brightness = (0.299 * pixel.R) + (0.587 * pixel.G) + (0.114 * pixel.B);
                        totalBrightness += (long)brightness;
                        pixelCount++;
                    }
                }

                var avgBrightness = totalBrightness / (double)pixelCount;

                // Normalize to 0-1 range (0-255 -> 0-1)
                return (float)(avgBrightness / 255.0);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error calculating brightness");
                return 0.5f; // Default moderate brightness
            }
        }

        /// <summary>
        /// Calculate contrast using standard deviation of pixel intensities
        /// Higher standard deviation = higher contrast
        /// </summary>
        private float CalculateContrast(Image<Rgb24> image)
        {
            try
            {
                var pixels = new List<double>();

                // Sample pixels for performance
                for (int y = 0; y < image.Height; y += 10)
                {
                    for (int x = 0; x < image.Width; x += 10)
                    {
                        var pixel = image[x, y];
                        var gray = (pixel.R + pixel.G + pixel.B) / 3.0;
                        pixels.Add(gray);
                    }
                }

                if (pixels.Count == 0)
                    return 0.5f;

                var mean = pixels.Average();
                var variance = pixels.Sum(p => Math.Pow(p - mean, 2)) / pixels.Count;
                var stdDev = Math.Sqrt(variance);

                // Normalize to 0-1 range (typical good contrast has stdDev > 30)
                var contrast = Math.Min(1.0f, (float)(stdDev / 100.0));

                return contrast;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error calculating contrast");
                return 0.5f; // Default moderate contrast
            }
        }

        /// <summary>
        /// Estimate noise level using local variance
        /// Higher variance in small regions = more noise
        /// </summary>
        private float EstimateNoise(Image<Rgb24> image)
        {
            try
            {
                var noiseLevels = new List<double>();

                // Sample 10x10 regions
                for (int y = 0; y < image.Height - 10; y += 50)
                {
                    for (int x = 0; x < image.Width - 10; x += 50)
                    {
                        var regionPixels = new List<double>();

                        // Get pixels in 10x10 region
                        for (int ry = 0; ry < 10 && (y + ry) < image.Height; ry++)
                        {
                            for (int rx = 0; rx < 10 && (x + rx) < image.Width; rx++)
                            {
                                var pixel = image[x + rx, y + ry];
                                var gray = (pixel.R + pixel.G + pixel.B) / 3.0;
                                regionPixels.Add(gray);
                            }
                        }

                        if (regionPixels.Count > 0)
                        {
                            var mean = regionPixels.Average();
                            var variance = regionPixels.Sum(p => Math.Pow(p - mean, 2)) / regionPixels.Count;
                            noiseLevels.Add(variance);
                        }
                    }
                }

                if (noiseLevels.Count == 0)
                    return 0.5f;

                var avgNoise = noiseLevels.Average();

                // Normalize to 0-1 range (typical noisy images have variance > 200)
                var noise = Math.Min(1.0f, (float)(avgNoise / 500.0));

                return noise;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error estimating noise");
                return 0.5f; // Default moderate noise
            }
        }

        /// <summary>
        /// Generate recommendations based on quality metrics
        /// </summary>
        private List<string> GenerateRecommendations(float sharpness, float brightness, float contrast, float noise)
        {
            var recommendations = new List<string>();

            if (sharpness < 0.5f)
                recommendations.Add("Image appears blurry - consider using enhanced view");

            if (brightness < 0.3f)
                recommendations.Add("Image is too dark - brightness enhancement recommended");

            if (brightness > 0.8f)
                recommendations.Add("Image is too bright - may need contrast adjustment");

            if (contrast < 0.4f)
                recommendations.Add("Low contrast detected - enhancement may improve visibility");

            if (noise > 0.6f)
                recommendations.Add("High noise level detected - noise reduction recommended");

            if (recommendations.Count == 0)
                recommendations.Add("Image quality is acceptable for analysis");

            return recommendations;
        }
    }

    /// <summary>
    /// Quality assessment result model
    /// </summary>
}


