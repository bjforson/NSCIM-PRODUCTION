using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Settings
{
    /// <summary>
    /// Provides caching configuration from system settings
    /// </summary>
    public class CachingConfigurationProvider
    {
        private readonly ISettingsProvider _settingsProvider;

        public CachingConfigurationProvider(ISettingsProvider settingsProvider)
        {
            _settingsProvider = settingsProvider;
        }

        /// <summary>
        /// Get whether Redis is enabled
        /// </summary>
        public async Task<bool> IsRedisEnabledAsync()
        {
            return await _settingsProvider.GetBoolAsync("Redis", "Enabled", false);
        }

        /// <summary>
        /// Get Redis connection string
        /// </summary>
        public async Task<string> GetRedisConnectionStringAsync()
        {
            return await _settingsProvider.GetStringAsync("Redis", "ConnectionString", "localhost:6379");
        }

        /// <summary>
        /// Get Redis instance name
        /// </summary>
        public async Task<string> GetRedisInstanceNameAsync()
        {
            return await _settingsProvider.GetStringAsync("Redis", "InstanceName", "NickScanPortal:");
        }

        /// <summary>
        /// Get whether response caching is enabled
        /// </summary>
        public async Task<bool> IsResponseCachingEnabledAsync()
        {
            return await _settingsProvider.GetBoolAsync("Caching", "EnableResponseCaching", false);
        }

        /// <summary>
        /// Get maximum response body size for caching (in MB)
        /// </summary>
        public async Task<int> GetMaxResponseBodySizeMBAsync()
        {
            return await _settingsProvider.GetIntAsync("Caching", "MaxResponseBodySizeMB", 1);
        }

        /// <summary>
        /// Get whether to use case-sensitive paths for caching
        /// </summary>
        public async Task<bool> UseCaseSensitivePathsAsync()
        {
            return await _settingsProvider.GetBoolAsync("Caching", "UseCaseSensitivePaths", false);
        }

        /// <summary>
        /// Get cache duration for containers (in seconds)
        /// </summary>
        public async Task<int> GetContainersCacheDurationSecondsAsync()
        {
            return await _settingsProvider.GetIntAsync("Caching", "Containers.DurationSeconds", 300);
        }

        /// <summary>
        /// Get cache duration for scans (in seconds)
        /// </summary>
        public async Task<int> GetScansCacheDurationSecondsAsync()
        {
            return await _settingsProvider.GetIntAsync("Caching", "Scans.DurationSeconds", 600);
        }

        /// <summary>
        /// Get cache duration for ICUMS data (in seconds)
        /// </summary>
        public async Task<int> GetICUMSCacheDurationSecondsAsync()
        {
            return await _settingsProvider.GetIntAsync("Caching", "ICUMS.DurationSeconds", 1800);
        }

        /// <summary>
        /// Get cache duration for users (in seconds)
        /// </summary>
        public async Task<int> GetUsersCacheDurationSecondsAsync()
        {
            return await _settingsProvider.GetIntAsync("Caching", "Users.DurationSeconds", 900);
        }

        /// <summary>
        /// Get cache duration for roles (in seconds)
        /// </summary>
        public async Task<int> GetRolesCacheDurationSecondsAsync()
        {
            return await _settingsProvider.GetIntAsync("Caching", "Roles.DurationSeconds", 3600);
        }

        /// <summary>
        /// Get all caching configuration
        /// </summary>
        public async Task<CachingConfiguration> GetConfigurationAsync()
        {
            return new CachingConfiguration
            {
                RedisEnabled = await IsRedisEnabledAsync(),
                RedisConnectionString = await GetRedisConnectionStringAsync(),
                RedisInstanceName = await GetRedisInstanceNameAsync(),
                ResponseCachingEnabled = await IsResponseCachingEnabledAsync(),
                MaxResponseBodySizeMB = await GetMaxResponseBodySizeMBAsync(),
                UseCaseSensitivePaths = await UseCaseSensitivePathsAsync(),
                ContainersDurationSeconds = await GetContainersCacheDurationSecondsAsync(),
                ScansDurationSeconds = await GetScansCacheDurationSecondsAsync(),
                ICUMSDurationSeconds = await GetICUMSCacheDurationSecondsAsync(),
                UsersDurationSeconds = await GetUsersCacheDurationSecondsAsync(),
                RolesDurationSeconds = await GetRolesCacheDurationSecondsAsync()
            };
        }
    }

    /// <summary>
    /// Caching configuration model
    /// </summary>
    public class CachingConfiguration
    {
        public bool RedisEnabled { get; set; } = false;
        public string RedisConnectionString { get; set; } = "localhost:6379";
        public string RedisInstanceName { get; set; } = "NickScanPortal:";
        public bool ResponseCachingEnabled { get; set; } = false;
        public int MaxResponseBodySizeMB { get; set; } = 1;
        public bool UseCaseSensitivePaths { get; set; } = false;
        public int ContainersDurationSeconds { get; set; } = 300;
        public int ScansDurationSeconds { get; set; } = 600;
        public int ICUMSDurationSeconds { get; set; } = 1800;
        public int UsersDurationSeconds { get; set; } = 900;
        public int RolesDurationSeconds { get; set; } = 3600;
    }
}

