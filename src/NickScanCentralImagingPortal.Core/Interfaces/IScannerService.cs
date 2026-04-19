using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IScannerService
    {
        Task<ScannerData> GetScannerDataAsync(string scannerId);
        Task<ImageData> GetImageDataAsync(string imageId);
        Task<bool> ValidateDataAsync(ScannerData data);
        Task<ScannerProcessingResult> ProcessImageAsync(ImageData image);
        Task<bool> IsHealthyAsync();
    }

    public class ScannerData
    {
        public string ContainerId { get; set; } = string.Empty;
        public string ScannerId { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanDateTime { get; set; }
        public string? RawData { get; set; }
        public string? ImagePath { get; set; }
    }

    public class ImageData
    {
        public string ImageId { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string ImageType { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
    }

    public class ScannerProcessingResult
    {
        public string ResultType { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty;
        public string? ResultData { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime ProcessedAt { get; set; }
    }
}
