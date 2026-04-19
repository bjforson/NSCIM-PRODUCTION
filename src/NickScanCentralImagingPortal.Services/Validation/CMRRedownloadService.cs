using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Validation
{
    /// <summary>
    /// Service for managing re-download queue for problematic CMR records
    /// </summary>
    public class CMRRedownloadService : ICMRRedownloadService
    {
        private readonly IcumDownloadsDbContext _context;
        private readonly IIcumApiService _icumApiService;
        private readonly ILogger<CMRRedownloadService> _logger;
        private static readonly object _lockObject = new object();
        private static bool _isProcessing = false;

        public CMRRedownloadService(
            IcumDownloadsDbContext context,
            IIcumApiService icumApiService,
            ILogger<CMRRedownloadService> logger)
        {
            _context = context;
            _icumApiService = icumApiService;
            _logger = logger;
        }

        public async Task<CMRRedownloadResult> QueueForRedownloadAsync(string containerNumber, string reason)
        {
            try
            {
                _logger.LogInformation("Queuing container {Container} for CMR re-download. Reason: {Reason}",
                    containerNumber, reason);

                // Check if already queued
                var existing = await _context.CMRRedownloadQueues
                    .FirstOrDefaultAsync(q => q.ContainerNumber == containerNumber &&
                                            (q.Status == "Pending" || q.Status == "Processing"));

                if (existing != null)
                {
                    return new CMRRedownloadResult
                    {
                        Success = false,
                        ContainerNumber = containerNumber,
                        Message = "Container is already queued for re-download",
                        QueueId = existing.Id.ToString()
                    };
                }

                // Get original record details
                var originalRecord = await _context.BOEDocuments
                    .FirstOrDefaultAsync(b => b.ContainerNumber == containerNumber);

                var queueItem = new CMRRedownloadQueue
                {
                    ContainerNumber = containerNumber,
                    Reason = reason,
                    Status = "Pending",
                    QueuedAt = DateTime.UtcNow,
                    OriginalDeclarationNumber = originalRecord?.DeclarationNumber,
                    OriginalClearanceType = originalRecord?.ClearanceType,
                    Priority = reason.Contains("Critical") ? "Critical" : "Normal"
                };

                _context.CMRRedownloadQueues.Add(queueItem);
                await _context.SaveChangesAsync();

                _logger.LogInformation("✅ Successfully queued container {Container} for re-download (ID: {QueueId})",
                    containerNumber, queueItem.Id);

                return new CMRRedownloadResult
                {
                    Success = true,
                    ContainerNumber = containerNumber,
                    Message = "Successfully queued for re-download",
                    QueueId = queueItem.Id.ToString()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error queuing container {Container} for re-download", containerNumber);
                return new CMRRedownloadResult
                {
                    Success = false,
                    ContainerNumber = containerNumber,
                    Message = $"Error queuing: {ex.Message}"
                };
            }
        }

        public async Task<CMRBatchRedownloadResult> QueueBatchForRedownloadAsync(List<string> containerNumbers, string reason)
        {
            var result = new CMRBatchRedownloadResult
            {
                TotalRequested = containerNumbers.Count
            };

            try
            {
                _logger.LogInformation("Queuing {Count} containers for CMR re-download. Reason: {Reason}",
                    containerNumbers.Count, reason);

                foreach (var containerNumber in containerNumbers)
                {
                    var queueResult = await QueueForRedownloadAsync(containerNumber, reason);
                    result.Results.Add(queueResult);

                    if (queueResult.Success)
                    {
                        result.SuccessfullyQueued++;
                    }
                    else
                    {
                        result.Failed++;
                        result.Errors.Add($"Container {containerNumber}: {queueResult.Message}");
                    }
                }

                _logger.LogInformation("Batch queue completed - Success: {Success}, Failed: {Failed}",
                    result.SuccessfullyQueued, result.Failed);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch queue operation");
                result.Errors.Add($"Batch operation failed: {ex.Message}");
                return result;
            }
        }

        public async Task<CMRQueueProcessingResult> ProcessRedownloadQueueAsync()
        {
            lock (_lockObject)
            {
                if (_isProcessing)
                {
                    _logger.LogWarning("Queue processing is already in progress, skipping this run");
                    return new CMRQueueProcessingResult
                    {
                        TotalProcessed = 0,
                        Successful = 0,
                        Failed = 0,
                        Skipped = 0
                    };
                }
                _isProcessing = true;
            }

            var startTime = DateTime.UtcNow;
            var result = new CMRQueueProcessingResult();

            try
            {
                _logger.LogInformation("Starting CMR re-download queue processing");

                // Get pending items (limit to 10 per batch to avoid overwhelming the API)
                var pendingItems = await _context.CMRRedownloadQueues
                    .Where(q => q.Status == "Pending" && q.RetryCount < q.MaxRetries)
                    .OrderBy(q => q.Priority == "Critical" ? 0 : 1)
                    .ThenBy(q => q.QueuedAt)
                    .Take(10)
                    .ToListAsync();

                result.TotalProcessed = pendingItems.Count;

                foreach (var item in pendingItems)
                {
                    try
                    {
                        // Mark as processing
                        item.Status = "Processing";
                        item.ProcessedBy = "CMRRedownloadService";
                        item.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();

                        _logger.LogInformation("Processing re-download for container {Container} (Queue ID: {QueueId})",
                            item.ContainerNumber, item.Id);

                        // Attempt to re-download using ICUMS API
                        var apiResult = await _icumApiService.FetchContainerDataAsync(item.ContainerNumber);

                        if (apiResult.Status == "Success" && apiResult.Data != null)
                        {
                            // Update the BOE document with new data
                            var boeDocument = await _context.BOEDocuments
                                .FirstOrDefaultAsync(b => b.ContainerNumber == item.ContainerNumber);

                            if (boeDocument != null)
                            {
                                // Update with fresh data from API
                                boeDocument.BlNumber = apiResult.Data.ManifestDetails?.MasterBlNumber;
                                boeDocument.RotationNumber = apiResult.Data.ManifestDetails?.RotationNumber;
                                boeDocument.HouseBl = apiResult.Data.ManifestDetails?.HouseBl;
                                boeDocument.UpdatedAt = DateTime.UtcNow;

                                // Mark queue item as completed
                                item.Status = "Completed";
                                item.ProcessedAt = DateTime.UtcNow;
                                item.ErrorMessage = null;

                                result.Successful++;
                                _logger.LogInformation("✅ Successfully re-downloaded and updated container {Container}",
                                    item.ContainerNumber);
                            }
                            else
                            {
                                throw new Exception("Original BOE document not found");
                            }
                        }
                        else
                        {
                            throw new Exception($"API call failed: {apiResult.Status}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing re-download for container {Container}", item.ContainerNumber);

                        item.RetryCount++;
                        item.ErrorMessage = ex.Message;
                        item.UpdatedAt = DateTime.UtcNow;

                        if (item.RetryCount >= item.MaxRetries)
                        {
                            item.Status = "Failed";
                            item.ProcessedAt = DateTime.UtcNow;
                            result.Failed++;
                        }
                        else
                        {
                            item.Status = "Pending"; // Retry later
                            result.Skipped++;
                        }

                        result.Errors.Add($"Container {item.ContainerNumber}: {ex.Message}");
                    }

                    await _context.SaveChangesAsync();
                }

                result.ProcessedAt = DateTime.UtcNow;
                result.ProcessingTime = DateTime.UtcNow - startTime;

                _logger.LogInformation("CMR re-download queue processing completed - Processed: {Processed}, Success: {Success}, Failed: {Failed}, Skipped: {Skipped}",
                    result.TotalProcessed, result.Successful, result.Failed, result.Skipped);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during CMR re-download queue processing");
                result.Errors.Add($"Queue processing failed: {ex.Message}");
                return result;
            }
            finally
            {
                lock (_lockObject)
                {
                    _isProcessing = false;
                }
            }
        }

        public async Task<CMRRedownloadQueueStatus> GetQueueStatusAsync()
        {
            try
            {
                var status = new CMRRedownloadQueueStatus
                {
                    PendingItems = await _context.CMRRedownloadQueues.CountAsync(q => q.Status == "Pending"),
                    ProcessingItems = await _context.CMRRedownloadQueues.CountAsync(q => q.Status == "Processing"),
                    CompletedItems = await _context.CMRRedownloadQueues.CountAsync(q => q.Status == "Completed"),
                    FailedItems = await _context.CMRRedownloadQueues.CountAsync(q => q.Status == "Failed"),
                    IsProcessing = _isProcessing
                };

                // Get last processed time
                var lastProcessed = await _context.CMRRedownloadQueues
                    .Where(q => q.ProcessedAt != null)
                    .OrderByDescending(q => q.ProcessedAt)
                    .Select(q => q.ProcessedAt)
                    .FirstOrDefaultAsync();

                status.LastProcessed = lastProcessed ?? DateTime.MinValue;

                // Get recent items
                status.RecentItems = await _context.CMRRedownloadQueues
                    .OrderByDescending(q => q.QueuedAt)
                    .Take(10)
                    .Select(q => new CMRRedownloadQueueItem
                    {
                        Id = q.Id.ToString(),
                        ContainerNumber = q.ContainerNumber,
                        Reason = q.Reason,
                        Status = q.Status,
                        QueuedAt = q.QueuedAt,
                        ProcessedAt = q.ProcessedAt,
                        ErrorMessage = q.ErrorMessage,
                        RetryCount = q.RetryCount,
                        MaxRetries = q.MaxRetries
                    })
                    .ToListAsync();

                return status;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting queue status");
                return new CMRRedownloadQueueStatus();
            }
        }

        public async Task<int> ClearCompletedItemsAsync()
        {
            try
            {
                var completedItems = await _context.CMRRedownloadQueues
                    .Where(q => q.Status == "Completed" && q.ProcessedAt < DateTime.UtcNow.AddDays(-7))
                    .ToListAsync();

                _context.CMRRedownloadQueues.RemoveRange(completedItems);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Cleared {Count} completed queue items older than 7 days", completedItems.Count);
                return completedItems.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing completed queue items");
                return 0;
            }
        }

        public async Task<CMRRedownloadStatistics> GetQueueStatisticsAsync()
        {
            try
            {
                // ✅ OPTIMIZED: Use database aggregation instead of loading all records into memory
                var stats = new CMRRedownloadStatistics
                {
                    TotalQueued = await _context.CMRRedownloadQueues.CountAsync(),
                    TotalProcessed = await _context.CMRRedownloadQueues.CountAsync(q => q.ProcessedAt != null),
                    TotalSuccessful = await _context.CMRRedownloadQueues.CountAsync(q => q.Status == "Completed"),
                    TotalFailed = await _context.CMRRedownloadQueues.CountAsync(q => q.Status == "Failed"),
                    CurrentlyProcessing = await _context.CMRRedownloadQueues.CountAsync(q => q.Status == "Processing"),
                    PendingProcessing = await _context.CMRRedownloadQueues.CountAsync(q => q.Status == "Pending")
                };

                stats.SuccessRate = stats.TotalProcessed > 0 ? (double)stats.TotalSuccessful / stats.TotalProcessed * 100 : 0;

                stats.LastProcessed = await _context.CMRRedownloadQueues
                    .Where(q => q.ProcessedAt != null)
                    .MaxAsync(q => (DateTime?)q.ProcessedAt) ?? DateTime.MinValue;

                // Calculate average processing time using database aggregation
                var processedCount = await _context.CMRRedownloadQueues
                    .CountAsync(q => q.ProcessedAt != null && q.QueuedAt != null);

                if (processedCount > 0)
                {
                    var avgMinutes = await _context.CMRRedownloadQueues
                        .Where(q => q.ProcessedAt != null && q.QueuedAt != null)
                        .AverageAsync(q => (double)((q.ProcessedAt!.Value - q.QueuedAt).TotalMinutes));
                    stats.AverageProcessingTimeMinutes = avgMinutes;
                }

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating queue statistics");
                return new CMRRedownloadStatistics();
            }
        }
    }
}
