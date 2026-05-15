using System;
using System.Data;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageProcessing.Kernel;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    public class ImageProcessingService : IImageProcessingService
    {
        private readonly ILogger<ImageProcessingService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly ApplicationDbContext _dbContext;
        private readonly IImageCacheService _cacheService;
        private readonly ScanProcessingPipeline _pipeline;
        private readonly IScanAssetResolver _scanAssetResolver;

        public ImageProcessingService(
            ILogger<ImageProcessingService> logger,
            IServiceProvider serviceProvider,
            ApplicationDbContext dbContext,
            IImageCacheService cacheService,
            ScanProcessingPipeline pipeline,
            IScanAssetResolver scanAssetResolver)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _dbContext = dbContext;
            _cacheService = cacheService;
            _pipeline = pipeline;
            _scanAssetResolver = scanAssetResolver;
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

            var sourceContainerNumber = await ResolveSourceContainerForScannerAsync(containerNumber, scannerType);
            return await pipeline.ProcessImageAsync(sourceContainerNumber);
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

            var sourceContainerNumber = await ResolveSourceContainerForScannerAsync(containerNumber, scannerType);
            return await pipeline.GetImageMetadataAsync(sourceContainerNumber);
        }

        public async Task<ScannerType> DetectScannerTypeAsync(string containerNumber)
        {
            _logger.LogDebug("Detecting scanner type for container: {ContainerNumber}", containerNumber);

            var resolution = await _scanAssetResolver.ResolveAsync(containerNumber);
            if (resolution.Found && !resolution.IsAmbiguous)
            {
                if (string.Equals(resolution.SourceScannerType, "FS6000", StringComparison.OrdinalIgnoreCase))
                    return ScannerType.FS6000;

                if (string.Equals(resolution.SourceScannerType, "ASE", StringComparison.OrdinalIgnoreCase))
                    return ScannerType.ASE;
            }

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

                var sourceContainerNumber = await ResolveSourceContainerForScannerAsync(containerNumber, scannerType);
                return await pipeline.GetImageAsBase64Async(sourceContainerNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception in GetImageAsBase64Async for container {ContainerNumber}", containerNumber);
                return string.Empty; // ✅ Return empty string instead of throwing - allows detection services to handle gracefully
            }
        }

        private async Task<string> ResolveSourceContainerForScannerAsync(string containerNumber, ScannerType scannerType)
        {
            if (scannerType == ScannerType.Unknown)
                return containerNumber;

            var resolution = await _scanAssetResolver.ResolveAsync(containerNumber);
            if (!resolution.Found || resolution.IsAmbiguous || string.IsNullOrWhiteSpace(resolution.SourceContainerNumbers))
                return containerNumber;

            if (scannerType == ScannerType.ASE
                && string.Equals(resolution.SourceScannerType, "ASE", StringComparison.OrdinalIgnoreCase))
                return resolution.SourceContainerNumbers;

            if (scannerType == ScannerType.FS6000
                && string.Equals(resolution.SourceScannerType, "FS6000", StringComparison.OrdinalIgnoreCase))
                return resolution.SourceContainerNumbers;

            return containerNumber;
        }

        private async Task<string> ResolveSourceContainerForPipelineAsync(string containerNumber, CancellationToken ct)
        {
            var resolution = await _scanAssetResolver.ResolveAsync(containerNumber, cancellationToken: ct);
            return resolution.Found && !resolution.IsAmbiguous && !string.IsNullOrWhiteSpace(resolution.SourceContainerNumbers)
                ? resolution.SourceContainerNumbers
                : containerNumber;
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
            var sourceContainerNumber = await ResolveSourceContainerForScannerAsync(containerNumber, scannerType);
            var servicesMetadata = await pipeline.GetImageMetadataAsync(sourceContainerNumber);

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

            var sourceContainerNumber = await ResolveSourceContainerForScannerAsync(containerNumber, scannerType);
            return await pipeline.GetImageAsBase64Async(sourceContainerNumber);
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
                var sourceContainerNumber = await ResolveSourceContainerForScannerAsync(containerNumber, scannerType);
                return await fs6000Pipeline.GetCompleteContainerDataAsync(sourceContainerNumber, imageType);
            }
            else if (pipeline is ASEImagePipeline asePipeline)
            {
                // ASE only has one image per container, so imageType parameter is ignored
                var sourceContainerNumber = await ResolveSourceContainerForScannerAsync(containerNumber, scannerType);
                return await asePipeline.GetCompleteContainerDataAsync(sourceContainerNumber);
            }

            _logger.LogWarning("Pipeline does not support complete data retrieval for scanner type: {ScannerType}", scannerType);
            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        //  v2.11.0 — the four methods below were per-pipeline with cut-and-
        //  paste dispatcher blocks spread across FS6000ImagePipeline +
        //  ASEImagePipeline. They're now thin delegations to the scanner-
        //  agnostic ScanProcessingPipeline, which routes container → IR via
        //  IScanFormatAdapter + IScanSourceRetriever and runs shared kernel
        //  operations. Adding a new scanner now requires only new adapter +
        //  retriever implementations — no changes to this class, no changes
        //  to the kernel, no changes to the controllers.
        //
        //  Partial-channel FS6000 handling (v2.10.5 hotfix) is preserved:
        //  the pipeline's GetCapabilitiesAsync returns the inventory hint
        //  when decode fails so the UI still gets the "missing: Material"
        //  tooltip instead of an empty response.
        // ══════════════════════════════════════════════════════════════════

        public async Task<byte[]?> GetRenderedImageBytesAsync(
            string containerNumber, string mode,
            float loPct = 1.0f, float hiPct = 99.5f, float gamma = 1.0f,
            CancellationToken ct = default)
            => await _pipeline.RenderAsync(await ResolveSourceContainerForPipelineAsync(containerNumber, ct), mode, loPct, hiPct, gamma, ct);

        public async Task<RoiInspectorResult?> GetRoiInspectorAsync(
            string containerNumber, int x, int y, int width, int height,
            CancellationToken ct = default)
            => await _pipeline.BuildRoiAsync(await ResolveSourceContainerForPipelineAsync(containerNumber, ct), x, y, width, height, ct);

        public async Task<PixelValueResult?> GetPixelValueAsync(
            string containerNumber, int x, int y,
            CancellationToken ct = default)
            => await _pipeline.ProbePixelAsync(await ResolveSourceContainerForPipelineAsync(containerNumber, ct), x, y, ct);

        public async Task<ScanModeCapabilities?> GetScanModeCapabilitiesAsync(
            string containerNumber,
            CancellationToken ct = default)
            => await _pipeline.GetCapabilitiesAsync(await ResolveSourceContainerForPipelineAsync(containerNumber, ct), ct);

        public async Task<RawPlaneResult?> GetRawPlaneAsync(
            string containerNumber, string plane,
            CancellationToken ct = default)
            => await _pipeline.GetRawPlaneAsync(await ResolveSourceContainerForPipelineAsync(containerNumber, ct), plane, ct);

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

        /// <summary>
        /// Report the pixel dimensions of the image currently served by
        /// <see cref="GetCompleteContainerDataAsync"/> for a container.
        ///
        /// Resolution logic:
        ///   1. Detect scanner type.
        ///   2. FS6000 with all three raw channels in fs6000images →
        ///      parse width/height from the HighEnergy .img header (6 bytes).
        ///      Mode = "fs6000-composite16bit".
        ///   3. FS6000 without full raw channels → parse width/height from
        ///      the vendor JPEG (imagetype='Main'). Mode = "fs6000-vendorjpeg".
        ///   4. ASE → currently not dimension-introspected (ASE pipeline already
        ///      percentile-renders at native ASE dims; the annotation scaling
        ///      story there is orthogonal to this v2.9.6 change).
        ///      Mode = "ase".
        ///   5. Anything unresolvable → <c>(0, 0, "unknown")</c>.
        ///
        /// Efficient on the hot path — a single <c>substring()</c> query pulls
        /// only the first 6 bytes (img) or 4 KB (JPEG), not the full blob.
        /// </summary>
        public async Task<ServedImageDimensions> GetServedImageDimensionsAsync(string containerNumber, CancellationToken ct = default)
        {
            try
            {
                var scannerType = await DetectScannerTypeAsync(containerNumber);

                if (scannerType == ScannerType.FS6000)
                {
                    var scan = await _dbContext.FS6000Scans
                        .AsNoTracking()
                        .Where(s => s.ContainerNumber == containerNumber)
                        .OrderByDescending(s => s.ScanTime)
                        .Select(s => new { s.Id })
                        .FirstOrDefaultAsync(ct);

                    if (scan == null) return new ServedImageDimensions { Mode = "unknown" };

                    var channels = await _dbContext.FS6000Images
                        .AsNoTracking()
                        .Where(i => i.ScanId == scan.Id
                                 && (i.ImageType == "HighEnergy" || i.ImageType == "LowEnergy" || i.ImageType == "Material"))
                        .Select(i => i.ImageType)
                        .ToListAsync(ct);

                    bool hasAllRaw = channels.Contains("HighEnergy")
                                     && channels.Contains("LowEnergy")
                                     && channels.Contains("Material");

                    if (hasAllRaw)
                    {
                        var imgHeader = await ReadBytesFromImageAsync(scan.Id, "HighEnergy", 6, ct);
                        var rawDims = ParseImgHeaderDimensions(imgHeader);
                        if (rawDims.HasValue)
                        {
                            return new ServedImageDimensions
                            {
                                Width = rawDims.Value.w,
                                Height = rawDims.Value.h,
                                Mode = "fs6000-composite16bit"
                            };
                        }
                        _logger.LogWarning("[SERVED-DIMS] FS6000 scan {ScanId} has all 3 raw channels but HighEnergy header parse failed — falling back to vendor JPEG", scan.Id);
                    }

                    var jpegHead = await ReadBytesFromImageAsync(scan.Id, "Main", 4096, ct);
                    var jpegDims = ParseJpegDimensions(jpegHead);
                    if (jpegDims.HasValue)
                    {
                        return new ServedImageDimensions
                        {
                            Width = jpegDims.Value.w,
                            Height = jpegDims.Value.h,
                            Mode = "fs6000-vendorjpeg"
                        };
                    }

                    return new ServedImageDimensions { Mode = "unknown" };
                }

                if (scannerType == ScannerType.ASE)
                {
                    // ASE pipeline already renders from 16-bit raw. Annotation
                    // coordinate space for ASE is out of scope for v2.9.6.
                    // Caller treats mode=="ase" as "don't scale; historical
                    // behavior applies".
                    return new ServedImageDimensions { Mode = "ase" };
                }

                return new ServedImageDimensions { Mode = "unknown" };
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[SERVED-DIMS] Failed to resolve dimensions for container {Container}", containerNumber);
                return new ServedImageDimensions { Mode = "unknown" };
            }
        }

        private async Task<byte[]?> ReadBytesFromImageAsync(Guid scanId, string imageType, int maxBytes, CancellationToken ct)
        {
            // Use raw SQL with substring() to fetch only a small prefix of the
            // blob rather than the full (up to 10 MB) image data.
            var conn = _dbContext.Database.GetDbConnection();
            var wasOpen = conn.State == ConnectionState.Open;
            if (!wasOpen) await conn.OpenAsync(ct);
            try
            {
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT substring(imagedata from 1 for @p_max) FROM fs6000images WHERE scanid = @p_scan AND imagetype = @p_type LIMIT 1";
                var pMax = cmd.CreateParameter(); pMax.ParameterName = "p_max";  pMax.Value = maxBytes; cmd.Parameters.Add(pMax);
                var pScan = cmd.CreateParameter(); pScan.ParameterName = "p_scan"; pScan.Value = scanId;  cmd.Parameters.Add(pScan);
                var pType = cmd.CreateParameter(); pType.ParameterName = "p_type"; pType.Value = imageType; cmd.Parameters.Add(pType);
                var result = await cmd.ExecuteScalarAsync(ct);
                return result as byte[];
            }
            finally
            {
                if (!wasOpen) await conn.CloseAsync();
            }
        }

        /// <summary>
        /// Parse a FS6000 .img header. Width is at bytes 2–3 (big-endian u16),
        /// height at bytes 4–5. See services/image-splitter/inspector/decoders/fs6000.py
        /// for the full format spec.
        /// </summary>
        private static (int w, int h)? ParseImgHeaderDimensions(byte[]? bytes)
        {
            if (bytes == null || bytes.Length < 6) return null;
            int w = (bytes[2] << 8) | bytes[3];
            int h = (bytes[4] << 8) | bytes[5];
            if (w <= 0 || h <= 0 || w > 100000 || h > 100000) return null;
            return (w, h);
        }

        /// <summary>
        /// Parse a JPEG's first SOF0 / SOF2 marker to extract width and height.
        /// The marker structure is: 0xFF, 0xC0|0xC2, length (2 bytes), precision
        /// (1 byte), height (2 bytes BE), width (2 bytes BE). Returns null if
        /// no marker is found in the provided prefix.
        /// </summary>
        private static (int w, int h)? ParseJpegDimensions(byte[]? bytes)
        {
            if (bytes == null || bytes.Length < 10) return null;
            int start = -1;
            for (int i = 0; i + 2 < bytes.Length; i++)
            {
                if (bytes[i] == 0xFF && bytes[i + 1] == 0xD8 && bytes[i + 2] == 0xFF)
                {
                    start = i;
                    break;
                }
            }
            if (start < 0) return null;

            for (int p = start; p + 9 < bytes.Length; p++)
            {
                if (bytes[p] == 0xFF && (bytes[p + 1] == 0xC0 || bytes[p + 1] == 0xC2))
                {
                    int height = (bytes[p + 5] << 8) | bytes[p + 6];
                    int width = (bytes[p + 7] << 8) | bytes[p + 8];
                    if (width > 0 && height > 0 && width < 100000 && height < 100000)
                        return (width, height);
                }
            }
            return null;
        }
    }
}
