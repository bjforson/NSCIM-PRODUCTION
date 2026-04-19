using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IContainerRepository : IRepository<Container>
    {
        // Basic CRUD operations
        Task<Container?> GetByContainerIdAsync(string containerId);
        Task<Container?> GetContainerByIdAsync(int id);
        Task<IEnumerable<Container>> GetByScannerTypeAsync(string scannerType);
        Task<IEnumerable<Container>> GetByProcessingStatusAsync(string status);

        // Search and filtering
        Task<ContainerSearchResult> SearchContainersAsync(ContainerSearchCriteria criteria);
        Task<IEnumerable<Container>> GetContainersReadyForProcessingAsync(int limit = 50);
        Task<IEnumerable<Container>> GetContainersByOperatorAsync(string operatorId, string? status = null, int limit = 50);

        // Status management
        Task<bool> UpdateContainerStatusAsync(int id, ContainerStatusUpdate statusUpdate);
        Task<bool> BulkUpdateContainerStatusAsync(IEnumerable<int> containerIds, ContainerStatusUpdate statusUpdate);

        // Assignment operations
        Task<bool> AssignContainerAsync(int id, ContainerAssignment assignment);
        Task<bool> UnassignContainerAsync(int id);

        // Processing operations
        Task<bool> StartContainerProcessingAsync(int id, ProcessingStartRequest request);
        Task<bool> CompleteContainerProcessingAsync(int id, ProcessingCompleteRequest request);
        Task<bool> CancelContainerProcessingAsync(int id, string reason);

        // History and tracking
        Task<IEnumerable<ContainerProcessingHistory>> GetContainerProcessingHistoryAsync(int id);
        Task<bool> AddProcessingHistoryEntryAsync(ContainerProcessingHistory history);

        // Statistics and reporting
        Task<ContainerStatistics> GetContainerStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null);
        Task<ContainerQueueStatus> GetContainerQueueStatusAsync();
        Task<IEnumerable<ContainerProcessingMetrics>> GetContainerProcessingMetricsAsync(int id);

        // Workflow management
        Task<IEnumerable<ContainerWorkflow>> GetContainerWorkflowsAsync();
        Task<ContainerWorkflow?> GetContainerWorkflowAsync(string clearanceType);
        Task<bool> UpdateContainerWorkflowAsync(ContainerWorkflow workflow);

        // Bulk operations
        Task<bool> BulkAssignContainersAsync(IEnumerable<int> containerIds, ContainerAssignment assignment);
        Task<bool> BulkStartProcessingAsync(IEnumerable<int> containerIds, ProcessingStartRequest request);
    }
}
