using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Main dashboard data model for Image Analysis Operations Dashboard
    /// </summary>
    public class ImageAnalysisDashboardData
    {
        public WorkflowStatusData WorkflowStatus { get; set; } = new();
        public AssignmentMetricsData Assignments { get; set; } = new();
        public PerformanceMetricsData Performance { get; set; } = new();
        public DataIntegrityMetricsData DataIntegrity { get; set; } = new();
        public RealTimeReadinessData RealTimeReadiness { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Workflow status data - tracks containers/groups at each workflow stage
    /// </summary>
    public class WorkflowStatusData
    {
        public Dictionary<string, StageMetrics> Stages { get; set; } = new();
        public ImageAnalysisWorkflowThroughput Throughput { get; set; } = new();
        public List<WorkflowStageDistribution> Distribution { get; set; } = new();
    }

    /// <summary>
    /// Metrics for a single workflow stage
    /// </summary>
    public class StageMetrics
    {
        public string StageName { get; set; } = "";
        public int Count { get; set; }
        public TimeSpan AverageAge { get; set; }
        public TimeSpan OldestAge { get; set; }
        public double IncomingRate { get; set; } // per hour
        public double OutgoingRate { get; set; } // per hour
        public double NetChange { get; set; }
        public string Status { get; set; } = "Normal"; // Normal, Warning, Critical
    }

    /// <summary>
    /// Workflow throughput metrics for Image Analysis Dashboard
    /// </summary>
    public class ImageAnalysisWorkflowThroughput
    {
        public double ContainersPerHour { get; set; }
        public double GroupsPerHour { get; set; }
        public double DecisionsPerHour { get; set; }
        public double PeakThroughput { get; set; }
        public double TargetThroughput { get; set; }
        public double PerformanceVsTarget { get; set; } // percentage
    }

    /// <summary>
    /// WorkflowStage distribution data
    /// </summary>
    public class WorkflowStageDistribution
    {
        public string StageName { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    /// <summary>
    /// Assignment metrics data
    /// </summary>
    public class AssignmentMetricsData
    {
        public string AssignmentMode { get; set; } = "";
        public bool ServiceEnabled { get; set; }
        public DateTime? LastCycleTime { get; set; }
        public TimeSpan? AverageCycleTime { get; set; }
        public List<UserAssignmentStatus> UserAssignments { get; set; } = new();
        public QueueStatus AnalystQueue { get; set; } = new();
        public QueueStatus AuditQueue { get; set; } = new();
        public string CurrentStrategy { get; set; } = "";
        public double BalanceScore { get; set; } // 0-100, how evenly distributed
    }

    /// <summary>
    /// User assignment status
    /// </summary>
    public class UserAssignmentStatus
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public int ActiveAssignments { get; set; }
        public int MaxConcurrent { get; set; }
        public double Utilization { get; set; } // percentage
        public TimeSpan AverageAge { get; set; }
        public TimeSpan OldestAge { get; set; }
    }

    /// <summary>
    /// Queue status for assignments
    /// </summary>
    public class QueueStatus
    {
        public string Role { get; set; } = "";
        public int ReadyForAssignment { get; set; }
        public double AssignmentRate { get; set; } // per hour
        public TimeSpan AverageWaitTime { get; set; }
        public TimeSpan LongestWait { get; set; }
        public double AssignmentSuccessRate { get; set; } // percentage
    }

    /// <summary>
    /// Performance metrics data
    /// </summary>
    public class PerformanceMetricsData
    {
        public ThroughputMetrics Throughput { get; set; } = new();
        public ProcessingTimeMetrics ProcessingTimes { get; set; } = new();
        public List<UserProductivity> UserProductivity { get; set; } = new();
        public ImageAnalysisSystemPerformanceMetrics SystemPerformance { get; set; } = new();
    }

    /// <summary>
    /// Throughput metrics
    /// </summary>
    public class ThroughputMetrics
    {
        public double ContainersPerHour { get; set; }
        public double GroupsPerHour { get; set; }
        public double DecisionsPerHour { get; set; }
        public double PeakThroughput { get; set; }
        public double TargetThroughput { get; set; }
        public double PerformanceVsTarget { get; set; } // percentage
    }

    /// <summary>
    /// Processing time metrics for each stage
    /// </summary>
    public class ProcessingTimeMetrics
    {
        public TimeSpan ReadyToAnalystAssigned { get; set; }
        public TimeSpan AnalystAssignedToCompleted { get; set; }
        public TimeSpan AnalystCompletedToAuditAssigned { get; set; }
        public TimeSpan AuditAssignedToCompleted { get; set; }
        public TimeSpan TotalEndToEnd { get; set; }
    }

    /// <summary>
    /// User productivity metrics
    /// </summary>
    public class UserProductivity
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public int ContainersCompletedToday { get; set; }
        public TimeSpan AverageTimePerContainer { get; set; }
        public TimeSpan ActiveTime { get; set; }
        public double ProductivityScore { get; set; } // containers per hour
        public double QualityScore { get; set; } // based on audit outcomes
    }

    /// <summary>
    /// System performance metrics for Image Analysis Dashboard
    /// </summary>
    public class ImageAnalysisSystemPerformanceMetrics
    {
        public TimeSpan AssignmentWorkerCycleTime { get; set; }
        public TimeSpan AverageDatabaseQueryTime { get; set; }
        public TimeSpan AverageApiResponseTime { get; set; }
        public double ErrorRate { get; set; } // errors per 1000 operations
        public SystemLoad SystemLoad { get; set; } = new();
    }

    /// <summary>
    /// System load metrics
    /// </summary>
    public class SystemLoad
    {
        public double CpuUsage { get; set; } // percentage
        public double MemoryUsage { get; set; } // percentage
        public int DatabaseConnections { get; set; }
        public double DiskIoRead { get; set; } // MB/s
        public double DiskIoWrite { get; set; } // MB/s
        public double NetworkBandwidth { get; set; } // Mbps
    }

    // ============================================
    // Phase 2: User Activity & Productivity Models
    // ============================================

    /// <summary>
    /// User activity data
    /// </summary>
    public class UserActivityData
    {
        public List<ActiveUser> ActiveUsers { get; set; } = new();
        public List<UserProductivity> UserProductivity { get; set; } = new();
        public TeamPerformanceComparison TeamComparison { get; set; } = new();
        public List<ActivityTimelineEvent> ActivityTimeline { get; set; } = new();
    }

    /// <summary>
    /// Currently active user information
    /// </summary>
    public class ActiveUser
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public DateTime LastActivity { get; set; }
        public TimeSpan TimeSinceLastActivity { get; set; }
        public string? CurrentAssignment { get; set; }
        public TimeSpan SessionDuration { get; set; }
        public double ActivityLevel { get; set; } // 0-100, based on recent actions
    }

    /// <summary>
    /// Team performance comparison data
    /// </summary>
    public class TeamPerformanceComparison
    {
        public string TimePeriod { get; set; } = "Today"; // Today, Week, Month
        public List<UserComparisonMetric> Users { get; set; } = new();
    }

    /// <summary>
    /// User comparison metric
    /// </summary>
    public class UserComparisonMetric
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public int ContainersCompleted { get; set; }
        public TimeSpan AverageTime { get; set; }
        public double QualityScore { get; set; }
        public int Rank { get; set; }
    }

    /// <summary>
    /// Activity timeline event
    /// </summary>
    public class ActivityTimelineEvent
    {
        public string Username { get; set; } = "";
        public string ActivityType { get; set; } = ""; // Assignment, Completion, Decision, etc.
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string? Details { get; set; }
    }

    // ============================================
    // Phase 2: Quality & Audit Metrics Models
    // ============================================

    /// <summary>
    /// Quality and audit metrics data
    /// </summary>
    public class QualityMetricsData
    {
        public DecisionDistribution DecisionDistribution { get; set; } = new();
        public AuditOutcomesMetrics AuditOutcomes { get; set; } = new();
        public QualityScoreMetrics QualityScores { get; set; } = new();
        public List<AuditDiscrepancy> Discrepancies { get; set; } = new();
    }

    /// <summary>
    /// Decision distribution (analyst decisions)
    /// </summary>
    public class DecisionDistribution
    {
        public int NormalCount { get; set; }
        public int AbnormalCount { get; set; }
        public int NotClearCount { get; set; }
        public int TotalCount { get; set; }
        public Dictionary<string, double> Percentages { get; set; } = new();
        public Dictionary<string, int> TrendTodayVsYesterday { get; set; } = new();
    }

    /// <summary>
    /// Audit outcomes metrics
    /// </summary>
    public class AuditOutcomesMetrics
    {
        public int TotalAudited { get; set; }
        public int ApprovedCount { get; set; }
        public int RejectedCount { get; set; }
        public double ApprovedRate { get; set; } // percentage
        public double RejectionRate { get; set; } // percentage
        public double AverageAuditTime { get; set; } // minutes
        public Dictionary<string, int> OutcomesByRole { get; set; } = new();
    }

    /// <summary>
    /// Quality score metrics
    /// </summary>
    public class QualityScoreMetrics
    {
        public double OverallQualityScore { get; set; } // 0-100
        public double AnalystAccuracy { get; set; } // percentage of decisions that match audit
        public double AverageDiscrepancyRate { get; set; } // percentage
        public Dictionary<string, double> QualityByUser { get; set; } = new();
    }

    /// <summary>
    /// Audit discrepancy (when analyst and audit disagree)
    /// </summary>
    public class AuditDiscrepancy
    {
        public string ContainerNumber { get; set; } = "";
        public string GroupIdentifier { get; set; } = "";
        public string AnalystDecision { get; set; } = "";
        public string AuditDecision { get; set; } = "";
        public string AnalystUsername { get; set; } = "";
        public string AuditorUsername { get; set; } = "";
        public DateTime AnalystDecisionDate { get; set; }
        public DateTime AuditDecisionDate { get; set; }
        public string? Notes { get; set; }
    }

    // ============================================
    // Phase 2: Historical Trends Models
    // ============================================

    /// <summary>
    /// Historical trends data
    /// </summary>
    public class HistoricalTrendsData
    {
        public string Period { get; set; } = "24h"; // 24h, 7d, 30d
        public List<ThroughputTrendPoint> ThroughputTrend { get; set; } = new();
        public List<StageDurationTrendPoint> StageDurationTrend { get; set; } = new();
        public List<AssignmentEfficiencyTrendPoint> AssignmentEfficiencyTrend { get; set; } = new();
        public List<QualityTrendPoint> QualityTrend { get; set; } = new();
    }

    /// <summary>
    /// Throughput trend data point
    /// </summary>
    public class ThroughputTrendPoint
    {
        public DateTime Timestamp { get; set; }
        public double ContainersPerHour { get; set; }
        public double GroupsPerHour { get; set; }
        public double DecisionsPerHour { get; set; }
    }

    /// <summary>
    /// Stage duration trend data point
    /// </summary>
    public class StageDurationTrendPoint
    {
        public DateTime Timestamp { get; set; }
        public string StageName { get; set; } = "";
        public TimeSpan AverageDuration { get; set; }
        public TimeSpan MedianDuration { get; set; }
    }

    /// <summary>
    /// Assignment efficiency trend data point
    /// </summary>
    public class AssignmentEfficiencyTrendPoint
    {
        public DateTime Timestamp { get; set; }
        public double AssignmentRate { get; set; } // per hour
        public TimeSpan AverageWaitTime { get; set; }
        public double SuccessRate { get; set; } // percentage
    }

    /// <summary>
    /// Quality trend data point
    /// </summary>
    public class QualityTrendPoint
    {
        public DateTime Timestamp { get; set; }
        public double QualityScore { get; set; }
        public double ApprovalRate { get; set; }
        public double DiscrepancyRate { get; set; }
    }

    // ============================================
    // Phase 2: Bottleneck Analysis Models
    // ============================================

    /// <summary>
    /// Bottleneck analysis data
    /// </summary>
    public class BottleneckAnalysisData
    {
        public List<Bottleneck> DetectedBottlenecks { get; set; } = new();
        public List<BottleneckResolution> SuggestedResolutions { get; set; } = new();
        public List<HistoricalBottleneck> HistoricalBottlenecks { get; set; } = new();
    }

    /// <summary>
    /// Detected bottleneck
    /// </summary>
    public class Bottleneck
    {
        public string StageName { get; set; } = "";
        public string Severity { get; set; } = ""; // Low, Medium, High, Critical
        public int QueueSize { get; set; }
        public TimeSpan AverageWaitTime { get; set; }
        public TimeSpan LongestWait { get; set; }
        public double ThroughputImpact { get; set; } // percentage reduction
        public string RootCause { get; set; } = "";
        public DateTime DetectedAt { get; set; }
    }

    /// <summary>
    /// Bottleneck resolution suggestion
    /// </summary>
    public class BottleneckResolution
    {
        public string BottleneckStage { get; set; } = "";
        public string ResolutionType { get; set; } = ""; // Reassign, AddResources, Optimize, etc.
        public string Description { get; set; } = "";
        public string Priority { get; set; } = ""; // Low, Medium, High
        public double ExpectedImpact { get; set; } // percentage improvement
    }

    /// <summary>
    /// Historical bottleneck record
    /// </summary>
    public class HistoricalBottleneck
    {
        public string StageName { get; set; } = "";
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public TimeSpan Duration { get; set; }
        public string Resolution { get; set; } = "";
        public bool Resolved { get; set; }
    }

    // ============================================
    // Phase 3: Predictive Analytics Models
    // ============================================

    /// <summary>
    /// Predictive analytics data
    /// </summary>
    public class PredictiveAnalyticsData
    {
        public WorkloadForecast WorkloadForecast { get; set; } = new();
        public CapacityPlanning CapacityPlanning { get; set; } = new();
        public List<BottleneckPrediction> BottleneckPredictions { get; set; } = new();
        public ResourceNeeds ResourceNeeds { get; set; } = new();
    }

    /// <summary>
    /// Workload forecast
    /// </summary>
    public class WorkloadForecast
    {
        public DateTime ForecastStart { get; set; }
        public DateTime ForecastEnd { get; set; }
        public List<ForecastPoint> ForecastPoints { get; set; } = new();
        public double ExpectedPeakLoad { get; set; }
        public DateTime? ExpectedPeakTime { get; set; }
        public double ConfidenceLevel { get; set; } // 0-100
    }

    /// <summary>
    /// Forecast data point
    /// </summary>
    public class ForecastPoint
    {
        public DateTime Timestamp { get; set; }
        public double PredictedContainers { get; set; }
        public double PredictedGroups { get; set; }
        public double LowerBound { get; set; }
        public double UpperBound { get; set; }
    }

    /// <summary>
    /// Capacity planning data
    /// </summary>
    public class CapacityPlanning
    {
        public int CurrentCapacity { get; set; } // containers per hour
        public int RequiredCapacity { get; set; } // containers per hour
        public int CapacityGap { get; set; } // required - current
        public double UtilizationRate { get; set; } // percentage
        public List<CapacityRecommendation> Recommendations { get; set; } = new();
    }

    /// <summary>
    /// Capacity recommendation
    /// </summary>
    public class CapacityRecommendation
    {
        public string Type { get; set; } = ""; // AddUsers, OptimizeProcess, ExtendHours
        public string Description { get; set; } = "";
        public int ExpectedCapacityIncrease { get; set; }
        public string Priority { get; set; } = ""; // Low, Medium, High
    }

    /// <summary>
    /// Bottleneck prediction
    /// </summary>
    public class BottleneckPrediction
    {
        public string StageName { get; set; } = "";
        public DateTime PredictedTime { get; set; }
        public double Probability { get; set; } // 0-100
        public string Severity { get; set; } = ""; // Low, Medium, High
        public string Reason { get; set; } = "";
        public List<string> PreventionActions { get; set; } = new();
    }

    /// <summary>
    /// Resource needs calculation
    /// </summary>
    public class ResourceNeeds
    {
        public int RequiredAnalysts { get; set; }
        public int CurrentAnalysts { get; set; }
        public int RequiredAuditors { get; set; }
        public int CurrentAuditors { get; set; }
        public double AnalystUtilization { get; set; } // percentage
        public double AuditorUtilization { get; set; } // percentage
        public DateTime CalculatedAt { get; set; }
    }

    // ============================================
    // Phase 3: Alerts & Notifications Models
    // ============================================

    /// <summary>
    /// Dashboard alerts data
    /// </summary>
    public class DashboardAlertsData
    {
        public List<DashboardAlert> ActiveAlerts { get; set; } = new();
        public AlertConfiguration Configuration { get; set; } = new();
        public List<AlertHistory> AlertHistory { get; set; } = new();
    }

    /// <summary>
    /// Dashboard alert
    /// </summary>
    public class DashboardAlert
    {
        public int Id { get; set; }
        public string AlertType { get; set; } = ""; // Bottleneck, Quality, Performance, System
        public string Severity { get; set; } = ""; // Low, Medium, High, Critical
        public string Title { get; set; } = "";
        public string Message { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public string? AcknowledgedBy { get; set; }
        public bool IsAcknowledged { get; set; }
        public bool IsResolved { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    /// <summary>
    /// Alert configuration
    /// </summary>
    public class AlertConfiguration
    {
        public Dictionary<string, AlertThreshold> Thresholds { get; set; } = new();
        public List<string> NotificationChannels { get; set; } = new(); // Email, SMS, Webhook, InApp
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Alert threshold
    /// </summary>
    public class AlertThreshold
    {
        public string Metric { get; set; } = "";
        public double WarningThreshold { get; set; }
        public double CriticalThreshold { get; set; }
        public bool Enabled { get; set; } = true;
    }

    /// <summary>
    /// Alert history
    /// </summary>
    public class AlertHistory
    {
        public int Id { get; set; }
        public string AlertType { get; set; } = "";
        public string Severity { get; set; } = "";
        public string Title { get; set; } = "";
        public DateTime CreatedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public string? ResolvedBy { get; set; }
        public bool WasAcknowledged { get; set; }
    }

    // ============================================
    // Phase 3: Export Models
    // ============================================

    /// <summary>
    /// Export request
    /// </summary>
    public class ExportRequest
    {
        public string Format { get; set; } = "CSV"; // CSV, PDF, Excel
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public List<string> Sections { get; set; } = new(); // Which sections to include
    }

    /// <summary>
    /// Export result
    /// </summary>
    public class ExportResult
    {
        public bool Success { get; set; }
        public string? FileUrl { get; set; }
        public string? FileName { get; set; }
        public long FileSize { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ExportedAt { get; set; }
    }

    // ============================================
    // Phase 4: System Health Models
    // ============================================

    /// <summary>
    /// System health data for dashboard
    /// </summary>
    public class SystemHealthData
    {
        public AssignmentWorkerStatus AssignmentWorker { get; set; } = new();
        public ImageAnalysisDatabasePerformance DatabasePerformance { get; set; } = new();
        public ApiPerformance ApiPerformance { get; set; } = new();
        public ResourceUtilization ResourceUtilization { get; set; } = new();
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// AssignmentWorker status
    /// </summary>
    public class AssignmentWorkerStatus
    {
        public bool IsRunning { get; set; }
        public DateTime? LastCycleTime { get; set; }
        public TimeSpan? AverageCycleTime { get; set; }
        public TimeSpan? LastCycleDuration { get; set; }
        public int CyclesCompleted24h { get; set; }
        public int Errors24h { get; set; }
        public string Status { get; set; } = "Unknown"; // Running, Stopped, Error
        public string? LastError { get; set; }
        public DateTime? LastErrorTime { get; set; }
    }

    /// <summary>
    /// Database performance metrics for Image Analysis Dashboard
    /// </summary>
    public class ImageAnalysisDatabasePerformance
    {
        public TimeSpan AverageQueryTime { get; set; }
        public TimeSpan SlowestQueryTime { get; set; }
        public int ActiveConnections { get; set; }
        public int MaxConnections { get; set; }
        public double ConnectionPoolUtilization { get; set; } // percentage
        public int QueriesPerSecond { get; set; }
        public int SlowQueries24h { get; set; } // queries > 1 second
        public bool IsHealthy { get; set; }
    }

    /// <summary>
    /// API performance metrics
    /// </summary>
    public class ApiPerformance
    {
        public TimeSpan AverageResponseTime { get; set; }
        public TimeSpan P95ResponseTime { get; set; }
        public TimeSpan P99ResponseTime { get; set; }
        public int RequestsPerSecond { get; set; }
        public int Requests24h { get; set; }
        public double ErrorRate { get; set; } // percentage
        public Dictionary<string, EndpointPerformance> EndpointPerformance { get; set; } = new();
    }

    /// <summary>
    /// Endpoint performance
    /// </summary>
    public class EndpointPerformance
    {
        public string Endpoint { get; set; } = "";
        public int RequestCount { get; set; }
        public TimeSpan AverageResponseTime { get; set; }
        public double ErrorRate { get; set; }
    }

    /// <summary>
    /// Resource utilization
    /// </summary>
    public class ResourceUtilization
    {
        public double CpuUsage { get; set; } // percentage
        public double MemoryUsage { get; set; } // percentage
        public long MemoryUsedMB { get; set; }
        public long MemoryAvailableMB { get; set; }
        public double DiskUsage { get; set; } // percentage
        public double NetworkBandwidth { get; set; } // Mbps
        public int ThreadCount { get; set; }
        public int GcGen0Collections { get; set; }
        public int GcGen1Collections { get; set; }
        public int GcGen2Collections { get; set; }
    }

    // ============================================
    // Data Integrity & Real-Time Readiness Models
    // ============================================

    /// <summary>
    /// Data integrity metrics for BOE/Container linkage
    /// </summary>
    public class DataIntegrityMetricsData
    {
        public int TotalContainers { get; set; }
        public int ContainersWithNullGroupIdentifier { get; set; }
        public int ContainersWithMissingBOEDocumentId { get; set; }
        public int ContainersWithWrongGroupIdentifier { get; set; }
        public double IntegrityPercentage { get; set; } // 0-100
        public string IntegrityStatus { get; set; } = "Healthy"; // Healthy, Warning, Critical
        public int PreventiveFixesLast24h { get; set; }
        public int PreventiveFixesLastHour { get; set; }
        public DateTime LastPreventiveFixTime { get; set; }
        public List<DataIntegrityIssue> RecentIssues { get; set; } = new();
    }

    /// <summary>
    /// Data integrity issue details
    /// </summary>
    public class DataIntegrityIssue
    {
        public string ContainerNumber { get; set; } = "";
        public string ScannerType { get; set; } = "";
        public string IssueType { get; set; } = ""; // NullGroupIdentifier, MissingBOEDocumentId, WrongGroupIdentifier
        public DateTime DetectedAt { get; set; }
        public DateTime? FixedAt { get; set; }
        public bool IsFixed { get; set; }
    }

    /// <summary>
    /// Real-time user readiness status
    /// </summary>
    public class RealTimeReadinessData
    {
        public List<ReadyUserStatus> ReadyUsers { get; set; } = new();
        public int TotalAnalysts { get; set; }
        public int ReadyAnalysts { get; set; }
        public int TotalAuditors { get; set; }
        public int ReadyAuditors { get; set; }
        public double AnalystReadinessPercentage { get; set; } // 0-100
        public double AuditorReadinessPercentage { get; set; } // 0-100
        public DateTime LastUpdate { get; set; }
    }

    /// <summary>
    /// Ready user status (from SignalR real-time state)
    /// </summary>
    public class ReadyUserStatus
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public bool IsReady { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public TimeSpan TimeSinceLastHeartbeat { get; set; }
        public string Source { get; set; } = "SignalR"; // SignalR or Database
    }
}
