using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Settings
{
    /// <summary>
    /// Scanner-specific configuration provider that reads from System Settings
    /// </summary>
    public class ScannerConfigurationProvider
    {
        private readonly ISettingsProvider _settingsProvider;
        private readonly ILogger<ScannerConfigurationProvider> _logger;

        public ScannerConfigurationProvider(
            ISettingsProvider settingsProvider,
            ILogger<ScannerConfigurationProvider> logger)
        {
            _settingsProvider = settingsProvider;
            _logger = logger;
        }

        #region FS6000 Settings

        /// <summary>
        /// Check if FS6000 scanner is enabled
        /// </summary>
        public async Task<bool> IsFS6000EnabledAsync()
        {
            return await _settingsProvider.GetBoolAsync("Scanners", "FS6000.Enabled", true);
        }

        /// <summary>
        /// Get FS6000 source path
        /// </summary>
        public async Task<string> GetFS6000SourcePathAsync()
        {
            return await _settingsProvider.GetStringAsync("Scanners", "FS6000.SourcePath", @"C:\FS6000\Export");
        }

        #endregion

        #region ASE Settings

        /// <summary>
        /// Check if ASE scanner is enabled
        /// </summary>
        public async Task<bool> IsASEEnabledAsync()
        {
            return await _settingsProvider.GetBoolAsync("Scanners", "ASE.Enabled", true);
        }

        #endregion

        #region Heimann Settings

        /// <summary>
        /// Check if Heimann scanner is enabled
        /// </summary>
        public async Task<bool> IsHeimannEnabledAsync()
        {
            return await _settingsProvider.GetBoolAsync("Scanners", "Heimann.Enabled", true);
        }

        #endregion

        /// <summary>
        /// Get all scanner settings as a configuration object
        /// </summary>
        public async Task<ScannerConfiguration> GetConfigurationAsync()
        {
            var settings = await _settingsProvider.GetCategorySettingsAsync("Scanners");

            return new ScannerConfiguration
            {
                FS6000Enabled = bool.TryParse(settings.GetValueOrDefault("FS6000.Enabled", "true"), out var fs6000) && fs6000,
                FS6000SourcePath = settings.GetValueOrDefault("FS6000.SourcePath", @"C:\FS6000\Export"),
                ASEEnabled = bool.TryParse(settings.GetValueOrDefault("ASE.Enabled", "true"), out var ase) && ase,
                HeimannEnabled = bool.TryParse(settings.GetValueOrDefault("Heimann.Enabled", "true"), out var heimann) && heimann
            };
        }

        /// <summary>
        /// Invalidate scanner configuration cache
        /// </summary>
        public void InvalidateCache()
        {
            _settingsProvider.InvalidateCache("Scanners");
            _logger.LogInformation("Scanner configuration cache invalidated");
        }
    }

    /// <summary>
    /// Scanner configuration object
    /// </summary>
    public class ScannerConfiguration
    {
        public bool FS6000Enabled { get; set; }
        public string FS6000SourcePath { get; set; } = string.Empty;
        public bool ASEEnabled { get; set; }
        public bool HeimannEnabled { get; set; }
    }
}

