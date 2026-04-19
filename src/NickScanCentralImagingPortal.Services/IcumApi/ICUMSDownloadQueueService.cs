using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    public interface IICUMSDownloadQueueService
    {
        /// <summary>
        /// Add container to download queue if not already present
        /// </summary>
        Task<bool> EnqueueContainerAsync(string containerNumber, int priority = 1, string? requestSource = null, string? requestedBy = null);

        /// <summary>
        /// Add multiple containers to queue
        /// </summary>
        Task<int> EnqueueContainersAsync(List<string> containerNumbers, int priority = 1, string? requestSource = null);

        /// <summary>
        /// Check if container needs to be queued (not in DB and not in queue)
        /// </summary>
        Task<bool> ShouldEnqueueAsync(string containerNumber);

        /// <summary>
        /// Get queue statistics
        /// </summary>
        Task<QueueStatistics> GetStatisticsAsync();

        /// <summary>
        /// Update priority for a container in queue
        /// </summary>
        Task UpdatePriorityAsync(string containerNumber, int priority);
    }

    public class ICUMSDownloadQueueService : IICUMSDownloadQueueService
    {
        private readonly IICUMSDownloadQueueRepository _queueRepository;
        private readonly IIcumDownloadsRepository _icumDownloadsRepository;
        private readonly ILogger<ICUMSDownloadQueueService> _logger;

        public ICUMSDownloadQueueService(
            IICUMSDownloadQueueRepository queueRepository,
            IIcumDownloadsRepository icumDownloadsRepository,
            ILogger<ICUMSDownloadQueueService> logger)
        {
            _queueRepository = queueRepository;
            _icumDownloadsRepository = icumDownloadsRepository;
            _logger = logger;
        }

        public async Task<bool> EnqueueContainerAsync(
            string containerNumber,
            int priority = 1,
            string? requestSource = null,
            string? requestedBy = null)
        {
            try
            {
                // ✅ FIX: Handle multiple container numbers (comma-separated or concatenated)
                // Examples: "MRKU9344715,MRKU8072459" or "MRKU9344715MRKU8072459"
                var containerNumbers = SplitContainerNumbers(containerNumber);

                if (containerNumbers.Count == 0)
                {
                    _logger.LogWarning("[ICUMS-QUEUE] No valid container numbers found in: {ContainerNumber}", containerNumber);
                    return false;
                }

                var enqueuedCount = 0;
                foreach (var normalizedContainerNumber in containerNumbers)
                {
                    // Check if should enqueue
                    if (!await ShouldEnqueueAsync(normalizedContainerNumber))
                    {
                        _logger.LogDebug("[ICUMS-QUEUE] Container {ContainerNumber} already has data or is in queue, skipping",
                            normalizedContainerNumber);
                        continue;
                    }

                    // ✅ SQL Server 2014 FIX: Truncate to 19 chars max to avoid SQL Server truncation errors
                    // (SQL Server nvarchar(20) can have issues with exactly 20 chars due to encoding)
                    var finalContainerNumber = normalizedContainerNumber;
                    if (finalContainerNumber.Length > 19)
                    {
                        finalContainerNumber = finalContainerNumber.Substring(0, 19);
                        _logger.LogWarning("[ICUMS-QUEUE] Container number truncated to 19 chars (safety margin): {Truncated} (original: {Original})",
                            finalContainerNumber, normalizedContainerNumber);
                    }

                    var queueItem = new ICUMSDownloadQueue
                    {
                        ContainerNumber = finalContainerNumber,
                        Priority = priority,
                        RequestSource = requestSource ?? RequestSource.Background,
                        RequestedBy = requestedBy,
                        QueuedAt = DateTime.UtcNow,
                        Status = QueueStatus.Pending
                    };

                    await _queueRepository.AddToQueueAsync(queueItem);
                    enqueuedCount++;
                }

                return enqueuedCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error enqueueing container: {ContainerNumber}", containerNumber);
                return false;
            }
        }

        /// <summary>
        /// Splits container numbers that may be comma-separated or concatenated
        /// Standard container format: 11 characters (4 letters + 7 digits, e.g., "MRKU9344715")
        /// </summary>
        private List<string> SplitContainerNumbers(string containerNumber)
        {
            var result = new List<string>();

            if (string.IsNullOrWhiteSpace(containerNumber))
            {
                return result;
            }

            // First, check if it's comma-separated
            if (containerNumber.Contains(','))
            {
                var parts = containerNumber.Split(',', StringSplitOptions.RemoveEmptyEntries);
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        result.Add(trimmed);
                    }
                }
                return result;
            }

            // Check if it's concatenated containers (e.g., "MRKU9344715MRKU8072459" = 22 chars = 2 × 11)
            // Standard container: 4 letters + 7 digits = 11 characters
            var trimmedInput = containerNumber.Trim();

            // If length is exactly 22, it's likely two concatenated 11-char containers
            if (trimmedInput.Length == 22)
            {
                var first = trimmedInput.Substring(0, 11);
                var second = trimmedInput.Substring(11, 11);

                // Validate both look like container numbers (4 letters + 7 digits)
                if (IsValidContainerFormat(first) && IsValidContainerFormat(second))
                {
                    _logger.LogInformation("[ICUMS-QUEUE] Detected concatenated containers: {Original} → {First}, {Second}",
                        containerNumber, first, second);
                    result.Add(first);
                    result.Add(second);
                    return result;
                }
            }

            // If length is a multiple of 11, try to split into 11-char chunks
            if (trimmedInput.Length > 11 && trimmedInput.Length % 11 == 0)
            {
                var chunkCount = trimmedInput.Length / 11;
                var allValid = true;
                var chunks = new List<string>();

                for (int i = 0; i < chunkCount; i++)
                {
                    var chunk = trimmedInput.Substring(i * 11, 11);
                    if (IsValidContainerFormat(chunk))
                    {
                        chunks.Add(chunk);
                    }
                    else
                    {
                        allValid = false;
                        break;
                    }
                }

                if (allValid && chunks.Count > 1)
                {
                    _logger.LogInformation("[ICUMS-QUEUE] Detected {Count} concatenated containers from: {Original}",
                        chunks.Count, containerNumber);
                    return chunks;
                }
            }

            // Single container number (or couldn't be split)
            result.Add(trimmedInput);
            return result;
        }

        /// <summary>
        /// Validates if a string matches standard container number format: 4 letters + 7 digits (11 chars total)
        /// </summary>
        private bool IsValidContainerFormat(string containerNumber)
        {
            if (string.IsNullOrEmpty(containerNumber) || containerNumber.Length != 11)
            {
                return false;
            }

            // First 4 characters should be letters
            for (int i = 0; i < 4; i++)
            {
                if (!char.IsLetter(containerNumber[i]))
                {
                    return false;
                }
            }

            // Next 7 characters should be digits
            for (int i = 4; i < 11; i++)
            {
                if (!char.IsDigit(containerNumber[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public async Task<int> EnqueueContainersAsync(
            List<string> containerNumbers,
            int priority = 1,
            string? requestSource = null)
        {
            var enqueuedCount = 0;

            foreach (var containerNumber in containerNumbers)
            {
                var enqueued = await EnqueueContainerAsync(containerNumber, priority, requestSource);
                if (enqueued)
                {
                    enqueuedCount++;
                }
            }

            _logger.LogInformation("[ICUMS-QUEUE] Enqueued {Count} out of {Total} containers",
                enqueuedCount, containerNumbers.Count);

            return enqueuedCount;
        }

        public async Task<bool> ShouldEnqueueAsync(string containerNumber)
        {
            try
            {
                // Check if data already exists in BOEDocuments
                var hasData = await _icumDownloadsRepository.ContainerHasICUMSDataAsync(containerNumber);
                if (hasData)
                {
                    return false; // Already have data
                }

                // Check if already in queue
                var inQueue = await _queueRepository.IsInQueueAsync(containerNumber);
                if (inQueue)
                {
                    return false; // Already queued
                }

                return true; // Should enqueue
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-QUEUE] Error checking if should enqueue: {ContainerNumber}", containerNumber);
                return false;
            }
        }

        public async Task<QueueStatistics> GetStatisticsAsync()
        {
            return await _queueRepository.GetQueueStatisticsAsync();
        }

        public async Task UpdatePriorityAsync(string containerNumber, int priority)
        {
            await _queueRepository.UpdatePriorityAsync(containerNumber, priority);
        }
    }
}
