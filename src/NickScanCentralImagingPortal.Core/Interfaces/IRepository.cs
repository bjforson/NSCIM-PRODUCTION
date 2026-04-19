using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IRepository<T> where T : class
    {
        Task<T?> GetByIdAsync(int id);
        Task<IEnumerable<T>> GetAllAsync();
        Task<T> AddAsync(T entity);
        Task UpdateAsync(T entity);
        Task DeleteAsync(int id);
        Task<bool> ExistsAsync(int id);
    }


    public interface IContainerImageRepository : IRepository<ContainerImage>
    {
        Task<IEnumerable<ContainerImage>> GetByContainerIdAsync(int containerId);
        Task<IEnumerable<ContainerImage>> GetByProcessingStatusAsync(string status);

        // Additional methods required by ImageProcessingController
        Task<ImageSearchResult> SearchImagesAsync(ImageSearchCriteria criteria);
        Task<ImageDetails?> GetImageByIdAsync(int id);
        Task<ImageProcessingStatistics> GetImageProcessingStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null);
        Task<ImageQualityMetrics> GetImageQualityMetricsAsync();
        Task<IEnumerable<ImageProcessingHistory>> GetImageProcessingHistoryAsync(int imageId);
        Task<byte[]?> GetImageBytesAsync(int imageId);
        Task<IEnumerable<ImageDetails>> GetImagesByContainerAsync(string containerNumber);
        Task<ImageProcessingQueueStatus> GetProcessingQueueStatusAsync();
        Task UpdateImageMetadataAsync(int imageId, ImageMetadataUpdate metadataUpdate);
    }

    public interface IProcessingResultRepository : IRepository<ProcessingResult>
    {
        Task<IEnumerable<ProcessingResult>> GetByContainerIdAsync(int containerId);
        Task<IEnumerable<ProcessingResult>> GetByResultTypeAsync(string resultType);
    }
}
