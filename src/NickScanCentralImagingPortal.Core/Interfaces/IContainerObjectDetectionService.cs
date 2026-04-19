using NickScanCentralImagingPortal.Core.DTOs.ImageProcessing;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IContainerObjectDetectionService
    {
        Task<ObjectDetectionResult> DetectObjectsAsync(string containerNumber);
    }
}
