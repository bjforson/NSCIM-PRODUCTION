using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services
{
    public interface IImageProcessingOrchestrator
    {
        Task<ScannerProcessingResult> ProcessContainerAsync(string containerId, string scannerType);
        Task<Container> CreateContainerFromScannerDataAsync(ScannerData scannerData);
        Task<ScannerProcessingResult> ProcessImageAsync(string imageId, string scannerType);
        Task<Dictionary<string, bool>> GetSystemHealthAsync();
        IEnumerable<string> GetAvailableScannerTypes();
    }

    public class ImageProcessingOrchestrator : IImageProcessingOrchestrator
    {
        private readonly IScannerServiceFactory _scannerServiceFactory;
        private readonly IContainerRepository _containerRepository;
        private readonly ILogger<ImageProcessingOrchestrator> _logger;

        public ImageProcessingOrchestrator(
            IScannerServiceFactory scannerServiceFactory,
            IContainerRepository containerRepository,
            ILogger<ImageProcessingOrchestrator> logger)
        {
            _scannerServiceFactory = scannerServiceFactory;
            _containerRepository = containerRepository;
            _logger = logger;
        }

        public async Task<ScannerProcessingResult> ProcessContainerAsync(string containerId, string scannerType)
        {
            _logger.LogInformation("Processing container {ContainerId} with scanner type {ScannerType}", containerId, scannerType);

            try
            {
                // Get the appropriate scanner service
                var scannerService = _scannerServiceFactory.GetScannerService(scannerType);

                // Get scanner data
                var scannerData = await scannerService.GetScannerDataAsync(containerId);

                // Validate the data
                var isValid = await scannerService.ValidateDataAsync(scannerData);
                if (!isValid)
                {
                    _logger.LogWarning("Invalid scanner data for container {ContainerId}", containerId);
                    return new ScannerProcessingResult
                    {
                        ResultType = "Validation",
                        Status = "Failed",
                        ErrorMessage = "Invalid scanner data",
                        ProcessedAt = DateTime.UtcNow
                    };
                }

                // Create or update container in database
                var container = await CreateContainerFromScannerDataAsync(scannerData);

                // Process all images for this container
                var results = new List<ScannerProcessingResult>();
                foreach (var image in container.Images)
                {
                    var imageData = new ImageData
                    {
                        ImageId = image.Id.ToString(),
                        ImagePath = image.ImagePath,
                        ImageType = image.ImageType,
                        FileSizeBytes = image.FileSizeBytes,
                        OriginalFileName = image.OriginalFileName
                    };

                    var result = await scannerService.ProcessImageAsync(imageData);
                    results.Add(result);

                    // Update image processing status
                    image.ProcessingStatus = result.Status;
                    image.ProcessedAt = result.ProcessedAt;
                }

                // Update container processing status
                container.ProcessingStatus = results.All(r => r.Status == "Completed") ? "Completed" : "Failed";
                container.UpdatedAt = DateTime.UtcNow;

                await _containerRepository.UpdateAsync(container);

                _logger.LogInformation("Successfully processed container {ContainerId}", containerId);

                return new ScannerProcessingResult
                {
                    ResultType = "Container_Processing",
                    Status = container.ProcessingStatus,
                    ResultData = $"{{\"processedImages\":{results.Count},\"containerId\":\"{containerId}\"}}",
                    ProcessedAt = DateTime.UtcNow
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process container {ContainerId}", containerId);
                return new ScannerProcessingResult
                {
                    ResultType = "Container_Processing",
                    Status = "Failed",
                    ErrorMessage = ex.Message,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        public async Task<Container> CreateContainerFromScannerDataAsync(ScannerData scannerData)
        {
            _logger.LogInformation("Creating container from scanner data for {ContainerId}", scannerData.ContainerId);

            // Check if container already exists
            var existingContainer = await _containerRepository.GetByContainerIdAsync(scannerData.ContainerId);
            if (existingContainer != null)
            {
                _logger.LogInformation("Container {ContainerId} already exists, updating", scannerData.ContainerId);
                return existingContainer;
            }

            // Create new container
            var container = new Container
            {
                ContainerId = scannerData.ContainerId,
                ScannerType = scannerData.ScannerType,
                ScannerId = scannerData.ScannerId,
                ScanDateTime = scannerData.ScanDateTime,
                CreatedAt = DateTime.UtcNow,
                ProcessingStatus = "Pending"
            };

            // Add image if path is provided
            if (!string.IsNullOrEmpty(scannerData.ImagePath))
            {
                container.Images.Add(new ContainerImage
                {
                    ImagePath = scannerData.ImagePath,
                    ImageType = "JPEG", // Default type
                    FileSizeBytes = 0, // Will be updated when actual file is processed
                    OriginalFileName = Path.GetFileName(scannerData.ImagePath),
                    CreatedAt = DateTime.UtcNow,
                    ProcessingStatus = "Pending"
                });
            }

            var createdContainer = await _containerRepository.AddAsync(container);
            _logger.LogInformation("Created container {ContainerId} with ID {Id}", scannerData.ContainerId, createdContainer.Id);

            return createdContainer;
        }

        public async Task<ScannerProcessingResult> ProcessImageAsync(string imageId, string scannerType)
        {
            _logger.LogInformation("Processing image {ImageId} with scanner type {ScannerType}", imageId, scannerType);

            try
            {
                var scannerService = _scannerServiceFactory.GetScannerService(scannerType);
                var imageData = await scannerService.GetImageDataAsync(imageId);
                var result = await scannerService.ProcessImageAsync(imageData);

                _logger.LogInformation("Successfully processed image {ImageId}", imageId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process image {ImageId}", imageId);
                return new ScannerProcessingResult
                {
                    ResultType = "Image_Processing",
                    Status = "Failed",
                    ErrorMessage = ex.Message,
                    ProcessedAt = DateTime.UtcNow
                };
            }
        }

        public async Task<Dictionary<string, bool>> GetSystemHealthAsync()
        {
            _logger.LogInformation("Getting system health status");

            var healthStatus = await _scannerServiceFactory.GetHealthStatusAsync();

            // Add database health check
            try
            {
                await _containerRepository.GetAllAsync();
                healthStatus["Database"] = true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Database health check failed");
                healthStatus["Database"] = false;
            }

            return healthStatus;
        }

        public IEnumerable<string> GetAvailableScannerTypes()
        {
            return _scannerServiceFactory.GetAvailableScannerTypes();
        }
    }
}
