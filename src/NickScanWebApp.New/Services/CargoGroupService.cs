using System.Net.Http.Json;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Service for accessing standardized cargo group data (consolidated and non-consolidated)
    /// </summary>
    public class CargoGroupService
    {
        private readonly ApiService _apiService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<CargoGroupService> _logger;
        private const string API_BASE = "/api/cargogroup";

        public CargoGroupService(
            ApiService apiService,
            IMemoryCache cache,
            ILogger<CargoGroupService> logger)
        {
            _apiService = apiService;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Get group identifier (Master BL or Declaration Number) for a container
        /// </summary>
        public async Task<CargoGroupIdentifierDto?> GetGroupIdentifierByContainerAsync(string containerNumber)
        {
            try
            {
                var url = $"{API_BASE}/by-container/{Uri.EscapeDataString(containerNumber)}";
                return await _apiService.GetAsync<CargoGroupIdentifierDto>(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting group identifier for container {Container}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Get complete cargo group by identifier (Master BL for consolidated, Declaration Number for non-consolidated)
        /// </summary>
        /// <param name="groupIdentifier">Master BL (consolidated) or Declaration Number (non-consolidated)</param>
        /// <param name="type">Cargo type (optional)</param>
        /// <param name="loadScannerData">Whether to load scanner data (default: true)</param>
        /// <param name="loadImageData">Whether to load image data (default: true)</param>
        /// <param name="loadICUMSData">Whether to load ICUMS data (default: true)</param>
        public async Task<CargoGroupDto?> GetCargoGroupAsync(
            string groupIdentifier,
            CargoType? type = null,
            bool loadScannerData = true,
            bool loadImageData = true,
            bool loadICUMSData = true)
        {
            try
            {
                // ✅ PERFORMANCE: Include data type flags in cache key to cache different requests separately
                var cacheKey = $"cargo_group_{groupIdentifier}_{type}_{loadScannerData}_{loadImageData}_{loadICUMSData}";

                if (_cache.TryGetValue(cacheKey, out CargoGroupDto? cachedGroup))
                {
                    _logger.LogInformation("Retrieved cargo group {Identifier} from cache", groupIdentifier);
                    return cachedGroup;
                }

                _logger.LogInformation("Fetching cargo group {Identifier} from API (Scanner: {Scanner}, Image: {Image}, ICUMS: {ICUMS})",
                    groupIdentifier, loadScannerData, loadImageData, loadICUMSData);

                var queryParams = new List<string>();
                if (type.HasValue)
                {
                    queryParams.Add($"type={type.Value}");
                }
                if (!loadScannerData)
                {
                    queryParams.Add("loadScannerData=false");
                }
                if (!loadImageData)
                {
                    queryParams.Add("loadImageData=false");
                }
                if (!loadICUMSData)
                {
                    queryParams.Add("loadICUMSData=false");
                }

                var url = $"{API_BASE}/{Uri.EscapeDataString(groupIdentifier)}";
                if (queryParams.Any())
                {
                    url += $"?{string.Join("&", queryParams)}";
                }

                var networkStopwatch = System.Diagnostics.Stopwatch.StartNew();
                _logger.LogDebug("API call START - URL: {Url}", url);
                var group = await _apiService.GetAsync<CargoGroupDto>(url);
                networkStopwatch.Stop();
                _logger.LogDebug("API call COMPLETE - Network time: {ElapsedMs}ms ({ElapsedSec:F2}s)",
                    networkStopwatch.ElapsedMilliseconds, networkStopwatch.Elapsed.TotalSeconds);

                if (group != null)
                {
                    // Cache for 5 minutes
                    _cache.Set(cacheKey, group, BuildCacheOptions(TimeSpan.FromMinutes(5), EstimateCargoGroupSize(group)));
                    _logger.LogInformation("Cached cargo group {Identifier}", groupIdentifier);
                }

                return group;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cargo group {Identifier}", groupIdentifier);
                return null;
            }
        }

        /// <summary>
        /// Get all data for a cargo group (ICUMS, Scanner, Images)
        /// </summary>
        public async Task<CargoGroupDataDto?> GetCargoGroupDataAsync(string groupIdentifier, CargoType type)
        {
            try
            {
                var cacheKey = $"cargo_group_data_{groupIdentifier}_{type}";

                if (_cache.TryGetValue(cacheKey, out CargoGroupDataDto? cachedData))
                {
                    _logger.LogInformation("Retrieved cargo group data {Identifier} from cache", groupIdentifier);
                    return cachedData;
                }

                _logger.LogInformation("Fetching cargo group data {Identifier} from API", groupIdentifier);

                var url = $"{API_BASE}/{Uri.EscapeDataString(groupIdentifier)}/data?type={type}";
                var data = await _apiService.GetAsync<CargoGroupDataDto>(url);

                if (data != null)
                {
                    // Cache for 2 minutes (data may update more frequently)
                    _cache.Set(cacheKey, data, BuildCacheOptions(TimeSpan.FromMinutes(2), EstimateCargoGroupDataSize(data)));
                    _logger.LogInformation("Cached cargo group data {Identifier}", groupIdentifier);
                }

                return data;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cargo group data {Identifier}", groupIdentifier);
                return null;
            }
        }

        /// <summary>
        /// Get ICUMS data only for a cargo group
        /// </summary>
        public async Task<List<ICUMSDataGroupDto>?> GetCargoGroupICUMSAsync(string groupIdentifier, CargoType type)
        {
            try
            {
                var url = $"{API_BASE}/{Uri.EscapeDataString(groupIdentifier)}/icums?type={type}";
                return await _apiService.GetAsync<List<ICUMSDataGroupDto>>(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ICUMS data for cargo group {Identifier}", groupIdentifier);
                return null;
            }
        }

        /// <summary>
        /// Get scanner data only for a cargo group
        /// </summary>
        public async Task<List<ScannerDataGroupDto>?> GetCargoGroupScannerAsync(string groupIdentifier, CargoType type)
        {
            try
            {
                var url = $"{API_BASE}/{Uri.EscapeDataString(groupIdentifier)}/scanner?type={type}";
                return await _apiService.GetAsync<List<ScannerDataGroupDto>>(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scanner data for cargo group {Identifier}", groupIdentifier);
                return null;
            }
        }

        /// <summary>
        /// Get images only for a cargo group
        /// </summary>
        public async Task<List<ImageDataGroupDto>?> GetCargoGroupImagesAsync(string groupIdentifier, CargoType type)
        {
            try
            {
                var url = $"{API_BASE}/{Uri.EscapeDataString(groupIdentifier)}/images?type={type}";
                return await _apiService.GetAsync<List<ImageDataGroupDto>>(url);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting images for cargo group {Identifier}", groupIdentifier);
                return null;
            }
        }

        /// <summary>
        /// Get list of cargo groups (summary) with filtering
        /// </summary>
        public async Task<List<CargoGroupSummaryDto>?> GetCargoGroupsAsync(
            CargoType? type = null,
            string? clearanceType = null,
            int page = 1,
            int pageSize = 50)
        {
            try
            {
                var queryParams = new List<string>();
                if (type.HasValue)
                    queryParams.Add($"type={type.Value}");
                if (!string.IsNullOrEmpty(clearanceType))
                    queryParams.Add($"clearanceType={Uri.EscapeDataString(clearanceType)}");
                queryParams.Add($"page={page}");
                queryParams.Add($"pageSize={pageSize}");

                var url = $"{API_BASE}?{string.Join("&", queryParams)}";
                return await _apiService.GetAsync<List<CargoGroupSummaryDto>>(url);
            }
            catch (NickScanWebApp.Shared.Services.ApiException ex) when (ex.Message.Contains("timed out", StringComparison.OrdinalIgnoreCase))
            {
                // Timeout - log as warning instead of error
                _logger.LogWarning("Request timeout getting cargo groups list. The API may be slow or unavailable.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cargo groups list");
                return null;
            }
        }

        private static MemoryCacheEntryOptions BuildCacheOptions(TimeSpan expiration, long size)
        {
            return new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration,
                Size = Math.Max(1, size)
            };
        }

        private static long EstimateCargoGroupSize(CargoGroupDto group)
        {
            var houseBills = group.HouseBLGroups?.Count ?? 0;
            var houseBillBoeDetails = group.HouseBLGroups?.Sum(h => h.BOEDetails?.Count ?? 0) ?? 0;
            return 1
                + Math.Max(group.TotalContainers, group.ContainerNumbers?.Count ?? 0)
                + group.TotalBOEs
                + houseBills
                + houseBillBoeDetails
                + EstimateCargoGroupDataSize(group.Data);
        }

        private static long EstimateCargoGroupDataSize(CargoGroupDataDto? data)
        {
            if (data == null)
            {
                return 1;
            }

            var icumsRecords = data.ICUMSData?.Sum(g => (g.Records?.Count ?? 0) + (g.BOEDetails?.Count ?? 0)) ?? 0;
            var scannerRecords = data.ScannerData?.Sum(g => g.Records?.Count ?? 0) ?? 0;
            var images = data.ImageData?.Sum(g => g.Images?.Count ?? 0) ?? 0;
            return 1 + icumsRecords + scannerRecords + (images * 2);
        }
    }
}

