using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class ICUMSDownloadQueueRepository : IICUMSDownloadQueueRepository
    {
        private readonly IcumDownloadsDbContext _context;
        private readonly ILogger<ICUMSDownloadQueueRepository> _logger;

        public ICUMSDownloadQueueRepository(
            IcumDownloadsDbContext context,
            ILogger<ICUMSDownloadQueueRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<int> AddToQueueAsync(ICUMSDownloadQueue queueItem)
        {
            try
            {
                // Check if already exists
                var existing = await _context.ICUMSDownloadQueue
                    .FirstOrDefaultAsync(q => q.ContainerNumber == queueItem.ContainerNumber);

                if (existing != null)
                {
                    // Update priority if new priority is higher
                    if (queueItem.Priority > existing.Priority)
                    {
                        existing.Priority = queueItem.Priority;
                        existing.RequestSource = queueItem.RequestSource;

                        // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                        _context.Entry(existing).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                        await _context.SaveChangesAsync();

                        // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                        _context.ChangeTracker.Clear();

                        _logger.LogInformation("[ICUMS-QUEUE] Updated priority for {ContainerNumber}: {Priority}",
                            queueItem.ContainerNumber, queueItem.Priority);
                    }
                    return existing.Id;
                }

                _context.ICUMSDownloadQueue.Add(queueItem);
                await _context.SaveChangesAsync();

                // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                _context.ChangeTracker.Clear();

                _logger.LogInformation("[ICUMS-QUEUE] Added to queue: {ContainerNumber}, Priority: {Priority}, Source: {Source}",
                    queueItem.ContainerNumber, queueItem.Priority, queueItem.RequestSource);

                return queueItem.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error adding to queue: {ContainerNumber}", queueItem.ContainerNumber);
                throw;
            }
        }

        public async Task<List<ICUMSDownloadQueue>> GetNextBatchAsync(int batchSize = 50)
        {
            try
            {
                // ✅ ENHANCED: Add diagnostic logging
                var totalPending = await _context.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == QueueStatus.Pending);

                var pendingWithRetries = await _context.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == QueueStatus.Pending && q.RetryCount < q.MaxRetries);

                var pendingExceededRetries = await _context.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == QueueStatus.Pending && q.RetryCount >= q.MaxRetries);

                _logger.LogDebug("[ICUMS-QUEUE] Queue status: {TotalPending} pending, {WithRetries} eligible (RetryCount < MaxRetries), {ExceededRetries} exceeded retries",
                    totalPending, pendingWithRetries, pendingExceededRetries);

                var items = await _context.ICUMSDownloadQueue
                    .Where(q => q.Status == QueueStatus.Pending && q.RetryCount < q.MaxRetries)
                    .OrderByDescending(q => q.Priority)
                    .ThenBy(q => q.QueuedAt)
                    .Take(batchSize)
                    .ToListAsync();

                if (items.Count > 0)
                {
                    _logger.LogInformation("[ICUMS-QUEUE] Retrieved {Count} items from queue (oldest: {OldestQueuedAt}, highest priority: {MaxPriority})",
                        items.Count, items.Min(i => i.QueuedAt), items.Max(i => i.Priority));
                }
                else if (totalPending > 0)
                {
                    _logger.LogWarning("[ICUMS-QUEUE] ⚠️ No items retrieved despite {TotalPending} pending items - all may have exceeded max retries", totalPending);
                }

                return items;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error getting next batch");
                return new List<ICUMSDownloadQueue>();
            }
        }

        /// <summary>
        /// Get all queue items (all statuses) for UI display
        /// </summary>
        public async Task<List<ICUMSDownloadQueue>> GetAllQueueItemsAsync(int limit = 100)
        {
            try
            {
                var items = await _context.ICUMSDownloadQueue
                    .OrderByDescending(q => q.Priority)
                    .ThenByDescending(q => q.QueuedAt)
                    .Take(limit)
                    .ToListAsync();

                _logger.LogDebug("[ICUMS-QUEUE] Retrieved {Count} total queue items (all statuses)", items.Count);
                return items;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error getting all queue items");
                return new List<ICUMSDownloadQueue>();
            }
        }

        public async Task MarkAsProcessingAsync(int id)
        {
            try
            {
                _logger.LogDebug("[ICUMS-QUEUE] MarkAsProcessingAsync called for ID: {Id}", id);

                var item = await _context.ICUMSDownloadQueue.FindAsync(id);
                if (item != null)
                {
                    _logger.LogDebug("[ICUMS-QUEUE] Found item {ContainerNumber}, current status: {Status}",
                        item.ContainerNumber, item.Status);

                    item.Status = QueueStatus.Processing;
                    item.FirstAttemptAt ??= DateTime.UtcNow;
                    item.LastAttemptAt = DateTime.UtcNow;

                    // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                    _context.Entry(item).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                    var changes = await _context.SaveChangesAsync();

                    // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                    _context.ChangeTracker.Clear();

                    _logger.LogInformation("[ICUMS-QUEUE] ✅ Marked as Processing: {ContainerNumber} (ID: {Id}, Changes: {Changes})",
                        item.ContainerNumber, id, changes);
                }
                else
                {
                    _logger.LogWarning("[ICUMS-QUEUE] ⚠️ Item not found for ID: {Id}", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] ❌ Error marking as processing: {Id}", id);
                throw; // Re-throw so service knows update failed
            }
        }

        public async Task MarkAsCompletedAsync(int id)
        {
            try
            {
                _logger.LogDebug("[ICUMS-QUEUE] MarkAsCompletedAsync called for ID: {Id}", id);

                var item = await _context.ICUMSDownloadQueue.FindAsync(id);
                if (item != null)
                {
                    _logger.LogDebug("[ICUMS-QUEUE] Found item {ContainerNumber}, current status: {Status}",
                        item.ContainerNumber, item.Status);

                    item.Status = QueueStatus.Completed;
                    item.CompletedAt = DateTime.UtcNow;

                    // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                    _context.Entry(item).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                    var changes = await _context.SaveChangesAsync();

                    // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                    _context.ChangeTracker.Clear();

                    _logger.LogInformation("[ICUMS-QUEUE] ✅ Marked as Completed: {ContainerNumber} (ID: {Id}, Changes: {Changes})",
                        item.ContainerNumber, id, changes);
                }
                else
                {
                    _logger.LogWarning("[ICUMS-QUEUE] ⚠️ Item not found for ID: {Id}", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] ❌ Error marking as completed: {Id}", id);
                throw; // Re-throw so service knows update failed
            }
        }

        public async Task MarkAsFailedAsync(int id, string errorMessage, string? errorCode = null)
        {
            try
            {
                var item = await _context.ICUMSDownloadQueue.FindAsync(id);
                if (item != null)
                {
                    item.Status = QueueStatus.Failed;
                    item.LastErrorMessage = errorMessage;
                    item.LastErrorCode = errorCode;
                    item.LastAttemptAt = DateTime.UtcNow;

                    // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                    _context.Entry(item).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                    await _context.SaveChangesAsync();

                    // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                    _context.ChangeTracker.Clear();

                    _logger.LogWarning("[ICUMS-QUEUE] Marked as failed: {ContainerNumber}, Error: {ErrorMessage}",
                        item.ContainerNumber, errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error marking as failed: {Id}", id);
            }
        }

        public async Task UpdateRetryInfoAsync(int id, string errorMessage, string? errorCode = null)
        {
            try
            {
                _logger.LogDebug("[ICUMS-QUEUE] UpdateRetryInfoAsync called for ID: {Id}, Error: {Error}", id, errorMessage);

                var item = await _context.ICUMSDownloadQueue.FindAsync(id);
                if (item != null)
                {
                    _logger.LogDebug("[ICUMS-QUEUE] Found item {ContainerNumber}, current retry: {RetryCount}/{MaxRetries}",
                        item.ContainerNumber, item.RetryCount, item.MaxRetries);

                    item.Status = QueueStatus.Pending; // Reset to pending for retry
                    item.LastErrorMessage = errorMessage;
                    item.LastErrorCode = errorCode;
                    item.LastAttemptAt = DateTime.UtcNow;
                    item.RetryCount++;

                    // ✅ FIX: If max retries reached, mark as failed AND set CompletedAt for cleanup
                    if (item.RetryCount >= item.MaxRetries)
                    {
                        item.Status = QueueStatus.Failed;
                        item.CompletedAt = DateTime.UtcNow; // Set CompletedAt so cleanup can archive it
                        _logger.LogWarning("[ICUMS-QUEUE] Max retries reached for {ContainerNumber} (RetryCount: {RetryCount}/{MaxRetries}), marking as Failed and ready for archive",
                            item.ContainerNumber, item.RetryCount, item.MaxRetries);
                    }

                    var changes = await _context.SaveChangesAsync();

                    // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                    _context.ChangeTracker.Clear();

                    _logger.LogInformation("[ICUMS-QUEUE] ✅ Updated retry info: {ContainerNumber} (ID: {Id}, RetryCount: {RetryCount}, Status: {Status}, Changes: {Changes})",
                        item.ContainerNumber, id, item.RetryCount, item.Status, changes);
                }
                else
                {
                    _logger.LogWarning("[ICUMS-QUEUE] ⚠️ Item not found for ID: {Id}", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] ❌ Error updating retry info: {Id}", id);
                throw; // Re-throw so service knows update failed
            }
        }

        public async Task<bool> IsInQueueAsync(string containerNumber)
        {
            try
            {
                return await _context.ICUMSDownloadQueue
                    .AnyAsync(q => q.ContainerNumber == containerNumber &&
                                   q.Status != QueueStatus.Completed);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error checking if in queue: {ContainerNumber}", containerNumber);
                return false;
            }
        }

        public async Task<ICUMSDownloadQueue?> GetByContainerNumberAsync(string containerNumber)
        {
            try
            {
                return await _context.ICUMSDownloadQueue
                    .FirstOrDefaultAsync(q => q.ContainerNumber == containerNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error getting by container number: {ContainerNumber}", containerNumber);
                return null;
            }
        }

        public async Task<QueueStatistics> GetQueueStatisticsAsync()
        {
            try
            {
                var stats = new QueueStatistics();

                stats.TotalPending = await _context.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == QueueStatus.Pending);

                stats.TotalProcessing = await _context.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == QueueStatus.Processing);

                stats.TotalCompleted = await _context.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == QueueStatus.Completed);

                stats.TotalFailed = await _context.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == QueueStatus.Failed);

                stats.HighPriority = await _context.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == QueueStatus.Pending && q.Priority >= 2);

                stats.NormalPriority = await _context.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == QueueStatus.Pending && q.Priority == 1);

                stats.LowPriority = await _context.ICUMSDownloadQueue
                    .CountAsync(q => q.Status == QueueStatus.Pending && q.Priority == 0);

                // Calculate average wait time for completed items
                var completedItems = await _context.ICUMSDownloadQueue
                    .Where(q => q.Status == QueueStatus.Completed && q.CompletedAt.HasValue)
                    .Select(q => new { q.QueuedAt, q.CompletedAt })
                    .ToListAsync();

                if (completedItems.Any())
                {
                    stats.AverageWaitTimeMinutes = completedItems
                        .Average(i => (i.CompletedAt!.Value - i.QueuedAt).TotalMinutes);
                }

                // Calculate success rate
                var totalProcessed = stats.TotalCompleted + stats.TotalFailed;
                stats.SuccessRate = totalProcessed > 0
                    ? (stats.TotalCompleted * 100.0 / totalProcessed)
                    : 0;

                // Get oldest pending item
                stats.OldestPendingQueuedAt = await _context.ICUMSDownloadQueue
                    .Where(q => q.Status == QueueStatus.Pending)
                    .OrderBy(q => q.QueuedAt)
                    .Select(q => (DateTime?)q.QueuedAt)
                    .FirstOrDefaultAsync();

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error getting queue statistics");
                return new QueueStatistics();
            }
        }

        public async Task<int> CleanupOldItemsAsync(int daysToKeep = 7)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);

                // ✅ FIX: Archive failed items that have exceeded max retries
                // For Failed items, use LastAttemptAt if CompletedAt is not set (backward compatibility)
                var oldItems = await _context.ICUMSDownloadQueue
                    .Where(q => (q.Status == QueueStatus.Completed || q.Status == QueueStatus.Failed) &&
                                ((q.CompletedAt.HasValue && q.CompletedAt.Value < cutoffDate) ||
                                 (q.Status == QueueStatus.Failed && !q.CompletedAt.HasValue && q.LastAttemptAt.HasValue && q.LastAttemptAt.Value < cutoffDate)))
                    .ToListAsync();

                if (oldItems.Any())
                {
                    _logger.LogInformation("[ICUMS-QUEUE] Archiving {Count} old queue items (Completed: {Completed}, Failed: {Failed})",
                        oldItems.Count,
                        oldItems.Count(q => q.Status == QueueStatus.Completed),
                        oldItems.Count(q => q.Status == QueueStatus.Failed));

                    _context.ICUMSDownloadQueue.RemoveRange(oldItems);
                    await _context.SaveChangesAsync();

                    // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entities
                    _context.ChangeTracker.Clear();

                    _logger.LogInformation("[ICUMS-QUEUE] ✅ Archived {Count} old queue items", oldItems.Count);
                }

                return oldItems.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error cleaning up old items");
                return 0;
            }
        }

        public async Task UpdatePriorityAsync(string containerNumber, int priority)
        {
            try
            {
                var item = await _context.ICUMSDownloadQueue
                    .AsTracking()
                    .FirstOrDefaultAsync(q => q.ContainerNumber == containerNumber);

                if (item != null && item.Status == QueueStatus.Pending)
                {
                    item.Priority = priority;
                    await _context.SaveChangesAsync();

                    // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                    _context.ChangeTracker.Clear();

                    _logger.LogInformation("[ICUMS-QUEUE] Updated priority for {ContainerNumber}: {Priority}",
                        containerNumber, priority);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error updating priority: {ContainerNumber}", containerNumber);
            }
        }

        public async Task<int> RecoverStuckProcessingItemsAsync(int timeoutMinutes = 30)
        {
            try
            {
                var timeoutThreshold = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

                // Find items stuck in Processing status for more than timeout
                var stuckItems = await _context.ICUMSDownloadQueue
                    .AsTracking()
                    .Where(q => q.Status == QueueStatus.Processing &&
                               q.LastAttemptAt.HasValue &&
                               q.LastAttemptAt.Value < timeoutThreshold)
                    .ToListAsync();

                if (stuckItems.Count == 0)
                {
                    return 0;
                }

                _logger.LogWarning("[ICUMS-QUEUE] Found {Count} items stuck in Processing status (stuck for > {Timeout} minutes), resetting to Pending",
                    stuckItems.Count, timeoutMinutes);

                foreach (var item in stuckItems)
                {
                    item.Status = QueueStatus.Pending;
                    // Don't increment retry count - these are recovery operations, not retries
                    _logger.LogInformation("[ICUMS-QUEUE] Recovered stuck item: {ContainerNumber} (ID: {Id}, was stuck since {LastAttempt})",
                        item.ContainerNumber, item.Id, item.LastAttemptAt);
                }

                await _context.SaveChangesAsync();

                // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entities
                _context.ChangeTracker.Clear();

                _logger.LogInformation("[ICUMS-QUEUE] ✅ Recovered {Count} stuck items, reset to Pending status", stuckItems.Count);
                return stuckItems.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error recovering stuck processing items");
                return 0;
            }
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();

            // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entities
            _context.ChangeTracker.Clear();
        }
    }
}
