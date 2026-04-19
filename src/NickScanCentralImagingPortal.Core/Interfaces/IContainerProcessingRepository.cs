using NickScanCentralImagingPortal.Core.DTOs.ContainerProcessing;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Repository for container processing operations
    /// </summary>
    public interface IContainerProcessingRepository
    {
        /// <summary>
        /// Get all container groups with smart grouping by clearance type
        /// IM/EX: Group by BOE Number
        /// CMR: Group by BL Number
        /// </summary>
        Task<List<ContainerGroupDto>> GetContainerGroupsAsync(string? clearanceTypeFilter = null, int page = 1, int pageSize = 50);

        /// <summary>
        /// Get summary statistics for container processing
        /// </summary>
        Task<ContainerProcessingSummaryDto> GetSummaryStatisticsAsync();

        /// <summary>
        /// Get containers for a specific group
        /// </summary>
        Task<ContainerGroupDto?> GetContainerGroupDetailsAsync(string clearanceType, string groupingValue);
    }
}

