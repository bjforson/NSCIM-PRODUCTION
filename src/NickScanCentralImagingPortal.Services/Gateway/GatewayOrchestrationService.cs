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
        private readonly IcumDownloadsDbContext _icumDownloadsDbContext;
        private readonly ILogger<GatewayOrchestrationService> _logger;

        public GatewayOrchestrationService(
            IImageProcessingService imageService,
            IIcumDownloadsRepository icumsRepo,
            IContainerValidationService validationService,
            ApplicationDbContext context,
            IcumDownloadsDbContext icumDownloadsDbContext,
            ILogger<GatewayOrchestrationService> logger)
        {
            _imageService = imageService;
            _icumsRepo = icumsRepo;
            _validationService = validationService;
            _context = context;
            _icumDownloadsDbContext = icumDownloadsDbContext;
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

            // Load requested data sections sequentially.
            //
            // CORRECTNESS: These methods share the scoped DbContext instances (_context +
            // _icumDownloadsDbContext) and the repos/services that internally use them.
            // DbContext is NOT thread-safe, so running the loads under Task.WhenAll
            // produced "A second operation was started on this context instance before
            // a previous operation completed" — the exception text was also being leaked
            // into anonymous response bodies (pre-fallback-policy). Sequential loads are
            // a few hundred ms slower but correct.
            //
            // TODO: migrate to IDbContextFactory<T> + fresh contexts per load to restore
            // parallel execution once we have integration coverage.
            if (options.IncludeScannerData || options.IncludeImage)
                await LoadScannerDataAsync(containerNumber, response);

            if (options.IncludeICUMS)
                await LoadICUMSDataAsync(containerNumber, response);

            if (options.IncludeValidation)
                await LoadValidationDataAsync(containerNumber, response);

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

                var hasICUMSData = await _icumsRepo.ContainerHasICUMSDataAsync(containerNumber);

                if (!hasICUMSData)
                {
                    response.Warnings.Add("ICUMS data not found");
                    _logger.LogWarning("No ICUMS data found for {Container}", containerNumber);
                    return;
                }

                // Get the most recent download file (for DownloadedAt + LineItemCount fallback)
                var downloadFile = await _icumsRepo.GetMostRecentDownloadForContainerAsync(containerNumber);

                // Get the most recent BOEDocument for this container — full field visibility
                var boe = await _icumDownloadsDbContext.BOEDocuments
                    .AsNoTracking()
                    .Where(b => b.ContainerNumber == containerNumber)
                    .OrderByDescending(b => b.Id)
                    .FirstOrDefaultAsync();

                var lineItemCount = boe != null
                    ? await _icumDownloadsDbContext.ManifestItems.AsNoTracking().CountAsync(i => i.BOEDocumentId == boe.Id)
                    : (downloadFile?.RecordCount ?? 0);

                response.ICUMS = new ICUMSDataSection
                {
                    // Identifiers
                    BOENumber         = boe?.DeclarationNumber,
                    DeclarationNumber = boe?.DeclarationNumber,
                    RotationNumber    = boe?.RotationNumber,
                    ManifestNumber    = boe?.BlNumber,
                    BlNumber          = boe?.BlNumber,
                    HouseBl           = boe?.HouseBl,
                    MasterBlNumber    = boe?.MasterBlNumber,

                    // Parties
                    Consignee         = boe?.ConsigneeName,
                    ConsigneeName     = boe?.ConsigneeName,
                    ConsigneeAddress  = boe?.ConsigneeAddress,
                    ShipperName       = boe?.ShipperName,
                    ShipperAddress    = boe?.ShipperAddress,
                    ImpName           = boe?.ImpName,
                    ImpAddress        = boe?.ImpAddress,
                    ExpName           = boe?.ExpName,
                    ExpAddress        = boe?.ExpAddress,
                    DeclarantName     = boe?.DeclarantName,
                    DeclarantAddress  = boe?.DeclarantAddress,

                    // Location / shipping
                    CountryOfOrigin   = boe?.CountryOfOrigin,
                    DeliveryPlace     = boe?.DeliveryPlace,
                    DestinationPort   = boe?.DeliveryPlace,

                    // Cargo / declaration
                    CargoDescription      = boe?.GoodsDescription,
                    GoodsDescription      = boe?.GoodsDescription,
                    MarksNumbers          = boe?.MarksNumbers,
                    ClearanceType         = boe?.ClearanceType,
                    OriginalClearanceType = boe?.OriginalClearanceType,
                    CmrUpgradedAt         = boe?.CmrUpgradedAt,
                    RegimeCode            = boe?.RegimeCode,
                    DeclarationDate       = boe?.DeclarationDate,
                    DeclarationVersion    = boe?.DeclarationVersion,
                    NoOfContainers        = boe?.NoOfContainers,

                    // Container details
                    ContainerDescription = boe?.ContainerDescription,
                    ContainerISO         = boe?.ContainerISO,
                    ContainerSize        = boe?.ContainerSize,
                    ContainerQuantity    = boe?.ContainerQuantity,
                    ContainerWeight      = boe?.ContainerWeight,
                    ContainerStatus      = boe?.ContainerStatus,
                    ContainerRemarks     = boe?.ContainerRemarks,
                    SealNumber           = boe?.SealNumber,
                    TruckPlateNumber     = boe?.TruckPlateNumber,
                    DriverName           = boe?.DriverName,
                    DriverLicense        = boe?.DriverLicense,

                    // Financial & risk
                    DeclaredValue    = boe?.TotalDutyPaid,
                    TotalDutyPaid    = boe?.TotalDutyPaid,
                    CrmsLevel        = boe?.CrmsLevel,
                    CompOffRemarks   = boe?.CompOffRemarks,
                    CcvrIntelRemarks = boe?.CcvrIntelRemarks,

                    // State
                    CustomsStatus         = boe?.ProcessingStatus ?? downloadFile?.ProcessingStatus,
                    ProcessingStatus      = boe?.ProcessingStatus ?? downloadFile?.ProcessingStatus,
                    ErrorMessage          = boe?.ErrorMessage,
                    IsConsolidated        = boe?.IsConsolidated ?? false,
                    HasIngestionWarnings  = boe?.HasIngestionWarnings ?? false,
                    IngestionWarnings     = boe?.IngestionWarnings,
                    ProcessedAt           = boe?.ProcessedAt,
                    UpdatedAt             = boe?.UpdatedAt,
                    DownloadedAt          = downloadFile?.DownloadDate,
                    LineItemCount         = lineItemCount
                };

                response.Available.HasICUMSData = true;

                _logger.LogDebug(
                    "ICUMS data loaded for {Container}: BOE={BOE}, Status={Status}, Items={Items}",
                    containerNumber, boe?.DeclarationNumber, boe?.ProcessingStatus, lineItemCount);
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

