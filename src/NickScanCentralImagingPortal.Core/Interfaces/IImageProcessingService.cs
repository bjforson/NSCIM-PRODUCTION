using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    public interface IImageProcessingService
    {
        Task<Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber);
        Task<Core.Models.ImageProcessingResult> ProcessImageAsync(string containerNumber, ScannerType preferredScanner);
        Task<Core.Models.ImageProcessingResult> ProcessImageAsync(ImageDetails image, ImageProcessingRequest request);
        Task<ImageMetadata> GetImageMetadataAsync(string containerNumber);
        Task<ImageMetadata> GetImageMetadataAsync(string containerNumber, ScannerType? preferredScanner = null);
        Task<ScannerType> DetectScannerTypeAsync(string containerNumber);
        Task<BatchProcessingResult> BatchProcessImagesAsync(BatchProcessingRequest request);
        Task RetryImageProcessingAsync(int imageId);
        Task<string> GetImageAsBase64Async(string containerNumber, ScannerType? preferredScanner = null);
        Task<Core.Models.ContainerImageDataResponse?> GetCompleteContainerDataAsync(string containerNumber);
        Task<Core.Models.ContainerImageDataResponse?> GetCompleteContainerDataAsync(string containerNumber, string? imageType);
    }


    public class ImageMetadata
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public long FileSizeBytes { get; set; }
        public DateTime ScanTime { get; set; }
        public string ScannerId { get; set; } = string.Empty;
        public string ImageFormat { get; set; } = string.Empty;
        public string ProcessingPipeline { get; set; } = string.Empty;
        public Dictionary<string, object> AdditionalProperties { get; set; } = new();
    }

    public enum ScannerType
    {
        Unknown = 0,
        FS6000 = 1,
        ASE = 2,
        HeimannSmith = 3
    }
}
