namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IAdvancedImageProcessingService
    {
        Task<byte[]?> GetEnhancedImageAsync(string containerNumber);
        Task<byte[]?> GetEnhancedImageAsync(string containerNumber, float brightness = 1.15f, float contrast = 1.1f, float blurAmount = 0.3f, bool applyHistogramEqualization = true);
    }
}
