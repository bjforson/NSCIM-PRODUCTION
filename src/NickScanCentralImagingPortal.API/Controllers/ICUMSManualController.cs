using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.IcumApi;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize(Policy = "CustomsOfficer")]
    [ApiController]
    [Route("api/[controller]")]
    public class ICUMSManualController : ControllerBase
    {
        private readonly ILogger<ICUMSManualController> _logger;
        private readonly IICUMSDownloadQueueService _downloadQueueService;
        private readonly IIcumApiService _icumApiService;
        private readonly IIcumDownloadsRepository _icumDownloadsRepository;

        public ICUMSManualController(
            ILogger<ICUMSManualController> logger,
            IICUMSDownloadQueueService downloadQueueService,
            IIcumApiService icumApiService,
            IIcumDownloadsRepository icumDownloadsRepository)
        {
            _logger = logger;
            _downloadQueueService = downloadQueueService;
            _icumApiService = icumApiService;
            _icumDownloadsRepository = icumDownloadsRepository;
        }

        [HttpPost("trigger-download/{containerNumber}")]
        public async Task<ActionResult<ManualDownloadResponse>> TriggerManualDownload(string containerNumber, [FromQuery] string? requestedBy = null)
        {
            try
            {
                _logger.LogInformation("[ICUMS-MANUAL] Manual download requested for container: {ContainerNumber} by {RequestedBy}",
                    containerNumber, requestedBy ?? "Unknown");

                var success = await _downloadQueueService.EnqueueContainerAsync(
                    containerNumber,
                    priority: 10, // High priority for manual requests
                    requestSource: "Manual UI Request",
                    requestedBy: requestedBy
                );

                if (success)
                {
                    return Ok(new ManualDownloadResponse
                    {
                        Success = true,
                        Message = $"Container {containerNumber} added to download queue with high priority",
                        ContainerNumber = containerNumber,
                        QueuedAt = DateTime.UtcNow
                    });
                }
                else
                {
                    return Ok(new ManualDownloadResponse
                    {
                        Success = false,
                        Message = $"Container {containerNumber} is already in the download queue or recently downloaded",
                        ContainerNumber = containerNumber
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-MANUAL] Error triggering manual download for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, new ManualDownloadResponse
                {
                    Success = false,
                    Message = $"Error: {ex.Message}",
                    ContainerNumber = containerNumber
                });
            }
        }

        [HttpPost("trigger-bulk-download")]
        public async Task<ActionResult<BulkDownloadResponse>> TriggerBulkDownload([FromBody] BulkDownloadRequest request)
        {
            try
            {
                _logger.LogInformation("[ICUMS-MANUAL] Bulk download requested for {Count} containers by {RequestedBy}",
                    request.ContainerNumbers.Count, request.RequestedBy ?? "Unknown");

                var queued = await _downloadQueueService.EnqueueContainersAsync(
                    request.ContainerNumbers,
                    priority: 10,
                    requestSource: "Manual Bulk UI Request"
                );

                return Ok(new BulkDownloadResponse
                {
                    Success = true,
                    Message = $"Successfully queued {queued} of {request.ContainerNumbers.Count} containers",
                    TotalRequested = request.ContainerNumbers.Count,
                    TotalQueued = queued,
                    QueuedAt = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-MANUAL] Error triggering bulk download");
                return StatusCode(500, new BulkDownloadResponse
                {
                    Success = false,
                    Message = $"Error: {ex.Message}",
                    TotalRequested = request.ContainerNumbers.Count,
                    TotalQueued = 0
                });
            }
        }

        [HttpGet("queue-status/{containerNumber}")]
        public Task<ActionResult<QueueStatusResponse>> GetQueueStatus(string containerNumber)
        {
            try
            {
                // This would check the actual queue status
                // For now, return a placeholder
                return Task.FromResult<ActionResult<QueueStatusResponse>>(Ok(new QueueStatusResponse
                {
                    ContainerNumber = containerNumber,
                    IsInQueue = false,
                    Position = null,
                    EstimatedProcessTime = null,
                    Status = "Unknown"
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-MANUAL] Error checking queue status for container: {ContainerNumber}", containerNumber);
                return Task.FromResult<ActionResult<QueueStatusResponse>>(StatusCode(500, new { error = ex.Message }));
            }
        }

        /// <summary>
        /// Directly download and process ICUMS data for a container (bypasses queue)
        /// </summary>
        [HttpPost("direct-download/{containerNumber}")]
        public async Task<ActionResult<DirectDownloadResponse>> DirectDownload(string containerNumber)
        {
            try
            {
                _logger.LogInformation("[ICUMS-MANUAL] Direct download requested for container: {ContainerNumber}", containerNumber);

                // Step 1: Check if container already has data
                var hasData = await _icumDownloadsRepository.ContainerHasICUMSDataAsync(containerNumber);
                if (hasData)
                {
                    return Ok(new DirectDownloadResponse
                    {
                        Success = true,
                        Message = $"Container {containerNumber} already has ICUMS data",
                        ContainerNumber = containerNumber,
                        DownloadedAt = DateTime.UtcNow,
                        AlreadyExists = true
                    });
                }

                // Step 2: Fetch from ICUMS API
                var icumsResponse = await _icumApiService.FetchContainerDataAsync(containerNumber);

                if (icumsResponse.Status != "Success" || icumsResponse.Data == null)
                {
                    var errorMsg = icumsResponse.Error?.ErrorMsg ?? icumsResponse.StatusMsg ?? "Unknown error";
                    _logger.LogWarning("[ICUMS-MANUAL] Failed to fetch ICUMS data for container {ContainerNumber}: {ErrorMessage}",
                        containerNumber, errorMsg);
                    return Ok(new DirectDownloadResponse
                    {
                        Success = false,
                        Message = $"Failed to fetch ICUMS data: {errorMsg}",
                        ContainerNumber = containerNumber,
                        ErrorMessage = errorMsg
                    });
                }

                // Step 3: Validate data
                if (!IsValidBoeScanDocument(icumsResponse.Data))
                {
                    _logger.LogWarning("[ICUMS-MANUAL] ICUMS API returned empty data for container {ContainerNumber}", containerNumber);
                    return Ok(new DirectDownloadResponse
                    {
                        Success = false,
                        Message = "Container not found in ICUMS (empty response)",
                        ContainerNumber = containerNumber,
                        ErrorMessage = "NO_DATA"
                    });
                }

                // Step 4: Save as DownloadedFile
                var downloadedFile = new DownloadedFile
                {
                    FileName = $"DirectDownload_{containerNumber}_{DateTime.UtcNow:yyyyMMddHHmmss}.json",
                    FilePath = $"DirectDownload/{containerNumber}",
                    FileSize = 0,
                    DownloadDate = DateTime.UtcNow,
                    ProcessingStatus = "Completed",
                    RecordCount = 1
                };

                var downloadedFileId = await _icumDownloadsRepository.SaveDownloadedFileAsync(downloadedFile);

                // Step 5: Convert and save BOE document
                var boeDocument = ConvertICUMSResponseToBOEDocument(icumsResponse.Data, downloadedFileId, containerNumber);

                if (boeDocument == null)
                {
                    return Ok(new DirectDownloadResponse
                    {
                        Success = false,
                        Message = "Failed to convert ICUMS response to BOE document",
                        ContainerNumber = containerNumber,
                        ErrorMessage = "CONVERSION_ERROR"
                    });
                }

                var boeId = await _icumDownloadsRepository.SaveBOEDocumentAsync(boeDocument);

                _logger.LogInformation("[ICUMS-MANUAL] Successfully downloaded and saved ICUMS data for container {ContainerNumber} (BOE ID: {BoeId})",
                    containerNumber, boeId);

                return Ok(new DirectDownloadResponse
                {
                    Success = true,
                    Message = $"Successfully downloaded and processed ICUMS data for container {containerNumber}",
                    ContainerNumber = containerNumber,
                    DownloadedAt = DateTime.UtcNow,
                    BOEDocumentId = boeId
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-MANUAL] Error during direct download for container: {ContainerNumber}", containerNumber);
                return StatusCode(500, new DirectDownloadResponse
                {
                    Success = false,
                    Message = $"Error: {ex.Message}",
                    ContainerNumber = containerNumber,
                    ErrorMessage = ex.GetType().Name
                });
            }
        }

        private bool IsValidBoeScanDocument(BoeScanDocument? data)
        {
            if (data == null) return false;
            // Check if it has meaningful data - at least container number or declaration number
            return !string.IsNullOrEmpty(data.ContainerDetails?.ContainerNumber) ||
                   !string.IsNullOrEmpty(data.Header?.DeclarationNumber);
        }

        private BOEDocument? ConvertICUMSResponseToBOEDocument(BoeScanDocument icumsData, int downloadedFileId, string containerNumber)
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
                    boeDocument.ContainerDescription = icumsData.ContainerDetails.ContainerType;
                    boeDocument.ContainerISO = icumsData.ContainerDetails.ContainerISO;
                    boeDocument.ContainerQuantity = 1;
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
                    boeDocument.NoOfContainers = icumsData.Header.NoofContainers;
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

                return boeDocument;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-MANUAL] Error converting ICUMS response to BOE document for container {ContainerNumber}", containerNumber);
                return null;
            }
        }
    }

    public class ManualDownloadResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime? QueuedAt { get; set; }
    }

    public class BulkDownloadRequest
    {
        public List<string> ContainerNumbers { get; set; } = new();
        public string? RequestedBy { get; set; }
    }

    public class BulkDownloadResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public int TotalRequested { get; set; }
        public int TotalQueued { get; set; }
        public DateTime? QueuedAt { get; set; }
    }

    public class QueueStatusResponse
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public bool IsInQueue { get; set; }
        public int? Position { get; set; }
        public DateTime? EstimatedProcessTime { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class DirectDownloadResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime? DownloadedAt { get; set; }
        public bool AlreadyExists { get; set; }
        public int? BOEDocumentId { get; set; }
        public string? ErrorMessage { get; set; }
    }
}

