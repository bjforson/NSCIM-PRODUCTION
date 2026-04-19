using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;

namespace NickScanWebApp.Mobile.Services
{
    /// <summary>
    /// Service for accessing standardized cargo group data (consolidated and non-consolidated)
    /// Mobile version - uses ApiService
    /// </summary>
    public class CargoGroupService
    {
        private readonly ApiService _apiService;
        private readonly DataCacheService _cacheService;
        private readonly ILogger<CargoGroupService> _logger;
        private const string API_BASE = "/api/cargogroup";

        public CargoGroupService(
            ApiService apiService,
            DataCacheService cacheService,
            ILogger<CargoGroupService> logger)
        {
            _apiService = apiService;
            _cacheService = cacheService;
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
        public async Task<CargoGroupDto?> GetCargoGroupAsync(string groupIdentifier, CargoType? type = null)
        {
            try
            {
                var cacheKey = $"cargo_group_{groupIdentifier}_{type}";
                
                // ✅ Use DataCacheService for cleaner caching with automatic logging
                return await _cacheService.GetOrCreateAsync(
                    cacheKey,
                    async () =>
                    {
                        _logger.LogInformation("Fetching cargo group {Identifier} from API", groupIdentifier);

                        var url = $"{API_BASE}/{Uri.EscapeDataString(groupIdentifier)}";
                        if (type.HasValue)
                        {
                            url += $"?type={type.Value}";
                        }

                        return await _apiService.GetAsync<CargoGroupDto>(url);
                    },
                    TimeSpan.FromMinutes(5) // Cache for 5 minutes
                );
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
                
                // ✅ Use DataCacheService for cleaner caching with automatic logging
                return await _cacheService.GetOrCreateAsync(
                    cacheKey,
                    async () =>
                    {
                        _logger.LogInformation("Fetching cargo group data {Identifier} from API", groupIdentifier);

                        var url = $"{API_BASE}/{Uri.EscapeDataString(groupIdentifier)}/data?type={type}";
                        return await _apiService.GetAsync<CargoGroupDataDto>(url);
                    },
                    TimeSpan.FromMinutes(2) // Cache for 2 minutes (data may update more frequently)
                );
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cargo group data {Identifier}", groupIdentifier);
                return null;
            }
        }
    }
}
