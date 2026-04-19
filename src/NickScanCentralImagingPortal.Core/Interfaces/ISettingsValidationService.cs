using NickScanCentralImagingPortal.Core.DTOs.Settings;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for validating settings values
    /// </summary>
    public interface ISettingsValidationService
    {
        /// <summary>
        /// Validate a single setting value
        /// </summary>
        Task<SettingsValidationResult> ValidateSettingAsync(string category, string key, string value);

        /// <summary>
        /// Validate multiple settings in a category
        /// </summary>
        Task<SettingsValidationResult> ValidateCategorySettingsAsync(Dictionary<string, string> settings, string category);

        /// <summary>
        /// Test connection for a specific category (ICUMS, Email, etc.)
        /// </summary>
        Task<ConnectionTestResult> TestConnectionAsync(string category);
    }
}

