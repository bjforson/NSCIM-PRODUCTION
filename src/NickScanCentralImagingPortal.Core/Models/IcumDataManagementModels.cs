using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Models
{
    public class IcumContainerSearchCriteria
    {
        public string? SearchTerm { get; set; }
        public string? ClearanceType { get; set; }
        public string? ConsigneeName { get; set; }
        public string? ShipperName { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "CreatedAt";
        public string SortOrder { get; set; } = "desc";
    }

    public class IcumContainerDetails
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string? BoeData { get; set; }
        public string? MasterBlNumber { get; set; }
        public string? HouseBl { get; set; }
        public string? RotationNumber { get; set; }
        public string? ConsigneeName { get; set; }
        public string? ShipperName { get; set; }
        public string? CountryOfOrigin { get; set; }
        public decimal? TotalDutyPaid { get; set; }
        public string? CrmsLevel { get; set; }
        public string? ClearanceType { get; set; }
        public string? DeclarationNumber { get; set; }
        public decimal? ContainerWeight { get; set; }
        public int? ContainerQuantity { get; set; }
        public string? ContainerISO { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? Status { get; set; }
        public int ManifestItemCount { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;
    }

    public class IcumContainerSearchResult
    {
        public IEnumerable<IcumContainerDetails> Containers { get; set; } = new List<IcumContainerDetails>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasNextPage => Page < TotalPages;
        public bool HasPreviousPage => Page > 1;
    }


    public class IcumManifestItemDetails
    {
        public int Id { get; set; }
        public int IcumContainerDataId { get; set; }
        public string HsCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal? Quantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public decimal? Weight { get; set; }
        public decimal? ItemFob { get; set; }
        public decimal? ItemDutyPaid { get; set; }
        public string FobCurrency { get; set; } = string.Empty;
        public string CountryOfOrigin { get; set; } = string.Empty;
        public int? ItemNo { get; set; }
        public string Cpc { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class IcumDataStatistics
    {
        public int TotalContainers { get; set; }
        public int TotalManifestItems { get; set; }
        public int ContainersToday { get; set; }
        public int ContainersThisWeek { get; set; }
        public int ContainersThisMonth { get; set; }
        public Dictionary<string, int> ClearanceTypeBreakdown { get; set; } = new();
        public Dictionary<string, int> CountryOfOriginBreakdown { get; set; } = new();
        public Dictionary<string, int> CrmsLevelBreakdown { get; set; } = new();
        public decimal TotalDutyPaid { get; set; }
        public decimal AverageDutyPerContainer { get; set; }
        public DateTime StatisticsDate { get; set; } = DateTime.UtcNow;
    }

    public class IcumProcessingStatus
    {
        public int TotalFilesDownloaded { get; set; }
        public int FilesPendingProcessing { get; set; }
        public int FilesProcessing { get; set; }
        public int FilesCompleted { get; set; }
        public int FilesFailed { get; set; }
        public DateTime LastDownloadTime { get; set; }
        public DateTime LastProcessingTime { get; set; }
        public double ProcessingSuccessRate { get; set; }
        public double AverageProcessingTime { get; set; } // in minutes
        public List<ProcessingStatusItem> RecentFiles { get; set; } = new();
    }

    public class ProcessingStatusItem
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ProcessingStatus { get; set; } = string.Empty;
        public DateTime DownloadDate { get; set; }
        public DateTime? ProcessedDate { get; set; }
        public int RecordCount { get; set; }
        public string? ErrorMessage { get; set; }
    }

    public class DownloadedFileStatus
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public DateTime DownloadDate { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public int? RecordCount { get; set; }
        public DateTime? ProcessedDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class IngestionLogDetails
    {
        public int Id { get; set; }
        public int? DownloadedFileId { get; set; }
        public string ProcessType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class IcumDataExportRequest
    {
        public string? ClearanceType { get; set; }
        public string? ConsigneeName { get; set; }
        public string? ShipperName { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public bool IncludeManifestItems { get; set; } = false;
        public List<string> Fields { get; set; } = new();
    }

    public class IcumExportData
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ClearanceType { get; set; } = string.Empty;
        public string ConsigneeName { get; set; } = string.Empty;
        public string ShipperName { get; set; } = string.Empty;
        public string MasterBlNumber { get; set; } = string.Empty;
        public string RotationNumber { get; set; } = string.Empty;
        public string CountryOfOrigin { get; set; } = string.Empty;
        public decimal? TotalDutyPaid { get; set; }
        public string CrmsLevel { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class IcumDataQualityMetrics
    {
        public int TotalRecords { get; set; }
        public int CompleteRecords { get; set; }
        public int IncompleteRecords { get; set; }
        public int RecordsWithMissingFields { get; set; }
        public double DataCompletenessRate { get; set; }
        public Dictionary<string, int> MissingFieldCounts { get; set; } = new();
        public Dictionary<string, int> DataQualityIssues { get; set; } = new();
        public List<DataQualityIssue> TopIssues { get; set; } = new();
        public DateTime MetricsDate { get; set; } = DateTime.UtcNow;
    }

    public class DataQualityIssue
    {
        public string FieldName { get; set; } = string.Empty;
        public string IssueType { get; set; } = string.Empty;
        public int Count { get; set; }
        public string Description { get; set; } = string.Empty;
        public double Percentage { get; set; }
    }

    public class IcumProcessingTrends
    {
        public List<DailyProcessingTrend> DailyTrends { get; set; } = new();
        public List<HourlyProcessingTrend> HourlyTrends { get; set; } = new();
        public Dictionary<string, int> ClearanceTypeTrends { get; set; } = new();
        public Dictionary<string, double> ProcessingTimeTrends { get; set; } = new();
        public DateTime TrendsDate { get; set; } = DateTime.UtcNow;
    }

    public class DailyProcessingTrend
    {
        public DateTime Date { get; set; }
        public int ContainersProcessed { get; set; }
        public int ManifestItemsProcessed { get; set; }
        public double AverageProcessingTime { get; set; }
        public int ErrorCount { get; set; }
    }

    public class HourlyProcessingTrend
    {
        public int Hour { get; set; }
        public int ContainersProcessed { get; set; }
        public int ManifestItemsProcessed { get; set; }
        public double AverageProcessingTime { get; set; }
    }

    public class IcumDataIntegrityReport
    {
        public bool IsValid { get; set; }
        public int TotalIssues { get; set; }
        public List<IntegrityIssue> Issues { get; set; } = new();
        public Dictionary<string, int> IssueCounts { get; set; } = new();
        public DateTime ReportDate { get; set; } = DateTime.UtcNow;
    }

    public class IntegrityIssue
    {
        public string IssueType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Severity { get; set; } = string.Empty; // Low, Medium, High, Critical
        public int Count { get; set; }
        public List<string> Examples { get; set; } = new();
    }
}
