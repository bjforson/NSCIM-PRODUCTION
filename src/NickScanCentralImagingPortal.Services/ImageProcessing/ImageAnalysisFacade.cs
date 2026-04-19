using NickScanCentralImagingPortal.Core.DTOs.ImageProcessing;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    /// <summary>
    /// Facade over image analysis services: enhancement, OCR, object detection, quality assessment.
    /// Reduces controller injection count by providing a single entry point.
    /// </summary>
    public class ImageAnalysisFacade : IImageAnalysisFacade
    {
        private readonly IAdvancedImageProcessingService _advancedProcessing;
        private readonly IImageProcessingService _imageProcessing;
        private readonly IContainerNumberOcrService _ocr;
        private readonly IContainerObjectDetectionService _objectDetection;
        private readonly IImageQualityAssessmentService _qualityAssessment;

        public ImageAnalysisFacade(
            IAdvancedImageProcessingService advancedProcessing,
            IImageProcessingService imageProcessing,
            IContainerNumberOcrService ocr,
            IContainerObjectDetectionService objectDetection,
            IImageQualityAssessmentService qualityAssessment)
        {
            _advancedProcessing = advancedProcessing;
            _imageProcessing = imageProcessing;
            _ocr = ocr;
            _objectDetection = objectDetection;
            _qualityAssessment = qualityAssessment;
        }

        public Task<byte[]?> GetEnhancedImageAsync(string containerNumber)
            => _advancedProcessing.GetEnhancedImageAsync(containerNumber);

        public Task<byte[]?> GetEnhancedImageAsync(string containerNumber, float brightness = 1.15f, float contrast = 1.1f, float blurAmount = 0.3f, bool applyHistogramEqualization = true)
            => _advancedProcessing.GetEnhancedImageAsync(containerNumber, brightness, contrast, blurAmount, applyHistogramEqualization);

        public Task<string> GetImageAsBase64Async(string containerNumber, ScannerType? preferredScanner = null)
            => _imageProcessing.GetImageAsBase64Async(containerNumber, preferredScanner);

        public Task<OcrResult> ExtractContainerNumberAsync(string containerNumber)
            => _ocr.ExtractContainerNumberAsync(containerNumber);

        public Task<ObjectDetectionResult> DetectObjectsAsync(string containerNumber)
            => _objectDetection.DetectObjectsAsync(containerNumber);

        public Task<QualityAssessment> AssessQualityAsync(string containerNumber)
            => _qualityAssessment.AssessQualityAsync(containerNumber);
    }
}
