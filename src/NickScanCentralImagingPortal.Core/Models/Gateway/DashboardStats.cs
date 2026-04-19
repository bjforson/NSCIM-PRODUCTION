using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Models.Gateway
{
    /// <summary>
    /// Dashboard statistics aggregated from multiple data sources
    /// </summary>
    public class DashboardStats
    {
        public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
        public int ResponseTimeMs { get; set; }

        // Container Statistics
        public ContainerStats Containers { get; set; } = new();

        // Scanner Statistics
        public ScannerStats Scanners { get; set; } = new();

        // ICUMS Statistics
        public ICUMSStats ICUMS { get; set; } = new();

        // Validation Statistics
        public ValidationStats Validation { get; set; } = new();

        // Image Processing Statistics
        public ImageProcessingStats Images { get; set; } = new();

        // Time-based trends (last 24 hours, 7 days, 30 days)
        public GatewayTrendData Trends { get; set; } = new();
    }

    public class ContainerStats
    {
        public int TotalScanned { get; set; }
        public int ScannedToday { get; set; }
        public int ScannedThisWeek { get; set; }
        public int ScannedThisMonth { get; set; }
        public int PendingProcessing { get; set; }
        public int WithImages { get; set; }
        public int WithoutImages { get; set; }

        public Dictionary<string, int> BySize { get; set; } = new(); // "20FT": 500, "40FT": 300
        public Dictionary<string, int> ByType { get; set; } = new(); // "Import": 600, "Export": 200
    }

    public class ScannerStats
    {
        public int TotalScans { get; set; }
        public int ASEScans { get; set; }
        public int FS6000Scans { get; set; }
        public int UnknownScans { get; set; }

        public Dictionary<string, int> ByLocation { get; set; } = new(); // Location → Count
        public Dictionary<string, int> ByOperator { get; set; } = new(); // Operator → Count

        public DateTime? LastASEScan { get; set; }
        public DateTime? LastFS6000Scan { get; set; }
    }

    public class ICUMSStats
    {
        public int TotalDownloads { get; set; }
        public int DownloadsToday { get; set; }
        public int DownloadsThisWeek { get; set; }
        public int DownloadsThisMonth { get; set; }

        public int ContainersWithBOE { get; set; }
        public int ContainersWithoutBOE { get; set; }

        public Dictionary<string, int> ByConsignee { get; set; } = new(); // Top 10 consignees
        public Dictionary<string, int> ByOriginPort { get; set; } = new(); // Top 10 ports
        public Dictionary<string, int> ByDestinationPort { get; set; } = new();

        public DateTime? LastDownload { get; set; }
    }

    public class ValidationStats
    {
        public int TotalValidated { get; set; }
        public int PassedValidation { get; set; }
        public int FailedValidation { get; set; }
        public int PendingValidation { get; set; }

        public double AverageCompletenessScore { get; set; }

        public Dictionary<string, int> ByStatus { get; set; } = new(); // "Complete": 500, "Incomplete": 100
        public Dictionary<string, int> CommonErrors { get; set; } = new(); // Error type → Count
    }

    public class ImageProcessingStats
    {
        public int TotalImagesProcessed { get; set; }
        public int ImagesProcessedToday { get; set; }
        public int CachedImages { get; set; }
        public int FailedConversions { get; set; }

        public long TotalImageSizeBytes { get; set; }
        public double AverageImageSizeMB { get; set; }
        public double CacheHitRate { get; set; } // Percentage

        public Dictionary<string, int> ByFormat { get; set; } = new(); // "JPEG": 800, "PNG": 200
        public Dictionary<string, int> ByPipeline { get; set; } = new(); // "ASE-DLL-PNG-to-JPEG": 600
    }

    /// <summary>
    /// Trend data for Gateway dashboard (renamed to avoid Swagger schema conflict with Core.Models.TrendData)
    /// </summary>
    public class GatewayTrendData
    {
        public List<GatewayDailyTrend> Last7Days { get; set; } = new();
        public List<GatewayDailyTrend> Last30Days { get; set; } = new();
        public List<GatewayHourlyTrend> Last24Hours { get; set; } = new();
    }

    /// <summary>
    /// Daily trend for Gateway dashboard (renamed to avoid Swagger schema conflict with Core.Models.DailyTrend)
    /// </summary>
    public class GatewayDailyTrend
    {
        public DateTime Date { get; set; }
        public int ScansCount { get; set; }
        public int ICUMSDownloadsCount { get; set; }
        public int ImagesProcessedCount { get; set; }
        public int ValidationCount { get; set; }
    }

    /// <summary>
    /// Hourly trend for Gateway dashboard (renamed to avoid Swagger schema conflict with Core.Models.HourlyTrend)
    /// </summary>
    public class GatewayHourlyTrend
    {
        public DateTime Hour { get; set; }
        public int ScansCount { get; set; }
        public int ICUMSDownloadsCount { get; set; }
        public int ImagesProcessedCount { get; set; }
    }
}

