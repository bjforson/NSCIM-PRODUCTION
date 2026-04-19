using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Unified response containing both image data and complete scanner records
    /// </summary>
    public class ContainerImageDataResponse
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public ScannerType DetectedScanner { get; set; }
        public string ImageBase64 { get; set; } = string.Empty;
        public byte[]? ImageBytes { get; set; }
        public string MimeType { get; set; } = "image/jpeg";

        // Scanner-specific data (only one will be populated based on DetectedScanner)
        public FS6000ScanData? FS6000Data { get; set; }
        public ASEScanData? ASEData { get; set; }

        // Common metadata
        public DateTime ScanTime { get; set; }
        public string ProcessingPipeline { get; set; } = string.Empty;
        public bool FromCache { get; set; }
        public int ImageSizeBytes { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string Quality { get; set; } = "Unknown";
    }

    /// <summary>
    /// Complete FS6000 scanner data
    /// </summary>
    public class FS6000ScanData
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public string XmlFilePath { get; set; } = string.Empty;
        public string ImageFilePath { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public string? ProcessingStatus { get; set; }
        public string? ErrorMessage { get; set; }

        // Image information
        public int ImageCount { get; set; }
        public List<FS6000ImageInfo>? Images { get; set; }

        // XML Data if available
        public string? VehicleMake { get; set; }
        public string? VehicleModel { get; set; }
        public string? ChassisNumber { get; set; }
        public string? EngineNumber { get; set; }
    }

    /// <summary>
    /// FS6000 Image information
    /// </summary>
    public class FS6000ImageInfo
    {
        public int Id { get; set; }
        public string ImageType { get; set; } = string.Empty;
        public long ImageSize { get; set; }
        public DateTime CaptureTime { get; set; }
    }

    /// <summary>
    /// Complete ASE scanner data
    /// </summary>
    public class ASEScanData
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public string ScanId { get; set; } = string.Empty;
        public string? OperatorId { get; set; }
        public string? Location { get; set; }
        public string? ScanMode { get; set; }
        public int? EnergyLevel { get; set; }
        public decimal? DoseRate { get; set; }
        public string? ProcessingStatus { get; set; }
        public DateTime? ProcessedAt { get; set; }

        // Image information
        public long ImageSizeBytes { get; set; }
        public int? ImageWidth { get; set; }
        public int? ImageHeight { get; set; }
        public string? ImageFormat { get; set; }

        // Detection information
        public bool? ThreatDetected { get; set; }
        public decimal? ThreatConfidence { get; set; }
        public string? DetectionNotes { get; set; }
    }
}

