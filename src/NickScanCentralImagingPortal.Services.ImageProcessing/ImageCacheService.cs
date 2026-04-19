using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageProcessing
{
    public class ImageCacheService : IImageCacheService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ImageCacheService> _logger;

        public ImageCacheService(ApplicationDbContext context, ILogger<ImageCacheService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<ImageCache?> GetCachedImageAsync(string containerNumber, ScannerType scannerType)
        {
            try
            {
                _logger.LogDebug("Looking for cached image for container: {ContainerNumber}, scanner: {ScannerType}", containerNumber, scannerType);

                var cachedImage = await _context.ImageCaches
                    .FirstOrDefaultAsync(ic => ic.ContainerNumber == containerNumber && ic.ScannerType == scannerType.ToString());

                if (cachedImage != null)
                {
                    _logger.LogDebug("Found cached image for container: {ContainerNumber}", containerNumber);
                }
                else
                {
                    _logger.LogDebug("No cached image found for container: {ContainerNumber}", containerNumber);
                }

                return cachedImage;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving cached image for container: {ContainerNumber}", containerNumber);
                return null;
            }
        }

        public async Task CacheImageAsync(ImageCache imageCache)
        {
            try
            {
                _logger.LogDebug("Caching image for container: {ContainerNumber}, scanner: {ScannerType}", imageCache.ContainerNumber, imageCache.ScannerType);

                // Check if image already exists
                var existingImage = await _context.ImageCaches
                    .FirstOrDefaultAsync(ic => ic.ContainerNumber == imageCache.ContainerNumber && ic.ScannerType == imageCache.ScannerType);

                if (existingImage != null)
                {
                    // Update existing image
                    existingImage.ImageData = imageCache.ImageData;
                    existingImage.MimeType = imageCache.MimeType;
                    existingImage.Width = imageCache.Width;
                    existingImage.Height = imageCache.Height;
                    existingImage.FileSizeBytes = imageCache.FileSizeBytes;
                    existingImage.ScanTime = imageCache.ScanTime;
                    existingImage.CachedAt = DateTime.UtcNow;
                    existingImage.ProcessingPipeline = imageCache.ProcessingPipeline;
                    existingImage.Quality = imageCache.Quality;

                    _logger.LogDebug("Updated existing cached image for container: {ContainerNumber}", imageCache.ContainerNumber);
                }
                else
                {
                    // Add new image
                    _context.ImageCaches.Add(imageCache);
                    _logger.LogDebug("Added new cached image for container: {ContainerNumber}", imageCache.ContainerNumber);
                }

                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully cached image for container: {ContainerNumber}", imageCache.ContainerNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error caching image for container: {ContainerNumber}", imageCache.ContainerNumber);
                throw;
            }
        }

        public async Task<bool> IsImageCachedAsync(string containerNumber, ScannerType scannerType)
        {
            try
            {
                return await _context.ImageCaches
                    .AnyAsync(ic => ic.ContainerNumber == containerNumber && ic.ScannerType == scannerType.ToString());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if image is cached for container: {ContainerNumber}", containerNumber);
                return false;
            }
        }

        public async Task SetCachedImageAsync(string containerNumber, ScannerType scannerType, byte[] imageData, string imageFormat)
        {
            try
            {
                var imageCache = new ImageCache
                {
                    ContainerNumber = containerNumber,
                    ScannerType = scannerType.ToString(),
                    ImageData = imageData,
                    MimeType = imageFormat,
                    CachedAt = DateTime.UtcNow
                };

                _context.ImageCaches.Add(imageCache);
                await _context.SaveChangesAsync();
                _logger.LogInformation("Successfully set cached image for container: {ContainerNumber}", containerNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting cached image for container: {ContainerNumber}", containerNumber);
                throw;
            }
        }

        public async Task RemoveCachedImageAsync(string containerNumber, ScannerType scannerType)
        {
            try
            {
                var cachedImage = await _context.ImageCaches
                    .FirstOrDefaultAsync(ic => ic.ContainerNumber == containerNumber && ic.ScannerType == scannerType.ToString());

                if (cachedImage != null)
                {
                    _context.ImageCaches.Remove(cachedImage);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully removed cached image for container: {ContainerNumber}", containerNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing cached image for container: {ContainerNumber}", containerNumber);
                throw;
            }
        }

        public async Task ClearExpiredCacheAsync()
        {
            try
            {
                var expiredDate = DateTime.UtcNow.AddDays(-30); // Remove cache older than 30 days
                var expiredCaches = await _context.ImageCaches
                    .Where(ic => ic.CachedAt < expiredDate)
                    .ToListAsync();

                if (expiredCaches.Any())
                {
                    _context.ImageCaches.RemoveRange(expiredCaches);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Successfully cleared {Count} expired cache entries", expiredCaches.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing expired cache");
                throw;
            }
        }

        public async Task<int> PurgeStaleEntriesAsync(int minSizeBytes, int minWidth, int minHeight)
        {
            try
            {
                var staleEntries = await _context.ImageCaches
                    .Where(ic => ic.FileSizeBytes < minSizeBytes || ic.Width < minWidth || ic.Height < minHeight)
                    .ToListAsync();

                if (staleEntries.Count > 0)
                {
                    _context.ImageCaches.RemoveRange(staleEntries);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation(
                        "🧹 Purged {Count} stale image cache entries (below {MinSize}B / {MinW}x{MinH}px thresholds)",
                        staleEntries.Count, minSizeBytes, minWidth, minHeight);
                }

                return staleEntries.Count;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error purging stale image cache entries");
                return 0;
            }
        }
    }
}
