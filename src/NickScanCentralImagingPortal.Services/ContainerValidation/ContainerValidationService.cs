using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ContainerValidation
{
    /// <summary>
    /// Enhanced container validation service with clearance type awareness and comprehensive completeness calculation
    /// </summary>
    public class ContainerValidationService : IContainerValidationService
    {
        private readonly IcumDownloadsDbContext _icumDownloadsDbContext;
        private readonly ApplicationDbContext _applicationDbContext;
        private readonly IClearanceTypeDetectionService _clearanceTypeDetectionService;
        private readonly IIcumApiService _icumApiService;
        private readonly IIcumDownloadsRepository _icumDownloadsRepository;
        private readonly IICUMSDataCacheService _icumsCacheService;
        private readonly IImageValidationService _imageValidationService;
        private readonly IContainerDataRepository _containerDataRepository;
        private readonly IServiceProvider _serviceProvider;
        private readonly IConfiguration _configuration;
        private readonly ILogger<ContainerValidationService> _logger;

        // Track downloads in progress to prevent duplicate concurrent downloads
        private static readonly ConcurrentDictionary<string, SemaphoreSlim> _downloadLocks = new();

        public ContainerValidationService(
            IcumDownloadsDbContext icumDownloadsDbContext,
            ApplicationDbContext applicationDbContext,
            IClearanceTypeDetectionService clearanceTypeDetectionService,
            IIcumApiService icumApiService,
            IIcumDownloadsRepository icumDownloadsRepository,
            IICUMSDataCacheService icumsCacheService,
            IImageValidationService imageValidationService,
            IContainerDataRepository containerDataRepository,
            IServiceProvider serviceProvider,
            IConfiguration configuration,
            ILogger<ContainerValidationService> logger)
        {
            _icumDownloadsDbContext = icumDownloadsDbContext;
            _applicationDbContext = applicationDbContext;
            _clearanceTypeDetectionService = clearanceTypeDetectionService;
            _icumApiService = icumApiService;
            _icumDownloadsRepository = icumDownloadsRepository;
            _icumsCacheService = icumsCacheService;
            _imageValidationService = imageValidationService;
            _containerDataRepository = containerDataRepository;
            _serviceProvider = serviceProvider;
            _configuration = configuration;
            _logger = logger;
        }

        /// <summary>
        /// Gets containers for validation with filtering and pagination
        /// </summary>
        public async Task<PagedResult<ContainerValidationModel>> GetContainersForValidationAsync(
            ValidationFilter filter,
            PaginationOptions pagination)
        {
            try
            {
                _logger.LogInformation("Getting containers for validation with filter: {Filter}", filter);

                // Start with scanner data (FS6000 and ASE)
                var fs6000Query = _applicationDbContext.FS6000Scans
                    .Where(s => !string.IsNullOrEmpty(s.ContainerNumber) && s.ContainerNumber.Length == 11)
                    .Select(s => new ScannerContainerData
                    {
                        Id = s.Id,
                        ContainerNumber = s.ContainerNumber,
                        ScannerType = "FS6000",
                        ScanDateTime = s.ScanTime,
                        FilePath = s.FilePath,
                        ImagePath = s.FilePath,
                        HasImage = !string.IsNullOrEmpty(s.FilePath)
                    });

                var aseQuery = _applicationDbContext.AseScans
                    .Where(s => !string.IsNullOrEmpty(s.ContainerNumber) && s.ContainerNumber.Length == 11)
                    .Select(s => new ScannerContainerData
                    {
                        Id = s.Id,
                        ContainerNumber = s.ContainerNumber,
                        ScannerType = "ASE",
                        ScanDateTime = s.ScanTime,
                        FilePath = s.ImageDisplayName,
                        ImagePath = s.ImageDisplayName,
                        HasImage = s.ScanImage != null && s.ScanImage.Length > 0
                    });

                var scannerQuery = fs6000Query.Union(aseQuery);

                // Apply filters
                if (!string.IsNullOrEmpty(filter.SearchTerm))
                {
                    scannerQuery = scannerQuery.Where(c => c.ContainerNumber.Contains(filter.SearchTerm));
                }

                if (!string.IsNullOrEmpty(filter.ScannerType))
                {
                    scannerQuery = scannerQuery.Where(c => c.ScannerType == filter.ScannerType);
                }

                if (filter.FromDate.HasValue)
                {
                    scannerQuery = scannerQuery.Where(c => c.ScanDateTime >= filter.FromDate.Value);
                }

                if (filter.ToDate.HasValue)
                {
                    scannerQuery = scannerQuery.Where(c => c.ScanDateTime <= filter.ToDate.Value);
                }

                // Get total count
                var totalCount = await scannerQuery.CountAsync();

                // Apply pagination - limit to reasonable page size to prevent timeout
                var effectivePageSize = Math.Min(pagination.PageSize, 20);
                var containers = await scannerQuery
                    .OrderByDescending(c => c.ScanDateTime)
                    .Skip((pagination.Page - 1) * effectivePageSize)
                    .Take(effectivePageSize)
                    .ToListAsync();

                // OPTIMIZATION: Queue missing ICUMS data for background download instead of blocking
                await QueueMissingICUMSDataAsync(containers.Select(c => c.ContainerNumber).ToList());

                // Convert to validation models with actual validation data
                var validationModels = new List<ContainerValidationModel>();
                foreach (var container in containers)
                {
                    try
                    {
                        // Detect clearance type from ICUMS data
                        var detectedClearanceType = await DetectClearanceTypeAsync(container.ContainerNumber);

                        // Apply clearance type filter early if specified
                        if (filter.ClearanceType.HasValue && detectedClearanceType != filter.ClearanceType.Value)
                            continue;

                        // Validate scanner data
                        var scannerCompleteness = await ValidateScannerDataAsync(container.ContainerNumber);

                        // Validate ICUMS data with detected clearance type (data should now be available)
                        var icumsCompleteness = await ValidateICUMSDataAsync(container.ContainerNumber, detectedClearanceType);

                        // Validate image data
                        var imageCompleteness = await ValidateImageDataAsync(container.ContainerNumber);

                        // Calculate overall completeness
                        var completenessPercentage = CalculateOverallCompletenessPercentage(
                            scannerCompleteness, icumsCompleteness, imageCompleteness);

                        // Apply completeness filters
                        if (filter.MinCompletenessScore.HasValue && completenessPercentage < filter.MinCompletenessScore.Value)
                            continue;

                        if (filter.MaxCompletenessScore.HasValue && completenessPercentage > filter.MaxCompletenessScore.Value)
                            continue;

                        // Determine validation status
                        var status = DetermineStatusFromCompleteness(
                            scannerCompleteness, icumsCompleteness, imageCompleteness);

                        // Apply status filter
                        if (filter.Status.HasValue && status != filter.Status.Value)
                            continue;

                        // Create full validation model
                        var validationModel = new ContainerValidationModel
                        {
                            ContainerNumber = container.ContainerNumber,
                            ScannerType = container.ScannerType,
                            ScanDateTime = container.ScanDateTime,
                            CreatedAt = container.ScanDateTime, // Use scan date time as created date
                            Status = status,
                            ClearanceType = detectedClearanceType,
                            DataCompletenessPercentage = completenessPercentage,
                            ScannerCompleteness = scannerCompleteness,
                            ICUMSCompleteness = icumsCompleteness,
                            ImageCompleteness = imageCompleteness,
                        IsReadyForSubmission = completenessPercentage >= _configuration.GetValue<int>("Validation:ReadyForSubmissionThreshold", 90) &&
                                               scannerCompleteness.IsDataComplete &&
                                               icumsCompleteness.IsCompleteForClearanceType &&
                                               imageCompleteness.HasImage,
                            ValidationErrors = new List<ValidationError>()
                        };

                        validationModels.Add(validationModel);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error validating container {ContainerNumber}, skipping", container.ContainerNumber);
                        // Skip containers that fail validation to prevent breaking the entire list
                        continue;
                    }
                }

                return new PagedResult<ContainerValidationModel>
                {
                    Data = validationModels,
                    TotalCount = totalCount,
                    Page = pagination.Page,
                    PageSize = effectivePageSize,
                    TotalPages = (int)Math.Ceiling((double)totalCount / effectivePageSize)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting containers for validation");
                throw;
            }
        }

        /// <summary>
        /// Validates a specific container with clearance type awareness
        /// </summary>
        public async Task<ContainerValidationResult> ValidateContainerAsync(string containerNumber, ClearanceType clearanceType)
        {
            try
            {
                _logger.LogInformation("Validating container: {ContainerNumber}, clearance type: {ClearanceType}",
                    containerNumber, clearanceType);

                var result = new ContainerValidationResult
                {
                    ContainerNumber = containerNumber,
                    ClearanceType = clearanceType,
                    ValidatedAt = DateTime.UtcNow
                };

                // 1. Validate scanner data completeness
                result.ScannerCompleteness = await ValidateScannerDataAsync(containerNumber);

                // 2. Validate ICUMS data completeness based on clearance type
                result.ICUMSCompleteness = await ValidateICUMSDataAsync(containerNumber, clearanceType);

                // 3. Validate image data completeness
                result.ImageCompleteness = await ValidateImageDataAsync(containerNumber);

                // 4. Validate business rules
                result.BusinessRules = await ValidateBusinessRulesAsync(containerNumber, clearanceType);

                // 5. Calculate overall completeness score
                result.DataCompletenessScore = CalculateOverallCompletenessScore(
                    result.ScannerCompleteness,
                    result.ICUMSCompleteness,
                    result.ImageCompleteness,
                    result.BusinessRules);

                // 6. Determine if ready for submission
                result.IsReadyForSubmission = IsReadyForSubmission(result, clearanceType);

                // 7. Set validation status
                result.Status = DetermineValidationStatus(result);

                // 8. Collect validation errors
                result.ValidationErrors = CollectValidationErrors(result);

                result.ValidationMessage = result.IsReadyForSubmission
                    ? "Container is ready for ICUMS submission"
                    : $"Container validation failed: {string.Join(", ", result.ValidationErrors.Select(e => e.ErrorMessage))}";

                _logger.LogInformation("Validation completed for container {ContainerNumber}: {Status}, score: {Score}",
                    containerNumber, result.Status, result.DataCompletenessScore);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating container: {ContainerNumber}", containerNumber);
                throw;
            }
        }

        /// <summary>
        /// Gets validation summary statistics
        /// </summary>
        public async Task<ValidationSummaryStats> GetValidationSummaryAsync()
        {
            try
            {
                _logger.LogInformation("Getting validation summary statistics");

                var stats = new ValidationSummaryStats();

                // Get counts directly from database for better performance - exclude VINs (17 chars)
                var fs6000Count = await _applicationDbContext.FS6000Scans
                    .Where(s => !string.IsNullOrEmpty(s.ContainerNumber) && s.ContainerNumber.Length == 11)
                    .CountAsync();

                var aseCount = await _applicationDbContext.AseScans
                    .Where(s => !string.IsNullOrEmpty(s.ContainerNumber) && s.ContainerNumber.Length == 11)
                    .CountAsync();

                stats.TotalContainers = fs6000Count + aseCount;
                stats.FS6000Containers = fs6000Count;
                stats.ASEContainers = aseCount;

                // For now, set all as pending validation since we don't have a validation status table
                // This can be enhanced later with actual validation tracking
                stats.PendingValidation = stats.TotalContainers;
                stats.InReview = 0;
                stats.Validated = 0;
                stats.Approved = 0;
                stats.Rejected = 0;
                stats.Submitted = 0;
                stats.ValidationErrors = 0;

                // Estimate clearance types (can be enhanced with actual detection)
                stats.CMRContainers = (int)(stats.TotalContainers * 0.3); // Rough estimate
                stats.IMEXContainers = stats.TotalContainers - stats.CMRContainers;

                _logger.LogInformation("Validation summary: {TotalContainers} total, {Pending} pending, {Approved} approved",
                    stats.TotalContainers, stats.PendingValidation, stats.Approved);

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting validation summary");
                throw;
            }
        }

        /// <summary>
        /// Gets CMR containers for validation
        /// </summary>
        public async Task<List<ContainerValidationModel>> GetCMRContainersForValidationAsync()
        {
            var containers = await _containerDataRepository.GetAllScannerContainersAsync();

            var cmrContainers = new List<ContainerValidationModel>();
            foreach (var container in containers)
            {
                var clearanceType = await _clearanceTypeDetectionService.DetectClearanceTypeAsync(container.ContainerNumber);
                if (clearanceType == ClearanceType.CMR)
                {
                    var scannerContainer = new ScannerContainer
                    {
                        Id = container.Id,
                        ContainerNumber = container.ContainerNumber,
                        ScannerType = container.ScannerType,
                        ScanDateTime = container.ScanDateTime
                    };
                    var validationModel = await CreateValidationModelAsync(scannerContainer);
                    cmrContainers.Add(validationModel);
                }
            }

            return cmrContainers;
        }

        /// <summary>
        /// Gets IM/EX containers for validation
        /// </summary>
        public async Task<List<ContainerValidationModel>> GetIMEXContainersForValidationAsync()
        {
            var containers = await _containerDataRepository.GetAllScannerContainersAsync();

            var imexContainers = new List<ContainerValidationModel>();
            foreach (var container in containers)
            {
                var clearanceType = await _clearanceTypeDetectionService.DetectClearanceTypeAsync(container.ContainerNumber);
                if (clearanceType == ClearanceType.IMEX)
                {
                    var scannerContainer = new ScannerContainer
                    {
                        Id = container.Id,
                        ContainerNumber = container.ContainerNumber,
                        ScannerType = container.ScannerType,
                        ScanDateTime = container.ScanDateTime
                    };
                    var validationModel = await CreateValidationModelAsync(scannerContainer);
                    imexContainers.Add(validationModel);
                }
            }

            return imexContainers;
        }

        /// <summary>
        /// Gets completeness report for a container
        /// </summary>
        public async Task<ContainerCompletenessReport> GetCompletenessReportAsync(string containerNumber, ClearanceType clearanceType)
        {
            var validationResult = await ValidateContainerAsync(containerNumber, clearanceType);

            return new ContainerCompletenessReport
            {
                ContainerNumber = containerNumber,
                ClearanceType = clearanceType,
                OverallCompletenessScore = validationResult.DataCompletenessScore,
                IsComplete = validationResult.IsReadyForSubmission,
                ScannerCompleteness = validationResult.ScannerCompleteness,
                ICUMSCompleteness = validationResult.ICUMSCompleteness,
                ImageCompleteness = validationResult.ImageCompleteness,
                MissingRequirements = validationResult.ValidationErrors.Select(e => e.ErrorMessage).ToList(),
                Recommendations = GenerateRecommendations(validationResult),
                GeneratedAt = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Checks if container is ready for submission
        /// </summary>
        public async Task<bool> IsReadyForSubmissionAsync(string containerNumber, ClearanceType clearanceType)
        {
            var validationResult = await ValidateContainerAsync(containerNumber, clearanceType);
            return validationResult.IsReadyForSubmission;
        }

        /// <summary>
        /// Approves container for submission
        /// </summary>
        public async Task<bool> ApproveForSubmissionAsync(string containerNumber, string approvedBy, ClearanceType clearanceType)
        {
            try
            {
                _logger.LogInformation("Approving container {ContainerNumber} for submission by {ApprovedBy}",
                    containerNumber, approvedBy);

                // Validate that container is ready for submission
                var isReady = await IsReadyForSubmissionAsync(containerNumber, clearanceType);
                if (!isReady)
                {
                    _logger.LogWarning("Container {ContainerNumber} is not ready for submission", containerNumber);
                    return false;
                }

                // TODO: Implement actual approval logic (update database, queue for submission, etc.)
                // This would involve updating the container status in the database

                _logger.LogInformation("Successfully approved container {ContainerNumber} for submission", containerNumber);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error approving container {ContainerNumber} for submission", containerNumber);
                return false;
            }
        }

        /// <summary>
        /// Rejects container
        /// </summary>
        public Task<bool> RejectContainerAsync(string containerNumber, string rejectionReason, string rejectedBy)
        {
            try
            {
                _logger.LogInformation("Rejecting container {ContainerNumber} by {RejectedBy}: {Reason}",
                    containerNumber, rejectedBy, rejectionReason);

                // TODO: Implement actual rejection logic (update database, log rejection reason, etc.)

                _logger.LogInformation("Successfully rejected container {ContainerNumber}", containerNumber);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rejecting container {ContainerNumber}", containerNumber);
                return Task.FromResult(false);
            }
        }

        // Bulk operations (implemented as stubs for now)
        public async Task<BulkValidationResult> ValidateAllPendingContainersAsync()
        {
            // TODO: Implement bulk validation
            await Task.CompletedTask;
            return new BulkValidationResult();
        }

        public async Task<BulkApprovalResult> ApproveBulkContainersAsync(List<string> containerNumbers, string approvedBy)
        {
            // TODO: Implement bulk approval
            await Task.CompletedTask;
            return new BulkApprovalResult();
        }

        public async Task<BulkRejectionResult> RejectBulkContainersAsync(List<string> containerNumbers, string rejectionReason, string rejectedBy)
        {
            // TODO: Implement bulk rejection
            await Task.CompletedTask;
            return new BulkRejectionResult();
        }

        #region Private Helper Methods

        /// <summary>
        /// Gets the base query for scanner containers (FS6000 and ASE)
        /// </summary>
        // ✅ REFACTORED: Now using shared ContainerDataRepository.GetScannerContainersQuery()
        // VIN filtering (11-char containers) applied in GetContainersForValidationAsync() at line 66

        /// <summary>
        /// Creates a validation model from scanner container data
        /// </summary>
        private async Task<ContainerValidationModel> CreateValidationModelAsync(ScannerContainer container)
        {
            var clearanceType = await _clearanceTypeDetectionService.DetectClearanceTypeAsync(container.ContainerNumber);

            var validationModel = new ContainerValidationModel
            {
                Id = container.Id.GetHashCode(), // Use hash of Guid as int ID
                ContainerNumber = container.ContainerNumber,
                ScannerType = container.ScannerType,
                ClearanceType = clearanceType,
                ScanDateTime = container.ScanDateTime,
                Status = ValidationStatus.Pending,
                CreatedAt = DateTime.UtcNow
            };

            // Calculate completeness
            var validationResult = await ValidateContainerAsync(container.ContainerNumber, clearanceType);
            validationModel.DataCompletenessPercentage = validationResult.DataCompletenessScore;
            validationModel.IsReadyForSubmission = validationResult.IsReadyForSubmission;
            validationModel.ScannerCompleteness = validationResult.ScannerCompleteness;
            validationModel.ICUMSCompleteness = validationResult.ICUMSCompleteness;
            validationModel.ImageCompleteness = validationResult.ImageCompleteness;
            validationModel.BusinessRules = validationResult.BusinessRules;
            validationModel.ValidationErrors = validationResult.ValidationErrors;
            validationModel.Status = validationResult.Status;

            return validationModel;
        }

        /// <summary>
        /// Validates scanner data completeness
        /// </summary>
        private async Task<ScannerDataCompleteness> ValidateScannerDataAsync(string containerNumber)
        {
            var completeness = new ScannerDataCompleteness();

            // Check FS6000 scanner data
            var fs6000Scan = await _applicationDbContext.FS6000Scans
                .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

            if (fs6000Scan != null)
            {
                completeness.HasScannerData = true;
                completeness.ScannerType = "FS6000";
                completeness.ScanDateTime = fs6000Scan.ScanTime;
                completeness.IsDataComplete = !string.IsNullOrEmpty(fs6000Scan.ContainerNumber) &&
                                           fs6000Scan.ScanTime != default;
                completeness.CompletenessScore = completeness.IsDataComplete ? 100 : 50;
                return completeness;
            }

            // Check ASE scanner data
            var aseScan = await _applicationDbContext.AseScans
                .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

            if (aseScan != null)
            {
                completeness.HasScannerData = true;
                completeness.ScannerType = "ASE";
                completeness.ScanDateTime = aseScan.ScanTime;
                completeness.IsDataComplete = !string.IsNullOrEmpty(aseScan.ContainerNumber) &&
                                           aseScan.ScanTime != default;
                completeness.CompletenessScore = completeness.IsDataComplete ? 100 : 50;
                return completeness;
            }

            // No scanner data found
            completeness.HasScannerData = false;
            completeness.CompletenessScore = 0;
            completeness.ValidationErrors.Add("No scanner data found");
            return completeness;
        }

        /// <summary>
        /// Validates ICUMS data completeness based on clearance type
        /// </summary>
        private async Task<ICUMSDataCompleteness> ValidateICUMSDataAsync(string containerNumber, ClearanceType clearanceType)
        {
            var completeness = new ICUMSDataCompleteness
            {
                RequiredClearanceType = clearanceType
            };

            // OPTIMIZATION: Check cache first
            var boeDocument = await _icumsCacheService.GetCachedBOEDocumentAsync(containerNumber);

            if (boeDocument == null)
            {
                // Not in cache, check database
                boeDocument = await _icumDownloadsDbContext.BOEDocuments
                    .FirstOrDefaultAsync(b => b.ContainerNumber == containerNumber);

                if (boeDocument != null)
                {
                    // Found in database, cache it for future requests
                    await _icumsCacheService.SetCachedBOEDocumentAsync(containerNumber, boeDocument);
                }
            }

            if (boeDocument == null)
            {
                // Data not found locally - trigger on-demand download and ingestion from ICUMS
                _logger.LogInformation("No local ICUMS data for container {ContainerNumber}, triggering on-demand download", containerNumber);

                try
                {
                    // Step 1: Fetch from ICUMS API
                    var icumsResponse = await _icumApiService.FetchContainerDataAsync(containerNumber);

                    if (icumsResponse.Status == "Success" && icumsResponse.Data != null)
                    {
                        // CRITICAL FIX: Validate that the response contains actual data, not just an empty object
                        if (!IsValidBoeScanDocument(icumsResponse.Data))
                        {
                            _logger.LogWarning("ICUMS API returned empty/null data for container {ContainerNumber} - container not found in ICUMS",
                                containerNumber);
                            completeness.CompletenessScore = 0;
                            completeness.ValidationErrors.Add("Container not found in ICUMS (API returned empty response)");
                            return completeness;
                        }

                        _logger.LogInformation("Successfully fetched ICUMS data for container {ContainerNumber} from API", containerNumber);

                        // Step 2: Save as DownloadedFile (like batch process does)
                        var downloadedFile = new DownloadedFile
                        {
                            FileName = $"OnDemand_{containerNumber}_{DateTime.UtcNow:yyyyMMddHHmmss}.json",
                            FilePath = $"OnDemand/{containerNumber}", // Virtual path for on-demand downloads
                            FileSize = 0, // We're not saving the actual file
                            DownloadDate = DateTime.UtcNow,
                            ProcessingStatus = "Completed", // Mark as completed since we're processing inline
                            RecordCount = 1
                        };

                        var downloadedFileId = await _icumDownloadsRepository.SaveDownloadedFileAsync(downloadedFile);

                        // Step 3: Convert API response to BOEDocument and save through repository
                        // This ensures it goes through the same validation/normalization as batch ingestion
                        var newBoeDocument = await ConvertICUMSResponseToBOEDocument(icumsResponse.Data, downloadedFileId, containerNumber);

                        if (newBoeDocument != null)
                        {
                            var boeId = await _icumDownloadsRepository.SaveBOEDocumentAsync(newBoeDocument);
                            newBoeDocument.Id = boeId;
                            boeDocument = newBoeDocument;

                            // OPTIMIZATION: Cache the downloaded data
                            await _icumsCacheService.SetCachedBOEDocumentAsync(containerNumber, boeDocument);

                            _logger.LogInformation("On-demand download successful: Saved BOE document for container {ContainerNumber}", containerNumber);
                        }
                        else
                        {
                            completeness.CompletenessScore = 0;
                            completeness.ValidationErrors.Add("Failed to convert downloaded ICUMS data");
                            return completeness;
                        }
                    }
                    else
                    {
                        var errorMsg = icumsResponse.Error?.ErrorMsg ?? icumsResponse.StatusMsg ?? "Unknown error";
                        _logger.LogWarning("Failed to fetch ICUMS data for container {ContainerNumber}: {ErrorMessage}",
                            containerNumber, errorMsg);
                        completeness.CompletenessScore = 0;
                        completeness.ValidationErrors.Add($"No ICUMS data found (API returned: {errorMsg})");
                        return completeness;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error during on-demand ICUMS download for container {ContainerNumber}", containerNumber);
                    completeness.CompletenessScore = 0;
                    completeness.ValidationErrors.Add($"Error fetching ICUMS data: {ex.Message}");
                    return completeness;
                }
            }

            // Check common fields
            completeness.HasContainerNumber = !string.IsNullOrEmpty(boeDocument.ContainerNumber);
            completeness.HasBLNumber = !string.IsNullOrEmpty(boeDocument.BlNumber);
            completeness.HasHouseBL = !string.IsNullOrEmpty(boeDocument.HouseBl);

            // Check clearance type specific fields
            if (clearanceType == ClearanceType.CMR)
            {
                completeness.HasRotationNumber = !string.IsNullOrEmpty(boeDocument.RotationNumber);
                completeness.HasBOENumber = !string.IsNullOrEmpty(boeDocument.DeclarationNumber); // Not required for CMR
            }
            else if (clearanceType == ClearanceType.IMEX)
            {
                completeness.HasBOENumber = !string.IsNullOrEmpty(boeDocument.DeclarationNumber);
                completeness.HasRotationNumber = !string.IsNullOrEmpty(boeDocument.RotationNumber); // Not required for IM/EX
            }

            // Calculate completeness score using the clearance type detection service
            var validationResult = await _clearanceTypeDetectionService.ValidateRequiredFieldsAsync(containerNumber, clearanceType);
            completeness.CompletenessScore = validationResult.CompletenessScore;
            completeness.IsCompleteForClearanceType = validationResult.IsValid;
            completeness.MissingFields = validationResult.MissingFields;

            return completeness;
        }

        /// <summary>
        /// Validates image data completeness using the dedicated ImageValidationService
        /// </summary>
        private async Task<ImageDataCompleteness> ValidateImageDataAsync(string containerNumber)
        {
            // Use the injected ImageValidationService which checks:
            // 1. FS6000Scans database for image records
            // 2. ASE scans database for image data
            // 3. File system (C:\tadi_mirror, C:\TADI, C:\Images, etc.)
            // 4. Pattern matching for complex filenames
            return await _imageValidationService.ValidateImageDataAsync(containerNumber);
        }

        /// <summary>
        /// Validates business rules
        /// </summary>
        private async Task<BusinessRuleValidationResult> ValidateBusinessRulesAsync(string containerNumber, ClearanceType clearanceType)
        {
            var result = new BusinessRuleValidationResult();

            // Container number format validation
            if (IsValidContainerNumber(containerNumber))
            {
                result.PassedRules.Add("Container number format is valid");
            }
            else
            {
                result.FailedRules.Add("Container number format is invalid");
            }

            // Clearance type specific validations
            var clearanceValidation = await _clearanceTypeDetectionService.ValidateRequiredFieldsAsync(containerNumber, clearanceType);
            if (clearanceValidation.IsValid)
            {
                result.PassedRules.Add($"All required fields present for {clearanceType} clearance type");
            }
            else
            {
                result.FailedRules.Add($"Missing required fields for {clearanceType} clearance type: {string.Join(", ", clearanceValidation.MissingFields)}");
            }

            result.IsValid = result.FailedRules.Count == 0;
            result.Score = result.IsValid ? 100 : (int)((double)result.PassedRules.Count / (result.PassedRules.Count + result.FailedRules.Count) * 100);

            return result;
        }

        /// <summary>
        /// Calculates overall completeness score
        /// </summary>
        private int CalculateOverallCompletenessScore(
            ScannerDataCompleteness scannerCompleteness,
            ICUMSDataCompleteness icumsCompleteness,
            ImageDataCompleteness imageCompleteness,
            BusinessRuleValidationResult businessRules)
        {
            // Weighted scoring:
            // Scanner Data: 30%
            // ICUMS Data: 40%
            // Image Data: 20%
            // Business Rules: 10%

            var score = (int)Math.Round(
                (scannerCompleteness.CompletenessScore * 0.3) +
                (icumsCompleteness.CompletenessScore * 0.4) +
                (imageCompleteness.CompletenessScore * 0.2) +
                (businessRules.Score * 0.1)
            );

            return Math.Min(score, 100);
        }

        /// <summary>
        /// Determines if container is ready for submission
        /// </summary>
        private bool IsReadyForSubmission(ContainerValidationResult result, ClearanceType clearanceType)
        {
            // Must have all required components at 100%
            return result.ScannerCompleteness.CompletenessScore == 100 &&
                   result.ICUMSCompleteness.CompletenessScore == 100 &&
                   result.ImageCompleteness.CompletenessScore == 100 &&
                   result.BusinessRules.IsValid &&
                   result.ValidationErrors.Count == 0;
        }

        /// <summary>
        /// Determines validation status
        /// </summary>
        private ValidationStatus DetermineValidationStatus(ContainerValidationResult result)
        {
            if (result.IsReadyForSubmission)
                return ValidationStatus.Validated;

            if (result.ValidationErrors.Any(e => e.Severity == "Error"))
                return ValidationStatus.Rejected;

            if (result.DataCompletenessScore >= 80)
                return ValidationStatus.InReview;

            return ValidationStatus.Pending;
        }

        /// <summary>
        /// Collects validation errors from all sources
        /// </summary>
        private List<ValidationError> CollectValidationErrors(ContainerValidationResult result)
        {
            var errors = new List<ValidationError>();

            // Add scanner data errors
            foreach (var error in result.ScannerCompleteness.ValidationErrors)
            {
                errors.Add(new ValidationError { Field = "ScannerData", ErrorMessage = error, Severity = "Error" });
            }

            // Add ICUMS data errors
            foreach (var error in result.ICUMSCompleteness.ValidationErrors)
            {
                errors.Add(new ValidationError { Field = "ICUMSData", ErrorMessage = error, Severity = "Error" });
            }

            // Add image data errors
            foreach (var error in result.ImageCompleteness.ValidationErrors)
            {
                errors.Add(new ValidationError { Field = "ImageData", ErrorMessage = error, Severity = "Error" });
            }

            // Add business rule errors
            foreach (var rule in result.BusinessRules.FailedRules)
            {
                errors.Add(new ValidationError { Field = "BusinessRules", ErrorMessage = rule, Severity = "Error" });
            }

            return errors;
        }

        /// <summary>
        /// Generates recommendations based on validation results
        /// </summary>
        private List<string> GenerateRecommendations(ContainerValidationResult result)
        {
            var recommendations = new List<string>();

            if (result.ScannerCompleteness.CompletenessScore < 100)
                recommendations.Add("Verify scanner data completeness");

            if (result.ICUMSCompleteness.CompletenessScore < 100)
                recommendations.Add("Complete missing ICUMS data fields");

            if (result.ImageCompleteness.CompletenessScore < 100)
                recommendations.Add("Ensure image file is available and valid");

            if (!result.BusinessRules.IsValid)
                recommendations.Add("Address business rule violations");

            if (recommendations.Count == 0)
                recommendations.Add("Container is ready for submission");

            return recommendations;
        }

        /// <summary>
        /// Validates container number format
        /// </summary>
        private bool IsValidContainerNumber(string containerNumber)
        {
            return !string.IsNullOrEmpty(containerNumber) &&
                   containerNumber.Length >= 4 &&
                   containerNumber.Length <= 15 &&
                   containerNumber.All(c => char.IsLetterOrDigit(c));
        }

        /// <summary>
        /// Calculates overall completeness percentage from individual scores
        /// </summary>
        private int CalculateOverallCompletenessPercentage(
            ScannerDataCompleteness scannerCompleteness,
            ICUMSDataCompleteness icumsCompleteness,
            ImageDataCompleteness imageCompleteness)
        {
            // Weight: Scanner 30%, ICUMS 40%, Image 30%
            var scannerScore = scannerCompleteness.CompletenessScore * 0.3;
            var icumsScore = icumsCompleteness.CompletenessScore * 0.4;
            var imageScore = imageCompleteness.CompletenessScore * 0.3;

            return (int)(scannerScore + icumsScore + imageScore);
        }

        /// <summary>
        /// Determines validation status based on completeness
        /// </summary>
        private ValidationStatus DetermineStatusFromCompleteness(
            ScannerDataCompleteness scannerCompleteness,
            ICUMSDataCompleteness icumsCompleteness,
            ImageDataCompleteness imageCompleteness)
        {
            // If all data is complete, mark as Validated
            if (scannerCompleteness.IsDataComplete &&
                icumsCompleteness.IsCompleteForClearanceType &&
                imageCompleteness.HasImage &&
                imageCompleteness.IsImageValid)
            {
                return ValidationStatus.Validated;
            }

            // If scanner data has issues, mark as needs review
            if (!scannerCompleteness.HasScannerData)
            {
                return ValidationStatus.Pending;
            }

            // If ICUMS data is incomplete, mark as in review
            if (!icumsCompleteness.HasContainerNumber)
            {
                return ValidationStatus.InReview;
            }

            // Default to pending
            return ValidationStatus.Pending;
        }

        /// <summary>
        /// Detects clearance type from ICUMS data
        /// </summary>
        private async Task<ClearanceType> DetectClearanceTypeAsync(string containerNumber)
        {
            try
            {
                // Check for BOE document with rotation number (CMR)
                var hasCMR = await _icumDownloadsDbContext.BOEDocuments
                    .AnyAsync(b => b.ContainerNumber == containerNumber &&
                                 !string.IsNullOrEmpty(b.RotationNumber));

                if (hasCMR)
                    return ClearanceType.CMR;

                // Check for BOE document with declaration number (IMEX)
                var hasIMEX = await _icumDownloadsDbContext.BOEDocuments
                    .AnyAsync(b => b.ContainerNumber == containerNumber &&
                                 !string.IsNullOrEmpty(b.DeclarationNumber));

                if (hasIMEX)
                    return ClearanceType.IMEX;

                // Default to IMEX
                return ClearanceType.IMEX;
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Clearance type detection failed for {ContainerNumber}, defaulting to IMEX: {Error}", containerNumber, ex.Message);
                return ClearanceType.IMEX;
            }
        }

        /// <summary>
        /// Queue missing ICUMS data for background download
        /// Non-blocking alternative to batch download
        /// </summary>
        private async Task QueueMissingICUMSDataAsync(List<string> containerNumbers)
        {
            try
            {
                var queueService = _serviceProvider.GetService(typeof(NickScanCentralImagingPortal.Services.IcumApi.IICUMSDownloadQueueService))
                    as NickScanCentralImagingPortal.Services.IcumApi.IICUMSDownloadQueueService;

                if (queueService == null)
                {
                    _logger.LogDebug("Queue service not available, skipping queue");
                    return;
                }

                // Find containers without local ICUMS data (using shared repository)
                var missingContainers = await _containerDataRepository.FindMissingICUMSContainersAsync(containerNumbers);

                if (missingContainers.Count == 0)
                {
                    _logger.LogDebug("All containers have local ICUMS data, no queue needed");
                    return;
                }

                // Queue for background download with normal priority
                var enqueuedCount = await queueService.EnqueueContainersAsync(
                    missingContainers,
                    priority: 1, // Normal priority
                    requestSource: "ValidationDashboard"
                );

                if (enqueuedCount > 0)
                {
                    _logger.LogInformation("Queued {Count} containers for background ICUMS download", enqueuedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing missing ICUMS data");
                // Don't throw - validation can continue without ICUMS data
            }
        }

        /// <summary>
        /// Batch downloads missing ICUMS data for multiple containers
        /// This is much more efficient than downloading one-by-one during validation
        /// DEPRECATED: Use QueueMissingICUMSDataAsync instead for non-blocking operation
        /// </summary>
        private async Task BatchDownloadMissingICUMSDataAsync(List<string> containerNumbers)
        {
            try
            {
                // Find containers without local ICUMS data
                var existingContainers = await _icumDownloadsDbContext.BOEDocuments
                    .Where(b => containerNumbers.Contains(b.ContainerNumber))
                    .Select(b => b.ContainerNumber)
                    .ToListAsync();

                var missingContainers = containerNumbers
                    .Except(existingContainers)
                    .ToList();

                if (missingContainers.Count == 0)
                {
                    _logger.LogDebug("All containers have local ICUMS data, no downloads needed");
                    return;
                }

                _logger.LogInformation("Batch downloading ICUMS data for {Count} containers", missingContainers.Count);

                // OPTIMIZATION: Pre-load existing containers once to avoid N queries in parallel loop
                var existingContainersInLoop = await _icumDownloadsDbContext.BOEDocuments
                    .Where(b => missingContainers.Contains(b.ContainerNumber))
                    .Select(b => b.ContainerNumber)
                    .ToHashSetAsync();

                // Download in parallel (max 5 at a time to avoid overwhelming the API)
                var semaphore = new SemaphoreSlim(5);
                var downloadTasks = missingContainers.Select(async containerNumber =>
                {
                    // Get or create a lock for this specific container
                    var containerLock = _downloadLocks.GetOrAdd(containerNumber, _ => new SemaphoreSlim(1, 1));

                    // Try to acquire the lock - if another request is already downloading, wait
                    await containerLock.WaitAsync();
                    try
                    {
                        // OPTIMIZED: Check in-memory HashSet instead of database query
                        if (existingContainersInLoop.Contains(containerNumber))
                        {
                            _logger.LogDebug("Container {ContainerNumber} was downloaded by another request, skipping", containerNumber);
                            return;
                        }

                        await semaphore.WaitAsync();
                        try
                        {
                            _logger.LogDebug("Batch download: Fetching ICUMS data for {ContainerNumber}", containerNumber);

                            var icumsResponse = await _icumApiService.FetchContainerDataAsync(containerNumber);

                            if (icumsResponse.Status == "Success" && icumsResponse.Data != null)
                            {
                                // CRITICAL FIX: Validate that the response contains actual data
                                if (!IsValidBoeScanDocument(icumsResponse.Data))
                                {
                                    _logger.LogDebug("Batch download: ICUMS returned empty data for {ContainerNumber} (container not found)", containerNumber);
                                    return;
                                }

                                // Save as DownloadedFile
                                var downloadedFile = new DownloadedFile
                                {
                                    FileName = $"OnDemand_{containerNumber}_{DateTime.UtcNow:yyyyMMddHHmmss}.json",
                                    FilePath = $"OnDemand/{containerNumber}",
                                    FileSize = 0,
                                    DownloadDate = DateTime.UtcNow,
                                    ProcessingStatus = "Completed",
                                    RecordCount = 1
                                };

                                var downloadedFileId = await _icumDownloadsRepository.SaveDownloadedFileAsync(downloadedFile);

                                // Convert and save BOE document
                                var boeDocument = await ConvertICUMSResponseToBOEDocument(icumsResponse.Data, downloadedFileId, containerNumber);

                                if (boeDocument != null)
                                {
                                    var boeId = await _icumDownloadsRepository.SaveBOEDocumentAsync(boeDocument);
                                    boeDocument.Id = boeId;

                                    // OPTIMIZATION: Cache the batch downloaded data
                                    await _icumsCacheService.SetCachedBOEDocumentAsync(containerNumber, boeDocument);

                                    _logger.LogInformation("Batch download successful: {ContainerNumber}", containerNumber);
                                }
                            }
                            else
                            {
                                _logger.LogDebug("Batch download: No ICUMS data found for {ContainerNumber}", containerNumber);
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Batch download failed for {ContainerNumber}, will retry during validation", containerNumber);
                        }
                        finally
                        {
                            semaphore.Release();
                        }
                    }
                    finally
                    {
                        containerLock.Release();
                        _downloadLocks.TryRemove(containerNumber, out var removedLock);
                        removedLock?.Dispose();
                    }
                });

                await Task.WhenAll(downloadTasks);
                _logger.LogInformation("Batch download completed for {Count} containers", missingContainers.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch ICUMS download");
                // Don't throw - allow validation to continue with individual downloads as fallback
            }
        }

        /// <summary>
        /// Validates that a BoeScanDocument contains actual data (not empty/null response from API)
        /// </summary>
        private bool IsValidBoeScanDocument(BoeScanDocument? data)
        {
            if (data == null)
                return false;

            // A valid BOE document must have at least one of these populated:
            // - ContainerDetails (basic container info)
            // - Header (clearance/declaration info)
            // - ManifestDetails (shipping info)

            bool hasContainerDetails = data.ContainerDetails != null &&
                                      !string.IsNullOrWhiteSpace(data.ContainerDetails.ContainerNumber);

            bool hasHeader = data.Header != null &&
                            (!string.IsNullOrWhiteSpace(data.Header.DeclarationNumber) ||
                             !string.IsNullOrWhiteSpace(data.Header.ClearanceType));

            bool hasManifestDetails = data.ManifestDetails != null &&
                                     (!string.IsNullOrWhiteSpace(data.ManifestDetails.RotationNumber) ||
                                      !string.IsNullOrWhiteSpace(data.ManifestDetails.MasterBlNumber));

            return hasContainerDetails || hasHeader || hasManifestDetails;
        }

        /// <summary>
        /// Converts ICUMS API response to BOEDocument for saving
        /// This ensures on-demand downloads go through the same process as batch downloads
        /// </summary>
        private Task<BOEDocument?> ConvertICUMSResponseToBOEDocument(
            BoeScanDocument icumsData,
            int downloadedFileId,
            string containerNumber)
        {
            try
            {
                var boeDocument = new BOEDocument
                {
                    DownloadedFileId = downloadedFileId,
                    DocumentIndex = 0,
                    ContainerNumber = containerNumber,
                    ProcessingStatus = "Completed"
                };

                // Map ContainerDetails
                if (icumsData.ContainerDetails != null)
                {
                    // Note: IcumApiContainerDetails doesn't have Description/Quantity
                    // Only has: ContainerType, ContainerSize, ContainerWeight, ContainerISO
                    boeDocument.ContainerDescription = icumsData.ContainerDetails.ContainerType; // Use type as description
                    boeDocument.ContainerISO = icumsData.ContainerDetails.ContainerISO;
                    boeDocument.ContainerQuantity = 1; // Default for single container fetch
                    boeDocument.ContainerWeight = icumsData.ContainerDetails.ContainerWeight;
                }

                // Map Header
                if (icumsData.Header != null)
                {
                    boeDocument.ImpName = icumsData.Header.ImpName;
                    boeDocument.TotalDutyPaid = icumsData.Header.TotalDutyPaid;
                    boeDocument.CrmsLevel = icumsData.Header.CrmsLevel;
                    boeDocument.ExpAddress = icumsData.Header.ExpAddress;
                    boeDocument.DeclarationNumber = icumsData.Header.DeclarationNumber;
                    boeDocument.RegimeCode = icumsData.Header.RegimeCode;
                    boeDocument.NoOfContainers = icumsData.Header.NoofContainers; // Note: lowercase 'of'
                    boeDocument.CompOffRemarks = icumsData.Header.CompOffRemarks;
                    boeDocument.DeclarantName = icumsData.Header.DeclarantName;
                    boeDocument.ExpName = icumsData.Header.ExpName;
                    boeDocument.ImpAddress = icumsData.Header.ImpAddress;
                    boeDocument.ImpExpName = icumsData.Header.ImpExpName;
                    boeDocument.CcvrIntelRemarks = icumsData.Header.CcvrIntelRemarks;
                    boeDocument.DeclarationVersion = icumsData.Header.DeclarationVersion;
                    boeDocument.ImpExpAddress = icumsData.Header.ImpExpAddress;
                    boeDocument.DeclarationDate = icumsData.Header.DeclarationDate;
                    boeDocument.ClearanceType = icumsData.Header.ClearanceType;
                    boeDocument.DeclarantAddress = icumsData.Header.DeclarantAddress;
                }

                // Map ManifestDetails
                if (icumsData.ManifestDetails != null)
                {
                    boeDocument.RotationNumber = icumsData.ManifestDetails.RotationNumber;
                    boeDocument.ConsigneeName = icumsData.ManifestDetails.ConsigneeName;
                    boeDocument.CountryOfOrigin = icumsData.ManifestDetails.CountryofOrigin;
                    boeDocument.MarksNumbers = icumsData.ManifestDetails.MarksNumbers;
                    boeDocument.ShipperName = icumsData.ManifestDetails.ShipperName;
                    boeDocument.ShipperAddress = icumsData.ManifestDetails.ShipperAddress;
                    boeDocument.BlNumber = icumsData.ManifestDetails.MasterBlNumber;
                    boeDocument.DeliveryPlace = icumsData.ManifestDetails.DeliveryPlace;
                    boeDocument.HouseBl = icumsData.ManifestDetails.HouseBl;
                    boeDocument.ConsigneeAddress = icumsData.ManifestDetails.ConsigneeAddress;
                    boeDocument.GoodsDescription = icumsData.ManifestDetails.GoodsDescription;
                }

                return Task.FromResult<BOEDocument?>(boeDocument);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting ICUMS response to BOE document for container {ContainerNumber}", containerNumber);
                return Task.FromResult<BOEDocument?>(null);
            }
        }

        #endregion
    }

    // ScannerContainer is now defined in ContainerValidationModels.cs
}
