using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    public interface IICUMSIntegrationService
    {
        Task<ICUMSImageData> PrepareImageForICUMSAsync(string containerNumber, byte[] imageData, ImageMetadata metadata);
        Task<ICUMSBatchData> PrepareBatchForICUMSAsync(List<ICUMSImageData> images);
        Task<bool> ValidateICUMSDataAsync(ICUMSImageData imageData);
    }

    public class ICUMSIntegrationService : IICUMSIntegrationService
    {
        private readonly ILogger<ICUMSIntegrationService> _logger;

        public ICUMSIntegrationService(ILogger<ICUMSIntegrationService> logger)
        {
            _logger = logger;
        }

        public async Task<ICUMSImageData> PrepareImageForICUMSAsync(string containerNumber, byte[] imageData, ImageMetadata metadata)
        {
            _logger.LogInformation("Preparing image for ICUMS integration: {ContainerNumber}", containerNumber);

            try
            {
                var icumsData = new ICUMSImageData
                {
                    ContainerNumber = containerNumber,
                    ImageData = ConvertToBase64(imageData),
                    ImageFormat = metadata?.ImageFormat ?? "JPEG",
                    Width = metadata?.Width ?? 0,
                    Height = metadata?.Height ?? 0,
                    FileSizeBytes = imageData.Length,
                    ProcessingPipeline = metadata?.ProcessingPipeline ?? "Unknown",
                    Quality = "High", // Default to high quality
                    Timestamp = DateTime.UtcNow,
                    ScannerType = DetermineScannerType(metadata?.ProcessingPipeline),
                    EnhancementApplied = metadata?.ProcessingPipeline?.Contains("Enhanced") ?? false,
                    OriginalFileSizeBytes = imageData.Length, // Use current size as original
                    CompressionRatio = 1.0 // No compression ratio available
                };

                // Validate the data
                var isValid = await ValidateICUMSDataAsync(icumsData);
                if (!isValid)
                {
                    _logger.LogWarning("ICUMS data validation failed for container: {ContainerNumber}", containerNumber);
                }

                return icumsData;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preparing image for ICUMS: {ContainerNumber}", containerNumber);
                throw;
            }
        }

        public Task<ICUMSBatchData> PrepareBatchForICUMSAsync(List<ICUMSImageData> images)
        {
            _logger.LogInformation("Preparing batch of {Count} images for ICUMS integration", images.Count);

            try
            {
                var batchData = new ICUMSBatchData
                {
                    BatchId = Guid.NewGuid().ToString(),
                    Timestamp = DateTime.UtcNow,
                    TotalImages = images.Count,
                    Images = images,
                    TotalSizeBytes = images.Sum(img => img.FileSizeBytes),
                    AverageCompressionRatio = images.Average(img => img.CompressionRatio),
                    ScannerTypes = images.Select(img => img.ScannerType).Distinct().ToList(),
                    ProcessingPipelines = images.Select(img => img.ProcessingPipeline).Distinct().ToList()
                };

                _logger.LogInformation("Prepared ICUMS batch: {BatchId} with {Count} images", batchData.BatchId, batchData.TotalImages);
                return Task.FromResult(batchData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error preparing batch for ICUMS");
                throw;
            }
        }

        public Task<bool> ValidateICUMSDataAsync(ICUMSImageData imageData)
        {
            try
            {
                // Validate required fields
                if (string.IsNullOrEmpty(imageData.ContainerNumber))
                {
                    _logger.LogWarning("ICUMS validation failed: ContainerNumber is required");
                    return Task.FromResult(false);
                }

                if (string.IsNullOrEmpty(imageData.ImageData))
                {
                    _logger.LogWarning("ICUMS validation failed: ImageData is required");
                    return Task.FromResult(false);
                }

                if (imageData.Width <= 0 || imageData.Height <= 0)
                {
                    _logger.LogWarning("ICUMS validation failed: Invalid dimensions {Width}x{Height}", imageData.Width, imageData.Height);
                    return Task.FromResult(false);
                }

                if (imageData.FileSizeBytes <= 0)
                {
                    _logger.LogWarning("ICUMS validation failed: Invalid file size {FileSizeBytes}", imageData.FileSizeBytes);
                    return Task.FromResult(false);
                }

                // Validate Base64 data
                try
                {
                    Convert.FromBase64String(imageData.ImageData);
                }
                catch (FormatException)
                {
                    _logger.LogWarning("ICUMS validation failed: Invalid Base64 image data");
                    return Task.FromResult(false);
                }

                _logger.LogDebug("ICUMS validation passed for container: {ContainerNumber}", imageData.ContainerNumber);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating ICUMS data for container: {ContainerNumber}", imageData.ContainerNumber);
                return Task.FromResult(false);
            }
        }

        private string ConvertToBase64(byte[] imageData)
        {
            return Convert.ToBase64String(imageData);
        }

        private string DetermineScannerType(string processingPipeline)
        {
            if (string.IsNullOrEmpty(processingPipeline))
                return "Unknown";

            if (processingPipeline.Contains("ASE"))
                return "ASE";
            if (processingPipeline.Contains("FS6000"))
                return "FS6000";
            if (processingPipeline.Contains("Nuctech"))
                return "Nuctech";
            if (processingPipeline.Contains("HeimannSmith"))
                return "HeimannSmith";

            return "Unknown";
        }

        private double CalculateCompressionRatio(long compressedSize, long originalSize)
        {
            if (originalSize == 0) return 0;
            return (double)compressedSize / originalSize;
        }
    }

    public class ICUMSImageData
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ImageData { get; set; } = string.Empty; // Base64 encoded
        public string ImageFormat { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public long FileSizeBytes { get; set; }
        public string ProcessingPipeline { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public bool EnhancementApplied { get; set; }
        public long OriginalFileSizeBytes { get; set; }
        public double CompressionRatio { get; set; }
    }

    public class ICUMSBatchData
    {
        public string BatchId { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public int TotalImages { get; set; }
        public List<ICUMSImageData> Images { get; set; } = new();
        public long TotalSizeBytes { get; set; }
        public double AverageCompressionRatio { get; set; }
        public List<string> ScannerTypes { get; set; } = new();
        public List<string> ProcessingPipelines { get; set; } = new();
    }
}
