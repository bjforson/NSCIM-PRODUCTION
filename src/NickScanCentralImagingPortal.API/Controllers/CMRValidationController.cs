using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API controller for CMR validation and re-download operations
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CMRValidationController : ControllerBase
    {
        private readonly ICMRValidationService _validationService;
        private readonly ICMRRedownloadService _redownloadService;
        private readonly ILogger<CMRValidationController> _logger;

        public CMRValidationController(
            ICMRValidationService validationService,
            ICMRRedownloadService redownloadService,
            ILogger<CMRValidationController> logger)
        {
            _validationService = validationService;
            _redownloadService = redownloadService;
            _logger = logger;
        }

        /// <summary>
        /// Get CMR validation statistics
        /// </summary>
        [ResponseCache(Duration = 120)]
        [HttpGet("statistics")]
        public async Task<ActionResult<CMRValidationStatistics>> GetStatistics()
        {
            try
            {
                _logger.LogInformation("Fetching CMR validation statistics");
                var statistics = await _validationService.GetCMRValidationStatisticsAsync();
                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching CMR validation statistics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get problematic CMR records that need attention
        /// </summary>
        [HttpGet("problematic-records")]
        public async Task<ActionResult<List<ProblematicCMRRecord>>> GetProblematicRecords()
        {
            try
            {
                _logger.LogInformation("Fetching problematic CMR records");
                var records = await _validationService.GetProblematicCMRRecordsAsync();
                return Ok(records);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching problematic CMR records");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Validate a specific CMR record
        /// </summary>
        [HttpPost("validate-record")]
        public async Task<ActionResult<CMRValidationResult>> ValidateRecord([FromBody] ValidateCMRRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ContainerNumber))
                {
                    return BadRequest("Container number is required");
                }

                _logger.LogInformation("Validating CMR record for container {Container}", request.ContainerNumber);

                // Get the BOE document from database
                var boeDocument = await GetBOEDocumentAsync(request.ContainerNumber);
                if (boeDocument == null)
                {
                    return NotFound($"Container {request.ContainerNumber} not found");
                }

                var result = await _validationService.ValidateCMRRecordAsync(boeDocument);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating CMR record for container {Container}", request.ContainerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Queue a container for re-download
        /// </summary>
        [HttpPost("queue-redownload")]
        public async Task<ActionResult<CMRRedownloadResult>> QueueRedownload([FromBody] QueueRedownloadRequest request)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(request.ContainerNumber))
                {
                    return BadRequest("Container number is required");
                }

                _logger.LogInformation("Queuing container {Container} for re-download. Reason: {Reason}",
                    request.ContainerNumber, request.Reason);

                var result = await _redownloadService.QueueForRedownloadAsync(request.ContainerNumber, request.Reason);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing container {Container} for re-download", request.ContainerNumber);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Queue multiple containers for re-download
        /// </summary>
        [HttpPost("queue-batch-redownload")]
        public async Task<ActionResult<CMRBatchRedownloadResult>> QueueBatchRedownload([FromBody] QueueBatchRedownloadRequest request)
        {
            try
            {
                if (request.ContainerNumbers == null || !request.ContainerNumbers.Any())
                {
                    return BadRequest("Container numbers are required");
                }

                _logger.LogInformation("Queuing {Count} containers for batch re-download. Reason: {Reason}",
                    request.ContainerNumbers.Count, request.Reason);

                var result = await _redownloadService.QueueBatchForRedownloadAsync(request.ContainerNumbers, request.Reason);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing batch re-download for {Count} containers", request.ContainerNumbers?.Count ?? 0);
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get re-download queue status
        /// </summary>
        [HttpGet("queue-status")]
        public async Task<ActionResult<CMRRedownloadQueueStatus>> GetQueueStatus()
        {
            try
            {
                _logger.LogInformation("Fetching CMR re-download queue status");
                var status = await _redownloadService.GetQueueStatusAsync();
                return Ok(status);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching queue status");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get re-download queue statistics
        /// </summary>
        [HttpGet("queue-statistics")]
        public async Task<ActionResult<CMRRedownloadStatistics>> GetQueueStatistics()
        {
            try
            {
                _logger.LogInformation("Fetching CMR re-download queue statistics");
                var statistics = await _redownloadService.GetQueueStatisticsAsync();
                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching queue statistics");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Clear completed items from the queue
        /// </summary>
        [HttpPost("clear-completed")]
        public async Task<ActionResult<int>> ClearCompletedItems()
        {
            try
            {
                _logger.LogInformation("Clearing completed items from CMR re-download queue");
                var clearedCount = await _redownloadService.ClearCompletedItemsAsync();
                return Ok(new { ClearedCount = clearedCount });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing completed queue items");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Process the re-download queue manually
        /// </summary>
        [HttpPost("process-queue")]
        public async Task<ActionResult<CMRQueueProcessingResult>> ProcessQueue()
        {
            try
            {
                _logger.LogInformation("Manually processing CMR re-download queue");
                var result = await _redownloadService.ProcessRedownloadQueueAsync();
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing re-download queue");
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get historical metrics for charts
        /// </summary>
        [HttpGet("metrics/historical")]
        public Task<ActionResult<List<CMRValidationMetrics>>> GetHistoricalMetrics([FromQuery] int days = 30)
        {
            try
            {
                _logger.LogInformation("Fetching historical CMR validation metrics for {Days} days", days);

                var cutoffDate = DateTime.UtcNow.AddDays(-days);

                // This would be better in a repository, but for now we'll inject the context
                // TODO: Move to repository pattern
                // var metrics = await _context.CMRValidationMetrics
                //     .Where(m => m.RecordedAt >= cutoffDate)
                //     .OrderBy(m => m.RecordedAt)
                //     .ToListAsync();

                // For now, return empty list with log
                _logger.LogWarning("Historical metrics endpoint not fully implemented - requires DbContext injection");
                return Task.FromResult<ActionResult<List<CMRValidationMetrics>>>(Ok(new List<CMRValidationMetrics>()));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching historical metrics");
                return Task.FromResult<ActionResult<List<CMRValidationMetrics>>>(StatusCode(500, "Internal server error"));
            }
        }

        private Task<BOEDocument?> GetBOEDocumentAsync(string containerNumber)
        {
            // This would typically be injected as a service
            // For now, we'll return null and let the calling method handle it
            return Task.FromResult<BOEDocument?>(null);
        }
    }

    public class CMRValidationMetrics
    {
        public int Id { get; set; }
        public DateTime RecordedAt { get; set; }
        public int TotalCMRRecords { get; set; }
        public int ValidCMRRecords { get; set; }
        public int InvalidCMRRecords { get; set; }
        public double ValidationSuccessRate { get; set; }
        public int MissingBlNumber { get; set; }
        public int MissingRotationNumber { get; set; }
        public int MissingBothFields { get; set; }
        public int NewRecordsToday { get; set; }
        public int FixedRecordsToday { get; set; }
        public int NewIssuesDetectedToday { get; set; }
        public int QueuePendingCount { get; set; }
        public int QueueProcessingCount { get; set; }
        public int QueueCompletedCount { get; set; }
        public int QueueFailedCount { get; set; }
        public double AverageRedownloadTimeMinutes { get; set; }
        public double QueueSuccessRate { get; set; }
    }

    /// <summary>
    /// Request model for validating a CMR record
    /// </summary>
    public class ValidateCMRRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request model for queuing a single container for re-download
    /// </summary>
    public class QueueRedownloadRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string Reason { get; set; } = string.Empty;
    }

    /// <summary>
    /// Request model for queuing multiple containers for re-download
    /// </summary>
    public class QueueBatchRedownloadRequest
    {
        public List<string> ContainerNumbers { get; set; } = new();
        public string Reason { get; set; } = string.Empty;
    }
}
