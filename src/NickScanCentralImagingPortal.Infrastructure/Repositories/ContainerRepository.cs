using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class ContainerRepository : Repository<Container>, IContainerRepository
    {
        private readonly ILogger<ContainerRepository> _logger;

        public ContainerRepository(ApplicationDbContext context, ILogger<ContainerRepository> logger) : base(context)
        {
            _logger = logger;
        }

        // Basic CRUD operations - using existing Container entity
        public async Task<Container?> GetByContainerIdAsync(string containerId)
        {
            return await _context.Containers
                .FirstOrDefaultAsync(c => c.ContainerId == containerId);
        }

        public async Task<Container?> GetContainerByIdAsync(int id)
        {
            return await _context.Containers
                .FirstOrDefaultAsync(c => c.Id == id);
        }

        public async Task<IEnumerable<Container>> GetByScannerTypeAsync(string scannerType)
        {
            return await _context.Containers
                .Where(c => c.ScannerType == scannerType)
                .ToListAsync();
        }

        public async Task<IEnumerable<Container>> GetByProcessingStatusAsync(string status)
        {
            return await _context.Containers
                .Where(c => c.ProcessingStatus == status)
                .ToListAsync();
        }

        // Mock implementations for new functionality - return empty results for now
        public Task<ContainerSearchResult> SearchContainersAsync(ContainerSearchCriteria criteria)
        {
            // Mock implementation - return empty results
            var result = new ContainerSearchResult
            {
                Containers = new List<ContainerDetails>(),
                TotalCount = 0,
                Page = criteria.Page,
                PageSize = criteria.PageSize
            };
            return Task.FromResult(result);
        }

        public async Task<IEnumerable<Container>> GetContainersReadyForProcessingAsync(int limit = 50)
        {
            return await _context.Containers
                .Where(c => c.ProcessingStatus == "Pending")
                .Take(limit)
                .ToListAsync();
        }

        public Task<IEnumerable<Container>> GetContainersByOperatorAsync(string operatorId, string? status = null, int limit = 50)
        {
            // Mock implementation - return empty results
            return Task.FromResult<IEnumerable<Container>>(Array.Empty<Container>());
        }

        public async Task<bool> UpdateContainerStatusAsync(int id, ContainerStatusUpdate statusUpdate)
        {
            // ✅ AsTracking() required since NoTracking is now default for updates
            var container = await _context.Containers.AsTracking().FirstOrDefaultAsync(c => c.Id == id);
            if (container != null)
            {
                container.ProcessingStatus = statusUpdate.Status;
                container.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();
                return true;
            }
            return false;
        }

        public async Task<bool> BulkUpdateContainerStatusAsync(IEnumerable<int> containerIds, ContainerStatusUpdate statusUpdate)
        {
            // ✅ AsTracking() required since NoTracking is now default for updates
            var containers = await _context.Containers
                .AsTracking()
                .Where(c => containerIds.Contains(c.Id))
                .ToListAsync();

            foreach (var container in containers)
            {
                container.ProcessingStatus = statusUpdate.Status;
                container.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();
            return true;
        }

        // Mock implementations for assignment operations
        public async Task<bool> AssignContainerAsync(int id, ContainerAssignment assignment)
        {
            // Mock implementation - just update status
            return await UpdateContainerStatusAsync(id, new ContainerStatusUpdate { Status = "Assigned" });
        }

        public async Task<bool> UnassignContainerAsync(int id)
        {
            // Mock implementation - just update status
            return await UpdateContainerStatusAsync(id, new ContainerStatusUpdate { Status = "Pending" });
        }

        // Mock implementations for processing operations
        public async Task<bool> StartContainerProcessingAsync(int id, ProcessingStartRequest request)
        {
            return await UpdateContainerStatusAsync(id, new ContainerStatusUpdate { Status = "Processing" });
        }

        public async Task<bool> CompleteContainerProcessingAsync(int id, ProcessingCompleteRequest request)
        {
            return await UpdateContainerStatusAsync(id, new ContainerStatusUpdate { Status = "Completed" });
        }

        public async Task<bool> CancelContainerProcessingAsync(int id, string reason)
        {
            return await UpdateContainerStatusAsync(id, new ContainerStatusUpdate { Status = "Cancelled" });
        }

        // Mock implementations for history and tracking
        public Task<IEnumerable<ContainerProcessingHistory>> GetContainerProcessingHistoryAsync(int id)
        {
            return Task.FromResult<IEnumerable<ContainerProcessingHistory>>(Array.Empty<ContainerProcessingHistory>());
        }

        public Task<bool> AddProcessingHistoryEntryAsync(ContainerProcessingHistory history)
        {
            return Task.FromResult(true);
        }

        // Mock implementations for statistics and reporting
        public async Task<ContainerStatistics> GetContainerStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null)
        {
            var containers = await _context.Containers.ToListAsync();

            return new ContainerStatistics
            {
                TotalContainers = containers.Count,
                ContainersProcessed = containers.Count(c => c.ProcessingStatus == "Completed"),
                ContainersPending = containers.Count(c => c.ProcessingStatus == "Pending"),
                ContainersInProgress = containers.Count(c => c.ProcessingStatus == "Processing"),
                ContainersFailed = containers.Count(c => c.ProcessingStatus == "Failed"),
                ContainersCompleted = containers.Count(c => c.ProcessingStatus == "Completed"),
                AverageProcessingTime = 15.5, // Mock data
                ProcessingSuccessRate = 95.2, // Mock data
                StatisticsDate = DateTime.UtcNow
            };
        }

        public async Task<ContainerQueueStatus> GetContainerQueueStatusAsync()
        {
            var containers = await _context.Containers.ToListAsync();

            return new ContainerQueueStatus
            {
                TotalInQueue = containers.Count(c => c.ProcessingStatus == "Pending"),
                HighPriorityCount = 0, // Mock data
                MediumPriorityCount = 0, // Mock data
                LowPriorityCount = containers.Count(c => c.ProcessingStatus == "Pending"),
                AssignedCount = 0, // Mock data
                UnassignedCount = containers.Count(c => c.ProcessingStatus == "Pending"),
                ProcessingCount = containers.Count(c => c.ProcessingStatus == "Processing"),
                AverageWaitTime = 5.5, // Mock data
                QueueItems = containers.Where(c => c.ProcessingStatus == "Pending")
                    .Select(MapToQueueItem)
                    .ToList()
            };
        }

        public Task<IEnumerable<ContainerProcessingMetrics>> GetContainerProcessingMetricsAsync(int id)
        {
            return Task.FromResult<IEnumerable<ContainerProcessingMetrics>>(Array.Empty<ContainerProcessingMetrics>());
        }

        // Mock implementations for workflow management
        public Task<IEnumerable<ContainerWorkflow>> GetContainerWorkflowsAsync()
        {
            return Task.FromResult<IEnumerable<ContainerWorkflow>>(Array.Empty<ContainerWorkflow>());
        }

        public Task<ContainerWorkflow?> GetContainerWorkflowAsync(string clearanceType)
        {
            return Task.FromResult<ContainerWorkflow?>(null);
        }

        public Task<bool> UpdateContainerWorkflowAsync(ContainerWorkflow workflow)
        {
            return Task.FromResult(true);
        }

        // Mock implementations for bulk operations
        public async Task<bool> BulkAssignContainersAsync(IEnumerable<int> containerIds, ContainerAssignment assignment)
        {
            return await BulkUpdateContainerStatusAsync(containerIds, new ContainerStatusUpdate { Status = "Assigned" });
        }

        public async Task<bool> BulkStartProcessingAsync(IEnumerable<int> containerIds, ProcessingStartRequest request)
        {
            return await BulkUpdateContainerStatusAsync(containerIds, new ContainerStatusUpdate { Status = "Processing" });
        }

        private ContainerDetails MapToContainerDetails(Container container)
        {
            return new ContainerDetails
            {
                Id = container.Id,
                ContainerNumber = container.ContainerId,
                Status = container.ProcessingStatus,
                ClearanceType = "Unknown", // Mock data
                ConsigneeName = "Unknown", // Mock data
                ShipperName = "Unknown", // Mock data
                CreatedAt = container.CreatedAt,
                UpdatedAt = container.UpdatedAt ?? container.CreatedAt,
                AssignedOperator = null, // Mock data
                AssignedAt = null, // Mock data
                ProcessingStartedAt = null, // Mock data
                ProcessingCompletedAt = null, // Mock data
                ScannerType = container.ScannerType,
                ImageCount = container.Images?.Count ?? 0,
                ProcessingResultsCount = container.ProcessingResults?.Count ?? 0
            };
        }

        private ContainerQueueItem MapToQueueItem(Container container)
        {
            return new ContainerQueueItem
            {
                ContainerId = container.Id,
                ContainerNumber = container.ContainerId,
                Status = container.ProcessingStatus,
                Priority = 1, // Mock data - Low priority
                AssignedOperator = null, // Mock data
                QueuedAt = container.CreatedAt,
                AssignedAt = null, // Mock data
                StartedAt = null, // Mock data
                ClearanceType = "Unknown", // Mock data
                Comments = null // Mock data
            };
        }
    }
}
