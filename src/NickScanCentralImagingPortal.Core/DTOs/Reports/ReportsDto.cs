using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.DTOs.Reports
{
    /// <summary>
    /// Container summary report data
    /// </summary>
    public class ContainerSummaryReportDto
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Summary Statistics
        public int TotalContainers { get; set; }
        public int ScannedContainers { get; set; }
        public int PendingContainers { get; set; }
        public int FailedContainers { get; set; }

        // Clearance Type Breakdown
        public int ImportContainers { get; set; }
        public int ExportContainers { get; set; }
        public int CMRContainers { get; set; }

        // Daily Statistics
        public List<DailyContainerStatsDto> DailyStats { get; set; } = new();

        // Scanner Breakdown
        public List<ScannerContainerStatsDto> ScannerStats { get; set; } = new();

        // Top Containers by Images
        public List<TopContainerDto> TopContainersByImages { get; set; } = new();
    }

    /// <summary>
    /// Scanner performance report data
    /// </summary>
    public class ScannerPerformanceReportDto
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Overall Statistics
        public int TotalScans { get; set; }
        public int SuccessfulScans { get; set; }
        public int FailedScans { get; set; }
        public double SuccessRate { get; set; }

        // Scanner Details
        public List<ScannerDetailsDto> ScannerDetails { get; set; } = new();

        // Daily Performance
        public List<DailyPerformanceDto> DailyPerformance { get; set; } = new();

        // Performance Metrics
        public List<PerformanceMetricDto> PerformanceMetrics { get; set; } = new();

        // Uptime Statistics
        public List<ScannerUptimeDto> UptimeStats { get; set; } = new();
    }

    /// <summary>
    /// ICUMS activity report data
    /// </summary>
    public class ICUMSActivityReportDto
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Download Statistics
        public int TotalDownloads { get; set; }
        public int SuccessfulDownloads { get; set; }
        public int FailedDownloads { get; set; }
        public double DownloadSuccessRate { get; set; }

        // Submission Statistics
        public int TotalSubmissions { get; set; }
        public int SuccessfulSubmissions { get; set; }
        public int FailedSubmissions { get; set; }
        public double SubmissionSuccessRate { get; set; }

        // Queue Statistics
        public int PendingDownloads { get; set; }
        public int PendingSubmissions { get; set; }

        // Daily Activity
        public List<DailyICUMSActivityDto> DailyActivity { get; set; } = new();

        // Error Analysis
        public List<ErrorAnalysisDto> ErrorAnalysis { get; set; } = new();

        // Common Errors
        public List<ICUMSErrorDto> CommonErrors { get; set; } = new();
    }

    /// <summary>
    /// User activity report data
    /// </summary>
    public class UserActivityReportDto
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // User Statistics
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int NewUsers { get; set; }
        public int TotalLogins { get; set; }
        public int UniqueLogins { get; set; }

        // User Details
        public List<UserActivityDetailsDto> UserActivityDetails { get; set; } = new();

        // Daily Activity
        public List<DailyUserActivityDto> DailyActivity { get; set; } = new();

        // Role Breakdown
        public List<RoleActivityDto> RoleActivity { get; set; } = new();
    }

    /// <summary>
    /// Vehicle imports report data
    /// </summary>
    public class VehicleImportsReportDto
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Vehicle Statistics
        public int TotalVehicles { get; set; }
        public int ProcessedVehicles { get; set; }
        public int PendingVehicles { get; set; }
        public int FailedVehicles { get; set; }
        public int VINsExtracted { get; set; }
        public int VINsFailed { get; set; }
        public double VINExtractionRate { get; set; }

        // Daily Statistics
        public List<DailyVehicleStatsDto> DailyStats { get; set; } = new();

        // Top Vehicle Types
        public List<VehicleTypeStatsDto> VehicleTypeStats { get; set; } = new();

        // Processing Status Statistics
        public List<VehicleProcessingStatusDto> ProcessingStatusStats { get; set; } = new();

        // Processing Status
        public List<VehicleProcessingStatusDto> ProcessingStatus { get; set; } = new();
    }

    /// <summary>
    /// Validation summary report data
    /// </summary>
    public class ValidationSummaryReportDto
    {
        public DateTime GeneratedAt { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }

        // Validation Statistics
        public int TotalValidations { get; set; }
        public int PassedValidations { get; set; }
        public int FailedValidations { get; set; }
        public double ValidationPassRate { get; set; }

        // Validation Types
        public List<ValidationTypeStatsDto> ValidationTypes { get; set; } = new();

        // Common Issues
        public List<ValidationIssueDto> CommonIssues { get; set; } = new();

        // Daily Validation Stats
        public List<DailyValidationStatsDto> DailyStats { get; set; } = new();
    }

    // Supporting DTOs
    public class DailyContainerStatsDto
    {
        public DateTime Date { get; set; }
        public int TotalContainers { get; set; }
        public int ScannedContainers { get; set; }
        public int PendingContainers { get; set; }
        public int FailedContainers { get; set; }
    }

    public class ScannerContainerStatsDto
    {
        public string ScannerName { get; set; } = "";
        public int TotalScans { get; set; }
        public int SuccessfulScans { get; set; }
        public int FailedScans { get; set; }
        public double SuccessRate { get; set; }
    }

    public class TopContainerDto
    {
        public string ContainerNumber { get; set; } = "";
        public int ImageCount { get; set; }
        public string ClearanceType { get; set; } = "";
        public DateTime ScanDate { get; set; }
    }

    public class ScannerDetailsDto
    {
        public string ScannerName { get; set; } = "";
        public string ScannerType { get; set; } = "";
        public int TotalScans { get; set; }
        public int SuccessfulScans { get; set; }
        public int FailedScans { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
    }

    public class DailyPerformanceDto
    {
        public DateTime Date { get; set; }
        public int TotalScans { get; set; }
        public int SuccessfulScans { get; set; }
        public int FailedScans { get; set; }
        public double SuccessRate { get; set; }
        public TimeSpan AverageProcessingTime { get; set; }
    }

    public class ScannerUptimeDto
    {
        public string ScannerName { get; set; } = "";
        public TimeSpan TotalUptime { get; set; }
        public TimeSpan TotalDowntime { get; set; }
        public double UptimePercentage { get; set; }
        public int DowntimeEvents { get; set; }
    }

    public class DailyICUMSActivityDto
    {
        public DateTime Date { get; set; }
        public int Downloads { get; set; }
        public int Submissions { get; set; }
        public int FailedDownloads { get; set; }
        public int FailedSubmissions { get; set; }
        public double SuccessRate { get; set; }
    }

    public class ICUMSErrorDto
    {
        public string ErrorType { get; set; } = "";
        public int Count { get; set; }
        public string Description { get; set; } = "";
    }

    public class UserActivityDetailsDto
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public int LoginCount { get; set; }
        public DateTime LastLogin { get; set; }
        public int ActionsPerformed { get; set; }
    }

    public class DailyUserActivityDto
    {
        public DateTime Date { get; set; }
        public int TotalLogins { get; set; }
        public int UniqueUsers { get; set; }
        public int TotalActions { get; set; }
    }

    public class RoleActivityDto
    {
        public string Role { get; set; } = "";
        public int UserCount { get; set; }
        public int TotalActions { get; set; }
        public int AverageActionsPerUser { get; set; }
    }

    public class DailyVehicleStatsDto
    {
        public DateTime Date { get; set; }
        public int TotalVehicles { get; set; }
        public int VINsExtracted { get; set; }
        public int VINsFailed { get; set; }
        public double ExtractionRate { get; set; }
    }

    public class VehicleTypeStatsDto
    {
        public string VehicleType { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class VehicleProcessingStatusDto
    {
        public string Status { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    public class ValidationTypeStatsDto
    {
        public string ValidationType { get; set; } = "";
        public int TotalValidations { get; set; }
        public int PassedValidations { get; set; }
        public int FailedValidations { get; set; }
        public double PassRate { get; set; }
    }

    public class ValidationIssueDto
    {
        public string IssueType { get; set; } = "";
        public int Count { get; set; }
        public string Description { get; set; } = "";
    }

    public class DailyValidationStatsDto
    {
        public DateTime Date { get; set; }
        public int TotalValidations { get; set; }
        public int PassedValidations { get; set; }
        public int FailedValidations { get; set; }
        public double PassRate { get; set; }
    }

    /// <summary>
    /// Report type information
    /// </summary>
    public class ReportTypeDto
    {
        public string Type { get; set; } = "";
        public string Name { get; set; } = "";
        public string Description { get; set; } = "";
    }

    /// <summary>
    /// Performance metric data
    /// </summary>
    public class PerformanceMetricDto
    {
        public string MetricName { get; set; } = "";
        public double Value { get; set; }
        public string Unit { get; set; } = "";
    }

    /// <summary>
    /// Error analysis data
    /// </summary>
    public class ErrorAnalysisDto
    {
        public string ErrorType { get; set; } = "";
        public int Count { get; set; }
        public double Percentage { get; set; }
    }

    /// <summary>
    /// Export result
    /// </summary>
    public class ExportResultDto
    {
        public bool Success { get; set; }
        public string FileName { get; set; } = "";
        public string ContentType { get; set; } = "";
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public string? ErrorMessage { get; set; }
    }
}

