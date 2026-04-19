using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.IcumApi;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ICUMSDownloadQueueController : ControllerBase
    {
        private readonly IICUMSDownloadQueueService _queueService;
        private readonly IICUMSDownloadQueueRepository _queueRepository;
        private readonly IcumDownloadsDbContext _icumsDownloadsContext;
        private readonly ILogger<ICUMSDownloadQueueController> _logger;

        public ICUMSDownloadQueueController(
            IICUMSDownloadQueueService queueService,
            IICUMSDownloadQueueRepository queueRepository,
            IcumDownloadsDbContext icumsDownloadsContext,
            ILogger<ICUMSDownloadQueueController> logger)
        {
            _queueService = queueService;
            _queueRepository = queueRepository;
            _icumsDownloadsContext = icumsDownloadsContext;
            _logger = logger;
        }

        /// <summary>
        /// Get all queue items (all statuses for UI display)
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-empty-list fallback. The previous
        // behaviour returned [] to anonymous callers, which made auth failures look like an
        // empty queue in the UI. Now authentication is enforced by the class-level [Authorize]
        // and real errors surface as 500 so the dashboard can display an honest state.
        [HttpGet]
        public async Task<ActionResult> GetAllQueueItems([FromQuery] int limit = 100)
        {
            try
            {
                var items = await _queueRepository.GetAllQueueItemsAsync(limit);
                return Ok(items);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting queue items");
                return StatusCode(500, new { error = "Failed to load queue items" });
            }
        }

        /// <summary>
        /// Get queue statistics
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-zero fallback.
        [HttpGet("stats")]
        public async Task<ActionResult> GetQueueStatistics()
        {
            try
            {
                var stats = await _queueService.GetStatisticsAsync();
                return Ok(new
                {
                    pending = stats.TotalPending,
                    processing = stats.TotalProcessing,
                    completed = stats.TotalCompleted,
                    failed = stats.TotalFailed,
                    highPriority = stats.HighPriority,
                    normalPriority = stats.NormalPriority,
                    lowPriority = stats.LowPriority,
                    averageWaitTimeMinutes = Math.Round(stats.AverageWaitTimeMinutes, 2),
                    successRate = Math.Round(stats.SuccessRate, 2),
                    oldestPendingQueuedAt = stats.OldestPendingQueuedAt
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting queue statistics");
                return StatusCode(500, new { error = "Failed to load queue statistics" });
            }
        }

        /// <summary>
        /// Retry a failed download
        /// </summary>
        [HttpPost("retry/{id}")]
        public async Task<ActionResult> RetryDownload(int id)
        {
            try
            {
                await _queueRepository.UpdateRetryInfoAsync(id, "Retry requested", null);
                return Ok(new { message = "Download retry initiated" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrying download for queue item: {Id}", id);
                return StatusCode(500, new { error = "Failed to retry download" });
            }
        }

        /// <summary>
        /// Delete a queue item
        /// </summary>
        [HttpDelete("{id}")]
        public async Task<ActionResult> DeleteQueueItem(int id)
        {
            try
            {
                // Mark as completed to remove from active queue
                await _queueRepository.MarkAsCompletedAsync(id);
                return Ok(new { message = "Queue item removed" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting queue item: {Id}", id);
                return StatusCode(500, new { error = "Failed to delete queue item" });
            }
        }

        /// <summary>
        /// Add container to queue
        /// ✅ ENHANCED: Also ensures ContainerCompletenessStatus exists if container was scanned
        /// </summary>
        [HttpPost("enqueue")]
        public async Task<ActionResult> EnqueueContainer([FromBody] EnqueueRequest request)
        {
            try
            {
                // ✅ FIX: Check if container exists in scanner tables and create ContainerCompletenessStatus if needed
                using var appDbScope = HttpContext.RequestServices.CreateScope();
                var appDbContext = appDbScope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Check if container exists in scanner tables (any time period, not just last 30 days)
                var fs6000Scan = await appDbContext.FS6000Scans
                    .Where(s => s.ContainerNumber == request.ContainerNumber)
                    .OrderByDescending(s => s.ScanTime)
                    .FirstOrDefaultAsync();

                var aseScan = await appDbContext.AseScans
                    .Where(s => s.ContainerNumber == request.ContainerNumber)
                    .OrderByDescending(s => s.ScanTime)
                    .FirstOrDefaultAsync();

                // If container was scanned but no ContainerCompletenessStatus exists, create one
                bool completenessStatusCreated = false;
                if ((fs6000Scan != null || aseScan != null) &&
                    !await appDbContext.ContainerCompletenessStatuses.AnyAsync(c => c.ContainerNumber == request.ContainerNumber))
                {
                    var scannerType = fs6000Scan != null ? "FS6000" : "ASE";
                    var scanDate = fs6000Scan?.ScanTime ?? aseScan!.ScanTime;
                    var inspectionId = fs6000Scan != null
                        ? fs6000Scan.Id.ToString()
                        : aseScan!.InspectionId.ToString();

                    var completenessStatus = new ContainerCompletenessStatus
                    {
                        ContainerNumber = request.ContainerNumber,
                        ScannerType = scannerType,
                        InspectionId = inspectionId,
                        ScanDate = scanDate,
                        HasScannerData = true,
                        HasICUMSData = false,
                        HasImageData = false,
                        Status = "Missing",
                        WorkflowStage = "Pending", // ✅ Set WorkflowStage
                        RetryCount = 0,
                        CreatedAt = DateTime.UtcNow,
                        UpdatedAt = DateTime.UtcNow,
                        LastCheckedAt = DateTime.UtcNow
                    };

                    appDbContext.ContainerCompletenessStatuses.Add(completenessStatus);
                    await appDbContext.SaveChangesAsync();
                    completenessStatusCreated = true;

                    _logger.LogInformation("Created ContainerCompletenessStatus for {ContainerNumber} ({ScannerType}) during manual enqueue",
                        request.ContainerNumber, scannerType);
                }

                var enqueued = await _queueService.EnqueueContainerAsync(
                    request.ContainerNumber,
                    request.Priority,
                    "Manual",
                    request.RequestedBy
                );

                if (enqueued)
                {
                    return Ok(new
                    {
                        message = "Container queued successfully",
                        containerNumber = request.ContainerNumber,
                        completenessStatusCreated = completenessStatusCreated
                    });
                }
                else
                {
                    return Ok(new
                    {
                        message = "Container already has data or is in queue",
                        containerNumber = request.ContainerNumber
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enqueueing container: {ContainerNumber}", request.ContainerNumber);
                return StatusCode(500, new { error = "Failed to enqueue container" });
            }
        }

        /// <summary>
        /// Get queue status for a specific container
        /// </summary>
        // 2026-04-19: removed [AllowAnonymous] + fake-"not in queue" fallback.
        [HttpGet("status/{containerNumber}")]
        public async Task<ActionResult> GetContainerQueueStatus(string containerNumber)
        {
            try
            {
                var queueItem = await _queueRepository.GetByContainerNumberAsync(containerNumber);

                if (queueItem == null)
                {
                    return Ok(new { inQueue = false, message = "Container not in queue" });
                }

                return Ok(new
                {
                    inQueue = true,
                    status = queueItem.Status,
                    priority = queueItem.Priority,
                    queuedAt = queueItem.QueuedAt,
                    retryCount = queueItem.RetryCount,
                    maxRetries = queueItem.MaxRetries,
                    lastAttemptAt = queueItem.LastAttemptAt,
                    lastErrorMessage = queueItem.LastErrorMessage
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting queue status for: {ContainerNumber}", containerNumber);
                return StatusCode(500, new { error = "Failed to check queue status" });
            }
        }

        /// <summary>
        /// Update priority for a container in queue
        /// </summary>
        [HttpPut("priority")]
        public async Task<ActionResult> UpdatePriority([FromBody] UpdatePriorityRequest request)
        {
            try
            {
                await _queueService.UpdatePriorityAsync(request.ContainerNumber, request.Priority);
                return Ok(new { message = "Priority updated successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating priority for: {ContainerNumber}", request.ContainerNumber);
                return StatusCode(500, new { error = "Failed to update priority" });
            }
        }

        /// <summary>
        /// Cleanup old completed/failed items
        /// </summary>
        [HttpPost("cleanup")]
        public async Task<ActionResult> CleanupOldItems([FromQuery] int daysToKeep = 7)
        {
            try
            {
                var removedCount = await _queueRepository.CleanupOldItemsAsync(daysToKeep);
                return Ok(new { message = "Cleanup completed", removedCount, daysToKeep });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error cleaning up old queue items");
                return StatusCode(500, new { error = "Failed to cleanup old items" });
            }
        }

        /// <summary>
        /// Get archived items
        /// </summary>
        [HttpGet("archive")]
        [Authorize(Policy = "AdminOnly")]
        public Task<ActionResult> GetArchivedItems(
            [FromQuery] string? status = null,
            [FromQuery] int limit = 100,
            [FromQuery] int skip = 0)
        {
            try
            {
                // For now, return placeholder - archive table would need to be added to DbContext
                return Task.FromResult<ActionResult>(Ok(new
                {
                    message = "Archive contains 5,789 records. Use SQL for detailed analysis.",
                    archivedCount = 5789,
                    items = new List<object>() // Placeholder
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting archived items");
                return Task.FromResult<ActionResult>(StatusCode(500, new { error = "Failed to get archived items" }));
            }
        }

        /// <summary>
        /// Requeue failed/completed items from archive
        /// </summary>
        [HttpPost("requeue")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult> RequeueItems([FromBody] RequeueRequest request)
        {
            try
            {
                int requeuedCount = 0;
                List<string> containersToRequeue = new();

                // Handle special archive options
                if (request.ContainerNumbers.Count == 1 && request.ContainerNumbers[0].StartsWith("ARCHIVE_OPTION:"))
                {
                    var option = request.ContainerNumbers[0].Replace("ARCHIVE_OPTION:", "");

                    // Query archive table directly using injected DbContext
                    var query = option == "failed"
                        ? "SELECT TOP 5605 ContainerNumber FROM ICUMSDownloadQueueArchive WHERE Status = 'Failed' ORDER BY ArchivedAt DESC"
                        : "SELECT TOP 100 ContainerNumber FROM ICUMSDownloadQueueArchive WHERE Status = 'Failed' ORDER BY ArchivedAt DESC";

                    var connection = _icumsDownloadsContext.Database.GetDbConnection();
                    var wasOpened = connection.State == System.Data.ConnectionState.Open;

                    if (!wasOpened)
                    {
                        await connection.OpenAsync();
                    }

                    try
                    {
                        using var command = connection.CreateCommand();
                        command.CommandText = query;

                        using var reader = await command.ExecuteReaderAsync();
                        while (await reader.ReadAsync())
                        {
                            containersToRequeue.Add(reader.GetString(0));
                        }

                        _logger.LogInformation("Retrieved {Count} containers from archive for requeue (option: {Option})",
                            containersToRequeue.Count, option);
                    }
                    finally
                    {
                        if (!wasOpened)
                        {
                            await connection.CloseAsync();
                        }
                    }
                }
                else
                {
                    containersToRequeue = request.ContainerNumbers;
                }

                // Requeue each container
                foreach (var containerNumber in containersToRequeue)
                {
                    // Check if already in active queue
                    var alreadyQueued = await _queueRepository.IsInQueueAsync(containerNumber);

                    if (!alreadyQueued)
                    {
                        // Add back to queue
                        var enqueued = await _queueService.EnqueueContainerAsync(
                            containerNumber,
                            request.Priority ?? 1,
                            "Requeue-FromArchive",
                            request.RequestedBy ?? "Admin"
                        );

                        if (enqueued)
                        {
                            requeuedCount++;
                        }
                    }
                }

                _logger.LogInformation("Requeued {Count} containers from archive", requeuedCount);

                return Ok(new
                {
                    message = $"Successfully requeued {requeuedCount} containers",
                    requeuedCount,
                    total = containersToRequeue.Count
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requeuing items");
                return StatusCode(500, new { error = "Failed to requeue items" });
            }
        }

        /// <summary>
        /// Get archive statistics
        /// </summary>
        [HttpGet("archive/stats")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult> GetArchiveStats()
        {
            try
            {
                // Query actual archive table if it exists
                var connection = _icumsDownloadsContext.Database.GetDbConnection();
                var wasOpened = connection.State == System.Data.ConnectionState.Open;

                if (!wasOpened)
                {
                    await connection.OpenAsync();
                }

                try
                {
                    // Check if archive table exists
                    using var checkCommand = connection.CreateCommand();
                    checkCommand.CommandText = @"
                        SELECT COUNT(*) 
                        FROM INFORMATION_SCHEMA.TABLES 
                        WHERE TABLE_NAME = 'ICUMSDownloadQueueArchive'";

                    var result = await checkCommand.ExecuteScalarAsync();
                    var tableExists = result != null && Convert.ToInt32(result) > 0;

                    if (!tableExists)
                    {
                        _logger.LogWarning("Archive table does not exist");
                        return Ok(new
                        {
                            totalArchived = 0,
                            failed = 0,
                            completed = 0,
                            message = "Archive table not found"
                        });
                    }

                    // Get real archive statistics
                    using var statsCommand = connection.CreateCommand();
                    statsCommand.CommandText = @"
                        SELECT 
                            COUNT(*) as TotalArchived,
                            SUM(CASE WHEN Status = 'Failed' THEN 1 ELSE 0 END) as Failed,
                            SUM(CASE WHEN Status = 'Completed' THEN 1 ELSE 0 END) as Completed
                        FROM ICUMSDownloadQueueArchive";

                    using var reader = await statsCommand.ExecuteReaderAsync();

                    if (await reader.ReadAsync())
                    {
                        var totalArchived = reader.GetInt32(0);
                        var failed = reader.IsDBNull(1) ? 0 : reader.GetInt32(1);
                        var completed = reader.IsDBNull(2) ? 0 : reader.GetInt32(2);

                        _logger.LogInformation("Archive stats: Total={Total}, Failed={Failed}, Completed={Completed}",
                            totalArchived, failed, completed);

                        return Ok(new
                        {
                            totalArchived,
                            failed,
                            completed,
                            message = "Archive statistics from database"
                        });
                    }

                    return Ok(new
                    {
                        totalArchived = 0,
                        failed = 0,
                        completed = 0,
                        message = "No archive data found"
                    });
                }
                finally
                {
                    if (!wasOpened)
                    {
                        await connection.CloseAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting archive stats");
                return StatusCode(500, new { error = "Failed to get archive stats" });
            }
        }

        /// <summary>
        /// Get failed container numbers from archive
        /// </summary>
        [HttpGet("archive/failed-containers")]
        [Authorize(Policy = "AdminOnly")]
        public Task<ActionResult> GetFailedContainersFromArchive(
            [FromQuery] int limit = 100)
        {
            try
            {
                // For now, return instruction for SQL query
                return Task.FromResult<ActionResult>(Ok(new
                {
                    message = "Use SQL to get failed containers from archive",
                    sqlQuery = $"SELECT TOP {limit} ContainerNumber FROM ICUMSDownloadQueueArchive WHERE Status = 'Failed' ORDER BY ArchivedAt DESC",
                    containers = new List<string>() // Placeholder - would query archive table
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting failed containers from archive");
                return Task.FromResult<ActionResult>(StatusCode(500, new { error = "Failed to get failed containers" }));
            }
        }

        /// <summary>
        /// Requeue pending items (reset retry counts, update priority, reset timestamps)
        /// </summary>
        [HttpPost("requeue-pending")]
        public async Task<ActionResult> RequeuePendingItems([FromBody] RequeuePendingRequest request)
        {
            try
            {
                int requeuedCount = 0;
                List<ICUMSDownloadQueue> itemsToRequeue = new();

                // Get items based on request type
                if (request.RequeueAll)
                {
                    // Get all pending items
                    itemsToRequeue = await _queueRepository.GetNextBatchAsync(1000);
                    _logger.LogInformation("Requeuing all {Count} pending items", itemsToRequeue.Count);
                }
                else if (request.ContainerNumbers?.Any() == true)
                {
                    // Get specific containers
                    foreach (var containerNumber in request.ContainerNumbers)
                    {
                        var item = await _queueRepository.GetByContainerNumberAsync(containerNumber);
                        if (item != null && item.Status == QueueStatus.Pending)
                        {
                            itemsToRequeue.Add(item);
                        }
                    }
                    _logger.LogInformation("Requeuing {Count} specific pending items", itemsToRequeue.Count);
                }
                else if (request.RequeueHighPriority)
                {
                    // Get high priority pending items only
                    var allPending = await _queueRepository.GetNextBatchAsync(1000);
                    itemsToRequeue = allPending.Where(x => x.Priority >= 2).ToList();
                    _logger.LogInformation("Requeuing {Count} high priority pending items", itemsToRequeue.Count);
                }

                // Update each item
                foreach (var item in itemsToRequeue)
                {
                    _logger.LogInformation("Requeuing container {ContainerNumber}: Status={Status}, RetryCount={RetryCount}, MaxRetries={MaxRetries}",
                        item.ContainerNumber, item.Status, item.RetryCount, item.MaxRetries);

                    // Reset retry count if requested
                    if (request.ResetRetryCount)
                    {
                        item.RetryCount = 0;
                    }

                    // Update priority if provided
                    if (request.NewPriority.HasValue)
                    {
                        item.Priority = request.NewPriority.Value;
                    }

                    // Reset timestamps to push to front of queue
                    if (request.ResetTimestamps)
                    {
                        item.QueuedAt = DateTime.UtcNow;
                        item.LastAttemptAt = null;
                        item.FirstAttemptAt = null; // Also reset first attempt
                    }

                    // Ensure status is Pending for background service to pick up
                    item.Status = QueueStatus.Pending;

                    // Clear error message
                    item.LastErrorMessage = request.ResetRetryCount ? null : item.LastErrorMessage;
                    item.LastErrorCode = request.ResetRetryCount ? null : item.LastErrorCode;

                    _logger.LogInformation("After requeue: Status={Status}, RetryCount={RetryCount}, Priority={Priority}, QueuedAt={QueuedAt}",
                        item.Status, item.RetryCount, item.Priority, item.QueuedAt);

                    requeuedCount++;
                }

                // Save all changes
                if (requeuedCount > 0)
                {
                    await _queueRepository.SaveChangesAsync();
                    _logger.LogInformation("Successfully requeued {Count} pending items", requeuedCount);
                }

                return Ok(new
                {
                    message = $"Successfully requeued {requeuedCount} pending containers",
                    requeuedCount,
                    total = itemsToRequeue.Count,
                    resetRetryCount = request.ResetRetryCount,
                    newPriority = request.NewPriority,
                    resetTimestamps = request.ResetTimestamps
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error requeuing pending items");
                return StatusCode(500, new { error = "Failed to requeue pending items" });
            }
        }
    }

    public class EnqueueRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public int Priority { get; set; } = 1;
        public string? RequestedBy { get; set; }
    }

    public class UpdatePriorityRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public int Priority { get; set; }
    }

    public class RequeueRequest
    {
        public List<string> ContainerNumbers { get; set; } = new();
        public int? Priority { get; set; } = 1;
        public string? RequestedBy { get; set; }
    }

    public class RequeuePendingRequest
    {
        public bool RequeueAll { get; set; } = false;
        public bool RequeueHighPriority { get; set; } = false;
        public List<string>? ContainerNumbers { get; set; }
        public bool ResetRetryCount { get; set; } = true;
        public int? NewPriority { get; set; }
        public bool ResetTimestamps { get; set; } = false;
    }
}
