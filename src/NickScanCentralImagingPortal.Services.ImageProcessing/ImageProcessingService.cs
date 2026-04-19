using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    public class ImageProcessingService : IImageProcessingService
    {
        private readonly ILogger<ImageProcessingService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ApplicationDbContext _dbContext;
        private readonly IImageCacheService _cacheService;

        public ImageProcessingService(
            ILogger<ImageProcessingService> logger,
            IServiceProvider serviceProvider,
            ApplicationDbContext dbContext,
            IImageCacheService cacheService)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _dbContext = dbContext;
            _cacheService = cacheService;
        }

        public async Task<Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber)
        {
            return await ProcessImageAsync(containerNumber, ScannerType.Unknown);
        }

        public async Task<Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber, ScannerType preferredScanner)
        {
            _logger.LogInformation("Processing image for container: {ContainerNumber} with preferred scanner: {PreferredScanner}", containerNumber, preferredScanner);

            ScannerType scannerType = preferredScanner == ScannerType.Unknown ? await DetectScannerTypeAsync(containerNumber) : preferredScanner;

            if (scannerType == ScannerType.Unknown)
            {
                return new Core.Models.ImageProcessingResult
                {
                    ImageId = 0,
                    Status = "Failed",
                    ProcessingType = "Unknown",
                    ProcessedAt = DateTime.UtcNow,
                    ErrorMessage = "Could not determine scanner type for container."
                };
            }

            var pipeline = GetImagePipeline(scannerType);
            if (pipeline == null)
            {
                return new Core.Models.ImageProcessingResult
                {
                    ImageId = 0,
                    Status = "Failed",
                    ProcessingType = "Unknown",
                    ProcessedAt = DateTime.UtcNow,
                    ErrorMessage = $"No image processing pipeline found for scanner type: {scannerType}"
                };
            }

            return await pipeline.ProcessImageAsync(containerNumber);
        }

        public async Task<Core.Interfaces.ImageMetadata> GetImageMetadataAsync(string containerNumber)
        {
            _logger.LogInformation("Getting image metadata for container: {ContainerNumber}", containerNumber);

            ScannerType scannerType = await DetectScannerTypeAsync(containerNumber);

            if (scannerType == ScannerType.Unknown)
            {
                return new Core.Interfaces.ImageMetadata { ImageFormat = "Unknown", AdditionalProperties = new Dictionary<string, object> { ["ErrorMessage"] = "Could not determine scanner type for container." } };
            }

            var pipeline = GetImagePipeline(scannerType);
            if (pipeline == null)
            {
                return new Core.Interfaces.ImageMetadata { ImageFormat = "Unknown", AdditionalProperties = new Dictionary<string, object> { ["ErrorMessage"] = $"No image processing pipeline found for scanner type: {scannerType}" } };
            }

            return await pipeline.GetImageMetadataAsync(containerNumber);
        }

        public async Task<ScannerType> DetectScannerTypeAsync(string containerNumber)
        {
            _logger.LogDebug("Detecting scanner type for container: {ContainerNumber}", containerNumber);

            // Check FS6000 scans
            var fs6000Scan = await _dbContext.FS6000Scans
                .AnyAsync(s => s.ContainerNumber == containerNumber);
            if (fs6000Scan)
            {
                _logger.LogDebug("Detected FS6000 scanner for container: {ContainerNumber}", containerNumber);
                return ScannerType.FS6000;
            }

            // Check ASE scans
            var aseScan = await _dbContext.AseScans
                .AnyAsync(s => s.ContainerNumber == containerNumber);
            if (aseScan)
            {
                _logger.LogDebug("Detected ASE scanner for container: {ContainerNumber}", containerNumber);
                return ScannerType.ASE;
            }

            // TODO: Add Heimann Smith detection when implemented
            // var heimannSmithScan = await _dbContext.HeimannSmithScans
            //     .AnyAsync(s => s.ContainerNumber == containerNumber);
            // if (heimannSmithScan)
            // {
            //     _logger.LogDebug("Detected Heimann Smith scanner for container: {ContainerNumber}", containerNumber);
            //     return ScannerType.HeimannSmith;
            // }

            _logger.LogWarning("Could not detect scanner type for container: {ContainerNumber}", containerNumber);
            return ScannerType.Unknown;
        }

        public async Task<string> GetImageAsBase64Async(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Getting image as Base64 for container: {ContainerNumber}", containerNumber);

                ScannerType scannerType = await DetectScannerTypeAsync(containerNumber);

                if (scannerType == ScannerType.Unknown)
                {
                    _logger.LogWarning("Could not determine scanner type for container: {ContainerNumber}", containerNumber);
                    return string.Empty;
                }

                var pipeline = GetImagePipeline(scannerType);
                if (pipeline == null)
                {
                    _logger.LogWarning("No image processing pipeline found for scanner type: {ScannerType}", scannerType);
                    return string.Empty;
                }

                return await pipeline.GetImageAsBase64Async(containerNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in GetImageAsBase64Async for container {ContainerNumber}", containerNumber);
                return string.Empty; // ✅ Return empty string instead of throwing - allows detection services to handle gracefully
            }
        }

        private IImagePipeline? GetImagePipeline(ScannerType scannerType)
        {
            return scannerType switch
            {
                ScannerType.FS6000 => _serviceProvider.GetService(typeof(FS6000ImagePipeline)) as FS6000ImagePipeline,
                ScannerType.ASE => _serviceProvider.GetService(typeof(ASEImagePipeline)) as ASEImagePipeline,
                // ScannerType.HeimannSmith => _serviceProvider.GetService(typeof(HeimannSmithImagePipeline)) as HeimannSmithImagePipeline,
                _ => null
            };
        }


        public Task<Core.Models.ImageProcessingResult> ProcessImageAsync(ImageDetails image, ImageProcessingRequest request)
        {
            _logger.LogInformation("Processing image {ImageId} with request type {ProcessingType}", image.Id, request.ProcessingType);

            try
            {
                // Create a processing result based on the image details
                var result = new Core.Models.ImageProcessingResult
                {
                    ImageId = image.Id,
                    Status = "Success",
                    ProcessingType = request.ProcessingType,
                    ProcessedAt = DateTime.UtcNow,
                    ProcessingTime = 1.0, // Placeholder
                    Result = $"Processed {image.FileName} using {request.ProcessingType}",
                    AnalysisResults = new Dictionary<string, object>(),
                    QualityScore = new ImageQualityScore
                    {
                        OverallScore = 85.0,
                        SharpnessScore = 80.0,
                        BrightnessScore = 90.0,
                        ContrastScore = 85.0,
                        ColorAccuracyScore = 88.0,
                        ResolutionScore = 82.0,
                        Issues = new List<string>(),
                        Recommendations = new List<string>()
                    }
                };

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing image {ImageId}", image.Id);
                return Task.FromResult(new Core.Models.ImageProcessingResult
                {
                    ImageId = image.Id,
                    Status = "Failed",
                    ProcessingType = request.ProcessingType,
                    ProcessedAt = DateTime.UtcNow,
                    ErrorMessage = ex.Message
                });
            }
        }

        public async Task<BatchProcessingResult> BatchProcessImagesAsync(BatchProcessingRequest request)
        {
            _logger.LogInformation("Starting batch processing for {Count} images", request.ImageIds.Count);

            var result = new BatchProcessingResult
            {
                TotalImages = request.ImageIds.Count,
                StartedAt = DateTime.UtcNow
            };

            foreach (var imageId in request.ImageIds)
            {
                try
                {
                    // Create a mock image details object
                    var imageDetails = new ImageDetails
                    {
                        Id = imageId,
                        FileName = $"image_{imageId}.jpg",
                        ContainerNumber = "BATCH_PROCESSING",
                        ProcessingStatus = "Processing"
                    };

                    var processingRequest = new ImageProcessingRequest
                    {
                        ProcessingType = request.ProcessingType,
                        Parameters = request.Parameters,
                        OperatorId = request.OperatorId,
                        Priority = request.Priority
                    };

                    var processingResult = await ProcessImageAsync(imageDetails, processingRequest);
                    result.Results.Add(processingResult);

                    if (processingResult.Status == "Success")
                        result.SuccessfulImages++;
                    else
                        result.FailedImages++;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing image {ImageId} in batch", imageId);
                    result.FailedImages++;
                    result.Errors.Add($"Image {imageId}: {ex.Message}");
                }
            }

            result.CompletedAt = DateTime.UtcNow;
            result.TotalProcessingTime = (result.CompletedAt.Value - result.StartedAt).TotalSeconds;
            result.QueuedImages = result.TotalImages - result.SuccessfulImages - result.FailedImages;

            _logger.LogInformation("Batch processing completed: {Successful} successful, {Failed} failed",
                result.SuccessfulImages, result.FailedImages);

            return result;
        }

        public async Task RetryImageProcessingAsync(int imageId)
        {
            _logger.LogInformation("Retrying image processing for image {ImageId}", imageId);

            try
            {
                // This would typically update the processing status and requeue the image
                // For now, just log the retry attempt
                await Task.Delay(100); // Simulate processing time
                _logger.LogInformation("Image processing retry initiated for image {ImageId}", imageId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying image processing for image {ImageId}", imageId);
                throw;
            }
        }

        public async Task<NickScanCentralImagingPortal.Core.Interfaces.ImageMetadata> GetImageMetadataAsync(string containerNumber, ScannerType? preferredScanner = null)
        {
            _logger.LogInformation("Getting image metadata for container: {ContainerNumber}", containerNumber);

            NickScanCentralImagingPortal.Core.Interfaces.ScannerType scannerType = preferredScanner ?? await DetectScannerTypeAsync(containerNumber);

            if (scannerType == NickScanCentralImagingPortal.Core.Interfaces.ScannerType.Unknown)
            {
                return new NickScanCentralImagingPortal.Core.Interfaces.ImageMetadata
                {
                    ImageFormat = "Unknown",
                    AdditionalProperties = new Dictionary<string, object> { { "ErrorMessage", "Could not determine scanner type for container." } }
                };
            }

            var pipeline = GetImagePipeline(scannerType);
            if (pipeline == null)
            {
                return new NickScanCentralImagingPortal.Core.Interfaces.ImageMetadata
                {
                    ImageFormat = "Unknown",
                    AdditionalProperties = new Dictionary<string, object> { { "ErrorMessage", $"No image processing pipeline found for scanner type: {scannerType}" } }
                };
            }

            // Assuming pipeline.GetImageMetadataAsync returns Services.ImageProcessing.ImageMetadata
            var servicesMetadata = await pipeline.GetImageMetadataAsync(containerNumber);

            // Convert Services.ImageProcessing.ImageMetadata to Core.Interfaces.ImageMetadata
            return new NickScanCentralImagingPortal.Core.Interfaces.ImageMetadata
            {
                Width = servicesMetadata.Width,
                Height = servicesMetadata.Height,
                FileSizeBytes = servicesMetadata.FileSizeBytes,
                ScanTime = servicesMetadata.ScanTime,
                ScannerId = servicesMetadata.ScannerId,
                ImageFormat = servicesMetadata.ImageFormat,
                ProcessingPipeline = servicesMetadata.ProcessingPipeline,
                AdditionalProperties = new Dictionary<string, object>
                {
                    { "Quality", servicesMetadata.AdditionalProperties.TryGetValue("Quality", out var quality) ? quality : "Unknown" },
                    { "ErrorMessage", servicesMetadata.AdditionalProperties.TryGetValue("ErrorMessage", out var errorMessage) ? errorMessage : null }
                }
            };
        }

        public async Task<string> GetImageAsBase64Async(string containerNumber, ScannerType? preferredScanner = null)
        {
            _logger.LogInformation("Getting image as base64 for container: {ContainerNumber}", containerNumber);

            NickScanCentralImagingPortal.Core.Interfaces.ScannerType scannerType = preferredScanner ?? await DetectScannerTypeAsync(containerNumber);

            if (scannerType == NickScanCentralImagingPortal.Core.Interfaces.ScannerType.Unknown)
            {
                return string.Empty;
            }

            var pipeline = GetImagePipeline(scannerType);
            if (pipeline == null)
            {
                return string.Empty;
            }

            return await pipeline.GetImageAsBase64Async(containerNumber);
        }

        /// <summary>
        /// Get complete container data including image and full scanner records
        /// </summary>
        public async Task<ContainerImageDataResponse?> GetCompleteContainerDataAsync(string containerNumber)
        {
            return await GetCompleteContainerDataAsync(containerNumber, (string?)null);
        }

        /// <summary>
        /// Get complete container data including image and full scanner records with image type filter
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <param name="imageType">Optional: Filter by image type (Main, Icon, CCR, LPR for FS6000). If not provided, returns first available image.</param>
        public async Task<ContainerImageDataResponse?> GetCompleteContainerDataAsync(string containerNumber, string? imageType)
        {
            _logger.LogInformation("Getting complete container data for: {ContainerNumber}, imageType: {ImageType}", containerNumber, imageType ?? "default");

            var scannerType = await DetectScannerTypeAsync(containerNumber);

            if (scannerType == ScannerType.Unknown)
            {
                _logger.LogWarning("Could not determine scanner type for container: {ContainerNumber}", containerNumber);
                return null;
            }

            var pipeline = GetImagePipeline(scannerType);
            if (pipeline == null)
            {
                _logger.LogWarning("No image processing pipeline found for scanner type: {ScannerType}", scannerType);
                return null;
            }

            // ✅ Call the pipeline's method with image type parameter
            if (pipeline is FS6000ImagePipeline fs6000Pipeline)
            {
                return await fs6000Pipeline.GetCompleteContainerDataAsync(containerNumber, imageType);
            }
            else if (pipeline is ASEImagePipeline asePipeline)
            {
                // ASE only has one image per container, so imageType parameter is ignored
                return await asePipeline.GetCompleteContainerDataAsync(containerNumber);
            }

            _logger.LogWarning("Pipeline does not support complete data retrieval for scanner type: {ScannerType}", scannerType);
            return null;
        }

        /// <summary>
        /// Ingest FS6000 raw .img channels from a stable folder into
        /// fs6000images. Delegates to <see cref="NickScanCentralImagingPortal.Services.ImageProcessing.FS6000.FS6000RawChannelIngester"/>.
        /// Used by the backfill endpoint and (eventually) by any live hook
        /// that fires after the scan folder has been archived.
        /// </summary>
        public async Task<Core.Interfaces.FS6000RawChannelIngestionReport> IngestFS6000RawChannelsAsync(
            Guid scanId,
            string folderPath,
            System.Threading.CancellationToken ct = default)
        {
            var ingester = _serviceProvider.GetService(typeof(FS6000.FS6000RawChannelIngester))
                           as FS6000.FS6000RawChannelIngester;
            if (ingester == null)
            {
                throw new InvalidOperationException(
                    "FS6000RawChannelIngester is not registered. Register it as Scoped in Program.cs.");
            }

            var r = await ingester.IngestAsync(scanId, folderPath, ct);
            return new Core.Interfaces.FS6000RawChannelIngestionReport
            {
                ScanId = r.ScanId,
                FolderPath = r.FolderPath,
                IngestedChannels = r.IngestedChannels,
                IngestedBytes = r.IngestedBytes,
                AlreadyPresent = r.AlreadyPresent,
                MissingFiles = r.MissingFiles,
                FailedChannels = r.FailedChannels,
                ErrorMessage = r.ErrorMessage,
                LastError = r.LastError,
            };
        }
    }
}
