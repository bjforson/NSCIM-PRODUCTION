using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    public interface IImagePipeline
    {
        ScannerType ScannerType { get; }
        Task<Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber);
        Task<Core.Interfaces.ImageMetadata> GetImageMetadataAsync(string containerNumber);
        Task<string> GetImageAsBase64Async(string containerNumber);
    }

    public interface IImageCacheService
    {
        Task CacheImageAsync(ImageCache imageCache);
        Task<ImageCache?> GetCachedImageAsync(string containerNumber, ScannerType scannerType);
        Task SetCachedImageAsync(string containerNumber, ScannerType scannerType, byte[] imageData, string imageFormat);
        Task RemoveCachedImageAsync(string containerNumber, ScannerType scannerType);
        Task ClearExpiredCacheAsync();
        Task<int> PurgeStaleEntriesAsync(int minSizeBytes, int minWidth, int minHeight);
    }
}
