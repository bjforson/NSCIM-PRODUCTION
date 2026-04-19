using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Models
{
    public class ImageSearchCriteria
    {
        public string? ContainerNumber { get; set; }
        public string? ScannerType { get; set; }
        public string? ImageType { get; set; }
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "CreatedAt";
        public string SortOrder { get; set; } = "desc";
    }

    public class ImageSearchResult
    {
        public IEnumerable<ImageDetails> Images { get; set; } = new List<ImageDetails>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasNextPage => Page < TotalPages;
        public bool HasPreviousPage => Page > 1;
    }

    public class ImageDetails
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string ImageType { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public string ContainerNumber { get; set; } = string.Empty;
        public int Width { get; set; }
        public int Height { get; set; }
        public int Dpi { get; set; }
        public string ColorMode { get; set; } = string.Empty;
        public string Compression { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;
        public string? ProcessingResult { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> Metadata { get; set; } = new();
    }

    public class ImageProcessingRequest
    {
        [Required]
        public string ProcessingType { get; set; } = string.Empty; // OCR, Analysis, QualityCheck, Enhancement

        public Dictionary<string, object> Parameters { get; set; } = new();

        public string? OperatorId { get; set; }

        public string? Comments { get; set; }

        public int Priority { get; set; } = 1; // 1=Low, 2=Medium, 3=High, 4=Critical
    }

    public class ImageProcessingResult
    {
        public int ImageId { get; set; }
        public string Status { get; set; } = string.Empty; // Success, Failed, Processing, Queued
        public string ProcessingType { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public double ProcessingTime { get; set; } // in seconds
        public string? Result { get; set; }
        public string? ErrorMessage { get; set; }
        public Dictionary<string, object> AnalysisResults { get; set; } = new();
        public ImageQualityScore? QualityScore { get; set; }
    }

    public class BatchProcessingRequest
    {
        [Required]
        public List<int> ImageIds { get; set; } = new();

        [Required]
        public string ProcessingType { get; set; } = string.Empty;

        public Dictionary<string, object> Parameters { get; set; } = new();

        public string? OperatorId { get; set; }

        public int Priority { get; set; } = 1;
    }

    public class BatchProcessingResult
    {
        public int TotalImages { get; set; }
        public int SuccessfulImages { get; set; }
        public int FailedImages { get; set; }
        public int QueuedImages { get; set; }
        public List<ImageProcessingResult> Results { get; set; } = new();
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public double TotalProcessingTime { get; set; }
        public List<string> Errors { get; set; } = new();
    }

    public class ImageProcessingStatistics
    {
        public int TotalImages { get; set; }
        public int ProcessedImages { get; set; }
        public int PendingImages { get; set; }
        public int FailedImages { get; set; }
        public int ImagesToday { get; set; }
        public int ImagesThisWeek { get; set; }
        public int ImagesThisMonth { get; set; }
        public double AverageProcessingTime { get; set; } // in seconds
        public double ProcessingSuccessRate { get; set; } // percentage
        public Dictionary<string, int> ScannerTypeBreakdown { get; set; } = new();
        public Dictionary<string, int> ImageTypeBreakdown { get; set; } = new();
        public Dictionary<string, int> ProcessingTypeBreakdown { get; set; } = new();
        public DateTime StatisticsDate { get; set; } = DateTime.UtcNow;
    }

    public class ImageQualityMetrics
    {
        public int TotalImages { get; set; }
        public int HighQualityImages { get; set; }
        public int MediumQualityImages { get; set; }
        public int LowQualityImages { get; set; }
        public double AverageQualityScore { get; set; }
        public Dictionary<string, int> QualityIssues { get; set; } = new();
        public List<QualityIssue> TopIssues { get; set; } = new();
        public DateTime MetricsDate { get; set; } = DateTime.UtcNow;
    }

    public class QualityIssue
    {
        public string IssueType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public int Count { get; set; }
        public double Percentage { get; set; }
        public string Severity { get; set; } = string.Empty; // Low, Medium, High
    }

    public class ImageQualityScore
    {
        public double OverallScore { get; set; } // 0-100
        public double SharpnessScore { get; set; }
        public double BrightnessScore { get; set; }
        public double ContrastScore { get; set; }
        public double ColorAccuracyScore { get; set; }
        public double ResolutionScore { get; set; }
        public List<string> Issues { get; set; } = new();
        public List<string> Recommendations { get; set; } = new();
    }

    public class ImageProcessingHistory
    {
        public int Id { get; set; }
        public int ImageId { get; set; }
        public string ProcessingType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? OperatorId { get; set; }
        public string? Result { get; set; }
        public string? ErrorMessage { get; set; }
        public double ProcessingTime { get; set; }
        public DateTime ProcessedAt { get; set; }
        public Dictionary<string, object> Parameters { get; set; } = new();
    }

    public class ImageProcessingQueueStatus
    {
        public int TotalInQueue { get; set; }
        public int HighPriorityCount { get; set; }
        public int MediumPriorityCount { get; set; }
        public int LowPriorityCount { get; set; }
        public int ProcessingCount { get; set; }
        public double AverageWaitTime { get; set; } // in minutes
        public List<QueueItem> QueueItems { get; set; } = new();
        public Dictionary<string, int> ProcessingTypeCounts { get; set; } = new();
    }

    public class QueueItem
    {
        public int ImageId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string ContainerNumber { get; set; } = string.Empty;
        public string ProcessingType { get; set; } = string.Empty;
        public int Priority { get; set; }
        public DateTime QueuedAt { get; set; }
        public TimeSpan WaitTime => DateTime.UtcNow - QueuedAt;
        public string ScannerType { get; set; } = string.Empty;
    }

    public class ImageMetadataUpdate
    {
        public string? ImageType { get; set; }
        public string? ColorMode { get; set; }
        public string? Compression { get; set; }
        public int? Dpi { get; set; }
        public Dictionary<string, object> AdditionalMetadata { get; set; } = new();
        public string? UpdatedBy { get; set; }
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }

    public class ImageAnalysisResult
    {
        public int ImageId { get; set; }
        public string AnalysisType { get; set; } = string.Empty;
        public Dictionary<string, object> DetectedObjects { get; set; } = new();
        public Dictionary<string, object> TextExtraction { get; set; } = new();
        public Dictionary<string, object> QualityMetrics { get; set; } = new();
        public List<string> Anomalies { get; set; } = new();
        public double ConfidenceScore { get; set; }
        public DateTime AnalyzedAt { get; set; }
    }

    public class ImageEnhancementRequest
    {
        public bool EnhanceBrightness { get; set; } = false;
        public bool EnhanceContrast { get; set; } = false;
        public bool EnhanceSharpness { get; set; } = false;
        public bool CorrectColor { get; set; } = false;
        public bool RemoveNoise { get; set; } = false;
        public Dictionary<string, object> EnhancementParameters { get; set; } = new();
    }

    public class ImageEnhancementResult
    {
        public int OriginalImageId { get; set; }
        public int EnhancedImageId { get; set; }
        public string EnhancementType { get; set; } = string.Empty;
        public Dictionary<string, double> ImprovementMetrics { get; set; } = new();
        public DateTime EnhancedAt { get; set; }
    }
}
