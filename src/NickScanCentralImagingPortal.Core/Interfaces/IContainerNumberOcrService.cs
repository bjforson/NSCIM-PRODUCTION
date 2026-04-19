using NickScanCentralImagingPortal.Core.DTOs.ImageProcessing;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IContainerNumberOcrService
    {
        Task<OcrResult> ExtractContainerNumberAsync(string containerNumber);
    }
}
