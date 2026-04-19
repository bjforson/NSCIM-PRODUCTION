using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models.Gateway;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Gateway
{
    public class DashboardStatsService : IDashboardStatsService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<DashboardStatsService> _logger;

        public DashboardStatsService(
            ApplicationDbContext context,
            ILogger<DashboardStatsService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<DashboardStats> GetDashboardStatsAsync(
            bool includeContainers = true,
            bool includeScanners = true,
            bool includeICUMS = true,
            bool includeValidation = true,
            bool includeImages = true,
            bool includeTrends = true)
        {
            var sw = Stopwatch.StartNew();
            var stats = new DashboardStats();

            try
            {
                var tasks = new List<Task>();

                if (includeContainers)
                    tasks.Add(Task.Run(async () => stats.Containers = await GetContainerStatsAsync()));

                if (includeScanners)
                    tasks.Add(Task.Run(async () => stats.Scanners = await GetScannerStatsAsync()));

                if (includeICUMS)
                    tasks.Add(Task.Run(async () => stats.ICUMS = await GetICUMSStatsAsync()));

                if (includeValidation)
                    tasks.Add(Task.Run(async () => stats.Validation = await GetValidationStatsAsync()));

                if (includeImages)
                    tasks.Add(Task.Run(async () => stats.Images = await GetImageProcessingStatsAsync()));

                if (includeTrends)
                    tasks.Add(Task.Run(async () => stats.Trends = await GetTrendDataAsync()));

                await Task.WhenAll(tasks);

                sw.Stop();
                stats.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
                stats.GeneratedAt = DateTime.UtcNow;

                _logger.LogInformation("Dashboard stats generated in {ElapsedMs}ms", sw.ElapsedMilliseconds);
                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating dashboard stats");
                sw.Stop();
                stats.ResponseTimeMs = (int)sw.ElapsedMilliseconds;
                return stats;
            }
        }

        private async Task<ContainerStats> GetContainerStatsAsync()
        {
            var stats = new ContainerStats();
            // ✅ FIX: Use calendar day (12:00 AM to 11:59:59 PM) in local timezone instead of UTC
            var todayStart = DateTime.Today; // Today at 00:00:00 local time (12:00 AM)
            var todayEnd = todayStart.AddDays(1); // Tomorrow at 00:00:00 (12:00 AM next day)
            var weekStart = todayStart.AddDays(-(int)todayStart.DayOfWeek); // Start of week (Sunday)
            var monthStart = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1); // First day of month

            try
            {
                // ✅ MEMORY OPTIMIZATION: Filter to last 90 days to reduce buffer pool usage
                // Only load recent data into SQL Server memory instead of entire table
                var ninetyDaysAgo = DateTime.UtcNow.AddDays(-90);

                // Get ASE scans (filtered to last 90 days)
                var aseScans = await _context.AseScans
                    .AsNoTracking()
                    .Where(s => s.ScanTime >= ninetyDaysAgo)
                    .Select(s => new { s.ContainerNumber, s.ScanTime })
                    .ToListAsync();

                // Get FS6000 scans (filtered to last 90 days)
                var fs6000Scans = await _context.FS6000Scans
                    .AsNoTracking()
                    .Where(s => s.ScanTime >= ninetyDaysAgo)
                    .Select(s => new { s.ContainerNumber, s.ScanTime })
                    .ToListAsync();

                // Combine all scans
                var allScans = aseScans.Select(s => new { Container = s.ContainerNumber ?? "", Date = s.ScanTime })
                    .Concat(fs6000Scans.Select(s => new { Container = s.ContainerNumber, Date = s.ScanTime }))
                    .ToList();

                stats.TotalScanned = allScans.Select(s => s.Container).Distinct().Count();
                stats.ScannedToday = allScans.Count(s => s.Date >= todayStart && s.Date < todayEnd);
                stats.ScannedThisWeek = allScans.Count(s => s.Date >= weekStart);
                stats.ScannedThisMonth = allScans.Count(s => s.Date >= monthStart);

                // Get image counts
                var imageCache = await _context.ImageCaches.CountAsync();
                stats.WithImages = imageCache;
                stats.WithoutImages = stats.TotalScanned - imageCache;

                // Parse container sizes from container numbers (e.g., "SEKU1891357(20FT)")
                var containerSizes = allScans
                    .Select(s => ExtractContainerSize(s.Container))
                    .Where(size => !string.IsNullOrEmpty(size))
                    .GroupBy(size => size)
                    .ToDictionary(g => g.Key, g => g.Count());

                stats.BySize = containerSizes;
                stats.ByType = new Dictionary<string, int>
                {
                    { "Import", allScans.Count() / 2 }, // Placeholder - would need actual data
                    { "Export", allScans.Count() / 2 }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting container stats");
            }

            return stats;
        }

        private async Task<ScannerStats> GetScannerStatsAsync()
        {
            var stats = new ScannerStats();

            try
            {
                // ✅ MEMORY OPTIMIZATION: Filter to last 30 days to reduce buffer pool usage
                // Only load recent data into SQL Server memory instead of entire table
                var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
                var aseScansQuery = _context.AseScans
                    .Where(s => s.ScanTime >= thirtyDaysAgo)
                    .AsNoTracking();

                // ✅ Use database aggregation instead of loading all data
                stats.ASEScans = await aseScansQuery.CountAsync();
                stats.LastASEScan = await aseScansQuery
                    .OrderByDescending(s => s.ScanTime)
                    .Select(s => s.ScanTime)
                    .FirstOrDefaultAsync();

                // ASE doesn't have Location or OperatorId in current schema
                stats.ByLocation = new Dictionary<string, int> { { "ASE Scans", stats.ASEScans } };
                stats.ByOperator = new Dictionary<string, int> { { "ASE Scanner", stats.ASEScans } };

                // ✅ MEMORY OPTIMIZATION: Filter to last 30 days to reduce buffer pool usage
                var fs6000ScansQuery = _context.FS6000Scans
                    .Where(s => s.ScanTime >= thirtyDaysAgo)
                    .AsNoTracking();

                // ✅ Use database aggregation instead of loading all data
                stats.FS6000Scans = await fs6000ScansQuery.CountAsync();
                stats.LastFS6000Scan = await fs6000ScansQuery
                    .OrderByDescending(s => s.ScanTime)
                    .Select(s => s.ScanTime)
                    .FirstOrDefaultAsync();

                stats.TotalScans = stats.ASEScans + stats.FS6000Scans;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scanner stats");
            }

            return stats;
        }

        private async Task<ICUMSStats> GetICUMSStatsAsync()
        {
            var stats = new ICUMSStats();

            try
            {
                // ICUMS stats would go here - requires IcumDownloadsDbContext
                // Placeholder for now
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ICUMS stats");
            }

            return stats;
        }

        private async Task<ValidationStats> GetValidationStatsAsync()
        {
            var stats = new ValidationStats();

            try
            {
                // Placeholder - would integrate with actual validation service
                stats.TotalValidated = 0;
                stats.PassedValidation = 0;
                stats.FailedValidation = 0;
                stats.PendingValidation = 0;
                stats.AverageCompletenessScore = 0.0;

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting validation stats");
            }

            return stats;
        }

        private async Task<ImageProcessingStats> GetImageProcessingStatsAsync()
        {
            var stats = new ImageProcessingStats();

            try
            {
                var cachedImages = await _context.ImageCaches.ToListAsync();

                stats.CachedImages = cachedImages.Count;
                stats.TotalImagesProcessed = cachedImages.Count;
                stats.CacheHitRate = 85.0; // Placeholder

                stats.ByFormat = new Dictionary<string, int>
                {
                    { "JPEG", cachedImages.Count }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image processing stats");
            }

            return stats;
        }

        private async Task<GatewayTrendData> GetTrendDataAsync()
        {
            var trends = new GatewayTrendData();
            var now = DateTime.UtcNow;

            try
            {
                // Simplified trend data - would need proper implementation with all context types
                // Placeholder for now
                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting trend data");
            }

            return trends;
        }

        private string? ExtractContainerSize(string containerNumber)
        {
            if (string.IsNullOrEmpty(containerNumber))
                return null;

            // Check for size in parentheses: "SEKU1891357(20FT)"
            var openParen = containerNumber.IndexOf('(');
            var closeParen = containerNumber.IndexOf(')');

            if (openParen > 0 && closeParen > openParen)
            {
                return containerNumber.Substring(openParen + 1, closeParen - openParen - 1);
            }

            // Check for "20FT" or "40FT" anywhere in the string
            if (containerNumber.Contains("20FT", StringComparison.OrdinalIgnoreCase))
                return "20FT";
            if (containerNumber.Contains("40FT", StringComparison.OrdinalIgnoreCase))
                return "40FT";

            return null;
        }
    }
}

