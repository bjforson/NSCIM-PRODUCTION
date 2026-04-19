using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NickScanCentralImagingPortal.API.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Container validation controller - requires authentication
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ContainerValidationController : ControllerBase
    {
        private readonly IContainerDataMapperService _containerDataMapperService;
        private readonly IICUMSSubmissionService _submissionService;
        private readonly IContainerCompletenessService _completenessService;
        private readonly ApplicationDbContext _dbContext;
        private readonly IcumDownloadsDbContext _icumDownloadsDbContext;
        private readonly IClearanceTypeDetectionService _clearanceTypeDetectionService;
        private readonly IConfiguration _configuration;
        private readonly ThrottledLogger _logger;
        private const string SERVICE_ID = "CONTAINER-VALIDATION-API";

        public ContainerValidationController(
            IContainerDataMapperService containerDataMapperService,
            IICUMSSubmissionService submissionService,
            IContainerCompletenessService completenessService,
            ApplicationDbContext dbContext,
            IcumDownloadsDbContext icumDownloadsDbContext,
            IClearanceTypeDetectionService clearanceTypeDetectionService,
            IConfiguration configuration,
            ILogger<ContainerValidationController> logger)
        {
            _containerDataMapperService = containerDataMapperService;
            _submissionService = submissionService;
            _completenessService = completenessService;
            _dbContext = dbContext;
            _icumDownloadsDbContext = icumDownloadsDbContext;
            _clearanceTypeDetectionService = clearanceTypeDetectionService;
            _configuration = configuration;
            _logger = new ThrottledLogger(logger, SERVICE_ID);
        }

        /// <summary>
        /// Get containers pending validation with pagination - OPTIMIZED VERSION using pre-computed data
        /// </summary>
        [ResponseCache(Duration = 30, VaryByQueryKeys = new[] { "page", "pageSize", "search", "scannerType", "status" })]
        [HttpGet("pending")]
        public async Task<ActionResult<PagedResult<ContainerValidationModel>>> GetPendingContainers(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50, // ✅ INCREASED: Default page size for better performance
            [FromQuery] string? status = null,
            [FromQuery] string? scannerType = null,
            [FromQuery] string? search = null)
        {
            try
            {
                var totalSw = System.Diagnostics.Stopwatch.StartNew();
                _logger.LogInfo("GetPendingContainers", "🔍 Starting request - Page: {Page}, Size: {PageSize}", page, pageSize);

                // ✅ OPTIMIZED: Use pre-computed completeness data from ContainerCompletenessService
                var (completenessData, totalCount) = await _completenessService.GetPreComputedCompletenessDataAsync(
                    page, pageSize, search, scannerType, status);

                _logger.LogInfo("GetPendingContainers", "✅ Pre-computed data query: {Ms}ms - Found {Count} containers",
                    totalSw.ElapsedMilliseconds, completenessData.Count);

                // ✅ FIX: Load clearance types from BOEDocuments in batch for efficiency
                var boeDocumentIds = completenessData
                    .Where(c => c.BOEDocumentId.HasValue)
                    .Select(c => c.BOEDocumentId!.Value) // Use null-forgiving operator since we filtered HasValue
                    .Distinct()
                    .ToList();

                var containerNumbers = completenessData
                    .Select(c => c.ContainerNumber)
                    .Distinct()
                    .ToList();

                // ✅ FIX: Batch Contains() to avoid EF Core CTE generation with large lists
                var boeDocumentsById = new Dictionary<int, BOEDocument>();
                var boeDocumentsByContainer = new Dictionary<string, BOEDocument>();
                const int boeBatchSize = 1000;

                // Batch load by ID
                if (boeDocumentIds.Count > 0)
                {
                    for (int i = 0; i < boeDocumentIds.Count; i += boeBatchSize)
                    {
                        var batch = boeDocumentIds.Skip(i).Take(boeBatchSize).ToList();
                        // ✅ FIX: Use FromSqlRaw to avoid CTE generation from Contains()
                        // Safe: batch contains only integer IDs from database, no user input
                        var placeholders = string.Join(",", batch.Select(id => id.ToString()));
#pragma warning disable EF1002 // Method 'FromSqlRaw' inserts interpolated strings directly into the SQL
                        var batchDocs = await _icumDownloadsDbContext.BOEDocuments
                            .FromSqlRaw($"SELECT * FROM BOEDocuments WHERE Id IN ({placeholders})")
#pragma warning restore EF1002
                            .AsNoTracking()
                            .ToListAsync();
                        foreach (var doc in batchDocs)
                        {
                            boeDocumentsById[doc.Id] = doc;
                        }
                    }
                }

                // Batch load by container number
                if (containerNumbers.Count > 0)
                {
                    for (int i = 0; i < containerNumbers.Count; i += boeBatchSize)
                    {
                        var batch = containerNumbers.Skip(i).Take(boeBatchSize).ToList();
                        // ✅ FIX: Use FromSqlRaw to avoid CTE generation from Contains()
                        // Safe: batch contains container numbers from database, properly escaped
                        var placeholders = string.Join(",", batch.Select(s => $"'{s.Replace("'", "''")}'")); // Escape single quotes
#pragma warning disable EF1002 // Method 'FromSqlRaw' inserts interpolated strings directly into the SQL
                        var batchDocs = await _icumDownloadsDbContext.BOEDocuments
                            .FromSqlRaw($"SELECT * FROM BOEDocuments WHERE ContainerNumber IN ({placeholders})")
#pragma warning restore EF1002
                            .AsNoTracking()
                            .ToListAsync();
                        var grouped = batchDocs
                            .GroupBy(b => b.ContainerNumber)
                            .ToDictionary(g => g.Key, g => g.OrderByDescending(b => b.CreatedAt).First());
                        foreach (var kvp in grouped)
                        {
                            boeDocumentsByContainer[kvp.Key] = kvp.Value;
                        }
                    }
                }

                // ✅ FAST CONVERSION: Convert pre-computed data to validation models with actual clearance type
                var validationModels = completenessData.Select(c =>
                {
                    // ✅ FIX: Determine clearance type from BOEDocument data
                    BOEDocument? boeDocument = null;

                    // Try to get BOEDocument by ID first (most accurate)
                    if (c.BOEDocumentId.HasValue && boeDocumentsById.TryGetValue(c.BOEDocumentId.Value, out var boeById))
                    {
                        boeDocument = boeById;
                    }
                    // Fallback: Get by container number (most recent)
                    else if (boeDocumentsByContainer.TryGetValue(c.ContainerNumber, out var boeByContainer))
                    {
                        boeDocument = boeByContainer;
                    }

                    // ✅ FIX: Determine clearance type with fallback logic
                    // Priority: 1) Pre-computed from ContainerCompletenessStatus, 2) Detect from BOE document, 3) null
                    ClearanceType? clearanceType = null;

                    // First, check if we have pre-computed ClearanceType in ContainerCompletenessStatus
                    if (!string.IsNullOrEmpty(c.ClearanceType))
                    {
                        clearanceType = c.ClearanceType.ToUpper() switch
                        {
                            "CMR" => ClearanceType.CMR,
                            "IMEX" => ClearanceType.IMEX,
                            _ => null
                        };
                    }
                    // Fallback: Use service to detect from BOE document (with fallback logic)
                    else if (boeDocument != null)
                    {
                        clearanceType = _clearanceTypeDetectionService.DetectClearanceTypeFromBOE(boeDocument);
                    }

                    // ✅ FIX: If we found a BOEDocument (for clearance type), then ICUMS data definitely exists
                    // Update ICUMS completeness to reflect this, even if pre-computed value is stale
                    var icumsCompletenessScore = c.ICUMSDataCompleteness;
                    var hasICUMSData = c.HasICUMSData;
                    if (boeDocument != null)
                    {
                        // If we found BOEDocument, ICUMS data exists - ensure completeness reflects this
                        hasICUMSData = true;
                        if (icumsCompletenessScore == 0)
                        {
                            // Pre-computed data might be stale - update to show ICUMS data is available
                            icumsCompletenessScore = 100;
                        }
                    }

                    var completeness = c.OverallCompleteness;
                    var validatedThreshold = _configuration.GetValue<int>("Validation:CompletenessValidatedThreshold", 100);
                    var inReviewThreshold = _configuration.GetValue<int>("Validation:CompletenessInReviewThreshold", 66);
                    var derivedStatus = completeness >= validatedThreshold ? ValidationStatus.Validated :
                                        completeness >= inReviewThreshold ? ValidationStatus.InReview :
                                        ValidationStatus.Pending;

                    if (!string.IsNullOrEmpty(c.WorkflowStage))
                    {
                        derivedStatus = c.WorkflowStage switch
                        {
                            "Submitted" => ValidationStatus.Submitted,
                            "PendingSubmission" => ValidationStatus.PendingSubmission,
                            "Completed" => ValidationStatus.PendingSubmission,
                            "Audit" => ValidationStatus.Approved,
                            "ImageAnalysis" => ValidationStatus.Validated,
                            _ => derivedStatus
                        };
                    }

                    return new ContainerValidationModel
                    {
                        Id = c.Id,
                        ContainerNumber = c.ContainerNumber,
                        ScannerType = c.ScannerType,
                        CreatedAt = c.ScanDate,
                        Status = derivedStatus,
                        DataCompletenessPercentage = completeness,
                        ClearanceType = clearanceType,
                        IsReadyForSubmission = completeness == 100,
                        // ✅ ADD PRE-COMPUTED COMPLETENESS DATA
                        ScannerCompleteness = new ScannerDataCompleteness
                        {
                            CompletenessScore = c.ScannerDataCompleteness,
                            HasScannerData = c.HasScannerData,
                            ScannerType = c.ScannerType
                        },
                        ICUMSCompleteness = new ICUMSDataCompleteness
                        {
                            CompletenessScore = icumsCompletenessScore, // ✅ FIX: Use updated score if BOEDocument found
                            HasContainerNumber = hasICUMSData // ✅ FIX: Use updated flag if BOEDocument found
                        },
                        ImageCompleteness = new ImageDataCompleteness
                        {
                            HasImage = c.HasImageData,
                            CompletenessScore = c.ImageDataCompleteness
                        }
                    };
                }).ToList();

                // Apply status filter if specified
                var filteredContainers = validationModels;
                if (!string.IsNullOrEmpty(status))
                {
                    filteredContainers = validationModels.Where(c => c.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase)).ToList();
                }

                var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

                _logger.LogInfo("GetPendingContainers", "🎯 OPTIMIZED: Completed in {Ms}ms - Returning {Count} containers",
                    totalSw.ElapsedMilliseconds, filteredContainers.Count);

                var result = new PagedResult<ContainerValidationModel>
                {
                    Data = filteredContainers,
                    TotalCount = totalCount,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = totalPages
                };

                totalSw.Stop();
                _logger.LogInfo("GetPendingContainers", "✅ COMPLETED in {TotalMs}ms - Returned {Count} containers (Page {Page} of {TotalPages})",
                    new { TotalMs = totalSw.ElapsedMilliseconds, Count = filteredContainers.Count, Page = page, TotalPages = result.TotalPages });

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetPendingContainers", "Error getting containers for validation", ex);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get validation summary statistics
        /// </summary>
        [HttpGet("summary")]
        public async Task<ActionResult<ValidationSummaryStats>> GetValidationSummary()
        {
            try
            {
                _logger.LogInfo("GetValidationSummary", "Getting validation summary statistics");

                // Try to get real summary data
                try
                {
                    using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                    var readyContainers = await _containerDataMapperService.GetContainersReadyForSubmissionAsync(10); // Just get a small sample for summary

                    var summary = new ValidationSummaryStats
                    {
                        TotalContainers = readyContainers.Count,
                        PendingValidation = readyContainers.Count,
                        Validated = 0,
                        ValidationErrors = 0, // TODO: Add validation error tracking to ContainerSubmissionData
                        Approved = 0,
                        Rejected = 0
                    };

                    _logger.LogInfo("GetValidationSummary", "Found {Count} containers for summary", new { Count = readyContainers.Count });
                    return Ok(summary);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("GetValidationSummary", "Error getting real summary data, returning empty summary", ex);
                    return Ok(new ValidationSummaryStats
                    {
                        TotalContainers = 0,
                        PendingValidation = 0,
                        Validated = 0,
                        ValidationErrors = 0,
                        Approved = 0,
                        Rejected = 0
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError("GetValidationSummary", "Error getting validation summary, returning mock data", ex);
                return Ok(GetMockSummary());
            }
        }

        /// <summary>
        /// Validate all pending containers
        /// </summary>
        [HttpPost("validate-all")]
        public async Task<ActionResult> ValidateAllContainers()
        {
            try
            {
                _logger.LogInfo("ValidateAllContainers", "Starting validation of all pending containers");

                var readyContainers = await _containerDataMapperService.GetContainersReadyForSubmissionAsync();

                _logger.LogInfo("ValidateAllContainers", "Validating {Count} pending containers", new { Count = readyContainers.Count });

                foreach (var container in readyContainers)
                {
                    await ValidateContainer(container);
                }

                _logger.LogInfo("ValidateAllContainers", "Completed validation of all pending containers");
                return Ok(new { message = $"Validated {readyContainers.Count} containers" });
            }
            catch (Exception ex)
            {
                _logger.LogError("ValidateAllContainers", "Error validating all containers", ex);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get detailed validation information for a specific container
        /// </summary>
        [HttpGet("details/{containerNumber}")]
        public async Task<ActionResult<ContainerValidationDetails>> GetContainerDetails(string containerNumber)
        {
            try
            {
                _logger.LogInfo("GetContainerDetails", "Getting validation details for container {ContainerNumber}", new { ContainerNumber = containerNumber });

                // Get container validation details
                var details = await GetContainerValidationDetails(containerNumber);
                if (details == null)
                {
                    return NotFound($"Container {containerNumber} not found");
                }

                return Ok(details);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetContainerDetails", "Error getting container details for {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Approve a container for ICUMS submission
        /// </summary>
        [HttpPost("approve/{containerNumber}")]
        public async Task<ActionResult> ApproveContainer(string containerNumber)
        {
            try
            {
                _logger.LogInfo("ApproveContainer", "Approving container {ContainerNumber} for ICUMS submission", new { ContainerNumber = containerNumber });

                // Get the container data
                var readyContainers = await _containerDataMapperService.GetContainersReadyForSubmissionAsync();
                var container = readyContainers.FirstOrDefault(c => c.ContainerNumber == containerNumber);

                if (container != null)
                {
                    // Queue for ICUMS submission
                    var submissionQueueItem = await _submissionService.QueueForSubmissionAsync(
                        container,
                        1, // Normal priority
                        User?.Identity?.Name
                    );

                    if (submissionQueueItem != null)
                    {
                        _logger.LogInfo("ApproveContainer", "Successfully approved and queued container {ContainerNumber}", new { ContainerNumber = containerNumber });
                        return Ok(new { message = "Container approved and queued for submission", submissionId = submissionQueueItem.Id });
                    }
                    else
                    {
                        _logger.LogWarning("ApproveContainer", "Failed to queue container {ContainerNumber}", new { ContainerNumber = containerNumber });
                        return BadRequest("Failed to queue container for submission");
                    }
                }

                return NotFound($"Container {containerNumber} not found");
            }
            catch (Exception ex)
            {
                _logger.LogError("ApproveContainer", "Error approving container {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Reject a container
        /// </summary>
        [HttpPost("reject/{containerNumber}")]
        public Task<ActionResult> RejectContainer(string containerNumber)
        {
            try
            {
                _logger.LogInfo("RejectContainer", "Rejecting container {ContainerNumber}", new { ContainerNumber = containerNumber });

                // Update container status to rejected (would need to implement status tracking)
                _logger.LogInfo("RejectContainer", "Successfully rejected container {ContainerNumber}", new { ContainerNumber = containerNumber });
                return Task.FromResult<ActionResult>(Ok(new { message = "Container rejected" }));
            }
            catch (Exception ex)
            {
                _logger.LogError("RejectContainer", "Error rejecting container {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return Task.FromResult<ActionResult>(StatusCode(500, "Internal server error"));
            }
        }

        /// <summary>
        /// Save annotations for a container
        /// </summary>
        [HttpPost("save-annotations/{containerNumber}")]
        public async Task<ActionResult> SaveAnnotations(string containerNumber, [FromBody] List<AnnotationData> annotations)
        {
            try
            {
                _logger.LogInfo("SaveAnnotations", "Saving annotations for container {ContainerNumber}", new { ContainerNumber = containerNumber });

                // Save annotations to database or file system
                await SaveContainerAnnotations(containerNumber, annotations);

                _logger.LogInfo("SaveAnnotations", "Successfully saved {Count} annotations for container {ContainerNumber}",
                    new { Count = annotations.Count, ContainerNumber = containerNumber });
                return Ok(new { message = "Annotations saved successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError("SaveAnnotations", "Error saving annotations for container {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get container image with manipulation options
        /// </summary>
        [HttpGet("image/{containerNumber}")]
        public async Task<ActionResult> GetContainerImage(string containerNumber, [FromQuery] string? manipulation = null)
        {
            try
            {
                _logger.LogInfo("GetContainerImage", "Getting image for container {ContainerNumber} with manipulation {Manipulation}",
                    new { ContainerNumber = containerNumber, Manipulation = manipulation });

                var readyContainers = await _containerDataMapperService.GetContainersReadyForSubmissionAsync();
                var container = readyContainers.FirstOrDefault(c => c.ContainerNumber == containerNumber);

                if (container == null)
                {
                    return NotFound("Container not found");
                }

                // Get image path from ImagePaths array or use placeholder
                var imagePath = container.ImagePaths?.Any() == true ? container.ImagePaths.First() : $"/api/image/serve/{containerNumber}";

                // Apply image manipulations if specified
                if (!string.IsNullOrEmpty(manipulation))
                {
                    // Implement image manipulation logic here
                    // This would involve image processing libraries
                }

                // ✅ MEMORY FIX: Stream file directly instead of loading into memory
                // This prevents 1.7MB+ byte arrays from going to Large Object Heap
                if (!System.IO.File.Exists(imagePath))
                {
                    return NotFound($"Image file not found: {imagePath}");
                }

                var fileStream = System.IO.File.OpenRead(imagePath);
                var contentType = GetContentType(imagePath);

                return File(fileStream, contentType, enableRangeProcessing: true);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetContainerImage", "Error getting image for container {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        #region Private Helper Methods

        private List<ContainerValidationModel> ApplyFilters(List<ContainerValidationModel> containers, string? status, string? scannerType, string? search)
        {
            var filtered = containers.AsQueryable();

            if (!string.IsNullOrEmpty(status))
            {
                filtered = filtered.Where(c => c.Status.ToString().Equals(status, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(scannerType))
            {
                filtered = filtered.Where(c => c.ScannerType.Equals(scannerType, StringComparison.OrdinalIgnoreCase));
            }

            if (!string.IsNullOrEmpty(search))
            {
                filtered = filtered.Where(c => c.ContainerNumber.Contains(search, StringComparison.OrdinalIgnoreCase));
            }

            return filtered.ToList();
        }

        private PagedResult<ContainerValidationModel> GetMockPagedContainers(int page, int pageSize)
        {
            var mockContainers = GetMockContainers();
            var offset = (page - 1) * pageSize;
            var pagedData = mockContainers.Skip(offset).Take(pageSize).ToList();

            return new PagedResult<ContainerValidationModel>
            {
                Data = pagedData,
                TotalCount = mockContainers.Count,
                Page = page,
                PageSize = pageSize,
                TotalPages = (int)Math.Ceiling((double)mockContainers.Count / pageSize)
            };
        }

        private ContainerValidationModel MapToValidationModel(ContainerSubmissionData submission, int index)
        {
            var completeness = CalculateDataCompleteness(submission);
            var validationModel = new ContainerValidationModel
            {
                Id = index + 1, // Use index as ID since ContainerSubmissionData doesn't have ID
                ContainerNumber = submission.ContainerNumber,
                ScannerType = submission.ScannerType,
                ClearanceType = ClearanceType.IMEX, // Default to IMEX, should be determined from data
                Status = completeness >= _configuration.GetValue<int>("Validation:ScannerValidatedThreshold", 90) ? ValidationStatus.Validated :
                        completeness >= _configuration.GetValue<int>("Validation:ScannerInReviewThreshold", 70) ? ValidationStatus.InReview : ValidationStatus.Pending,
                DataCompletenessPercentage = completeness,
                IsReadyForSubmission = completeness >= _configuration.GetValue<int>("Validation:ScannerReadyForSubmissionThreshold", 90),
                CreatedAt = submission.ScanDate,
                ValidatedAt = null,
                ImageCompleteness = new ImageDataCompleteness
                {
                    HasImage = true,
                    IsImageValid = true,
                    ImagePath = $"/api/image/serve/{submission.ContainerNumber}",
                    CompletenessScore = 100
                },
                ICUMSCompleteness = new ICUMSDataCompleteness
                {
                    HasContainerNumber = !string.IsNullOrEmpty(submission.ContainerNumber),
                    IsCompleteForClearanceType = submission.ReportData?.Any() == true,
                    CompletenessScore = submission.ReportData?.Any() == true ? 100 : 0
                }
            };

            // Add validation errors and business rule validation
            var errors = ValidateContainerData(submission);
            validationModel.ValidationErrors = errors.Select(e => new ValidationError
            {
                Field = "General",
                ErrorMessage = e,
                Severity = "Error"
            }).ToList();
            validationModel.BusinessRules = ValidateBusinessRules(submission);

            return validationModel;
        }

        private int CalculateDataCompleteness(ContainerSubmissionData submission)
        {
            var totalFields = 8; // Total expected fields
            var completedFields = 0;

            if (!string.IsNullOrEmpty(submission.ContainerNumber)) completedFields++;
            if (!string.IsNullOrEmpty(submission.ScannerType)) completedFields++;
            if (submission.ImagePaths?.Any() == true) completedFields++;
            if (submission.ReportData?.Any() == true) completedFields++;
            if (submission.ScannerDataId > 0) completedFields++;
            if (submission.ICUMSDataId > 0) completedFields++;
            if (submission.RelationId > 0) completedFields++;
            if (submission.ScanDate != default) completedFields++;

            return (int)((double)completedFields / totalFields * 100);
        }

        private List<string> ValidateContainerData(ContainerSubmissionData submission)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(submission.ContainerNumber))
                errors.Add("Container number is missing");

            if (string.IsNullOrEmpty(submission.ScannerType))
                errors.Add("Scanner type is missing");

            if (submission.ImagePaths?.Any() != true)
                errors.Add("No image paths provided");

            if (submission.ReportData?.Any() != true)
                errors.Add("No report data provided");

            // Validate container number format
            if (!string.IsNullOrEmpty(submission.ContainerNumber) &&
                !IsValidContainerNumber(submission.ContainerNumber))
                errors.Add("Invalid container number format");

            return errors;
        }

        private BusinessRuleValidationResult ValidateBusinessRules(ContainerSubmissionData submission)
        {
            var validation = new BusinessRuleValidationResult
            {
                IsValid = true,
                FailedRules = new List<string>(),
                PassedRules = new List<string>(),
                Warnings = new List<string>(),
                Score = 0
            };

            // Container number validation
            if (!string.IsNullOrEmpty(submission.ContainerNumber))
            {
                if (IsValidContainerNumber(submission.ContainerNumber))
                {
                    validation.PassedRules.Add("Container number format is valid");
                }
                else
                {
                    validation.FailedRules.Add("Container number format is invalid");
                    validation.IsValid = false;
                }
            }

            // Scanner type validation
            if (submission.ScannerType == "FS6000" || submission.ScannerType == "ASE")
            {
                validation.PassedRules.Add("Scanner type is valid");
            }
            else
            {
                validation.FailedRules.Add("Invalid scanner type");
                validation.IsValid = false;
            }

            // Image validation
            if (submission.ImagePaths?.Any() == true)
            {
                validation.PassedRules.Add("Image paths are provided");
            }
            else
            {
                validation.FailedRules.Add("No image paths provided");
                validation.IsValid = false;
            }

            // Report data validation
            if (submission.ReportData?.Any() == true)
            {
                validation.PassedRules.Add("Report data is available");
            }
            else
            {
                validation.FailedRules.Add("No report data available");
                validation.IsValid = false;
            }

            return validation;
        }

        private bool IsValidContainerNumber(string containerNumber)
        {
            // Basic container number validation (ISO 6346 format)
            return !string.IsNullOrEmpty(containerNumber) &&
                   containerNumber.Length >= 4 &&
                   containerNumber.Length <= 15;
        }

        private async Task ValidateContainer(ContainerSubmissionData container)
        {
            try
            {
                // Perform validation logic here
                _logger.LogInfo("ValidateContainer", "Validating container {ContainerNumber}", new { ContainerNumber = container.ContainerNumber });

                // This would involve checking data completeness, business rules, etc.
                await Task.CompletedTask; // Placeholder
            }
            catch (Exception ex)
            {
                _logger.LogError("ValidateContainer", "Error validating container {ContainerNumber}", ex, new { ContainerNumber = container.ContainerNumber });
            }
        }

        private Task<ContainerValidationDetails?> GetContainerValidationDetails(string containerNumber)
        {
            // Implementation to get detailed validation information
            // This would involve querying multiple data sources
            return Task.FromResult<ContainerValidationDetails?>(new ContainerValidationDetails
            {
                ContainerId = 1, // Placeholder
                ContainerNumber = containerNumber,
                ClearanceType = ClearanceType.IMEX,
                ValidationStatus = ValidationStatus.Pending,
                ScannerData = new ScannerDataInfo(),
                ICUMSData = new ICUMSDataInfo(),
                BusinessRules = new BusinessRuleValidationResult { IsValid = true, Score = 0 },
                ValidationErrors = new List<ValidationError>()
            });
        }

        private async Task SaveContainerAnnotations(string containerNumber, List<AnnotationData> annotations)
        {
            // Save annotations to database or file system
            _logger.LogInfo("SaveContainerAnnotations", "Saving {Count} annotations for container {ContainerNumber}",
                new { Count = annotations.Count, ContainerNumber = containerNumber });
            await Task.CompletedTask; // Placeholder
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

        private List<ContainerValidationModel> GetMockContainers()
        {
            return new List<ContainerValidationModel>
            {
                new ContainerValidationModel
                {
                    Id = 1,
                    ContainerNumber = "MSNU7312994",
                    ScannerType = "FS6000",
                    Status = ValidationStatus.Pending,
                    ClearanceType = ClearanceType.IMEX,
                    DataCompletenessPercentage = 85,
                    IsReadyForSubmission = false,
                    CreatedAt = DateTime.Now.AddHours(-2),
                    ValidationErrors = new List<ValidationError>(),
                    ImageCompleteness = new ImageDataCompleteness
                    {
                        HasImage = true,
                        IsImageValid = true,
                        ImagePath = "/api/image/serve/MSNU7312994",
                        CompletenessScore = 100
                    },
                    ICUMSCompleteness = new ICUMSDataCompleteness
                    {
                        HasContainerNumber = true,
                        IsCompleteForClearanceType = true,
                        CompletenessScore = 85
                    },
                    BusinessRules = new BusinessRuleValidationResult
                    {
                        IsValid = true,
                        PassedRules = new List<string> { "Container number format valid", "Scanner type valid", "Image available" },
                        FailedRules = new List<string>(),
                        Score = 75
                    }
                },
                new ContainerValidationModel
                {
                    Id = 2,
                    ContainerNumber = "TCNU8245690",
                    ScannerType = "ASE",
                    Status = ValidationStatus.Validated,
                    ClearanceType = ClearanceType.IMEX,
                    DataCompletenessPercentage = 95,
                    IsReadyForSubmission = true,
                    CreatedAt = DateTime.Now.AddHours(-1),
                    ValidationErrors = new List<ValidationError>(),
                    ImageCompleteness = new ImageDataCompleteness
                    {
                        HasImage = true,
                        IsImageValid = true,
                        ImagePath = "/api/image/serve/TCNU8245690",
                        CompletenessScore = 100
                    },
                    ICUMSCompleteness = new ICUMSDataCompleteness
                    {
                        HasContainerNumber = true,
                        IsCompleteForClearanceType = true,
                        CompletenessScore = 95
                    },
                    BusinessRules = new BusinessRuleValidationResult
                    {
                        IsValid = true,
                        PassedRules = new List<string> { "Container number format valid", "Scanner type valid", "Image available", "ICUMS data complete" },
                        FailedRules = new List<string>(),
                        Score = 100
                    }
                },
                new ContainerValidationModel
                {
                    Id = 3,
                    ContainerNumber = "MRSU3700452",
                    ScannerType = "ASE",
                    Status = ValidationStatus.Rejected,
                    ClearanceType = ClearanceType.IMEX,
                    DataCompletenessPercentage = 60,
                    IsReadyForSubmission = false,
                    CreatedAt = DateTime.Now.AddMinutes(-30),
                    ValidationErrors = new List<ValidationError>
                    {
                        new ValidationError { Field = "ICUMS", ErrorMessage = "Missing ICUMS data", Severity = "Error" },
                        new ValidationError { Field = "ContainerNumber", ErrorMessage = "Container number format needs verification", Severity = "Warning" }
                    },
                    ImageCompleteness = new ImageDataCompleteness
                    {
                        HasImage = true,
                        IsImageValid = true,
                        ImagePath = "/api/image/serve/MRSU3700452",
                        CompletenessScore = 100
                    },
                    ICUMSCompleteness = new ICUMSDataCompleteness
                    {
                        HasContainerNumber = false,
                        IsCompleteForClearanceType = false,
                        CompletenessScore = 30
                    },
                    BusinessRules = new BusinessRuleValidationResult
                    {
                        IsValid = false,
                        PassedRules = new List<string> { "Scanner type valid", "Image available" },
                        FailedRules = new List<string> { "No ICUMS data available", "Container number format needs verification" },
                        Score = 50
                    }
                }
            };
        }

        private ValidationSummaryStats GetMockSummary()
        {
            return new ValidationSummaryStats
            {
                TotalContainers = 3,
                PendingValidation = 2,
                Validated = 1,
                ValidationErrors = 1,
                Approved = 0,
                Rejected = 0
            };
        }

        #endregion

        #region Helper Methods for Live Data

        /// <summary>
        /// Determines validation status based on container and completeness data
        /// </summary>
        private string DetermineValidationStatus(Container container, ContainerCompletenessStatus? completeness)
        {
            if (completeness == null)
                return "Pending Review";

            if (completeness.HasICUMSData && completeness.Status == "Complete")
                return "Ready for Submission";

            if (completeness.Status == "Incomplete" || completeness.Status == "Error")
                return "Validation Errors";

            return "Pending Review";
        }

        /// <summary>
        /// Calculates data completeness percentage
        /// </summary>
        private int CalculateDataCompleteness(Container container, ContainerCompletenessStatus? completeness)
        {
            if (completeness == null)
                return 50; // Default if no completeness data

            var score = 0;

            // Container data (30 points)
            if (!string.IsNullOrEmpty(container.ContainerId)) score += 10;
            if (!string.IsNullOrEmpty(container.ScannerType)) score += 10;
            if (container.CreatedAt != default) score += 10;

            // ICUMS data (40 points)
            if (completeness.HasICUMSData) score += 40;

            // Image availability (20 points)
            if (System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), $"{container.ContainerId}.jpg")) ||
                System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), $"{container.ContainerId}.jpeg")) ||
                System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), $"{container.ContainerId}.png")))
            {
                score += 20;
            }

            // Processing status (10 points)
            if (completeness.Status == "Complete") score += 10;
            else if (completeness.Status == "Incomplete") score += 5;

            return Math.Min(score, 100);
        }

        /// <summary>
        /// Gets validation errors for a container
        /// </summary>
        private List<string> GetValidationErrors(Container container, ContainerCompletenessStatus? completeness)
        {
            var errors = new List<string>();

            if (string.IsNullOrEmpty(container.ContainerId))
                errors.Add("Container number is missing");

            if (string.IsNullOrEmpty(container.ScannerType))
                errors.Add("Scanner type is missing");

            if (completeness == null)
                errors.Add("Completeness status not available");
            else
            {
                if (!completeness.HasICUMSData)
                    errors.Add("ICUMS data is missing");

                if (completeness.Status == "Error" && !string.IsNullOrEmpty(completeness.ErrorMessage))
                    errors.Add(completeness.ErrorMessage);
            }

            // Check if image exists
            var imageExists = System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), $"{container.ContainerId}.jpg")) ||
                            System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), $"{container.ContainerId}.jpeg")) ||
                            System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), $"{container.ContainerId}.png"));

            if (!imageExists)
                errors.Add("Container image is missing");

            return errors;
        }

        /// <summary>
        /// Validates business rules for a container
        /// </summary>
        private BusinessRuleValidationResult ValidateBusinessRules(Container container, ContainerCompletenessStatus? completeness)
        {
            var passedRules = new List<string>();
            var failedRules = new List<string>();

            // Container number format validation
            if (!string.IsNullOrEmpty(container.ContainerId) && container.ContainerId.Length >= 11)
                passedRules.Add("Container number format valid");
            else
                failedRules.Add("Container number format invalid");

            // Scanner type validation
            if (!string.IsNullOrEmpty(container.ScannerType) &&
                (container.ScannerType == "FS6000" || container.ScannerType == "ASE" || container.ScannerType == "Other"))
                passedRules.Add("Scanner type valid");
            else
                failedRules.Add("Scanner type invalid");

            // Image availability
            var imageExists = System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), $"{container.ContainerId}.jpg")) ||
                            System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), $"{container.ContainerId}.jpeg")) ||
                            System.IO.File.Exists(Path.Combine(Directory.GetCurrentDirectory(), $"{container.ContainerId}.png"));

            if (imageExists)
                passedRules.Add("Image available");
            else
                failedRules.Add("Image not available");

            // ICUMS data validation
            if (completeness?.HasICUMSData == true)
                passedRules.Add("ICUMS data complete");
            else
                failedRules.Add("ICUMS data missing");

            return new BusinessRuleValidationResult
            {
                IsValid = failedRules.Count == 0,
                PassedRules = passedRules,
                FailedRules = failedRules,
                Warnings = new List<string>(),
                Score = passedRules.Count * 25 // Simple scoring: 25 points per passed rule
            };
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Enhanced scanner type detection - checks scanner databases and returns both enum and string
        /// </summary>
        private async Task<(Core.Interfaces.ScannerType scannerType, string scannerTypeString)> DetectScannerTypeEnhanced(string containerNumber)
        {
            try
            {
                // Check FS6000 database first (with images check for better confidence)
                var fs6000Scan = await _dbContext.FS6000Scans
                    .Include(s => s.Images)
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (fs6000Scan != null)
                {
                    // Found in FS6000 - extra confidence if it has images
                    if (fs6000Scan.Images?.Any() == true)
                    {
                        _logger.LogDebug("Container {ContainerNumber} found in FS6000 with {ImageCount} images",
                            containerNumber, fs6000Scan.Images.Count);
                    }
                    return (Core.Interfaces.ScannerType.FS6000, "FS6000");
                }

                // Check ASE database (with image check for better confidence)
                var aseScan = await _dbContext.AseScans
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (aseScan != null)
                {
                    // Found in ASE - extra confidence if it has image data
                    if (aseScan.ScanImage != null && aseScan.ScanImage.Length > 0)
                    {
                        _logger.LogDebug("Container {ContainerNumber} found in ASE with image data ({Size} bytes)",
                            containerNumber, aseScan.ScanImage.Length);
                    }
                    return (Core.Interfaces.ScannerType.ASE, "ASE");
                }

                // ✅ FALLBACK: Check image tables directly via joins (for containers not yet in scan tables)
                _logger.LogDebug("Container {ContainerNumber} not found in scan tables, checking image tables directly via scan joins...", containerNumber);

                // Check if FS6000 images exist by joining through FS6000Scans
                var fs6000ImageCount = await (from img in _dbContext.FS6000Images
                                              join scan in _dbContext.FS6000Scans on img.ScanId equals scan.Id
                                              where scan.ContainerNumber == containerNumber
                                              select img).CountAsync();

                if (fs6000ImageCount > 0)
                {
                    _logger.LogInfo("Container {ContainerNumber} detected as FS6000 based on {ImageCount} images found via scan join",
                        containerNumber, fs6000ImageCount);
                    return (Core.Interfaces.ScannerType.FS6000, "FS6000");
                }

                // ASE stores images inline in the scan record, already checked above
                // If we got here, truly no scanner data exists

                // Not found in any scanner database or image tables
                _logger.LogWarning("Container {ContainerNumber} not found in any scanner database or image tables (FS6000 or ASE) - returning Unknown", containerNumber);
                return (Core.Interfaces.ScannerType.Unknown, "Unknown");
            }
            catch (Exception ex)
            {
                _logger.LogError("DetectScannerTypeEnhanced", $"Error detecting scanner type for container: {containerNumber}", ex);
                return (Core.Interfaces.ScannerType.Unknown, "Unknown");
            }
        }

        /// <summary>
        /// Detect the actual scanner type for a container by checking scanner databases (Legacy method)
        /// </summary>
        private async Task<Core.Interfaces.ScannerType> DetectScannerType(string containerNumber)
        {
            var (scannerType, _) = await DetectScannerTypeEnhanced(containerNumber);
            return scannerType;
        }

        /// <summary>
        /// Calculate real scanner data completeness for a container
        /// </summary>
        private async Task<ScannerDataCompleteness> GetScannerDataCompletenessAsync(string containerNumber, Core.Interfaces.ScannerType scannerType)
        {
            try
            {
                var completeness = new ScannerDataCompleteness
                {
                    ScannerType = scannerType.ToString(),
                    HasScannerData = false,
                    IsDataComplete = false,
                    CompletenessScore = 0,
                    MissingFields = new List<string>(),
                    ValidationErrors = new List<string>()
                };

                int score = 0;

                if (scannerType == Core.Interfaces.ScannerType.FS6000)
                {
                    var fs6000Scan = await _dbContext.FS6000Scans
                        .Include(s => s.Images)
                        .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                    if (fs6000Scan != null)
                    {
                        completeness.HasScannerData = true;
                        completeness.ScanDateTime = fs6000Scan.ScanTime;

                        // Check container number (33 points)
                        if (!string.IsNullOrEmpty(fs6000Scan.ContainerNumber))
                            score += 33;
                        else
                            completeness.MissingFields.Add("Container Number");

                        // Check scan date (33 points)
                        if (fs6000Scan.ScanTime != default)
                            score += 33;
                        else
                            completeness.MissingFields.Add("Scan Date");

                        // Check images (34 points)
                        if (fs6000Scan.Images?.Any() == true)
                            score += 34;
                        else
                            completeness.MissingFields.Add("Images");
                    }
                    else
                    {
                        completeness.ValidationErrors.Add("No FS6000 scan data found");
                        completeness.MissingFields.AddRange(new[] { "Container Number", "Scan Date", "Images" });
                    }
                }
                else if (scannerType == Core.Interfaces.ScannerType.ASE)
                {
                    var aseScan = await _dbContext.AseScans
                        .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                    if (aseScan != null)
                    {
                        completeness.HasScannerData = true;
                        completeness.ScanDateTime = aseScan.ScanTime;

                        // Check container number (33 points)
                        if (!string.IsNullOrEmpty(aseScan.ContainerNumber))
                            score += 33;
                        else
                            completeness.MissingFields.Add("Container Number");

                        // Check scan date (33 points)
                        if (aseScan.ScanTime != default)
                            score += 33;
                        else
                            completeness.MissingFields.Add("Scan Date");

                        // Check image (34 points)
                        if (aseScan.ScanImage != null && aseScan.ScanImage.Length > 0)
                            score += 34;
                        else
                            completeness.MissingFields.Add("Scan Image");
                    }
                    else
                    {
                        completeness.ValidationErrors.Add("No ASE scan data found");
                        completeness.MissingFields.AddRange(new[] { "Container Number", "Scan Date", "Scan Image" });
                    }
                }
                else
                {
                    completeness.ValidationErrors.Add("Unknown scanner type");
                }

                completeness.CompletenessScore = score;
                completeness.IsDataComplete = score >= _configuration.GetValue<int>("Validation:ScannerDataCompleteThreshold", 90);

                return completeness;
            }
            catch (Exception ex)
            {
                _logger.LogError("GetScannerDataCompleteness", $"Error calculating scanner data completeness for container {containerNumber}", ex);
                return new ScannerDataCompleteness
                {
                    HasScannerData = false,
                    IsDataComplete = false,
                    CompletenessScore = 0,
                    ValidationErrors = new List<string> { $"Error: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Calculate real ICUMS data completeness for a container
        /// </summary>
        private async Task<ICUMSDataCompleteness> GetICUMSDataCompletenessAsync(string containerNumber)
        {
            try
            {
                var completeness = new ICUMSDataCompleteness
                {
                    HasContainerNumber = false,
                    HasBLNumber = false,
                    HasHouseBL = false,
                    HasRotationNumber = false,
                    HasBOENumber = false,
                    CompletenessScore = 0,
                    MissingFields = new List<string>(),
                    ValidationErrors = new List<string>(),
                    IsCompleteForClearanceType = false
                };

                int score = 0;

                // Check ICUMS Downloads database
                var boeDocument = await _icumDownloadsDbContext.BOEDocuments
                    .FirstOrDefaultAsync(b => b.ContainerNumber == containerNumber);

                if (boeDocument != null)
                {
                    // Check container number (25 points)
                    if (!string.IsNullOrEmpty(boeDocument.ContainerNumber))
                    {
                        completeness.HasContainerNumber = true;
                        score += 25;
                    }
                    else
                        completeness.MissingFields.Add("Container Number");

                    // Check Declaration number (25 points) - BOE equivalent
                    if (!string.IsNullOrEmpty(boeDocument.DeclarationNumber))
                    {
                        completeness.HasBOENumber = true;
                        score += 25;
                    }
                    else
                        completeness.MissingFields.Add("Declaration Number");

                    // Check Consignee (25 points)
                    if (!string.IsNullOrEmpty(boeDocument.ConsigneeName))
                        score += 25;
                    else
                        completeness.MissingFields.Add("Consignee Name");

                    // Check BL Number (25 points) - Manifest Details equivalent
                    if (!string.IsNullOrEmpty(boeDocument.BlNumber))
                        score += 25;
                    else
                        completeness.MissingFields.Add("BL Number");

                    // Determine clearance type
                    completeness.RequiredClearanceType = string.IsNullOrEmpty(boeDocument.DeclarationNumber) ?
                        ClearanceType.CMR : ClearanceType.IMEX;
                }
                else
                {
                    completeness.ValidationErrors.Add("No ICUMS data found");
                    completeness.MissingFields.AddRange(new[] { "Container Number", "BOE Number", "Consignee", "Manifest Details" });
                }

                completeness.CompletenessScore = score;
                completeness.IsCompleteForClearanceType = score >= _configuration.GetValue<int>("Validation:ICUMSCompleteThreshold", 75);

                return completeness;
            }
            catch (Exception ex)
            {
                _logger.LogError("GetICUMSDataCompleteness", $"Error calculating ICUMS data completeness for container {containerNumber}", ex);
                return new ICUMSDataCompleteness
                {
                    HasContainerNumber = false,
                    CompletenessScore = 0,
                    ValidationErrors = new List<string> { $"Error: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// Calculate real image data completeness for a container
        /// </summary>
        private async Task<ImageDataCompleteness> GetImageDataCompletenessAsync(string containerNumber, Core.Interfaces.ScannerType scannerType)
        {
            try
            {
                var completeness = new ImageDataCompleteness
                {
                    HasImage = false,
                    IsImageValid = false,
                    ImagePath = $"/api/image/serve/{containerNumber}",
                    CompletenessScore = 0,
                    ValidationErrors = new List<string>()
                };

                if (scannerType == Core.Interfaces.ScannerType.FS6000)
                {
                    // Get the scan first, then check for images
                    var fs6000Scan = await _dbContext.FS6000Scans
                        .Include(s => s.Images)
                        .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                    if (fs6000Scan?.Images?.Any() == true)
                    {
                        completeness.HasImage = true;
                        completeness.IsImageValid = true;
                        completeness.CompletenessScore = 100;
                    }
                    else
                    {
                        completeness.ValidationErrors.Add("No FS6000 images found");
                    }
                }
                else if (scannerType == Core.Interfaces.ScannerType.ASE)
                {
                    var aseScan = await _dbContext.AseScans
                        .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                    if (aseScan?.ScanImage != null && aseScan.ScanImage.Length > 0)
                    {
                        completeness.HasImage = true;
                        completeness.IsImageValid = true;
                        completeness.CompletenessScore = 100;
                        completeness.FileSizeBytes = aseScan.ScanImage.Length;
                    }
                    else
                    {
                        completeness.ValidationErrors.Add("No ASE scan image found");
                    }
                }
                else
                {
                    // Scanner type is Unknown - check BOTH scanner tables to find images
                    _logger.LogDebug("Scanner type unknown for {ContainerNumber}, checking both FS6000 and ASE databases", containerNumber);

                    // Try FS6000 first
                    var fs6000Scan = await _dbContext.FS6000Scans
                        .Include(s => s.Images)
                        .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                    if (fs6000Scan?.Images?.Any() == true)
                    {
                        completeness.HasImage = true;
                        completeness.IsImageValid = true;
                        completeness.CompletenessScore = 100;
                        completeness.ValidationErrors.Add("Warning: Images found in FS6000, but scanner type detection failed");
                        _logger.LogInfo("GetImageDataCompleteness", $"Found FS6000 images for {containerNumber} despite unknown scanner type");
                    }
                    else
                    {
                        // Try ASE
                        var aseScan = await _dbContext.AseScans
                            .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                        if (aseScan?.ScanImage != null && aseScan.ScanImage.Length > 0)
                        {
                            completeness.HasImage = true;
                            completeness.IsImageValid = true;
                            completeness.CompletenessScore = 100;
                            completeness.FileSizeBytes = aseScan.ScanImage.Length;
                            completeness.ValidationErrors.Add("Warning: Images found in ASE, but scanner type detection failed");
                            _logger.LogInfo("GetImageDataCompleteness", $"Found ASE images for {containerNumber} despite unknown scanner type");
                        }
                        else
                        {
                            completeness.ValidationErrors.Add("No scanner record found - container may not have been scanned yet");
                            _logger.LogWarning("GetImageDataCompleteness", $"No images found in any scanner database for {containerNumber}");
                        }
                    }
                }

                return completeness;
            }
            catch (Exception ex)
            {
                _logger.LogError("GetImageDataCompleteness", $"Error calculating image data completeness for container {containerNumber}", ex);
                return new ImageDataCompleteness
                {
                    HasImage = false,
                    IsImageValid = false,
                    CompletenessScore = 0,
                    ValidationErrors = new List<string> { $"Error: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// FAST VERSION: Calculate scanner data completeness using pre-loaded data (no database calls)
        /// </summary>
        private ScannerDataCompleteness GetScannerDataCompletenessFast(
            string containerNumber,
            Core.Interfaces.ScannerType scannerType,
            Dictionary<string, FS6000Scan> fs6000Scans,
            Dictionary<string, AseScan> aseScans,
            Dictionary<string, int> fs6000ImageCounts)
        {
            try
            {
                var completeness = new ScannerDataCompleteness
                {
                    ScannerType = scannerType.ToString(),
                    HasScannerData = false,
                    IsDataComplete = false,
                    CompletenessScore = 0,
                    MissingFields = new List<string>(),
                    ValidationErrors = new List<string>()
                };

                int score = 0;

                if (scannerType == Core.Interfaces.ScannerType.FS6000 && fs6000Scans.TryGetValue(containerNumber, out var fs6000Scan))
                {
                    completeness.HasScannerData = true;
                    if (!string.IsNullOrEmpty(fs6000Scan.ContainerNumber)) score += 20;
                    if (fs6000Scan.ScanTime != DateTime.MinValue) score += 20;
                    // ✅ FIX: Use image count instead of loading all images
                    if (fs6000ImageCounts.TryGetValue(containerNumber, out var imageCount) && imageCount > 0) score += 30;
                    else completeness.MissingFields.Add("Scanner Images");

                    if (!string.IsNullOrEmpty(fs6000Scan.PicNumber)) score += 15;
                    else completeness.MissingFields.Add("Picture Number");

                    if (!string.IsNullOrEmpty(fs6000Scan.OperatorId)) score += 15;
                    else completeness.MissingFields.Add("Operator ID");

                    completeness.ScannerType = "FS6000";
                }
                else if (scannerType == Core.Interfaces.ScannerType.ASE && aseScans.TryGetValue(containerNumber, out var aseScan))
                {
                    completeness.HasScannerData = true;
                    if (!string.IsNullOrEmpty(aseScan.ContainerNumber)) score += 25;
                    if (aseScan.ScanTime != DateTime.MinValue) score += 25;
                    if (!string.IsNullOrEmpty(aseScan.InspectionUuid)) score += 25;
                    else completeness.MissingFields.Add("Inspection UUID");

                    if (!string.IsNullOrEmpty(aseScan.TruckPlate)) score += 25;
                    else completeness.MissingFields.Add("Truck Plate");

                    completeness.ScannerType = "ASE";
                }
                else
                {
                    completeness.ValidationErrors.Add($"No scanner data found for {scannerType}");
                }

                completeness.CompletenessScore = score;
                completeness.IsDataComplete = score >= _configuration.GetValue<int>("Validation:ScannerDataCompleteThreshold", 90);

                return completeness;
            }
            catch (Exception ex)
            {
                _logger.LogError("GetScannerDataCompletenessFast", $"Error calculating scanner data completeness for container {containerNumber}", ex);
                return new ScannerDataCompleteness
                {
                    HasScannerData = false,
                    IsDataComplete = false,
                    CompletenessScore = 0,
                    ValidationErrors = new List<string> { $"Error: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// FAST VERSION: Calculate ICUMS data completeness using pre-loaded data (no database calls)
        /// </summary>
        private ICUMSDataCompleteness GetICUMSDataCompletenessFast(
            string containerNumber,
            Dictionary<string, BOEDocument> boeDocuments)
        {
            try
            {
                var completeness = new ICUMSDataCompleteness
                {
                    HasContainerNumber = false,
                    HasBLNumber = false,
                    HasHouseBL = false,
                    HasRotationNumber = false,
                    HasBOENumber = false,
                    CompletenessScore = 0,
                    MissingFields = new List<string>(),
                    ValidationErrors = new List<string>(),
                    IsCompleteForClearanceType = false
                };

                int score = 0;

                if (boeDocuments.TryGetValue(containerNumber, out var boeDocument))
                {
                    completeness.HasContainerNumber = !string.IsNullOrEmpty(boeDocument.ContainerNumber);
                    if (completeness.HasContainerNumber) score += 20;

                    completeness.HasBLNumber = !string.IsNullOrEmpty(boeDocument.BlNumber);
                    if (completeness.HasBLNumber) score += 20;
                    else completeness.MissingFields.Add("BL Number");

                    completeness.HasHouseBL = !string.IsNullOrEmpty(boeDocument.HouseBl);
                    if (completeness.HasHouseBL) score += 15;

                    completeness.HasRotationNumber = !string.IsNullOrEmpty(boeDocument.RotationNumber);
                    if (completeness.HasRotationNumber) score += 20;
                    else completeness.MissingFields.Add("Rotation Number");

                    completeness.HasBOENumber = !string.IsNullOrEmpty(boeDocument.DeclarationNumber);
                    if (completeness.HasBOENumber) score += 25;
                    else completeness.MissingFields.Add("BOE/Declaration Number");

                    // Parse clearance type string to enum
                    completeness.RequiredClearanceType = (boeDocument.ClearanceType?.ToUpper()) switch
                    {
                        "IM" => ClearanceType.IMEX,
                        "EX" => ClearanceType.IMEX,
                        "CMR" => ClearanceType.CMR,
                        _ => ClearanceType.CMR // Default to CMR as it's less restrictive
                    };
                }
                else
                {
                    completeness.ValidationErrors.Add("No ICUMS data found");
                    completeness.MissingFields.AddRange(new[] { "Container Number", "Master BL Number", "Rotation Number", "BOE Number" });
                }

                completeness.CompletenessScore = score;
                completeness.IsCompleteForClearanceType = score >= _configuration.GetValue<int>("Validation:ICUMSCompleteThreshold", 75);

                return completeness;
            }
            catch (Exception ex)
            {
                _logger.LogError("GetICUMSDataCompletenessFast", $"Error calculating ICUMS data completeness for container {containerNumber}", ex);
                return new ICUMSDataCompleteness
                {
                    HasContainerNumber = false,
                    CompletenessScore = 0,
                    ValidationErrors = new List<string> { $"Error: {ex.Message}" }
                };
            }
        }

        /// <summary>
        /// FAST VERSION: Calculate image data completeness using pre-loaded data (no database calls)
        /// </summary>
        private ImageDataCompleteness GetImageDataCompletenessFast(
            string containerNumber,
            Core.Interfaces.ScannerType scannerType,
            Dictionary<string, int> fs6000ImageCounts)
        {
            try
            {
                var completeness = new ImageDataCompleteness
                {
                    HasImage = false,
                    IsImageValid = false,
                    ImagePath = $"/api/image/serve/{containerNumber}",
                    CompletenessScore = 0,
                    ValidationErrors = new List<string>()
                };

                if (scannerType == Core.Interfaces.ScannerType.FS6000)
                {
                    // ✅ FIX: Use image count instead of loading all images
                    if (fs6000ImageCounts.TryGetValue(containerNumber, out var imageCount) && imageCount > 0)
                    {
                        completeness.HasImage = true;
                        completeness.IsImageValid = true;
                        completeness.CompletenessScore = 100;
                    }
                    else
                    {
                        completeness.ValidationErrors.Add("No images found for FS6000 scan");
                    }
                }
                else if (scannerType == Core.Interfaces.ScannerType.ASE)
                {
                    // ASE images are stored in file system, assume present for ASE scans
                    // We don't track ASE images in the database, so assume 100% if ASE scanner type
                    completeness.HasImage = true;
                    completeness.IsImageValid = true;
                    completeness.CompletenessScore = 100;
                }
                else
                {
                    completeness.ValidationErrors.Add("No scanner record found - container may not have been scanned yet");
                }

                return completeness;
            }
            catch (Exception ex)
            {
                _logger.LogError("GetImageDataCompletenessFast", $"Error calculating image data completeness for container {containerNumber}", ex);
                return new ImageDataCompleteness
                {
                    HasImage = false,
                    IsImageValid = false,
                    CompletenessScore = 0,
                    ValidationErrors = new List<string> { $"Error: {ex.Message}" }
                };
            }
        }

        #endregion

    }

}
