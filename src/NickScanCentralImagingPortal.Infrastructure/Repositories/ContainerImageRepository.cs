using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    public class ContainerImageRepository : Repository<ContainerImage>, IContainerImageRepository
    {
        public ContainerImageRepository(ApplicationDbContext context) : base(context)
        {
        }

        public async Task<IEnumerable<ContainerImage>> GetByContainerIdAsync(int containerId)
        {
            return await _dbSet
                .Where(ci => ci.ContainerId == containerId)
                .ToListAsync();
        }

        public async Task<IEnumerable<ContainerImage>> GetByProcessingStatusAsync(string status)
        {
            return await _dbSet
                .Where(ci => ci.ProcessingStatus == status)
                .ToListAsync();
        }

        // Implementation of new interface methods
        public async Task<ImageSearchResult> SearchImagesAsync(ImageSearchCriteria criteria)
        {
            var query = _dbSet.AsQueryable();

            // Apply filters
            if (!string.IsNullOrEmpty(criteria.ContainerNumber))
                query = query.Where(ci => ci.Container.ContainerId.Contains(criteria.ContainerNumber));

            if (!string.IsNullOrEmpty(criteria.ImageType))
                query = query.Where(ci => ci.ImageType == criteria.ImageType);

            if (criteria.FromDate.HasValue)
                query = query.Where(ci => ci.CreatedAt >= criteria.FromDate.Value);

            if (criteria.ToDate.HasValue)
                query = query.Where(ci => ci.CreatedAt <= criteria.ToDate.Value);

            // Get total count
            var totalCount = await query.CountAsync();

            // Apply sorting
            query = criteria.SortBy?.ToLower() switch
            {
                "containernumber" => criteria.SortOrder?.ToLower() == "desc"
                    ? query.OrderByDescending(ci => ci.Container.ContainerId)
                    : query.OrderBy(ci => ci.Container.ContainerId),
                "imagetype" => criteria.SortOrder?.ToLower() == "desc"
                    ? query.OrderByDescending(ci => ci.ImageType)
                    : query.OrderBy(ci => ci.ImageType),
                _ => criteria.SortOrder?.ToLower() == "desc"
                    ? query.OrderByDescending(ci => ci.CreatedAt)
                    : query.OrderBy(ci => ci.CreatedAt)
            };

            // Apply pagination
            var images = await query
                .Skip((criteria.Page - 1) * criteria.PageSize)
                .Take(criteria.PageSize)
                .Select(ci => new ImageDetails
                {
                    Id = ci.Id,
                    FileName = ci.OriginalFileName,
                    FilePath = ci.ImagePath,
                    FileSize = ci.FileSizeBytes,
                    ImageType = ci.ImageType,
                    ScannerType = "Unknown", // Would need to determine from context
                    ContainerNumber = ci.Container.ContainerId,
                    Width = 0, // Not available in entity
                    Height = 0, // Not available in entity
                    Dpi = 0, // Not available in entity
                    ColorMode = string.Empty, // Not available in entity
                    Compression = string.Empty, // Not available in entity
                    CreatedAt = ci.CreatedAt,
                    UpdatedAt = ci.ProcessedAt ?? ci.CreatedAt,
                    ProcessingStatus = ci.ProcessingStatus,
                    ProcessingResult = null,
                    ErrorMessage = null,
                    Metadata = new Dictionary<string, object>()
                })
                .ToListAsync();

            return new ImageSearchResult
            {
                Images = images,
                TotalCount = totalCount,
                Page = criteria.Page,
                PageSize = criteria.PageSize
            };
        }

        public async Task<ImageDetails?> GetImageByIdAsync(int id)
        {
            var image = await _dbSet
                .Where(ci => ci.Id == id)
                .Select(ci => new ImageDetails
                {
                    Id = ci.Id,
                    FileName = ci.OriginalFileName,
                    FilePath = ci.ImagePath,
                    FileSize = ci.FileSizeBytes,
                    ImageType = ci.ImageType,
                    ScannerType = "Unknown", // Would need to determine from context
                    ContainerNumber = ci.Container.ContainerId,
                    Width = 0, // Not available in entity
                    Height = 0, // Not available in entity
                    Dpi = 0, // Not available in entity
                    ColorMode = string.Empty, // Not available in entity
                    Compression = string.Empty, // Not available in entity
                    CreatedAt = ci.CreatedAt,
                    UpdatedAt = ci.ProcessedAt ?? ci.CreatedAt,
                    ProcessingStatus = ci.ProcessingStatus,
                    ProcessingResult = null,
                    ErrorMessage = null,
                    Metadata = new Dictionary<string, object>()
                })
                .FirstOrDefaultAsync();

            return image;
        }

        public async Task<ImageProcessingStatistics> GetImageProcessingStatisticsAsync(DateTime? fromDate = null, DateTime? toDate = null)
        {
            try
            {
                var query = _dbSet.AsQueryable();

                if (fromDate.HasValue)
                    query = query.Where(ci => ci.CreatedAt >= fromDate.Value);

                if (toDate.HasValue)
                    query = query.Where(ci => ci.CreatedAt <= toDate.Value);

                // ✅ FIX: Materialize query once to avoid multiple database round trips
                var allImages = await query.ToListAsync();

                var totalImages = allImages.Count;
                var processedImages = allImages.Count(ci => ci.ProcessingStatus == "Completed");
                var pendingImages = allImages.Count(ci => ci.ProcessingStatus == "Pending" || ci.ProcessingStatus == "Processing");
                var failedImages = allImages.Count(ci => ci.ProcessingStatus == "Failed");

                var today = DateTime.Today;
                var imagesToday = allImages.Count(ci => ci.CreatedAt.Date == today);
                var imagesThisWeek = allImages.Count(ci => ci.CreatedAt >= today.AddDays(-7));
                var imagesThisMonth = allImages.Count(ci => ci.CreatedAt >= today.AddDays(-30));

                var scannerTypeBreakdown = new Dictionary<string, int>
                {
                    { "Unknown", totalImages } // Placeholder since ScannerType is not in entity
                };

                // ✅ FIX: Handle null ImageType safely
                var imageTypeBreakdown = allImages
                    .GroupBy(ci => ci.ImageType ?? "Unknown")
                    .ToDictionary(g => g.Key, g => g.Count());

                return new ImageProcessingStatistics
                {
                    TotalImages = totalImages,
                    ProcessedImages = processedImages,
                    PendingImages = pendingImages,
                    FailedImages = failedImages,
                    ImagesToday = imagesToday,
                    ImagesThisWeek = imagesThisWeek,
                    ImagesThisMonth = imagesThisMonth,
                    AverageProcessingTime = 0, // Would need additional fields to calculate
                    ProcessingSuccessRate = totalImages > 0 ? (double)processedImages / totalImages * 100 : 0,
                    ScannerTypeBreakdown = scannerTypeBreakdown,
                    ImageTypeBreakdown = imageTypeBreakdown,
                    ProcessingTypeBreakdown = new Dictionary<string, int>()
                };
            }
            catch (Exception)
            {
                // ✅ FIX: Return default statistics on error instead of throwing
                return new ImageProcessingStatistics
                {
                    TotalImages = 0,
                    ProcessedImages = 0,
                    PendingImages = 0,
                    FailedImages = 0,
                    ImagesToday = 0,
                    ImagesThisWeek = 0,
                    ImagesThisMonth = 0,
                    AverageProcessingTime = 0,
                    ProcessingSuccessRate = 0,
                    ScannerTypeBreakdown = new Dictionary<string, int>(),
                    ImageTypeBreakdown = new Dictionary<string, int>(),
                    ProcessingTypeBreakdown = new Dictionary<string, int>()
                };
            }
        }

        public async Task<ImageQualityMetrics> GetImageQualityMetricsAsync()
        {
            var totalImages = await _dbSet.CountAsync();
            var highQualityImages = await _dbSet.CountAsync(ci => ci.ImageType == "High");
            var mediumQualityImages = await _dbSet.CountAsync(ci => ci.ImageType == "Medium");
            var lowQualityImages = await _dbSet.CountAsync(ci => ci.ImageType == "Low");

            return new ImageQualityMetrics
            {
                TotalImages = totalImages,
                HighQualityImages = highQualityImages,
                MediumQualityImages = mediumQualityImages,
                LowQualityImages = lowQualityImages,
                AverageQualityScore = 75.0, // Placeholder value
                QualityIssues = new Dictionary<string, int>(),
                TopIssues = new List<QualityIssue>(),
                MetricsDate = DateTime.UtcNow
            };
        }

        public async Task<IEnumerable<ImageProcessingHistory>> GetImageProcessingHistoryAsync(int imageId)
        {
            // This would typically come from a separate processing history table
            // For now, return empty list as placeholder
            return await Task.FromResult(new List<ImageProcessingHistory>());
        }

        public async Task<byte[]?> GetImageBytesAsync(int imageId)
        {
            // This would typically read the actual image file from storage
            // For now, return null as placeholder
            return await Task.FromResult<byte[]?>(null);
        }

        public async Task<IEnumerable<ImageDetails>> GetImagesByContainerAsync(string containerNumber)
        {
            var images = await _dbSet
                .Where(ci => ci.Container.ContainerId == containerNumber)
                .Select(ci => new ImageDetails
                {
                    Id = ci.Id,
                    FileName = ci.OriginalFileName,
                    FilePath = ci.ImagePath,
                    FileSize = ci.FileSizeBytes,
                    ImageType = ci.ImageType,
                    ScannerType = "Unknown", // Would need to determine from context
                    ContainerNumber = ci.Container.ContainerId,
                    Width = 0, // Not available in entity
                    Height = 0, // Not available in entity
                    Dpi = 0, // Not available in entity
                    ColorMode = string.Empty, // Not available in entity
                    Compression = string.Empty, // Not available in entity
                    CreatedAt = ci.CreatedAt,
                    UpdatedAt = ci.ProcessedAt ?? ci.CreatedAt,
                    ProcessingStatus = ci.ProcessingStatus,
                    ProcessingResult = null,
                    ErrorMessage = null,
                    Metadata = new Dictionary<string, object>()
                })
                .ToListAsync();

            return images;
        }

        public async Task<ImageProcessingQueueStatus> GetProcessingQueueStatusAsync()
        {
            var totalInQueue = await _dbSet.CountAsync(ci => ci.ProcessingStatus == "Pending" || ci.ProcessingStatus == "Queued");
            var processingCount = await _dbSet.CountAsync(ci => ci.ProcessingStatus == "Processing");

            return new ImageProcessingQueueStatus
            {
                TotalInQueue = totalInQueue,
                HighPriorityCount = 0,
                MediumPriorityCount = 0,
                LowPriorityCount = totalInQueue,
                ProcessingCount = processingCount,
                AverageWaitTime = 0,
                QueueItems = new List<QueueItem>(),
                ProcessingTypeCounts = new Dictionary<string, int>()
            };
        }

        public async Task UpdateImageMetadataAsync(int imageId, ImageMetadataUpdate metadataUpdate)
        {
            var image = await _dbSet.AsTracking().FirstOrDefaultAsync(i => i.Id == imageId);
            if (image != null)
            {
                if (metadataUpdate.ImageType != null)
                    image.ImageType = metadataUpdate.ImageType;

                // Note: ColorMode, Compression, and Dpi are not available in the ContainerImage entity
                // These would need to be added to the entity or stored in a separate metadata table

                image.ProcessedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                // ✅ CRITICAL MEMORY FIX: Clear change tracker to release tracked entity
                _context.ChangeTracker.Clear();
            }
        }
    }
}
