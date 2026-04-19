using NickScanCentralImagingPortal.Core.DTOs.Settings;
using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Service for managing system settings and user preferences
    /// </summary>
    public interface ISettingsService
    {
        // System Settings Operations
        Task<SystemSettingDto?> GetSettingAsync(string category, string key);
        Task<List<SystemSettingDto>> GetSettingsByCategoryAsync(string category);
        Task<CategorySettingsDto?> GetCategorySettingsAsync(string category);
        Task<List<CategorySettingsDto>> GetAllCategoriesAsync();
        Task<SystemSettingDto> CreateSettingAsync(SystemSettingDto setting, string createdBy);
        Task<SystemSettingDto> UpdateSettingAsync(UpdateSettingDto update, string? ipAddress = null);
        Task<bool> DeleteSettingAsync(string category, string key, string deletedBy);
        Task<List<SystemSettingDto>> BulkUpdateSettingsAsync(BulkSettingsUpdateDto bulkUpdate, string? ipAddress = null);

        // Settings History
        Task<List<SettingsHistoryDto>> GetSettingHistoryAsync(string category, string key, int limit = 50);
        Task<List<SettingsHistoryDto>> GetRecentChangesAsync(int limit = 100);

        // User Preferences
        Task<UserPreferenceDto?> GetUserPreferenceAsync(int userId, string key);
        Task<List<UserPreferenceDto>> GetAllUserPreferencesAsync(int userId);
        Task<UserPreferenceDto> SetUserPreferenceAsync(int userId, string key, string value, string dataType = "string");
        Task<bool> DeleteUserPreferenceAsync(int userId, string key);

        // Validation
        Task<SettingsValidationResult> ValidateSettingAsync(string category, string key, string value);
        Task<SettingsValidationResult> ValidateCategorySettingsAsync(Dictionary<string, string> settings, string category);

        // Encryption
        Task<string> EncryptValueAsync(string value);
        Task<string> DecryptValueAsync(string encryptedValue);

        // Import/Export
        Task<SettingsExportDto> ExportSettingsAsync(string? category = null);
        Task<bool> ImportSettingsAsync(SettingsExportDto export, string importedBy, bool overwriteExisting = false);

        // Utilities
        Task<bool> ResetToDefaultsAsync(string category, string resetBy);
        Task<Dictionary<string, object>> GetSettingsAsConfigurationAsync(string category);
        Task<ConnectionTestResult> TestConnectionAsync(string category);
        Task ClearCacheAsync();
    }
}

