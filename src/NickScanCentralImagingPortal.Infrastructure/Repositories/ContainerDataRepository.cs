using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Repositories
{
    /// <summary>
    /// Shared repository for container data queries with caching support
    /// Eliminates query duplication across ContainerCompletenessService, ContainerValidationService, and ContainerDataMapperService
    /// Registered as Singleton - uses IServiceProvider to create scopes for DbContext access
    /// </summary>
    public class ContainerDataRepository : IContainerDataRepository
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly IMemoryCache _cache;
        private readonly ILogger<ContainerDataRepository> _logger;

        // Cache keys
        private const string CACHE_KEY_ALL_SCANNERS = "ContainerData:AllScanners";
        private const string CACHE_KEY_ICUMS_CONTAINERS = "ContainerData:ICUMSContainers";
        private const string CACHE_KEY_SCANNER_PREFIX = "ContainerData:Scanner:";

        // Cache durations
        private static readonly TimeSpan ScannerCacheDuration = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan ICUMSCacheDuration = TimeSpan.FromMinutes(1);

        public ContainerDataRepository(
            IServiceProvider serviceProvider,
            IMemoryCache cache,
            ILogger<ContainerDataRepository> logger)
        {
            _serviceProvider = serviceProvider;
            _cache = cache;
            _logger = logger;
        }

        public async Task<List<ScannerContainerData>> GetAllScannerContainersAsync(bool useCache = true)
        {
            if (!useCache)
            {
                return await QueryAllScannerContainersAsync();
            }

            return await _cache.GetOrCreateAsync(CACHE_KEY_ALL_SCANNERS, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = ScannerCacheDuration;
                entry.Size = 1;

                _logger.LogDebug("Cache miss for all scanner containers - loading from database");
                var data = await QueryAllScannerContainersAsync();
                _logger.LogInformation("Cached {Count} scanner containers for {Duration} minutes",
                    data.Count, ScannerCacheDuration.TotalMinutes);

                return data;
            }) ?? new List<ScannerContainerData>();
        }


        public async Task<HashSet<string>> GetContainersWithICUMSDataAsync(bool useCache = true)
        {
            if (!useCache)
            {
                return await QueryContainersWithICUMSDataAsync();
            }

            return await _cache.GetOrCreateAsync(CACHE_KEY_ICUMS_CONTAINERS, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = ICUMSCacheDuration;
                entry.Size = 1;

                _logger.LogDebug("Cache miss for ICUMS containers - loading from database");
                var data = await QueryContainersWithICUMSDataAsync();
                _logger.LogInformation("Cached {Count} ICUMS containers for {Duration} minute(s)",
                    data.Count, ICUMSCacheDuration.TotalMinutes);

                return data;
            }) ?? new HashSet<string>();
        }

        public async Task<List<string>> FindMissingICUMSContainersAsync(List<string> containerNumbers)
        {
            if (!containerNumbers.Any())
                return new List<string>();

            try
            {
                // Get containers with ICUMS data (uses cache)
                var containersWithICUMS = await GetContainersWithICUMSDataAsync(useCache: true);

                // Find containers that don't have ICUMS data
                var missingContainers = containerNumbers
                    .Where(cn => !containersWithICUMS.Contains(cn))
                    .ToList();

                _logger.LogDebug("Found {MissingCount} of {TotalCount} containers without ICUMS data",
                    missingContainers.Count, containerNumbers.Count);

                return missingContainers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error finding missing ICUMS containers");
                throw;
            }
        }

        public async Task<bool> CanConnectToDatabasesAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var icumDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

                var appDbTask = dbContext.Database.CanConnectAsync();
                var icumDbTask = icumDbContext.Database.CanConnectAsync();

                await Task.WhenAll(appDbTask, icumDbTask);

                var appDbConnected = await appDbTask;
                var icumDbConnected = await icumDbTask;

                var canConnect = appDbConnected && icumDbConnected;

                if (!canConnect)
                {
                    _logger.LogWarning("Database connectivity check failed - AppDB: {AppDb}, ICUMSDB: {IcumDb}",
                        appDbConnected, icumDbConnected);
                }

                return canConnect;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking database connectivity");
                return false;
            }
        }

        public async Task<List<ScannerContainerData>> GetScannerContainersByScannerTypeAsync(string scannerType, bool useCache = true)
        {
            var cacheKey = $"{CACHE_KEY_SCANNER_PREFIX}{scannerType}";

            if (!useCache)
            {
                return await QueryScannerContainersByScannerTypeAsync(scannerType);
            }

            return await _cache.GetOrCreateAsync(cacheKey, async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = ScannerCacheDuration;
                entry.Size = 1;

                _logger.LogDebug("Cache miss for {ScannerType} containers - loading from database", scannerType);
                return await QueryScannerContainersByScannerTypeAsync(scannerType);
            }) ?? new List<ScannerContainerData>();
        }

        public void ClearCache()
        {
            _cache.Remove(CACHE_KEY_ALL_SCANNERS);
            _cache.Remove(CACHE_KEY_ICUMS_CONTAINERS);

            // Clear scanner-specific caches
            _cache.Remove($"{CACHE_KEY_SCANNER_PREFIX}FS6000");
            _cache.Remove($"{CACHE_KEY_SCANNER_PREFIX}ASE");

            _logger.LogInformation("Container data cache cleared");
        }

        #region Private Query Methods

        private async Task<List<ScannerContainerData>> QueryAllScannerContainersAsync()
        {
            var containers = new List<ScannerContainerData>();

            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // ✅ CRITICAL FIX: Add date filter to prevent loading ALL data into buffer pool
                // This method was loading ALL FS6000Scans and AseScans, causing 18.82 GB buffer pool usage
                var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);

                // Get FS6000 containers (✅ OPTIMIZED: Filter to last 30 days + AsNoTracking for read-only)
                var fs6000Containers = await dbContext.FS6000Scans
                    .AsNoTracking() // ✅ Read-only query, no change tracking
                    .Where(s => s.ScanTime >= thirtyDaysAgo && !string.IsNullOrEmpty(s.ContainerNumber))
                    .Select(s => new ScannerContainerData
                    {
                        Id = s.Id,
                        ContainerNumber = s.ContainerNumber,
                        ScannerType = "FS6000",
                        ScanDateTime = s.ScanTime,
                        FilePath = s.FilePath,
                        ImagePath = s.FilePath,
                        HasImage = !string.IsNullOrEmpty(s.FilePath)
                    })
                    .ToListAsync();

                containers.AddRange(fs6000Containers);
                _logger.LogDebug("Loaded {Count} FS6000 containers from database (last 30 days)", fs6000Containers.Count);

                // Get ASE containers (✅ OPTIMIZED: Filter to last 30 days + No longer references ScanImage byte[] - saves 23 GB!)
                // ✅ CRITICAL FIX: Add date filter to prevent loading ALL AseScans into buffer pool (was using 18.82 GB!)
                var aseContainers = await dbContext.AseScans
                    .AsNoTracking() // ✅ Read-only query
                    .Where(s => s.ScanTime >= thirtyDaysAgo && !string.IsNullOrEmpty(s.ContainerNumber))
                    .Select(s => new ScannerContainerData
                    {
                        Id = s.Id,
                        ContainerNumber = s.ContainerNumber ?? string.Empty,
                        ScannerType = "ASE",
                        ScanDateTime = s.ScanTime,
                        FilePath = s.ImageDisplayName,
                        ImagePath = s.ImageDisplayName,
                        HasImage = !string.IsNullOrEmpty(s.ImageDisplayName) // ✅ Check DisplayName instead of ScanImage
                        // ✅ ScanImage byte[] NOT queried - prevents loading 1.7 MB per record!
                    })
                    .ToListAsync();

                containers.AddRange(aseContainers);
                _logger.LogDebug("Loaded {Count} ASE containers from database", aseContainers.Count);

                // Note: NUCTECH and HeimannSmith can be added here when tables are available

                return containers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying scanner containers");
                throw;
            }
        }

        private async Task<List<ScannerContainerData>> QueryScannerContainersByScannerTypeAsync(string scannerType)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                return scannerType.ToUpper() switch
                {
                    "FS6000" => await dbContext.FS6000Scans
                        .AsNoTracking() // ✅ Read-only query
                        .Where(s => !string.IsNullOrEmpty(s.ContainerNumber))
                        .Select(s => new ScannerContainerData
                        {
                            Id = s.Id,
                            ContainerNumber = s.ContainerNumber,
                            ScannerType = "FS6000",
                            ScanDateTime = s.ScanTime,
                            FilePath = s.FilePath,
                            ImagePath = s.FilePath,
                            HasImage = !string.IsNullOrEmpty(s.FilePath)
                        })
                        .ToListAsync(),

                    "ASE" => await dbContext.AseScans
                        .AsNoTracking() // ✅ Read-only query
                                        // ✅ MEMORY OPTIMIZATION: Filter to last 30 days to reduce buffer pool usage
                        .Where(s => s.ScanTime >= DateTime.UtcNow.AddDays(-30)
                            && !string.IsNullOrEmpty(s.ContainerNumber))
                        .Select(s => new ScannerContainerData
                        {
                            Id = s.Id,
                            ContainerNumber = s.ContainerNumber ?? string.Empty,
                            ScannerType = "ASE",
                            ScanDateTime = s.ScanTime,
                            FilePath = s.ImageDisplayName,
                            ImagePath = s.ImageDisplayName,
                            HasImage = !string.IsNullOrEmpty(s.ImageDisplayName) // ✅ Check DisplayName, don't load ScanImage
                            // ✅ ScanImage byte[] NOT queried - prevents loading 1.7 MB × 15K records!
                        })
                        .ToListAsync(),

                    _ => new List<ScannerContainerData>()
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying {ScannerType} containers", scannerType);
                throw;
            }
        }

        private async Task<HashSet<string>> QueryContainersWithICUMSDataAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var icumDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

                var containers = await icumDbContext.BOEDocuments
                    .Where(b => !string.IsNullOrEmpty(b.ContainerNumber))
                    .Select(b => b.ContainerNumber)
                    .Distinct()
                    .ToHashSetAsync();

                _logger.LogDebug("Loaded {Count} ICUMS containers from database", containers.Count);
                return containers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying ICUMS containers");
                throw;
            }
        }

        #endregion
    }
}

