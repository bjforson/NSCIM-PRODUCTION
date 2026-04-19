using System;

namespace NickScanCentralImagingPortal.Core.Entities
{
    public class ImageMetadata
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public long FileSizeBytes { get; set; }
        public string ImageFormat { get; set; } = string.Empty;
        public string ProcessingPipeline { get; set; } = string.Empty;
        public string Quality { get; set; } = string.Empty;
        public bool EnhancementApplied { get; set; }
        public long OriginalFileSizeBytes { get; set; }
        public double CompressionRatio { get; set; }
        public DateTime ProcessedAt { get; set; } = DateTime.UtcNow;
        public string ScannerType { get; set; } = string.Empty;
        public string EnhancementType { get; set; } = string.Empty;
        public double SharpeningFactor { get; set; }
        public double ContrastFactor { get; set; }
        public double BrightnessFactor { get; set; }
        public bool ColorCorrectionApplied { get; set; }
        public string ColorSpace { get; set; } = "sRGB";
        public int BitDepth { get; set; } = 8;
        public string CompressionAlgorithm { get; set; } = "JPEG";
        public int QualityLevel { get; set; } = 95;
    }
}
