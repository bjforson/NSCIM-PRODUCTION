namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for sending email notifications
    /// </summary>
    public interface IEmailService
    {
        /// <summary>
        /// Send a CMR validation failure alert to administrators
        /// </summary>
        Task<bool> SendCMRValidationAlertAsync(CMRValidationAlertModel alert);

        /// <summary>
        /// Send daily data quality report
        /// </summary>
        Task<bool> SendDailyDataQualityReportAsync(DataQualityReportModel report);

        /// <summary>
        /// Send generic email
        /// </summary>
        Task<bool> SendEmailAsync(string to, string subject, string body, bool isHtml = true);

        /// <summary>
        /// Send email to multiple recipients
        /// </summary>
        Task<bool> SendEmailAsync(List<string> to, string subject, string body, bool isHtml = true);
    }

    /// <summary>
    /// Model for CMR validation alert
    /// </summary>
    public class CMRValidationAlertModel
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string DeclarationNumber { get; set; } = string.Empty;
        public List<string> MissingFields { get; set; } = new();
        public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
        public string SourceFile { get; set; } = string.Empty;
        public bool AutoQueued { get; set; }
    }

    /// <summary>
    /// Model for daily data quality report
    /// </summary>
    public class DataQualityReportModel
    {
        public DateTime ReportDate { get; set; }
        public int TotalCMRRecords { get; set; }
        public int ValidRecords { get; set; }
        public int InvalidRecords { get; set; }
        public double SuccessRate { get; set; }
        public int NewRecordsToday { get; set; }
        public int FixedRecordsToday { get; set; }
        public List<string> ProblematicContainers { get; set; } = new();
        public int QueuedForRedownload { get; set; }
        public int SuccessfulRedownloads { get; set; }
        public int FailedRedownloads { get; set; }
    }
}

