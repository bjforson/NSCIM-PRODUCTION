using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.FS6000;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public interface IFS6000ImageProcessingService
    {
        Task<byte[]?> ConvertJpegToBase64Async(string jpegFilePath);
        Task<int> GetFileSizeBytesAsync(string filePath);
        Task<string> GetImageTypeFromFileNameAsync(string fileName);
        string ConvertBinaryToBase64(byte[] imageData);
        Task<string?> GetImageAsBase64Async(string jpegFilePath);
    }

    public class FS6000ImageProcessingService : IFS6000ImageProcessingService
    {
        private readonly ILogger<FS6000ImageProcessingService> _logger;

        public FS6000ImageProcessingService(ILogger<FS6000ImageProcessingService> logger)
        {
            _logger = logger;
        }

        public async Task<byte[]?> ConvertJpegToBase64Async(string jpegFilePath)
        {
            try
            {
                if (!File.Exists(jpegFilePath))
                {
                    _logger.LogWarning("JPEG file not found: {FilePath}", jpegFilePath);
                    return null;
                }

                var imageBytes = await File.ReadAllBytesAsync(jpegFilePath);
                _logger.LogDebug("Successfully converted JPEG to Base64. File: {FilePath}, Size: {Size} bytes", jpegFilePath, imageBytes.Length);
                return imageBytes;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting JPEG to Base64: {FilePath}", jpegFilePath);
                return null;
            }
        }

        public Task<int> GetFileSizeBytesAsync(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("File not found: {FilePath}", filePath);
                    return Task.FromResult(0);
                }

                var fileInfo = new FileInfo(filePath);
                return Task.FromResult((int)fileInfo.Length);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting file size: {FilePath}", filePath);
                return Task.FromResult(0);
            }
        }

        public Task<string> GetImageTypeFromFileNameAsync(string fileName)
        {
            try
            {
                if (string.IsNullOrEmpty(fileName))
                {
                    return Task.FromResult("Unknown");
                }

                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                return Task.FromResult(extension switch
                {
                    ".jpg" or ".jpeg" => "Main",
                    ".png" => "Icon",
                    ".bmp" => "CCR",
                    ".tiff" => "LPR",
                    ".pdf" => "Manifest",
                    _ => "Unknown"
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error determining image type from filename: {FileName}", fileName);
                return Task.FromResult("Unknown");
            }
        }

        /// <summary>
        /// Convert binary image data to base64 string
        /// </summary>
        public string ConvertBinaryToBase64(byte[] imageData)
        {
            try
            {
                if (imageData == null || imageData.Length == 0)
                {
                    _logger.LogWarning("Image data is null or empty");
                    return string.Empty;
                }

                var base64String = Convert.ToBase64String(imageData);
                _logger.LogDebug("Successfully converted binary data to base64. Size: {Size} bytes", imageData.Length);
                return base64String;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting binary data to base64");
                return string.Empty;
            }
        }

        /// <summary>
        /// Read JPEG file and convert directly to base64 string
        /// </summary>
        public async Task<string?> GetImageAsBase64Async(string jpegFilePath)
        {
            try
            {
                if (!File.Exists(jpegFilePath))
                {
                    _logger.LogWarning("JPEG file not found: {FilePath}", jpegFilePath);
                    return null;
                }

                var imageBytes = await File.ReadAllBytesAsync(jpegFilePath);
                var base64String = Convert.ToBase64String(imageBytes);

                _logger.LogDebug("Successfully converted JPEG to base64. File: {FilePath}, Size: {Size} bytes, Base64 length: {Base64Length}",
                    jpegFilePath, imageBytes.Length, base64String.Length);

                return base64String;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error converting JPEG to base64: {FilePath}", jpegFilePath);
                return null;
            }
        }
    }
}
