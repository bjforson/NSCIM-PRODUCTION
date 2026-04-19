using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IScannerRepository
    {
        // Basic CRUD operations
        Task<IEnumerable<ScannerStatus>> GetAllScannersAsync();
        Task<ScannerStatus?> GetScannerByIdAsync(int id);
        Task<ScannerStatus> CreateScannerAsync(ScannerStatus scanner);
        Task<ScannerStatus> UpdateScannerAsync(ScannerStatus scanner);
        Task<bool> DeleteScannerAsync(int id);

        // Scanner operations
        Task<bool> StartScannerAsync(int id);
        Task<bool> StopScannerAsync(int id);
        Task<bool> RestartScannerAsync(int id);
        Task<bool> UpdateScannerConfigurationAsync(int id, ScannerConfiguration config);
        Task<ScannerConfiguration?> GetScannerConfigurationAsync(int id);

        // Statistics and monitoring
        Task<ScannerStatistics?> GetScannerStatisticsAsync(int id);
        Task<IEnumerable<ScannerLog>> GetScannerLogsAsync(int id, int page = 1, int pageSize = 50);
        Task<bool> AddScannerLogAsync(ScannerLog log);

        // Connection and health
        Task<ConnectionTestResult> TestScannerConnectionAsync(int id);
        Task<bool> UpdateScannerHeartbeatAsync(int id);
        Task<IEnumerable<ScannerStatus>> GetScannersByStateAsync(ScannerState state);
        Task<IEnumerable<ScannerStatus>> GetActiveScannersAsync();

        // Bulk operations
        Task<bool> BulkUpdateScannerStateAsync(IEnumerable<int> scannerIds, ScannerState state);
        Task<Dictionary<int, ScannerStatistics>> GetBulkScannerStatisticsAsync(IEnumerable<int> scannerIds);
    }
}
