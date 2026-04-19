using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Core.DTOs.Monitoring
{
    public class ServiceHealthStatus
    {
        public string ServiceName { get; set; } = string.Empty;
        public HealthStatus Status { get; set; }
        public DateTime LastChecked { get; set; }
        public long ResponseTimeMs { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> AdditionalInfo { get; set; } = new();
    }

    public class SystemHealthSummary
    {
        public HealthStatus OverallStatus { get; set; }
        public DateTime Timestamp { get; set; }
        public int TotalServices { get; set; }
        public int HealthyServices { get; set; }
        public int DegradedServices { get; set; }
        public int UnhealthyServices { get; set; }
        public Dictionary<string, ServiceHealthStatus> ServiceStatuses { get; set; } = new();
    }
}
