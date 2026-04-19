using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Settings
{
    /// <summary>
    /// Performance-specific configuration provider that reads from System Settings
    /// </summary>
    public class PerformanceConfigurationProvider
    {
        private readonly ISettingsProvider _settingsProvider;
        private readonly ILogger<PerformanceConfigurationProvider> _logger;

        public PerformanceConfigurationProvider(
            ISettingsProvider settingsProvider,
            ILogger<PerformanceConfigurationProvider> logger)
        {
            _settingsProvider = settingsProvider;
            _logger = logger;
        }

        /// <summary>
        /// Get cache duration in seconds
        /// </summary>
        public async Task<int> GetCacheDurationSecondsAsync()
        {
            return await _settingsProvider.GetIntAsync("Performance", "CacheDurationSeconds", 300);
        }

        /// <summary>
        /// Get maximum image size in MB
        /// </summary>
        public async Task<int> GetMaxImageSizeMBAsync()
        {
            return await _settingsProvider.GetIntAsync("Performance", "MaxImageSizeMB", 10);
        }

        /// <summary>
        /// Get all performance settings as a configuration object
        /// </summary>
        public async Task<PerformanceConfiguration> GetConfigurationAsync()
        {
            var settings = await _settingsProvider.GetCategorySettingsAsync("Performance");

            return new PerformanceConfiguration
            {
                CacheDurationSeconds = int.TryParse(settings.GetValueOrDefault("CacheDurationSeconds", "300"), out var cache) ? cache : 300,
                MaxImageSizeMB = int.TryParse(settings.GetValueOrDefault("MaxImageSizeMB", "10"), out var maxSize) ? maxSize : 10
            };
        }

        /// <summary>
        /// Invalidate performance configuration cache
        /// </summary>
        public void InvalidateCache()
        {
            _settingsProvider.InvalidateCache("Performance");
            _logger.LogInformation("Performance configuration cache invalidated");
        }
    }

    /// <summary>
    /// Performance configuration object
    /// </summary>
    public class PerformanceConfiguration
    {
        public int CacheDurationSeconds { get; set; }
        public int MaxImageSizeMB { get; set; }
    }
}

