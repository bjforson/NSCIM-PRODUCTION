using System.Drawing;
using System.Drawing.Imaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ContainerValidation
{
    /// <summary>
    /// Service for validating image data completeness and quality
    /// </summary>
    public class ImageValidationService : IImageValidationService
    {
        private readonly ILogger<ImageValidationService> _logger;
        private readonly ApplicationDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private readonly string[] _supportedFormats = { ".jpg", ".jpeg", ".png", ".bmp", ".tiff" };
        private readonly string[] _searchPaths;

        public ImageValidationService(ILogger<ImageValidationService> logger, ApplicationDbContext dbContext, IConfiguration configuration)
        {
            _logger = logger;
            _dbContext = dbContext;
            _configuration = configuration;
            _searchPaths = new[]
            {
                _configuration["ImageValidation:TadiMirrorPath"] ?? @"C:\tadi_mirror",
                _configuration["ImageValidation:TadiPath"] ?? @"C:\TADI",
                _configuration["ImageValidation:ImagesPath"] ?? @"C:\Images",
                _configuration["ImageValidation:ContainerImagesPath"] ?? @"C:\ContainerImages",
                Directory.GetCurrentDirectory()
            };
        }

        /// <summary>
        /// Validates image data for a specific container
        /// </summary>
        public async Task<ImageDataCompleteness> ValidateImageDataAsync(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Validating image data for container: {ContainerNumber}", containerNumber);

                var completeness = new ImageDataCompleteness();

                // Check database for scanner records (FS6000 or ASE)
                // If a scanner record exists, the image is available through the image processing pipeline
                bool hasFS6000Scan = false;
                bool hasASEScan = false;

                try
                {
                    // Check if there's a FS6000 scan record (image will be available through the pipeline)
                    hasFS6000Scan = await _dbContext.FS6000Scans
                        .AnyAsync(s => s.ContainerNumber == containerNumber);

                    if (hasFS6000Scan)
                    {
                        _logger.LogInformation("Found FS6000 scan record for container: {ContainerNumber}", containerNumber);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error checking FS6000 scans for container {ContainerNumber}: {Error}", containerNumber, ex.Message);
                }

                try
                {
                    // Check if there's an ASE scan record
                    hasASEScan = await _dbContext.AseScans
                        .AnyAsync(s => s.ContainerNumber == containerNumber);

                    if (hasASEScan)
                    {
                        _logger.LogInformation("Found ASE scan record for container: {ContainerNumber}", containerNumber);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("Error checking ASE scans for container {ContainerNumber}: {Error}", containerNumber, ex.Message);
                }

                // If we have a scanner record, the image is available
                if (hasFS6000Scan || hasASEScan)
                {
                    completeness.HasImage = true;
                    completeness.ImagePath = $"Scanner:{(hasFS6000Scan ? "FS6000" : "ASE")}";
                    completeness.IsImageValid = true;
                    completeness.ImageFormat = "JPEG";
                    completeness.CompletenessScore = 100;

                    _logger.LogInformation("Image available for container {ContainerNumber} via {ScannerType} scanner",
                        containerNumber, hasFS6000Scan ? "FS6000" : "ASE");

                    return completeness;
                }

                // Fallback: Check file system for images
                var imagePath = await GetImagePathAsync(containerNumber);
                if (string.IsNullOrEmpty(imagePath))
                {
                    completeness.HasImage = false;
                    completeness.CompletenessScore = 0;
                    completeness.ValidationErrors.Add("No image found for container");
                    return completeness;
                }

                completeness.HasImage = true;
                completeness.ImagePath = imagePath;

                // Get file information
                var fileInfo = new FileInfo(imagePath);
                completeness.FileSizeBytes = fileInfo.Length;
                completeness.ImageFormat = Path.GetExtension(imagePath).TrimStart('.').ToUpperInvariant();

                // Validate file size
                if (fileInfo.Length == 0)
                {
                    completeness.ValidationErrors.Add("Image file is empty");
                    completeness.IsImageValid = false;
                    completeness.CompletenessScore = 0;
                    return completeness;
                }

                // Validate image quality
                var qualityMetrics = await ValidateImageQualityAsync(imagePath);
                completeness.QualityMetrics = qualityMetrics;
                completeness.IsImageValid = qualityMetrics.QualityRating != "Poor";

                // Calculate completeness score
                completeness.CompletenessScore = CalculateImageCompletenessScore(fileInfo, qualityMetrics);

                _logger.LogInformation("Image validation completed for container {ContainerNumber}: Valid {IsValid}, Score {Score}",
                    containerNumber, completeness.IsImageValid, completeness.CompletenessScore);

                return completeness;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating image data for container: {ContainerNumber}", containerNumber);
                return new ImageDataCompleteness
                {
                    HasImage = false,
                    CompletenessScore = 0,
                    ValidationErrors = new List<string> { "Error validating image data" }
                };
            }
        }

        /// <summary>
        /// Gets image file path for a container
        /// </summary>
        public async Task<string?> GetImagePathAsync(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Getting image path for container: {ContainerNumber}", containerNumber);

                // First, check database for the actual filename
                try
                {
                    var fs6000Image = await _dbContext.FS6000Scans
                        .Where(s => s.ContainerNumber == containerNumber)
                        .SelectMany(s => s.Images)
                        .Where(i => i.ImageType == "Main" || i.ImageType == "Icon")
                        .OrderByDescending(i => i.CreatedAt)
                        .FirstOrDefaultAsync();

                    if (fs6000Image != null && !string.IsNullOrEmpty(fs6000Image.FileName))
                    {
                        _logger.LogInformation("Found FS6000 image filename in database: {FileName}", fs6000Image.FileName);

                        // Search for this specific filename in the search paths
                        foreach (var searchPath in _searchPaths)
                        {
                            if (!Directory.Exists(searchPath))
                                continue;

                            var imagePath = Path.Combine(searchPath, fs6000Image.FileName);
                            if (File.Exists(imagePath))
                            {
                                _logger.LogInformation("Found image for container {ContainerNumber} at: {ImagePath}", containerNumber, imagePath);
                                return imagePath;
                            }
                        }
                    }
                }
                catch (Exception dbEx)
                {
                    _logger.LogWarning(dbEx, "Could not query database for image filename, falling back to file system search");
                }

                // Fallback: Search for files with container number in the name
                foreach (var searchPath in _searchPaths)
                {
                    if (!Directory.Exists(searchPath))
                        continue;

                    foreach (var format in _supportedFormats)
                    {
                        // Try exact match first
                        var exactPath = Path.Combine(searchPath, $"{containerNumber}{format}");
                        if (File.Exists(exactPath))
                        {
                            _logger.LogInformation("Found image for container {ContainerNumber} at: {ImagePath}", containerNumber, exactPath);
                            return exactPath;
                        }

                        // Try pattern match (files containing the container number)
                        try
                        {
                            var matchingFiles = Directory.GetFiles(searchPath, $"*{containerNumber}*{format}", SearchOption.TopDirectoryOnly);
                            if (matchingFiles.Length > 0)
                            {
                                _logger.LogInformation("Found image for container {ContainerNumber} at: {ImagePath}", containerNumber, matchingFiles[0]);
                                return matchingFiles[0];
                            }
                        }
                        catch (Exception searchEx)
                        {
                            _logger.LogWarning(searchEx, "Error searching for images in {SearchPath}", searchPath);
                        }
                    }
                }

                _logger.LogWarning("No image found for container: {ContainerNumber}", containerNumber);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image path for container: {ContainerNumber}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Validates image file quality
        /// </summary>
        public Task<ContainerImageQualityMetrics> ValidateImageQualityAsync(string imagePath)
        {
            try
            {
                _logger.LogInformation("Validating image quality for: {ImagePath}", imagePath);

                var metrics = new ContainerImageQualityMetrics();

                if (!File.Exists(imagePath))
                {
                    metrics.QualityRating = "Poor";
                    metrics.QualityIssues.Add("Image file does not exist");
                    return Task.FromResult(metrics);
                }

                try
                {
                    using var image = Image.FromFile(imagePath);
                    metrics.Width = image.Width;
                    metrics.Height = image.Height;
                    metrics.AspectRatio = (double)image.Width / image.Height;

                    // Validate dimensions
                    if (image.Width < 100 || image.Height < 100)
                    {
                        metrics.QualityIssues.Add("Image dimensions too small");
                    }
                    else
                    {
                        metrics.QualityStrengths.Add("Image dimensions are adequate");
                    }

                    // Validate aspect ratio (containers are typically wider than tall)
                    if (metrics.AspectRatio < 0.5 || metrics.AspectRatio > 4.0)
                    {
                        metrics.QualityIssues.Add("Unusual aspect ratio - may not be a container image");
                    }
                    else
                    {
                        metrics.QualityStrengths.Add("Aspect ratio appears normal for container");
                    }

                    // Validate pixel format
                    var pixelFormat = image.PixelFormat;
                    if (pixelFormat == PixelFormat.Format8bppIndexed || pixelFormat == PixelFormat.Format1bppIndexed)
                    {
                        metrics.QualityIssues.Add("Image appears to be grayscale or monochrome");
                    }
                    else
                    {
                        metrics.QualityStrengths.Add("Image appears to be color");
                    }

                    // Calculate quality rating
                    metrics.QualityRating = CalculateQualityRating(metrics);

                    _logger.LogInformation("Image quality validation completed: {Width}x{Height}, Rating: {Rating}",
                        metrics.Width, metrics.Height, metrics.QualityRating);

                    return Task.FromResult(metrics);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error reading image file: {ImagePath}", imagePath);
                    metrics.QualityRating = "Poor";
                    metrics.QualityIssues.Add("Unable to read image file - file may be corrupted");
                    return Task.FromResult(metrics);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating image quality for: {ImagePath}", imagePath);
                return Task.FromResult(new ContainerImageQualityMetrics
                {
                    QualityRating = "Poor",
                    QualityIssues = new List<string> { "Error validating image quality" }
                });
            }
        }

        /// <summary>
        /// Gets image statistics
        /// </summary>
        public async Task<ImageStatistics> GetImageStatisticsAsync()
        {
            try
            {
                _logger.LogInformation("Getting image statistics");

                var stats = new ImageStatistics();

                foreach (var searchPath in _searchPaths)
                {
                    if (!Directory.Exists(searchPath))
                        continue;

                    var imageFiles = Directory.GetFiles(searchPath, "*.*", SearchOption.TopDirectoryOnly)
                        .Where(file => _supportedFormats.Contains(Path.GetExtension(file).ToLowerInvariant()))
                        .ToList();

                    foreach (var imageFile in imageFiles)
                    {
                        try
                        {
                            var fileInfo = new FileInfo(imageFile);
                            stats.TotalImages++;
                            stats.TotalImageSizeBytes += fileInfo.Length;

                            var extension = Path.GetExtension(imageFile).ToLowerInvariant();
                            stats.ImagesByFormat[extension] = stats.ImagesByFormat.GetValueOrDefault(extension, 0) + 1;

                            // Validate image quality
                            var qualityMetrics = await ValidateImageQualityAsync(imageFile);
                            stats.ImagesByQuality[qualityMetrics.QualityRating] = stats.ImagesByQuality.GetValueOrDefault(qualityMetrics.QualityRating, 0) + 1;

                            if (qualityMetrics.QualityRating == "Poor")
                                stats.InvalidImages++;
                            else
                                stats.ValidImages++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Error processing image file: {ImageFile}", imageFile);
                            stats.InvalidImages++;
                        }
                    }
                }

                stats.AverageFileSizeKB = stats.TotalImages > 0 ? (stats.TotalImageSizeBytes / stats.TotalImages) / 1024.0 : 0;

                _logger.LogInformation("Image statistics: {TotalImages} total images, {ValidImages} valid, {InvalidImages} invalid",
                    stats.TotalImages, stats.ValidImages, stats.InvalidImages);

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image statistics");
                throw;
            }
        }

        /// <summary>
        /// Checks if image exists for container
        /// </summary>
        public async Task<bool> ImageExistsAsync(string containerNumber)
        {
            var imagePath = await GetImagePathAsync(containerNumber);
            return !string.IsNullOrEmpty(imagePath);
        }

        /// <summary>
        /// Gets image file information
        /// </summary>
        public async Task<ImageFileInfo?> GetImageFileInfoAsync(string containerNumber)
        {
            try
            {
                var imagePath = await GetImagePathAsync(containerNumber);
                if (string.IsNullOrEmpty(imagePath))
                    return null;

                var fileInfo = new FileInfo(imagePath);
                var qualityMetrics = await ValidateImageQualityAsync(imagePath);

                return new ImageFileInfo
                {
                    FilePath = imagePath,
                    FileName = Path.GetFileName(imagePath),
                    FileSizeBytes = fileInfo.Length,
                    ImageFormat = Path.GetExtension(imagePath).TrimStart('.').ToUpperInvariant(),
                    CreatedDate = fileInfo.CreationTime,
                    ModifiedDate = fileInfo.LastWriteTime,
                    Width = qualityMetrics.Width,
                    Height = qualityMetrics.Height,
                    AspectRatio = qualityMetrics.AspectRatio,
                    QualityRating = qualityMetrics.QualityRating
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image file info for container: {ContainerNumber}", containerNumber);
                return null;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// Calculates image completeness score
        /// </summary>
        private int CalculateImageCompletenessScore(FileInfo fileInfo, ContainerImageQualityMetrics qualityMetrics)
        {
            var score = 0;

            // File existence and size (40 points)
            if (fileInfo.Length > 0)
            {
                score += 40;
            }

            // Image dimensions (30 points)
            if (qualityMetrics.Width >= 100 && qualityMetrics.Height >= 100)
            {
                score += 30;
            }

            // Quality rating (30 points)
            score += qualityMetrics.QualityRating switch
            {
                "Excellent" => 30,
                "Good" => 25,
                "Fair" => 15,
                "Poor" => 5,
                _ => 0
            };

            return Math.Min(score, 100);
        }

        /// <summary>
        /// Calculates quality rating based on metrics
        /// </summary>
        private string CalculateQualityRating(ContainerImageQualityMetrics metrics)
        {
            var issues = metrics.QualityIssues.Count;
            var strengths = metrics.QualityStrengths.Count;

            if (issues == 0 && strengths >= 3)
                return "Excellent";
            else if (issues <= 1 && strengths >= 2)
                return "Good";
            else if (issues <= 2 && strengths >= 1)
                return "Fair";
            else
                return "Poor";
        }

        #endregion
    }
}
