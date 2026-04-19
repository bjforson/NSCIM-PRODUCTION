using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IIcumApiService
    {
        Task<IcumApiResponse<BoeSelectivityResponse>> GetApiStatusAsync();
        Task<IcumApiResponse<BoeSelectivityResponse>> FetchBatchDataAsync(DateTime startDate, DateTime endDate);
        Task<IcumApiResponse<BoeScanDocument>> FetchContainerDataAsync(string containerNumber);
        Task<IcumApiResponse<BoeSelectivityResponse>> FetchBOEDataAsync(string declarationNumber);
        Task<IcumApiResponse<object>> SubmitScanResultAsync(BoeScanDocument scanResult);

        // Enhanced functionality
        Task<IcumHealthStatus> GetHealthStatusAsync();
        Task<bool> IsApiHealthyAsync();
        Task<ServiceMetrics> GetServiceMetricsAsync();
    }

    public class IcumHealthStatus
    {
        public bool IsHealthy { get; set; }
        public string CircuitBreakerState { get; set; } = string.Empty;
        public int ConsecutiveFailures { get; set; }
        public DateTime LastFailureTime { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public string? Error { get; set; }
        public DateTime LastSuccessfulCall { get; set; }
    }

    public class ServiceMetrics
    {
        public int TotalApiCalls { get; set; }
        public int SuccessfulCalls { get; set; }
        public int FailedCalls { get; set; }
        public double SuccessRate { get; set; }
        public DateTime LastSuccessfulCall { get; set; }
        public DateTime LastFailedCall { get; set; }
        public int CircuitBreakerFailures { get; set; }
        public int RetryAttempts { get; set; }
        public int BackupFilesCreated { get; set; }
        public long TotalDataDownloadedBytes { get; set; }
    }
}
