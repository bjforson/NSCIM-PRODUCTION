using NickScanCentralImagingPortal.Core.DTOs.Monitoring;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IComprehensiveHealthCheckService
    {
        Dictionary<string, ServiceHealthStatus> GetServiceStatuses();
        ServiceHealthStatus GetServiceStatus(string serviceName);
        SystemHealthSummary GetSystemHealthSummary();
    }
}
