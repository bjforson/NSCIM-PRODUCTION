using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.FS6000
{
    public class FS6000ImageCompletenessService : IFS6000ImageCompletenessService
    {
        private readonly ApplicationDbContext _dbContext;
        private readonly ILogger<FS6000ImageCompletenessService> _logger;

        public FS6000ImageCompletenessService(
            ApplicationDbContext dbContext,
            ILogger<FS6000ImageCompletenessService> logger)
        {
            _dbContext = dbContext;
            _logger = logger;
        }

        public async Task<bool> ValidateScanHasImageAsync(Guid scanId)
        {
            try
            {
                var imageCount = await _dbContext.FS6000Images
                    .Where(i => i.ScanId == scanId)
                    .CountAsync();

                return imageCount > 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-IMAGE-VALIDATION] Error validating image for scan {ScanId}", scanId);
                return false;
            }
        }

        public async Task<List<FS6000Scan>> GetScansWithoutImagesAsync(int limit = 100)
        {
            try
            {
                var scansWithoutImages = await _dbContext.FS6000Scans
                    .Where(s => !s.HasImage || s.ImageCount == 0)
                    .OrderByDescending(s => s.ScanTime)
                    .Take(limit)
                    .ToListAsync();

                _logger.LogInformation("[FS6000-IMAGE-VALIDATION] Found {Count} scans without images", scansWithoutImages.Count);
                return scansWithoutImages;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-IMAGE-VALIDATION] Error getting scans without images");
                return new List<FS6000Scan>();
            }
        }

        public async Task<FS6000ImageCompletenessStats> GetImageCompletenessStatsAsync()
        {
            try
            {
                var totalScans = await _dbContext.FS6000Scans.CountAsync();
                var scansWithImages = await _dbContext.FS6000Scans.Where(s => s.HasImage).CountAsync();
                var scansWithoutImages = totalScans - scansWithImages;
                var totalImages = await _dbContext.FS6000Images.CountAsync();

                var oldestWithoutImage = await _dbContext.FS6000Scans
                    .Where(s => !s.HasImage)
                    .OrderBy(s => s.ScanTime)
                    .Select(s => s.ScanTime)
                    .FirstOrDefaultAsync();

                var newestWithoutImage = await _dbContext.FS6000Scans
                    .Where(s => !s.HasImage)
                    .OrderByDescending(s => s.ScanTime)
                    .Select(s => s.ScanTime)
                    .FirstOrDefaultAsync();

                // Get image count distribution
                var imageCountDistribution = await _dbContext.FS6000Scans
                    .GroupBy(s => s.ImageCount)
                    .Select(g => new { ImageCount = g.Key, Count = g.Count() })
                    .ToDictionaryAsync(x => $"{x.ImageCount} images", x => x.Count);

                var stats = new FS6000ImageCompletenessStats
                {
                    TotalScans = totalScans,
                    ScansWithImages = scansWithImages,
                    ScansWithoutImages = scansWithoutImages,
                    TotalImages = totalImages,
                    CompletenessPercentage = totalScans > 0 ? (double)scansWithImages / totalScans * 100 : 0,
                    OldestScanWithoutImage = oldestWithoutImage == default ? null : oldestWithoutImage,
                    NewestScanWithoutImage = newestWithoutImage == default ? null : newestWithoutImage,
                    ImageCountDistribution = imageCountDistribution
                };

                _logger.LogInformation("[FS6000-IMAGE-VALIDATION] Stats: {TotalScans} total, {WithImages} with images ({Percentage:F2}%), {WithoutImages} without images",
                    stats.TotalScans, stats.ScansWithImages, stats.CompletenessPercentage, stats.ScansWithoutImages);

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-IMAGE-VALIDATION] Error getting image completeness stats");
                return new FS6000ImageCompletenessStats();
            }
        }

        public async Task UpdateScanImageCompletenessAsync(Guid scanId)
        {
            try
            {
                var scan = await _dbContext.FS6000Scans
                    .Include(s => s.Images)
                    .FirstOrDefaultAsync(s => s.Id == scanId);

                if (scan == null)
                {
                    _logger.LogWarning("[FS6000-IMAGE-VALIDATION] Scan {ScanId} not found", scanId);
                    return;
                }

                var imageCount = scan.Images.Count;
                var hasImage = imageCount > 0;

                scan.HasImage = hasImage;
                scan.ImageCount = imageCount;

                if (hasImage && scan.ImageIngestedAt == null)
                {
                    scan.ImageIngestedAt = DateTime.UtcNow;
                }

                if (!hasImage)
                {
                    scan.ImageValidationError = "No images found for this scan";
                }
                else
                {
                    scan.ImageValidationError = null;
                }

                await _dbContext.SaveChangesAsync();

                _logger.LogDebug("[FS6000-IMAGE-VALIDATION] Updated scan {ContainerNumber}: HasImage={HasImage}, ImageCount={ImageCount}",
                    scan.ContainerNumber, hasImage, imageCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-IMAGE-VALIDATION] Error updating image completeness for scan {ScanId}", scanId);
            }
        }

        public async Task<int> BackfillMissingImagesAsync(DateTime? fromDate = null, DateTime? toDate = null)
        {
            try
            {
                _logger.LogInformation("[FS6000-IMAGE-BACKFILL] Starting backfill for missing images from {FromDate} to {ToDate}",
                    fromDate?.ToString("yyyy-MM-dd") ?? "beginning", toDate?.ToString("yyyy-MM-dd") ?? "now");

                // Get scans without images
                var query = _dbContext.FS6000Scans
                    .Where(s => !s.HasImage || s.ImageCount == 0);

                if (fromDate.HasValue)
                    query = query.Where(s => s.ScanTime >= fromDate.Value);

                if (toDate.HasValue)
                    query = query.Where(s => s.ScanTime <= toDate.Value);

                var scansWithoutImages = await query.ToListAsync();

                _logger.LogInformation("[FS6000-IMAGE-BACKFILL] Found {Count} scans without images to backfill", scansWithoutImages.Count);

                // Note: Actual backfill would require access to the original image files
                // For now, we'll just validate and update the flags
                int backfilledCount = 0;

                foreach (var scan in scansWithoutImages)
                {
                    // Check if images exist in database but flag is not set
                    var imageCount = await _dbContext.FS6000Images
                        .Where(i => i.ScanId == scan.Id)
                        .CountAsync();

                    if (imageCount > 0)
                    {
                        // Images exist, just update the flags
                        scan.HasImage = true;
                        scan.ImageCount = imageCount;
                        scan.ImageIngestedAt = DateTime.UtcNow;
                        scan.ImageValidationError = null;
                        backfilledCount++;
                    }
                    else
                    {
                        // No images found - mark as missing
                        scan.HasImage = false;
                        scan.ImageCount = 0;
                        scan.ImageValidationError = "Image file not found during backfill";
                    }
                }

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[FS6000-IMAGE-BACKFILL] Backfill completed: {BackfilledCount} scans updated with existing images", backfilledCount);

                return backfilledCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-IMAGE-BACKFILL] Error during backfill");
                return 0;
            }
        }

        public async Task<int> ValidateAllScansImageCompletenessAsync()
        {
            try
            {
                _logger.LogInformation("[FS6000-IMAGE-VALIDATION] Starting validation of all scans");

                var allScans = await _dbContext.FS6000Scans.ToListAsync();
                int updatedCount = 0;

                foreach (var scan in allScans)
                {
                    var imageCount = await _dbContext.FS6000Images
                        .Where(i => i.ScanId == scan.Id)
                        .CountAsync();

                    var hasImage = imageCount > 0;

                    // Only update if status changed
                    if (scan.HasImage != hasImage || scan.ImageCount != imageCount)
                    {
                        scan.HasImage = hasImage;
                        scan.ImageCount = imageCount;

                        if (hasImage && scan.ImageIngestedAt == null)
                        {
                            scan.ImageIngestedAt = DateTime.UtcNow;
                        }

                        scan.ImageValidationError = hasImage ? null : "No images found";
                        updatedCount++;
                    }
                }

                await _dbContext.SaveChangesAsync();

                _logger.LogInformation("[FS6000-IMAGE-VALIDATION] Validation completed: {UpdatedCount} scans updated", updatedCount);

                return updatedCount;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[FS6000-IMAGE-VALIDATION] Error validating all scans");
                return 0;
            }
        }
    }
}
