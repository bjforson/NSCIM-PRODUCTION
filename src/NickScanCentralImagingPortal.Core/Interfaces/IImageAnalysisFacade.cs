using NickScanCentralImagingPortal.Core.DTOs.ImageProcessing;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Facade over image analysis services: enhancement, OCR, object detection, quality assessment.
    /// Reduces controller injection count and provides a unified entry point for image analysis operations.
    /// </summary>
    public interface IImageAnalysisFacade
    {
        // Advanced image processing
        Task<byte[]?> GetEnhancedImageAsync(string containerNumber);
        Task<byte[]?> GetEnhancedImageAsync(string containerNumber, float brightness = 1.15f, float contrast = 1.1f, float blurAmount = 0.3f, bool applyHistogramEqualization = true);

        // Standard image processing (delegated to IImageProcessingService)
        Task<string> GetImageAsBase64Async(string containerNumber, ScannerType? preferredScanner = null);

        // OCR
        Task<OcrResult> ExtractContainerNumberAsync(string containerNumber);

        // Object detection
        Task<ObjectDetectionResult> DetectObjectsAsync(string containerNumber);

        // Quality assessment
        Task<QualityAssessment> AssessQualityAsync(string containerNumber);
    }
}
