using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Settings
{
    /// <summary>
    /// Provides rate limiting configuration from system settings
    /// </summary>
    public class RateLimitingConfigurationProvider
    {
        private readonly ISettingsProvider _settingsProvider;

        public RateLimitingConfigurationProvider(ISettingsProvider settingsProvider)
        {
            _settingsProvider = settingsProvider;
        }

        /// <summary>
        /// Get login rate limit (requests per minute)
        /// </summary>
        public async Task<int> GetLoginLimitAsync()
        {
            return await _settingsProvider.GetIntAsync("RateLimiting", "Login.PerMinute", 5);
        }

        /// <summary>
        /// Get general API rate limit (requests per minute)
        /// </summary>
        public async Task<int> GetApiLimitAsync()
        {
            return await _settingsProvider.GetIntAsync("RateLimiting", "API.PerMinute", 500);
        }

        /// <summary>
        /// Get dashboard rate limit (requests per minute)
        /// </summary>
        public async Task<int> GetDashboardLimitAsync()
        {
            return await _settingsProvider.GetIntAsync("RateLimiting", "Dashboard.PerMinute", 200);
        }

        /// <summary>
        /// Get export/bulk operation rate limit (requests per minute)
        /// </summary>
        public async Task<int> GetExportLimitAsync()
        {
            return await _settingsProvider.GetIntAsync("RateLimiting", "Export.PerMinute", 50);
        }

        /// <summary>
        /// Get admin operation rate limit (requests per minute)
        /// </summary>
        public async Task<int> GetAdminLimitAsync()
        {
            return await _settingsProvider.GetIntAsync("RateLimiting", "Admin.PerMinute", 1000);
        }

        /// <summary>
        /// Get all rate limiting configuration
        /// </summary>
        public async Task<RateLimitingConfiguration> GetConfigurationAsync()
        {
            return new RateLimitingConfiguration
            {
                LoginLimitPerMinute = await GetLoginLimitAsync(),
                ApiLimitPerMinute = await GetApiLimitAsync(),
                DashboardLimitPerMinute = await GetDashboardLimitAsync(),
                ExportLimitPerMinute = await GetExportLimitAsync(),
                AdminLimitPerMinute = await GetAdminLimitAsync()
            };
        }
    }

    /// <summary>
    /// Rate limiting configuration model
    /// </summary>
    public class RateLimitingConfiguration
    {
        public int LoginLimitPerMinute { get; set; } = 5;
        public int ApiLimitPerMinute { get; set; } = 500;
        public int DashboardLimitPerMinute { get; set; } = 200;
        public int ExportLimitPerMinute { get; set; } = 50;
        public int AdminLimitPerMinute { get; set; } = 1000;
    }
}

