using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Settings
{
    /// <summary>
    /// Centralized provider for accessing System Settings with caching
    /// Acts as a bridge between services and the SystemSettings database
    /// </summary>
    public class SettingsProvider : ISettingsProvider
    {
        private readonly ISettingsRepository _repository;
        private readonly ISettingsEncryptionService _encryptionService;
        private readonly IMemoryCache _cache;
        private readonly ILogger<SettingsProvider> _logger;
        private const int CACHE_DURATION_MINUTES = 5; // ✅ Cache settings for 5 minutes for better performance

        public SettingsProvider(
            ISettingsRepository repository,
            ISettingsEncryptionService encryptionService,
            IMemoryCache cache,
            ILogger<SettingsProvider> logger)
        {
            _repository = repository;
            _encryptionService = encryptionService;
            _cache = cache;
            _logger = logger;
        }

        /// <summary>
        /// Get setting value as string with fallback to default
        /// </summary>
        public async Task<string> GetStringAsync(string category, string key, string defaultValue = "")
        {
            try
            {
                var cacheKey = $"setting_{category}_{key}";

                if (_cache.TryGetValue<string>(cacheKey, out var cachedValue))
                {
                    return cachedValue!;
                }

                var setting = await _repository.GetSettingAsync(category, key);

                if (setting == null || !setting.IsActive)
                {
                    _logger.LogDebug("Setting {Category}.{Key} not found or inactive, using default: {Default}",
                        category, key, defaultValue);
                    return defaultValue;
                }

                var value = setting.IsEncrypted
                    ? await _encryptionService.DecryptAsync(setting.SettingValue)
                    : setting.SettingValue;

                // Cache the value with size for memory limit compliance
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CACHE_DURATION_MINUTES),
                    Size = 1 // Each setting counts as 1 unit
                };
                _cache.Set(cacheKey, value, cacheOptions);

                return value;
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx) when (IsDatabaseConnectivityError(sqlEx))
            {
                // Database connectivity issues are expected during startup - log at Debug level
                _logger.LogDebug("Database not available for setting {Category}.{Key}, using default: {Default} (This is normal during startup)",
                    category, key, defaultValue);
                return defaultValue;
            }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                // Other database connectivity issues - log at Debug level
                _logger.LogDebug("Database connectivity issue for setting {Category}.{Key}, using default: {Default}",
                    category, key, defaultValue);
                return defaultValue;
            }
            catch (Exception ex)
            {
                // Actual errors (not connectivity issues) - log at Error level
                _logger.LogError(ex, "Error getting setting {Category}.{Key}, using default: {Default}",
                    category, key, defaultValue);
                return defaultValue;
            }
        }

        /// <summary>
        /// Check if SQL exception is a database connectivity error (expected during startup)
        /// </summary>
        private static bool IsDatabaseConnectivityError(Microsoft.Data.SqlClient.SqlException sqlEx)
        {
            // SQL Server error numbers for connectivity issues
            // 2 = The system cannot find the file specified (Named Pipes)
            // 40 = Could not open a connection to SQL Server
            // 53 = Network path not found
            // 121 = Semaphore timeout period has expired
            // 10053 = An established connection was aborted
            // 10054 = An existing connection was forcibly closed
            // 10060 = A network-related or instance-specific error occurred
            return sqlEx.Number == 2 || sqlEx.Number == 40 || sqlEx.Number == 53 ||
                   sqlEx.Number == 121 || sqlEx.Number == 10053 || sqlEx.Number == 10054 ||
                   sqlEx.Number == 10060 || sqlEx.Message.Contains("network-related") ||
                   sqlEx.Message.Contains("instance-specific error") ||
                   sqlEx.Message.Contains("cannot find the file specified");
        }

        /// <summary>
        /// Check if exception is a database connectivity issue
        /// </summary>
        private static bool IsDatabaseConnectivityException(Exception ex)
        {
            if (ex is Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                return IsDatabaseConnectivityError(sqlEx);
            }

            // Check for common database connectivity error messages
            var message = ex.Message.ToLowerInvariant();
            return message.Contains("network-related") ||
                   message.Contains("instance-specific error") ||
                   message.Contains("cannot find the file specified") ||
                   message.Contains("could not open a connection") ||
                   (ex.InnerException != null && IsDatabaseConnectivityException(ex.InnerException));
        }

        /// <summary>
        /// Get setting value as integer with fallback to default
        /// </summary>
        public async Task<int> GetIntAsync(string category, string key, int defaultValue = 0)
        {
            var value = await GetStringAsync(category, key, defaultValue.ToString());
            return int.TryParse(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Get setting value as boolean with fallback to default
        /// </summary>
        public async Task<bool> GetBoolAsync(string category, string key, bool defaultValue = false)
        {
            var value = await GetStringAsync(category, key, defaultValue.ToString().ToLower());
            return bool.TryParse(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Get setting value as decimal with fallback to default
        /// </summary>
        public async Task<decimal> GetDecimalAsync(string category, string key, decimal defaultValue = 0m)
        {
            var value = await GetStringAsync(category, key, defaultValue.ToString());
            return decimal.TryParse(value, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Get all settings for a category as a dictionary
        /// </summary>
        public async Task<Dictionary<string, string>> GetCategorySettingsAsync(string category)
        {
            try
            {
                var cacheKey = $"category_settings_{category}";

                if (_cache.TryGetValue<Dictionary<string, string>>(cacheKey, out var cachedSettings))
                {
                    return cachedSettings!;
                }

                var settings = await _repository.GetSettingsByCategoryAsync(category);
                var result = new Dictionary<string, string>();

                foreach (var setting in settings)
                {
                    var value = setting.IsEncrypted
                        ? await _encryptionService.DecryptAsync(setting.SettingValue)
                        : setting.SettingValue;

                    result[setting.SettingKey] = value;
                }

                // Cache the category settings with size for memory limit compliance
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(CACHE_DURATION_MINUTES),
                    Size = 1 // Each category counts as 1 unit
                };
                _cache.Set(cacheKey, result, cacheOptions);

                return result;
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx) when (IsDatabaseConnectivityError(sqlEx))
            {
                _logger.LogDebug("Database not available for category settings {Category} (This is normal during startup)", category);
                return new Dictionary<string, string>();
            }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                _logger.LogDebug("Database connectivity issue for category settings {Category}", category);
                return new Dictionary<string, string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting category settings for {Category}", category);
                return new Dictionary<string, string>();
            }
        }

        /// <summary>
        /// Invalidate cache for a specific setting or entire category
        /// Call this when settings are updated
        /// </summary>
        public void InvalidateCache(string category, string? key = null)
        {
            if (string.IsNullOrEmpty(key))
            {
                // Invalidate entire category
                _cache.Remove($"category_settings_{category}");
                _logger.LogInformation("Invalidated cache for category: {Category}", category);
            }
            else
            {
                // Invalidate specific setting
                _cache.Remove($"setting_{category}_{key}");
                _logger.LogInformation("Invalidated cache for setting: {Category}.{Key}", category, key);
            }
        }

        /// <summary>
        /// Clear all settings cache
        /// </summary>
        public void ClearCache()
        {
            // Note: IMemoryCache doesn't have a Clear() method
            // In production, you might want to track cache keys for bulk removal
            _logger.LogInformation("Settings cache clear requested - individual invalidation needed");
        }

        /// <summary>
        /// Check if a setting exists and is active
        /// </summary>
        public async Task<bool> ExistsAsync(string category, string key)
        {
            try
            {
                var setting = await _repository.GetSettingAsync(category, key);
                return setting != null && setting.IsActive;
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx) when (IsDatabaseConnectivityError(sqlEx))
            {
                _logger.LogDebug("Database not available for checking setting {Category}.{Key} (This is normal during startup)", category, key);
                return false;
            }
            catch (Exception ex) when (IsDatabaseConnectivityException(ex))
            {
                _logger.LogDebug("Database connectivity issue for checking setting {Category}.{Key}", category, key);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if setting exists: {Category}.{Key}", category, key);
                return false;
            }
        }
    }
}

