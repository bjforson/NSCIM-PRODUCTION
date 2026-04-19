using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Settings
{
    /// <summary>
    /// ICUMS-specific configuration provider that reads from System Settings
    /// Replaces IConfiguration for ICUMS services
    /// </summary>
    public class ICUMSConfigurationProvider
    {
        private readonly ISettingsProvider _settingsProvider;
        private readonly ILogger<ICUMSConfigurationProvider> _logger;

        public ICUMSConfigurationProvider(
            ISettingsProvider settingsProvider,
            ILogger<ICUMSConfigurationProvider> logger)
        {
            _settingsProvider = settingsProvider;
            _logger = logger;
        }

        /// <summary>
        /// Get ICUMS Base URL
        /// </summary>
        public async Task<string> GetBaseUrlAsync()
        {
            return await _settingsProvider.GetStringAsync("ICUMS", "BaseUrl",
                "https://www.icumsghana.com/nickscanintegrationservice");
        }

        /// <summary>
        /// Get API timeout in seconds
        /// </summary>
        public async Task<int> GetTimeoutSecondsAsync()
        {
            return await _settingsProvider.GetIntAsync("ICUMS", "TimeoutSeconds", 120);
        }

        /// <summary>
        /// Get batch interval in minutes
        /// </summary>
        public async Task<int> GetBatchIntervalMinutesAsync()
        {
            return await _settingsProvider.GetIntAsync("ICUMS", "BatchIntervalMinutes", 30);
        }

        /// <summary>
        /// Get downloads path
        /// </summary>
        public async Task<string> GetDownloadsPathAsync()
        {
            return await _settingsProvider.GetStringAsync("ICUMS", "DownloadsPath", @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads");
        }

        /// <summary>
        /// Get fetch batch URL endpoint
        /// </summary>
        public async Task<string> GetFetchBatchUrlAsync()
        {
            return await _settingsProvider.GetStringAsync("ICUMS", "FetchBatchUrl",
                "/api/BOEScanData/FetchBatchBOEScanDocument");
        }

        /// <summary>
        /// Get fetch single container URL endpoint
        /// </summary>
        public async Task<string> GetFetchUrlAsync()
        {
            return await _settingsProvider.GetStringAsync("ICUMS", "FetchUrl",
                "/api/BOEScanData/FetchBOEScanDocument");
        }

        /// <summary>
        /// Get submit result URL endpoint
        /// </summary>
        public async Task<string> GetSubmitResultUrlAsync()
        {
            return await _settingsProvider.GetStringAsync("ICUMS", "SubmitResultUrl",
                "/api/BOEScanData/SubmitScanResult");
        }

        /// <summary>
        /// Get all ICUMS settings as a configuration object
        /// </summary>
        public async Task<ICUMSConfiguration> GetConfigurationAsync()
        {
            var settings = await _settingsProvider.GetCategorySettingsAsync("ICUMS");

            return new ICUMSConfiguration
            {
                BaseUrl = settings.GetValueOrDefault("BaseUrl", "https://www.icumsghana.com/nickscanintegrationservice"),
                FetchBatchUrl = settings.GetValueOrDefault("FetchBatchUrl", "/api/BOEScanData/FetchBatchBOEScanDocument"),
                FetchUrl = settings.GetValueOrDefault("FetchUrl", "/api/BOEScanData/FetchBOEScanDocument"),
                SubmitResultUrl = settings.GetValueOrDefault("SubmitResultUrl", "/api/BOEScanData/SubmitScanResult"),
                TimeoutSeconds = int.TryParse(settings.GetValueOrDefault("TimeoutSeconds", "120"), out var timeout) ? timeout : 120,
                BatchIntervalMinutes = int.TryParse(settings.GetValueOrDefault("BatchIntervalMinutes", "30"), out var interval) ? interval : 30,
                DownloadsPath = settings.GetValueOrDefault("DownloadsPath", @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads")
            };
        }

        /// <summary>
        /// Check if ICUMS integration is enabled
        /// </summary>
        public async Task<bool> IsEnabledAsync()
        {
            // Could add an "Enabled" setting to ICUMS category
            return await _settingsProvider.GetBoolAsync("ICUMS", "Enabled", true);
        }

        /// <summary>
        /// Invalidate ICUMS configuration cache
        /// Call this after updating ICUMS settings
        /// </summary>
        public void InvalidateCache()
        {
            _settingsProvider.InvalidateCache("ICUMS");
            _logger.LogInformation("ICUMS configuration cache invalidated");
        }
    }

    /// <summary>
    /// ICUMS configuration object
    /// </summary>
    public class ICUMSConfiguration
    {
        public string BaseUrl { get; set; } = string.Empty;
        public string FetchBatchUrl { get; set; } = string.Empty;
        public string FetchUrl { get; set; } = string.Empty;
        public string SubmitResultUrl { get; set; } = string.Empty;
        public int TimeoutSeconds { get; set; }
        public int BatchIntervalMinutes { get; set; }
        public string DownloadsPath { get; set; } = string.Empty;
    }
}

