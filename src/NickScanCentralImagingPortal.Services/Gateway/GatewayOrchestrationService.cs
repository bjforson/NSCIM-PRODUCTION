using System.Diagnostics;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Core.Models.Gateway;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Gateway
{
    /// <summary>
    /// Orchestrates data gathering from multiple services and aggregates into unified responses
    /// </summary>
    public class GatewayOrchestrationService : IGatewayOrchestrationService
    {
        private readonly IImageProcessingService _imageService;
        private readonly IIcumDownloadsRepository _icumsRepo;
        private readonly IContainerValidationService _validationService;
        private readonly ApplicationDbContext _context;
        private readonly ILogger<GatewayOrchestrationService> _logger;

        public GatewayOrchestrationService(
            IImageProcessingService imageService,
            IIcumDownloadsRepository icumsRepo,
            IContainerValidationService validationService,
            ApplicationDbContext context,
            ILogger<GatewayOrchestrationService> logger)
        {
            _imageService = imageService;
            _icumsRepo = icumsRepo;
            _validationService = validationService;
            _context = context;
            _logger = logger;
        }

        public async Task<ContainerCompleteResponse> GetContainerCompleteAsync(
            string containerNumber,
            GatewayRequestOptions options)
        {
            var stopwatch = Stopwatch.StartNew();

            _logger.LogInformation(
                "Gateway: Getting complete data for {Container} with options: {@Options}",
                containerNumber,
                options);

            var response = new ContainerCompleteResponse
            {
                ContainerNumber = containerNumber,
                RequestedAt = DateTime.UtcNow
            };

            // Create parallel tasks for requested data
            var tasks = new List<Task>();

            if (options.IncludeScannerData || options.IncludeImage)
                tasks.Add(LoadScannerDataAsync(containerNumber, response));

            if (options.IncludeICUMS)
                tasks.Add(LoadICUMSDataAsync(containerNumber, response));

            if (options.IncludeValidation)
                tasks.Add(LoadValidationDataAsync(containerNumber, response));

            // Execute all tasks in parallel
            await Task.WhenAll(tasks);

            stopwatch.Stop();
            response.ResponseTimeMs = (int)stopwatch.ElapsedMilliseconds;

            _logger.LogInformation(
                "Gateway: Completed for {Container} in {Ms}ms. Available: {@Availability}",
                containerNumber,
                stopwatch.ElapsedMilliseconds,
                response.Available);

            return response;
        }

        private async Task LoadScannerDataAsync(string containerNumber, ContainerCompleteResponse response)
        {
            try
            {
                _logger.LogDebug("Loading scanner data for {Container}", containerNumber);

                var data = await _imageService.GetCompleteContainerDataAsync(containerNumber);

                if (data != null)
                {
                    response.Scanner = new ScannerDataSection
                    {
                        DetectedScanner = data.DetectedScanner,
                        ImageBase64 = data.ImageBase64,
                        ImageBytes = data.ImageBytes,
                        MimeType = data.MimeType,
                        ScanTime = data.ScanTime,
                        ImageSizeBytes = data.ImageSizeBytes,
                        Width = data.Width,
                        Height = data.Height,
                        Quality = data.Quality,
                        FromCache = data.FromCache,
                        ProcessingPipeline = data.ProcessingPipeline,
                        FS6000Data = data.FS6000Data,
                        ASEData = data.ASEData
                    };

                    response.Available.HasScannerData = true;
                    response.Available.HasImage = data.ImageBytes != null && data.ImageBytes.Length > 0;

                    _logger.LogDebug(
                        "Scanner data loaded for {Container}: {Scanner}, ImageSize={Size}KB",
                        containerNumber,
                        data.DetectedScanner,
                        data.ImageSizeBytes / 1024);
                }
                else
                {
                    response.Warnings.Add("Scanner data not found");
                    _logger.LogWarning("No scanner data found for {Container}", containerNumber);
                }
            }
            catch (Exception ex)
            {
                response.Errors.Add($"Scanner data error: {ex.Message}");
                _logger.LogError(ex, "Failed to load scanner data for {Container}", containerNumber);
            }
        }

        private async Task LoadICUMSDataAsync(string containerNumber, ContainerCompleteResponse response)
        {
            try
            {
                _logger.LogDebug("Loading ICUMS data for {Container}", containerNumber);

                // Check if container has ICUMS data
                var hasICUMSData = await _icumsRepo.ContainerHasICUMSDataAsync(containerNumber);

                if (hasICUMSData)
                {
                    // Get the most recent download for this container
                    var downloadFile = await _icumsRepo.GetMostRecentDownloadForContainerAsync(containerNumber);

                    if (downloadFile != null)
                    {
                        response.ICUMS = new ICUMSDataSection
                        {
                            BOENumber = downloadFile.FileName, // Filename often contains BOE
                            ManifestNumber = null, // TODO: Extract from manifest items
                            Consignee = null, // TODO: Get from BOEDocument
                            ConsigneeAddress = null,
                            OriginPort = null,
                            DestinationPort = null,
                            VesselName = null,
                            ArrivalDate = null,
                            CargoDescription = null,
                            DeclaredValue = null,
                            CustomsStatus = downloadFile.ProcessingStatus,
                            DownloadedAt = downloadFile.DownloadDate, // FIXED: Property is DownloadDate not DownloadedAt
                            LineItemCount = downloadFile.RecordCount ?? 0
                        };

                        response.Available.HasICUMSData = true;

                        _logger.LogDebug(
                            "ICUMS data loaded for {Container}: File={File}, Status={Status}",
                            containerNumber,
                            downloadFile.FileName,
                            downloadFile.ProcessingStatus);
                    }
                }
                else
                {
                    response.Warnings.Add("ICUMS data not found");
                    _logger.LogWarning("No ICUMS data found for {Container}", containerNumber);
                }
            }
            catch (Exception ex)
            {
                response.Errors.Add($"ICUMS data error: {ex.Message}");
                _logger.LogError(ex, "Failed to load ICUMS data for {Container}", containerNumber);
            }
        }

        private async Task LoadValidationDataAsync(string containerNumber, ContainerCompleteResponse response)
        {
            try
            {
                _logger.LogDebug("Loading validation data for {Container}", containerNumber);

                // Use existing validation methods - default to CMR clearance type
                var validationResult = await _validationService.ValidateContainerAsync(
                    containerNumber,
                    ClearanceType.CMR); // Default to CMR, can be made configurable later

                if (validationResult != null)
                {
                    response.Validation = new ValidationDataSection
                    {
                        ValidationStatus = validationResult.Status.ToString(),
                        CompletenessScore = validationResult.DataCompletenessScore,
                        LastValidatedAt = validationResult.ValidatedAt,
                        ValidatedBy = null, // TODO: Add to validation result
                        MissingFields = new List<string>(), // TODO: Extract from completeness reports
                        ValidationErrors = validationResult.ValidationErrors?.Select(e => e.ErrorMessage).ToList() ?? new List<string>(),
                        IsReadyForSubmission = validationResult.IsReadyForSubmission
                    };

                    response.Available.HasValidationData = true;

                    _logger.LogDebug(
                        "Validation data loaded for {Container}: Status={Status}, Completeness={Score}%",
                        containerNumber,
                        validationResult.Status,
                        validationResult.DataCompletenessScore);
                }
                else
                {
                    response.Warnings.Add("Validation data not found");
                    _logger.LogWarning("No validation data found for {Container}", containerNumber);
                }
            }
            catch (Exception ex)
            {
                response.Errors.Add($"Validation data error: {ex.Message}");
                _logger.LogError(ex, "Failed to load validation data for {Container}", containerNumber);
            }
        }

        // ===== Admin Methods (Cache Management) =====

        public async Task<int> ClearPlaceholderCacheAsync(int minSizeBytes)
        {
            try
            {
                // Delete all cached images smaller than minSizeBytes
                var deletedCount = await _context.Database
                    .ExecuteSqlAsync($"DELETE FROM dbo.ImageCaches WHERE LEN(ImageData) < {minSizeBytes}");

                _logger.LogInformation("Admin: Cleared {Count} placeholder caches (< {MinSize} bytes)", deletedCount, minSizeBytes);
                return deletedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing placeholder cache");
                throw;
            }
        }

        public async Task<object> GetCacheStatsAsync()
        {
            try
            {
                var totalCached = await _context.ImageCaches.CountAsync();

                // Count by size categories
                var placeholders = await _context.ImageCaches.CountAsync(c => c.ImageData.Length < 10000);
                var small = await _context.ImageCaches.CountAsync(c => c.ImageData.Length >= 10000 && c.ImageData.Length < 100000);
                var medium = await _context.ImageCaches.CountAsync(c => c.ImageData.Length >= 100000 && c.ImageData.Length < 500000);
                var large = await _context.ImageCaches.CountAsync(c => c.ImageData.Length >= 500000);

                // Total size
                var totalSizeBytes = await _context.ImageCaches.SumAsync(c => (long)c.ImageData.Length);

                return new
                {
                    totalCached,
                    totalSizeMB = totalSizeBytes / (1024.0 * 1024.0),
                    bySizeCategory = new
                    {
                        placeholders = new { count = placeholders, description = "< 10 KB (likely placeholders)" },
                        small = new { count = small, description = "10-100 KB" },
                        medium = new { count = medium, description = "100-500 KB" },
                        large = new { count = large, description = "> 500 KB" }
                    },
                    timestamp = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cache stats");
                throw;
            }
        }
    }
}

