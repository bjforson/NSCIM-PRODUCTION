using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Comprehensive dashboard data model - captures every aspect of the system
    /// </summary>
    public class ComprehensiveDashboardData
    {
        public SystemOverview SystemOverview { get; set; } = new();
        public Dictionary<string, BackgroundServiceStatus> BackgroundServices { get; set; } = new();
        public Dictionary<string, ScannerDetailedStatus> Scanners { get; set; } = new();
        public DatabaseStatistics Databases { get; set; } = new();
        public ICUMSIntegrationStatus ICUMSIntegration { get; set; } = new();
        public QueueStatistics Queues { get; set; } = new();
        public ContainerValidationWorkflow ContainerValidation { get; set; } = new();
        public ImageProcessingMetrics ImageProcessing { get; set; } = new();
        public DashboardVehicleStats VehicleImports { get; set; } = new();
        public FileSystemStatus FileSystem { get; set; } = new();
        public DashboardPerformanceMetrics Performance { get; set; } = new();
        public ErrorStatistics Errors { get; set; } = new();
        public List<ActivityEvent> RecentActivity { get; set; } = new();
        public TrendData Trends { get; set; } = new();
        public List<ActiveOperation> CurrentOperations { get; set; } = new();
        public AlertsSummary Alerts { get; set; } = new();
        public UserActivitySummary UserActivity { get; set; } = new();
        public RBACStatusSummary RBACStatus { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    public class SystemOverview
    {
        public string Status { get; set; } = "Healthy"; // Healthy|Degraded|Critical
        public string Uptime { get; set; } = "";
        public string Version { get; set; } = "1.0.0";
        public string Environment { get; set; } = "Production";
        public DateTime LastRestartAt { get; set; }
        public int TotalErrors24h { get; set; }
        public int TotalWarnings24h { get; set; }
        public double HealthScore { get; set; } // 0-100
    }

    public class BackgroundServiceStatus
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = ""; // Running|Stopped|Error
        public DateTime? LastHeartbeat { get; set; }
        public long CyclesCompleted { get; set; }
        public int Errors24h { get; set; }
        public string AverageCycleTime { get; set; } = "";
        public DateTime? NextCycleAt { get; set; }
        public int Priority { get; set; }
        public Dictionary<string, object> Metrics { get; set; } = new();
    }

    public class ScannerDetailedStatus
    {
        public string ScannerId { get; set; } = "";
        public string ScannerType { get; set; } = "";
        public string Location { get; set; } = "";
        public string Status { get; set; } = ""; // Online|Offline|Maintenance|Error
        public ScannerHealth Health { get; set; } = new();
        public ScannerConnection? Connection { get; set; }
        public ScannerPerformance Performance { get; set; } = new();
        public ServiceErrors Errors { get; set; } = new();
    }

    public class ScannerHealth
    {
        public double Score { get; set; } // 0-100
        public DateTime? LastScan { get; set; }
        public int ScansToday { get; set; }
        public int ScansThisWeek { get; set; }
        public int ScansThisMonth { get; set; }
        public double SuccessRate { get; set; }
        public string AverageScanTime { get; set; } = "";
    }

    public class ScannerConnection
    {
        public string ConnectionString { get; set; } = "";
        public bool Connected { get; set; }
        public DateTime? LastSync { get; set; }
        public string SyncInterval { get; set; } = "";
        public string Latency { get; set; } = "";
    }

    public class ScannerPerformance
    {
        public string ImageProcessingTime { get; set; } = "";
        public double ConversionSuccessRate { get; set; }
        public int ImagesProcessed24h { get; set; }
    }

    public class ServiceErrors
    {
        public int Last24h { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastErrorAt { get; set; }
    }

    public class DatabaseStatistics
    {
        public DatabaseInfo NS_CIS { get; set; } = new();
        public DatabaseInfo ICUMS { get; set; } = new();
        public DatabaseInfo ICUMS_Downloads { get; set; } = new();
    }

    public class DatabaseInfo
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = ""; // Connected|Disconnected
        public Dictionary<string, int> TableCounts { get; set; } = new();
        public DatabaseGrowth Growth { get; set; } = new();
        public DatabasePerformance Performance { get; set; } = new();
        public DatabaseSize Size { get; set; } = new();
    }

    public class DatabaseGrowth
    {
        public int RecordsToday { get; set; }
        public int RecordsThisWeek { get; set; }
        public int RecordsThisMonth { get; set; }
    }

    public class DatabasePerformance
    {
        public string AvgQueryTime { get; set; } = "";
        public string SlowestQuery { get; set; } = "";
        public long TotalQueries24h { get; set; }
        public double IndexHitRate { get; set; }
    }

    public class DatabaseSize
    {
        public long TotalSizeMB { get; set; }
        public long DataFileSizeMB { get; set; }
        public long LogFileSizeMB { get; set; }
        public string GrowthRate { get; set; } = "";
    }

    public class ICUMSIntegrationStatus
    {
        public string APIStatus { get; set; } = ""; // Connected|Degraded|Offline
        public Dictionary<string, ICUMSEndpointStatus> Endpoints { get; set; } = new();
        public CircuitBreakerStatus CircuitBreaker { get; set; } = new();
        public ProxyStatus Proxy { get; set; } = new();
        public CacheStatus Cache { get; set; } = new();
        public DownloadsInfo Downloads { get; set; } = new();
    }

    public class ICUMSEndpointStatus
    {
        public string Url { get; set; } = "";
        public string Status { get; set; } = "";
        public string AvgResponseTime { get; set; } = "";
        public int Calls24h { get; set; }
        public double SuccessRate { get; set; }
        public DateTime? LastSuccess { get; set; }
        public DateTime? LastFailure { get; set; }
    }

    public class CircuitBreakerStatus
    {
        public string State { get; set; } = ""; // Closed|Open|HalfOpen
        public int FailureCount { get; set; }
        public DateTime? LastStateChange { get; set; }
        public DateTime? NextRetryAt { get; set; }
    }

    public class ProxyStatus
    {
        public string Address { get; set; } = "";
        public string Status { get; set; } = "";
        public string Latency { get; set; } = "";
    }

    public class CacheStatus
    {
        public bool Enabled { get; set; }
        public double HitRate { get; set; }
        public int Entries { get; set; }
        public int SizeLimit { get; set; }
    }

    public class DownloadsInfo
    {
        public string Directory { get; set; } = "";
        public int TotalFiles { get; set; }
        public double TotalSizeGB { get; set; }
        public DateTime? OldestFile { get; set; }
        public DateTime? NewestFile { get; set; }
    }

    public class QueueStatistics
    {
        public QueueInfo DownloadQueue { get; set; } = new();
        public QueueInfo SubmissionQueue { get; set; } = new();
    }

    public class QueueInfo
    {
        public int TotalPending { get; set; }
        public int HighPriority { get; set; }
        public int MediumPriority { get; set; }
        public int LowPriority { get; set; }
        public int Processing { get; set; }
        public int Completed24h { get; set; }
        public int Failed24h { get; set; }
        public string AverageWaitTime { get; set; } = "";
        public string AverageProcessingTime { get; set; } = "";
        public DashboardQueueItem? OldestRequest { get; set; }
    }

    public class DashboardQueueItem
    {
        public string ContainerNumber { get; set; } = "";
        public DateTime RequestedAt { get; set; }
        public string Priority { get; set; } = "";
        public int Retries { get; set; }
    }

    public class ContainerValidationWorkflow
    {
        public WorkflowPipeline Pipeline { get; set; } = new();
        public WorkflowThroughput Throughput { get; set; } = new();
        public Dictionary<string, ClearanceTypeStats> ClearanceTypes { get; set; } = new();
    }

    public class WorkflowPipeline
    {
        public int TotalContainers { get; set; }
        public Dictionary<string, PipelineStage> Stages { get; set; } = new();
    }

    public class PipelineStage
    {
        public int Count { get; set; }
        public double Percentage { get; set; }
        public Dictionary<string, int>? Breakdown { get; set; }
    }

    public class WorkflowThroughput
    {
        public double Hourly { get; set; }
        public double Daily { get; set; }
        public double Weekly { get; set; }
        public string Bottleneck { get; set; } = "";
    }

    public class ClearanceTypeStats
    {
        public int Total { get; set; }
        public int Pending { get; set; }
        public int Cleared { get; set; }
        public string AverageTime { get; set; } = "";
    }

    public class ImageProcessingMetrics
    {
        public Dictionary<string, ImagePipelineMetrics> Pipelines { get; set; } = new();
        public ImageCacheMetrics Cache { get; set; } = new();
        public AnnotationMetrics Annotations { get; set; } = new();
    }

    public class ImagePipelineMetrics
    {
        public int ImagesProcessed24h { get; set; }
        public string AverageSize { get; set; } = "";
        public double ConversionSuccessRate { get; set; }
        public string AverageConversionTime { get; set; } = "";
        public int CacheMisses24h { get; set; }
        public double CacheHitRate { get; set; }
    }

    public class ImageCacheMetrics
    {
        public int TotalEntries { get; set; }
        public double TotalSizeGB { get; set; }
        public double HitRate { get; set; }
        public int ExpiredEntries24h { get; set; }
        public int EvictedEntries24h { get; set; }
    }

    public class AnnotationMetrics
    {
        public int Total { get; set; }
        public int Created24h { get; set; }
        public Dictionary<string, int> Types { get; set; } = new();
    }

    public class DashboardVehicleStats
    {
        public VehicleStats Statistics { get; set; } = new();
        public Dictionary<string, int> ImportTypes { get; set; } = new();
        public VINExtractionStats VINExtraction { get; set; } = new();
        public List<TopMake> TopMakes { get; set; } = new();
    }

    public class VehicleStats
    {
        public int TotalVehicles { get; set; }
        public int ImportedToday { get; set; }
        public int ImportedThisWeek { get; set; }
        public int ImportedThisMonth { get; set; }
    }

    public class VINExtractionStats
    {
        public int Total24h { get; set; }
        public double SuccessRate { get; set; }
        public int DuplicatesDetected24h { get; set; }
    }

    public class TopMake
    {
        public string Make { get; set; } = "";
        public int Count { get; set; }
    }

    public class FileSystemStatus
    {
        public Dictionary<string, FileSystemPath> Paths { get; set; } = new();
        public List<DiskSpaceInfo> DiskSpace { get; set; } = new();
    }

    public class FileSystemPath
    {
        public string Path { get; set; } = "";
        public bool Accessible { get; set; }
        public int FileCount { get; set; }
        public double TotalSizeGB { get; set; }
        public DateTime? OldestFile { get; set; }
        public DateTime? NewestFile { get; set; }
        public bool RequiresAttention { get; set; }
    }

    public class DiskSpaceInfo
    {
        public string Drive { get; set; } = "";
        public long TotalGB { get; set; }
        public long UsedGB { get; set; }
        public long FreeGB { get; set; }
        public double UsagePercent { get; set; }
        public string Status { get; set; } = ""; // Healthy|Warning|Critical
    }

    public class DashboardPerformanceMetrics
    {
        public CPUMetrics CPU { get; set; } = new();
        public MemoryMetrics Memory { get; set; } = new();
        public NetworkMetrics Network { get; set; } = new();
        public APIMetrics API { get; set; } = new();
    }

    public class CPUMetrics
    {
        public double UsagePercent { get; set; }
        public int ProcessesRunning { get; set; }
        public int Cores { get; set; }
        public List<double> LoadAverage { get; set; } = new();
    }

    public class MemoryMetrics
    {
        public long TotalGB { get; set; }
        public long UsedGB { get; set; }
        public long FreeGB { get; set; }
        public double UsagePercent { get; set; }
        public long APIProcessMB { get; set; }
        public long BackgroundServicesMB { get; set; }
    }

    public class NetworkMetrics
    {
        public bool InternetConnected { get; set; }
        public bool ICUMSReachable { get; set; }
        public bool ASEDbReachable { get; set; }
        public bool NetworkShareReachable { get; set; }
        public Dictionary<string, string> AvgLatency { get; set; } = new();
        public BandwidthInfo Bandwidth24h { get; set; } = new();
    }

    public class BandwidthInfo
    {
        public string Downloaded { get; set; } = "";
        public string Uploaded { get; set; } = "";
    }

    public class APIMetrics
    {
        public long RequestsLast24h { get; set; }
        public string AvgResponseTime { get; set; } = "";
        public string SlowestEndpoint { get; set; } = "";
        public double ErrorRate { get; set; }
        public int ActiveConnections { get; set; }
        public int SignalRConnections { get; set; }
    }

    public class ErrorStatistics
    {
        public int CriticalErrors24h { get; set; }
        public int Errors24h { get; set; }
        public int Warnings24h { get; set; }
        public Dictionary<string, ServiceErrorInfo> ByService { get; set; } = new();
        public List<ErrorEvent> RecentErrors { get; set; } = new();
    }

    public class ServiceErrorInfo
    {
        public int Errors { get; set; }
        public int Warnings { get; set; }
        public string? LastError { get; set; }
        public DateTime? LastErrorAt { get; set; }
    }

    public class ErrorEvent
    {
        public DateTime Timestamp { get; set; }
        public string Service { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Message { get; set; } = "";
        public string? ContainerNumber { get; set; }
        public string? File { get; set; }
    }

    public class ActivityEvent
    {
        public DateTime Timestamp { get; set; }
        public string Type { get; set; } = ""; // Scan|Validation|Submission|Download|etc
        public string Service { get; set; } = "";
        public string Icon { get; set; } = "";
        public string Message { get; set; } = "";
        public string Severity { get; set; } = ""; // Info|Success|Warning|Error
        public string? ContainerNumber { get; set; }
        public string? User { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class TrendData
    {
        public List<HourlyTrend> ScansPerHour24h { get; set; } = new();
        public List<DailyTrend> ScansPerDay7d { get; set; } = new();
        public ValidationFlowTime ValidationFlowTime { get; set; } = new();
        public Dictionary<string, ServicePerformanceTrend> ServicePerformance { get; set; } = new();
    }

    public class HourlyTrend
    {
        public int Hour { get; set; }
        public int ASE { get; set; }
        public int FS6000 { get; set; }
        public int Total { get; set; }
    }

    public class DailyTrend
    {
        public string Date { get; set; } = "";
        public int ASE { get; set; }
        public int FS6000 { get; set; }
        public int Total { get; set; }
    }

    public class ValidationFlowTime
    {
        public string AverageTimeFromScanToSubmission { get; set; } = "";
        public string AverageTimeToGetICUMSData { get; set; } = "";
        public string AverageTimeToValidate { get; set; } = "";
        public string AverageTimeToSubmit { get; set; } = "";
    }

    public class ServicePerformanceTrend
    {
        public string AvgTime { get; set; } = "";
        public string Trend { get; set; } = ""; // Improving|Stable|Degrading
        public string Change { get; set; } = "";
    }

    public class ActiveOperation
    {
        public string Id { get; set; } = "";
        public string Type { get; set; } = "";
        public string? ContainerNumber { get; set; }
        public DateTime StartedAt { get; set; }
        public int Progress { get; set; } // 0-100
        public string EstimatedCompletion { get; set; } = "";
        public string CurrentStep { get; set; } = "";
    }

    public class AlertsSummary
    {
        public int Critical { get; set; }
        public int High { get; set; }
        public int Medium { get; set; }
        public int Low { get; set; }
        public List<Alert> ActiveAlerts { get; set; } = new();
    }

    public class Alert
    {
        public string Id { get; set; } = "";
        public string Severity { get; set; } = ""; // Critical|High|Medium|Low
        public string Type { get; set; } = "";
        public string Message { get; set; } = "";
        public string Service { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public bool Acknowledged { get; set; }
        public string? ActionRequired { get; set; }
    }

    public class UserActivitySummary
    {
        public int ActiveUsers24h { get; set; }
        public int TotalLogins24h { get; set; }
        public string AverageSessionDuration { get; set; } = "";
        public Dictionary<string, int> ByRole { get; set; } = new();
        public List<UserAction> RecentActions { get; set; } = new();
    }

    public class UserAction
    {
        public DateTime Timestamp { get; set; }
        public string User { get; set; } = "";
        public string Role { get; set; } = "";
        public string Action { get; set; } = "";
        public string? ContainerNumber { get; set; }
    }

    public class RBACStatusSummary
    {
        public int TotalPermissions { get; set; }
        public int TotalRoles { get; set; }
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public Dictionary<string, int> RoleDistribution { get; set; } = new();
        public Dictionary<string, int> PermissionUsage24h { get; set; } = new();
    }
}

