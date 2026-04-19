using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Settings
{
    /// <summary>
    /// Security-specific configuration provider that reads from System Settings
    /// </summary>
    public class SecurityConfigurationProvider
    {
        private readonly ISettingsProvider _settingsProvider;
        private readonly ILogger<SecurityConfigurationProvider> _logger;

        public SecurityConfigurationProvider(
            ISettingsProvider settingsProvider,
            ILogger<SecurityConfigurationProvider> logger)
        {
            _settingsProvider = settingsProvider;
            _logger = logger;
        }

        /// <summary>
        /// Get minimum password length requirement
        /// </summary>
        public async Task<int> GetPasswordMinLengthAsync()
        {
            return await _settingsProvider.GetIntAsync("Security", "PasswordMinLength", 8);
        }

        /// <summary>
        /// Get session timeout in minutes
        /// </summary>
        public async Task<int> GetSessionTimeoutMinutesAsync()
        {
            return await _settingsProvider.GetIntAsync("Security", "SessionTimeoutMinutes", 30);
        }

        /// <summary>
        /// Get JWT expiration in hours
        /// </summary>
        public async Task<int> GetJwtExpirationHoursAsync()
        {
            return await _settingsProvider.GetIntAsync("Security", "JwtExpirationHours", 24);
        }

        /// <summary>
        /// Get maximum failed login attempts before lockout
        /// </summary>
        public async Task<int> GetMaxFailedLoginAttemptsAsync()
        {
            return await _settingsProvider.GetIntAsync("Security", "MaxFailedLoginAttempts", 5);
        }

        /// <summary>
        /// Get all security settings as a configuration object
        /// </summary>
        public async Task<SecurityConfiguration> GetConfigurationAsync()
        {
            var settings = await _settingsProvider.GetCategorySettingsAsync("Security");

            return new SecurityConfiguration
            {
                PasswordMinLength = int.TryParse(settings.GetValueOrDefault("PasswordMinLength", "8"), out var minLen) ? minLen : 8,
                SessionTimeoutMinutes = int.TryParse(settings.GetValueOrDefault("SessionTimeoutMinutes", "30"), out var timeout) ? timeout : 30,
                JwtExpirationHours = int.TryParse(settings.GetValueOrDefault("JwtExpirationHours", "24"), out var jwtExp) ? jwtExp : 24,
                MaxFailedLoginAttempts = int.TryParse(settings.GetValueOrDefault("MaxFailedLoginAttempts", "5"), out var maxAttempts) ? maxAttempts : 5
            };
        }

        /// <summary>
        /// Invalidate security configuration cache
        /// </summary>
        public void InvalidateCache()
        {
            _settingsProvider.InvalidateCache("Security");
            _logger.LogInformation("Security configuration cache invalidated");
        }
    }

    /// <summary>
    /// Security configuration object
    /// </summary>
    public class SecurityConfiguration
    {
        public int PasswordMinLength { get; set; }
        public int SessionTimeoutMinutes { get; set; }
        public int JwtExpirationHours { get; set; }
        public int MaxFailedLoginAttempts { get; set; }
    }
}

