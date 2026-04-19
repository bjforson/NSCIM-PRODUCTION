namespace NickScanCentralImagingPortal.Core.DTOs.ImageProcessing
{
    public class OcrResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public string DetectedText { get; set; } = string.Empty;
        public string? ContainerNumber { get; set; }
        public bool MatchesExpected { get; set; }
        public string? ExpectedContainerNumber { get; set; }
        public float Confidence { get; set; }
    }

    public class ObjectDetectionResult
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public List<BoundingBox> ContainerRegions { get; set; } = new();
        public List<BoundingBox> VehicleRegions { get; set; } = new();
        public List<BoundingBox> Anomalies { get; set; } = new();
        public bool HasAnomalies { get; set; }
    }

    public class BoundingBox
    {
        public int X { get; set; }
        public int Y { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Type { get; set; } = string.Empty;
        public float Confidence { get; set; }
    }

    public class QualityAssessment
    {
        public bool Success { get; set; }
        public string? ErrorMessage { get; set; }
        public float OverallScore { get; set; }
        public float Sharpness { get; set; }
        public float Brightness { get; set; }
        public float Contrast { get; set; }
        public float NoiseLevel { get; set; }
        public bool IsAcceptable { get; set; }
        public List<string> Recommendations { get; set; } = new();
    }
}
