using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Configuration for all background services with enable/disable flags
    /// </summary>
    public class BackgroundServicesConfiguration
    {
        public ServiceConfig ServiceOrchestrator { get; set; } = new();
        public HealthCheckServiceConfig ComprehensiveHealthCheck { get; set; } = new();
        public ServiceConfig FS6000BackgroundService { get; set; } = new();
        public ServiceConfig AseDatabaseSyncService { get; set; } = new();
        public BatchServiceConfig IcumBackgroundService { get; set; } = new();
        public ProcessServiceConfig ICUMSDownloadBackgroundService { get; set; } = new();
        public ScanServiceConfig IcumFileScannerService { get; set; } = new();
        public ProcessServiceConfig IcumJsonIngestionService { get; set; } = new();
        public TransferServiceConfig IcumDataTransferService { get; set; } = new();
        public MonitorServiceConfig ContainerCompletenessService { get; set; } = new();
        public ProcessServiceConfig ManualBOESelectivityService { get; set; } = new();
        public MappingServiceConfig ContainerDataMapperService { get; set; } = new();
        public SubmissionServiceConfig ICUMSSubmissionService { get; set; } = new();
        public BroadcastServiceConfig DashboardBroadcastService { get; set; } = new();

        /// <summary>
        /// Get all service configurations as a dictionary
        /// </summary>
        public Dictionary<string, ServiceConfig> GetAllServices()
        {
            return new Dictionary<string, ServiceConfig>
            {
                { nameof(ServiceOrchestrator), ServiceOrchestrator },
                { nameof(ComprehensiveHealthCheck), ComprehensiveHealthCheck },
                { nameof(FS6000BackgroundService), FS6000BackgroundService },
                { nameof(AseDatabaseSyncService), AseDatabaseSyncService },
                { nameof(IcumBackgroundService), IcumBackgroundService },
                { nameof(ICUMSDownloadBackgroundService), ICUMSDownloadBackgroundService },
                { nameof(IcumFileScannerService), IcumFileScannerService },
                { nameof(IcumJsonIngestionService), IcumJsonIngestionService },
                { nameof(IcumDataTransferService), IcumDataTransferService },
                { nameof(ContainerCompletenessService), ContainerCompletenessService },
                { nameof(ManualBOESelectivityService), ManualBOESelectivityService },
                { nameof(ContainerDataMapperService), ContainerDataMapperService },
                { nameof(ICUMSSubmissionService), ICUMSSubmissionService },
                { nameof(DashboardBroadcastService), DashboardBroadcastService }
            };
        }

        /// <summary>
        /// Get list of enabled services
        /// </summary>
        public List<string> GetEnabledServices()
        {
            var enabledServices = new List<string>();

            foreach (var kvp in GetAllServices())
            {
                if (kvp.Value.Enabled)
                {
                    enabledServices.Add(kvp.Key);
                }
            }

            return enabledServices;
        }

        /// <summary>
        /// Get list of disabled services
        /// </summary>
        public List<string> GetDisabledServices()
        {
            var disabledServices = new List<string>();

            foreach (var kvp in GetAllServices())
            {
                if (!kvp.Value.Enabled)
                {
                    disabledServices.Add(kvp.Key);
                }
            }

            return disabledServices;
        }
    }

    /// <summary>
    /// Base service configuration
    /// </summary>
    public class ServiceConfig
    {
        public bool Enabled { get; set; } = true;
        public string? Description { get; set; }
    }

    /// <summary>
    /// Health check service configuration
    /// </summary>
    public class HealthCheckServiceConfig : ServiceConfig
    {
        public int CheckIntervalMinutes { get; set; } = 5;
    }

    /// <summary>
    /// Batch processing service configuration
    /// </summary>
    public class BatchServiceConfig : ServiceConfig
    {
        public int BatchIntervalMinutes { get; set; } = 30;
    }

    /// <summary>
    /// Process service configuration
    /// </summary>
    public class ProcessServiceConfig : ServiceConfig
    {
        public int ProcessIntervalMinutes { get; set; } = 2;
        public int? BatchSize { get; set; }
    }

    /// <summary>
    /// Scan service configuration
    /// </summary>
    public class ScanServiceConfig : ServiceConfig
    {
        public int ScanIntervalMinutes { get; set; } = 1;
    }

    /// <summary>
    /// Transfer service configuration
    /// </summary>
    public class TransferServiceConfig : ServiceConfig
    {
        public int TransferIntervalMinutes { get; set; } = 5;
    }

    /// <summary>
    /// Monitor service configuration
    /// </summary>
    public class MonitorServiceConfig : ServiceConfig
    {
        public int CheckIntervalMinutes { get; set; } = 10;
    }

    /// <summary>
    /// Mapping service configuration
    /// </summary>
    public class MappingServiceConfig : ServiceConfig
    {
        public int MappingIntervalMinutes { get; set; } = 5;
    }

    /// <summary>
    /// Submission service configuration
    /// </summary>
    public class SubmissionServiceConfig : ServiceConfig
    {
        public int SubmissionIntervalMinutes { get; set; } = 10;
    }

    /// <summary>
    /// Broadcast service configuration
    /// </summary>
    public class BroadcastServiceConfig : ServiceConfig
    {
        public int BroadcastIntervalSeconds { get; set; } = 30;
    }
}

