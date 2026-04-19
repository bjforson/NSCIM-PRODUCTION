using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IIcumRepository
    {
        // Existing methods
        Task<IcumContainerData?> GetContainerDataAsync(string containerNumber);
        Task<List<IcumContainerData>> GetBatchDataAsync(DateTime startDate, DateTime endDate, int? limit = null);
        Task SaveContainerDataAsync(IcumContainerData containerData);
        Task SaveBatchDataAsync(List<IcumContainerData> batchData);
        Task SaveContainerDataWithItemsAsync(IcumContainerData containerData, List<IcumManifestItem> manifestItems);
        Task<List<IcumContainerData>> GetAllContainerDataAsync();
        Task DeleteContainerDataAsync(string containerNumber);
        Task<bool> ContainerDataExistsAsync(string containerNumber);
        Task SaveBatchLogAsync(IcumBatchLog batchLog);
        Task<List<IcumBatchLog>> GetBatchLogsAsync(int limit = 1000);
        Task<List<IcumManifestItem>> GetManifestItemsByContainerAsync(string containerNumber);
        Task<List<IcumManifestItem>> GetManifestItemsByHsCodeAsync(string hsCode);

        // New ICUMS Data Management methods
        Task<IcumContainerSearchResult> SearchIcumContainersAsync(IcumContainerSearchCriteria criteria);
        Task<IcumContainerDetails?> GetIcumContainerByIdAsync(int id);
        Task<IEnumerable<IcumManifestItemDetails>> GetIcumManifestItemsAsync(int containerId);
        Task<IcumDataStatistics> GetIcumDataStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null);
        Task<IcumProcessingStatus> GetIcumProcessingStatusAsync();
        Task<IcumDataQualityMetrics> GetIcumDataQualityMetricsAsync();
        Task<IcumProcessingTrends> GetIcumProcessingTrendsAsync(int days = 30);
        Task<IcumDataIntegrityReport> ValidateIcumDataIntegrityAsync();
        Task<IEnumerable<IcumExportData>> ExportIcumDataAsync(IcumDataExportRequest request);
    }
}
