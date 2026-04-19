using System.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    /// <summary>
    /// Repository for managing ContainerScanQueue
    /// Handles all queue operations for container completeness processing
    /// </summary>
    public class ContainerScanQueueRepository : IContainerScanQueueRepository
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ContainerScanQueueRepository> _logger;

        public ContainerScanQueueRepository(
            ApplicationDbContext context,
            ILogger<ContainerScanQueueRepository> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<int> AddToQueueAsync(ContainerScanQueue queueItem)
        {
            try
            {
                // Check if already exists (by ContainerNumber, ScannerType, and InspectionId)
                var existing = await _context.ContainerScanQueues
                    .AsTracking()
                    .FirstOrDefaultAsync(q =>
                        q.ContainerNumber == queueItem.ContainerNumber &&
                        q.ScannerType == queueItem.ScannerType &&
                        q.InspectionId == queueItem.InspectionId &&
                        (q.Status == ContainerScanQueueStatus.Pending || q.Status == ContainerScanQueueStatus.Processing));

                if (existing != null)
                {
                    // Update priority if new priority is higher
                    if (queueItem.Priority > existing.Priority)
                    {
                        existing.Priority = queueItem.Priority;
                        existing.UpdatedAt = DateTime.UtcNow;
                        await _context.SaveChangesAsync();

                        // Clear change tracker to release tracked entity
                        _context.ChangeTracker.Clear();

                        _logger.LogDebug("[CONTAINER-SCAN-QUEUE] Updated priority for {ContainerNumber} ({ScannerType}, {InspectionId}): {Priority}",
                            queueItem.ContainerNumber, queueItem.ScannerType, queueItem.InspectionId, queueItem.Priority);
                    }
                    else
                    {
                        _logger.LogDebug("[CONTAINER-SCAN-QUEUE] Item already in queue: {ContainerNumber} ({ScannerType}, {InspectionId})",
                            queueItem.ContainerNumber, queueItem.ScannerType, queueItem.InspectionId);
                    }
                    return existing.Id;
                }

                queueItem.CreatedAt = DateTime.UtcNow;
                queueItem.UpdatedAt = DateTime.UtcNow;
                queueItem.QueuedAt = DateTime.UtcNow;

                _context.ContainerScanQueues.Add(queueItem);
                await _context.SaveChangesAsync();

                // Clear change tracker to release tracked entity
                _context.ChangeTracker.Clear();

                _logger.LogInformation("[CONTAINER-SCAN-QUEUE] ✅ Added to queue: {ContainerNumber} ({ScannerType}, {InspectionId}), Priority: {Priority}",
                    queueItem.ContainerNumber, queueItem.ScannerType, queueItem.InspectionId, queueItem.Priority);

                return queueItem.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error adding to queue: {ContainerNumber} ({ScannerType})",
                    queueItem.ContainerNumber, queueItem.ScannerType);
                throw;
            }
        }

        public async Task<int> AddBatchToQueueAsync(List<ContainerScanQueue> queueItems)
        {
            if (queueItems == null || !queueItems.Any())
                return 0;

            try
            {
                int addedCount = 0;
                var now = DateTime.UtcNow;

                // ✅ FIX: SQL Server 2014 compatibility - avoid CTE generation from Contains()
                // Strategy: Check each item individually using GetByKeyAsync (simple, reliable, works with SQL Server 2014)
                var itemsToAdd = new List<ContainerScanQueue>();

                foreach (var item in queueItems)
                {
                    // Check if already exists (by ContainerNumber + ScannerType + InspectionId)
                    var existing = await GetByKeyAsync(item.ContainerNumber, item.ScannerType, item.InspectionId);

                    // Only add if doesn't exist OR if existing is Completed/Failed (allow re-queueing)
                    if (existing == null || existing.Status == ContainerScanQueueStatus.Completed || existing.Status == ContainerScanQueueStatus.Failed)
                    {
                        item.CreatedAt = now;
                        item.UpdatedAt = now;
                        item.QueuedAt = now;
                        itemsToAdd.Add(item);
                        addedCount++;
                    }
                }

                if (itemsToAdd.Any())
                {
                    _context.ContainerScanQueues.AddRange(itemsToAdd);
                    await _context.SaveChangesAsync();

                    // Clear change tracker
                    _context.ChangeTracker.Clear();

                    _logger.LogInformation("[CONTAINER-SCAN-QUEUE] ✅ Added {AddedCount} items to queue (skipped {SkippedCount} duplicates)",
                        addedCount, queueItems.Count - addedCount);
                }
                else
                {
                    _logger.LogDebug("[CONTAINER-SCAN-QUEUE] All {Count} items were duplicates, none added", queueItems.Count);
                }

                return addedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error adding batch to queue: {Count} items", queueItems.Count);
                throw;
            }
        }

        public async Task<List<ContainerScanQueue>> GetNextBatchAsync(int batchSize = 100)
        {
            try
            {
                var totalPending = await _context.ContainerScanQueues
                    .CountAsync(q => q.Status == ContainerScanQueueStatus.Pending);

                var pendingWithRetries = await _context.ContainerScanQueues
                    .CountAsync(q => q.Status == ContainerScanQueueStatus.Pending && q.RetryCount < q.MaxRetries);

                _logger.LogDebug("[CONTAINER-SCAN-QUEUE] Queue status: {TotalPending} pending, {WithRetries} eligible (RetryCount < MaxRetries)",
                    totalPending, pendingWithRetries);

                var items = await _context.ContainerScanQueues
                    .Where(q => q.Status == ContainerScanQueueStatus.Pending && q.RetryCount < q.MaxRetries)
                    .OrderByDescending(q => q.Priority)
                    .ThenBy(q => q.QueuedAt)
                    .Take(batchSize)
                    .AsNoTracking()
                    .ToListAsync();

                if (items.Count > 0)
                {
                    _logger.LogInformation("[CONTAINER-SCAN-QUEUE] Retrieved {Count} items from queue (oldest: {OldestQueuedAt}, highest priority: {MaxPriority})",
                        items.Count, items.Min(i => i.QueuedAt), items.Max(i => i.Priority));
                }
                else if (totalPending > 0)
                {
                    _logger.LogWarning("[CONTAINER-SCAN-QUEUE] ⚠️ No items retrieved despite {TotalPending} pending items - all may have exceeded max retries", totalPending);
                }

                return items;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error getting next batch");
                return new List<ContainerScanQueue>();
            }
        }

        public async Task MarkAsProcessingAsync(int id)
        {
            try
            {
                _logger.LogDebug("[CONTAINER-SCAN-QUEUE] 🔍 MarkAsProcessingAsync called for ID: {Id}", id);

                var item = await _context.ContainerScanQueues.FindAsync(id);
                if (item != null)
                {
                    _logger.LogDebug("[CONTAINER-SCAN-QUEUE] 📋 Found item: {ContainerNumber} (ID: {Id}, Current Status: {Status}, Current RetryCount: {RetryCount})",
                        item.ContainerNumber, id, item.Status, item.RetryCount);

                    var oldStatus = item.Status;
                    var oldRetryCount = item.RetryCount;

                    item.Status = ContainerScanQueueStatus.Processing;
                    item.ProcessedAt = DateTime.UtcNow;
                    item.RetryCount++;
                    item.UpdatedAt = DateTime.UtcNow;

                    _logger.LogDebug("[CONTAINER-SCAN-QUEUE] 💾 Attempting SaveChangesAsync for ID: {Id} (Status: {OldStatus} -> {NewStatus}, RetryCount: {OldRetryCount} -> {NewRetryCount})",
                        id, oldStatus, item.Status, oldRetryCount, item.RetryCount);

                    // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                    // This fixes the "Changes=0" bug where status updates were not persisting
                    _context.Entry(item).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                    var changesSaved = await _context.SaveChangesAsync();

                    // Clear change tracker
                    _context.ChangeTracker.Clear();

                    _logger.LogInformation("[CONTAINER-SCAN-QUEUE] ✅ Marked as Processing: {ContainerNumber} (ID: {Id}, Changes: {Changes}, Status: {Status}, RetryCount: {RetryCount})",
                        item.ContainerNumber, id, changesSaved, item.Status, item.RetryCount);
                }
                else
                {
                    _logger.LogWarning("[CONTAINER-SCAN-QUEUE] ⚠️ Item not found for ID: {Id} - Item may have been deleted or ID is invalid", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error marking as processing: {Id} - Exception: {ExceptionType}, Message: {Message}",
                    id, ex.GetType().Name, ex.Message);
                throw;
            }
        }

        public async Task MarkAsCompletedAsync(int id)
        {
            try
            {
                _logger.LogDebug("[CONTAINER-SCAN-QUEUE] 🔍 MarkAsCompletedAsync called for ID: {Id}", id);

                var item = await _context.ContainerScanQueues.FindAsync(id);
                if (item != null)
                {
                    _logger.LogDebug("[CONTAINER-SCAN-QUEUE] 📋 Found item: {ContainerNumber} (ID: {Id}, Current Status: {Status})",
                        item.ContainerNumber, id, item.Status);

                    var oldStatus = item.Status;
                    item.Status = ContainerScanQueueStatus.Completed;
                    item.CompletedAt = DateTime.UtcNow;
                    item.UpdatedAt = DateTime.UtcNow;

                    _logger.LogDebug("[CONTAINER-SCAN-QUEUE] 💾 Attempting SaveChangesAsync for ID: {Id} (Status: {OldStatus} -> {NewStatus})",
                        id, oldStatus, item.Status);

                    // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                    // This fixes the "Changes=0" bug where status updates were not persisting
                    _context.Entry(item).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                    var changesSaved = await _context.SaveChangesAsync();

                    // Clear change tracker
                    _context.ChangeTracker.Clear();

                    _logger.LogInformation("[CONTAINER-SCAN-QUEUE] ✅ Marked as Completed: {ContainerNumber} (ID: {Id}, Changes: {Changes}, Status: {Status})",
                        item.ContainerNumber, id, changesSaved, item.Status);
                }
                else
                {
                    _logger.LogWarning("[CONTAINER-SCAN-QUEUE] ⚠️ Item not found for ID: {Id} - Item may have been deleted or ID is invalid", id);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error marking as completed: {Id} - Exception: {ExceptionType}, Message: {Message}",
                    id, ex.GetType().Name, ex.Message);
                throw;
            }
        }

        public async Task MarkAsFailedAsync(int id, string errorMessage)
        {
            try
            {
                var item = await _context.ContainerScanQueues.FindAsync(id);
                if (item != null)
                {
                    item.Status = ContainerScanQueueStatus.Failed;
                    item.ErrorMessage = errorMessage;
                    item.UpdatedAt = DateTime.UtcNow;

                    // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                    _context.Entry(item).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                    await _context.SaveChangesAsync();

                    // Clear change tracker
                    _context.ChangeTracker.Clear();

                    _logger.LogWarning("[CONTAINER-SCAN-QUEUE] Marked as failed: {ContainerNumber}, Error: {ErrorMessage}",
                        item.ContainerNumber, errorMessage);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error marking as failed: {Id}", id);
                throw;
            }
        }

        public async Task UpdateRetryInfoAsync(int id, string errorMessage)
        {
            try
            {
                var item = await _context.ContainerScanQueues.FindAsync(id);
                if (item != null)
                {
                    item.Status = ContainerScanQueueStatus.Pending; // Reset to pending for retry
                    item.ErrorMessage = errorMessage;
                    item.RetryCount++;
                    item.UpdatedAt = DateTime.UtcNow;

                    // If max retries reached, mark as failed
                    if (item.RetryCount >= item.MaxRetries)
                    {
                        item.Status = ContainerScanQueueStatus.Failed;
                        item.CompletedAt = DateTime.UtcNow;
                        _logger.LogWarning("[CONTAINER-SCAN-QUEUE] ⚠️ Max retries reached for {ContainerNumber}, marking as Failed",
                            item.ContainerNumber);
                    }

                    // ✅ CRITICAL FIX: Explicitly mark entity as modified to ensure SaveChanges works
                    _context.Entry(item).State = Microsoft.EntityFrameworkCore.EntityState.Modified;

                    await _context.SaveChangesAsync();

                    // Clear change tracker
                    _context.ChangeTracker.Clear();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error updating retry info: {Id}", id);
                throw;
            }
        }

        public async Task<bool> IsInQueueAsync(string containerNumber, string scannerType, string? inspectionId)
        {
            try
            {
                return await _context.ContainerScanQueues
                    .AnyAsync(q =>
                        q.ContainerNumber == containerNumber &&
                        q.ScannerType == scannerType &&
                        q.InspectionId == inspectionId &&
                        (q.Status == ContainerScanQueueStatus.Pending || q.Status == ContainerScanQueueStatus.Processing));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error checking if in queue: {ContainerNumber}", containerNumber);
                return false;
            }
        }

        public async Task<ContainerScanQueue?> GetByKeyAsync(string containerNumber, string scannerType, string? inspectionId)
        {
            try
            {
                return await _context.ContainerScanQueues
                    .FirstOrDefaultAsync(q =>
                        q.ContainerNumber == containerNumber &&
                        q.ScannerType == scannerType &&
                        q.InspectionId == inspectionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error getting by key: {ContainerNumber}", containerNumber);
                return null;
            }
        }

        public async Task<ContainerScanQueueStatistics> GetQueueStatisticsAsync()
        {
            try
            {
                var stats = new ContainerScanQueueStatistics();

                // ✅ MEMORY OPTIMIZATION: Filter to last 24 hours to reduce buffer pool usage
                // Only load recent data into SQL Server memory instead of entire table
                var oneDayAgo = DateTime.UtcNow.AddDays(-1);
                var baseQuery = _context.ContainerScanQueues
                    .Where(q => q.QueuedAt >= oneDayAgo)
                    .AsNoTracking(); // Read-only query, no change tracking

                // ✅ Use database aggregation instead of loading all data into memory
                stats.TotalPending = await baseQuery
                    .CountAsync(q => q.Status == ContainerScanQueueStatus.Pending);

                stats.TotalProcessing = await baseQuery
                    .CountAsync(q => q.Status == ContainerScanQueueStatus.Processing);

                stats.TotalCompleted = await baseQuery
                    .CountAsync(q => q.Status == ContainerScanQueueStatus.Completed);

                stats.TotalFailed = await baseQuery
                    .CountAsync(q => q.Status == ContainerScanQueueStatus.Failed);

                // Priority breakdowns (using database aggregation)
                stats.HighPriority = await baseQuery
                    .CountAsync(q => q.Priority >= 2);

                stats.NormalPriority = await baseQuery
                    .CountAsync(q => q.Priority == 1);

                stats.LowPriority = await baseQuery
                    .CountAsync(q => q.Priority == 0);

                // Scanner type breakdowns (using database aggregation)
                stats.ByScannerTypeFS6000 = await baseQuery
                    .CountAsync(q => q.ScannerType == CommonScannerTypes.FS6000);

                stats.ByScannerTypeASE = await baseQuery
                    .CountAsync(q => q.ScannerType == CommonScannerTypes.ASE);

                stats.ByScannerTypeHeimannSmith = await baseQuery
                    .CountAsync(q => q.ScannerType == CommonScannerTypes.HeimannSmith);

                // ✅ Only load pending items for min/average calculations (still filtered to last 24 hours)
                var pendingQuery = baseQuery.Where(q => q.Status == ContainerScanQueueStatus.Pending);
                var pendingCount = await pendingQuery.CountAsync();

                if (pendingCount > 0)
                {
                    // Get oldest pending item using database query
                    var oldestPending = await pendingQuery
                        .OrderBy(q => q.QueuedAt)
                        .Select(q => q.QueuedAt)
                        .FirstOrDefaultAsync();

                    if (oldestPending != default)
                    {
                        stats.OldestPendingQueuedAt = oldestPending;

                        // Calculate average wait time using database aggregation
                        var pendingItems = await pendingQuery
                            .Select(q => new { q.QueuedAt })
                            .ToListAsync();

                        if (pendingItems.Any())
                        {
                            stats.AverageWaitTimeMinutes = pendingItems
                                .Average(q => (DateTime.UtcNow - q.QueuedAt).TotalMinutes);
                        }
                    }
                }

                // Calculate success rate
                var completedCount = stats.TotalCompleted;
                var totalProcessed = completedCount + stats.TotalFailed;
                if (totalProcessed > 0)
                {
                    stats.SuccessRate = (double)completedCount / totalProcessed * 100;
                }

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error getting queue statistics");
                return new ContainerScanQueueStatistics();
            }
        }

        public async Task<int> CleanupOldItemsAsync(int daysToKeep = 7)
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-daysToKeep);

                var itemsToRemove = await _context.ContainerScanQueues
                    .Where(q =>
                        (q.Status == ContainerScanQueueStatus.Completed || q.Status == ContainerScanQueueStatus.Failed) &&
                        q.CompletedAt.HasValue &&
                        q.CompletedAt.Value < cutoffDate)
                    .ToListAsync();

                if (itemsToRemove.Any())
                {
                    _context.ContainerScanQueues.RemoveRange(itemsToRemove);
                    var removed = await _context.SaveChangesAsync();

                    // Clear change tracker
                    _context.ChangeTracker.Clear();

                    _logger.LogInformation("[CONTAINER-SCAN-QUEUE] ✅ Cleaned up {Count} old items (older than {Days} days)",
                        removed, daysToKeep);

                    return removed;
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error cleaning up old items");
                return 0;
            }
        }

        public async Task<int> RecoverStuckProcessingItemsAsync(int timeoutMinutes = 30)
        {
            try
            {
                var cutoffTime = DateTime.UtcNow.AddMinutes(-timeoutMinutes);

                var stuckItems = await _context.ContainerScanQueues
                    .AsTracking()
                    .Where(q =>
                        q.Status == ContainerScanQueueStatus.Processing &&
                        q.ProcessedAt.HasValue &&
                        q.ProcessedAt.Value < cutoffTime)
                    .ToListAsync();

                if (stuckItems.Any())
                {
                    foreach (var item in stuckItems)
                    {
                        item.Status = ContainerScanQueueStatus.Pending;
                        item.ProcessedAt = null; // Reset processed time
                        item.UpdatedAt = DateTime.UtcNow;
                    }

                    var recovered = await _context.SaveChangesAsync();

                    // Clear change tracker
                    _context.ChangeTracker.Clear();

                    _logger.LogWarning("[CONTAINER-SCAN-QUEUE] ⚠️ Recovered {Count} stuck items (processing for >{Minutes} minutes)",
                        recovered, timeoutMinutes);

                    return recovered;
                }

                return 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CONTAINER-SCAN-QUEUE] ❌ Error recovering stuck items");
                return 0;
            }
        }

        public async Task SaveChangesAsync()
        {
            await _context.SaveChangesAsync();
            _context.ChangeTracker.Clear();
        }
    }
}

