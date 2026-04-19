using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for validating image data completeness and quality
    /// </summary>
    public interface IImageValidationService
    {
        /// <summary>
        /// Validates image data for a specific container
        /// </summary>
        /// <param name="containerNumber">Container number to validate</param>
        /// <returns>Image data completeness report</returns>
        Task<ImageDataCompleteness> ValidateImageDataAsync(string containerNumber);

        /// <summary>
        /// Gets image file path for a container
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <returns>Image file path if found</returns>
        Task<string?> GetImagePathAsync(string containerNumber);

        /// <summary>
        /// Validates image file quality
        /// </summary>
        /// <param name="imagePath">Path to image file</param>
        /// <returns>Image quality metrics</returns>
        Task<ContainerImageQualityMetrics> ValidateImageQualityAsync(string imagePath);

        /// <summary>
        /// Gets image statistics
        /// </summary>
        /// <returns>Image statistics</returns>
        Task<ImageStatistics> GetImageStatisticsAsync();

        /// <summary>
        /// Checks if image exists for container
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <returns>True if image exists</returns>
        Task<bool> ImageExistsAsync(string containerNumber);

        /// <summary>
        /// Gets image file information
        /// </summary>
        /// <param name="containerNumber">Container number</param>
        /// <returns>Image file information</returns>
        Task<ImageFileInfo?> GetImageFileInfoAsync(string containerNumber);
    }

    public class ImageFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public string ImageFormat { get; set; } = string.Empty;
        public DateTime CreatedDate { get; set; }
        public DateTime ModifiedDate { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public double AspectRatio { get; set; }
        public string QualityRating { get; set; } = string.Empty;
    }

    public class ImageStatistics
    {
        public int TotalImages { get; set; }
        public long TotalImageSizeBytes { get; set; }
        public Dictionary<string, int> ImagesByFormat { get; set; } = new();
        public Dictionary<string, int> ImagesByQuality { get; set; } = new();
        public double AverageFileSizeKB { get; set; }
        public int MissingImages { get; set; }
        public int ValidImages { get; set; }
        public int InvalidImages { get; set; }
    }
}
