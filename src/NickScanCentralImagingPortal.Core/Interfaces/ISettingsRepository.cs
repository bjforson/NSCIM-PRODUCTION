using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Repository for settings data access
    /// </summary>
    public interface ISettingsRepository
    {
        // System Settings CRUD
        Task<SystemSetting?> GetSettingAsync(string category, string key);
        Task<List<SystemSetting>> GetSettingsByCategoryAsync(string category);
        Task<List<SystemSetting>> GetAllSettingsAsync();
        Task<List<string>> GetCategoriesAsync();
        Task<SystemSetting> CreateSettingAsync(SystemSetting setting);
        Task<SystemSetting> UpdateSettingAsync(SystemSetting setting);
        Task<bool> DeleteSettingAsync(int id);
        Task<int> BulkUpdateSettingsAsync(List<SystemSetting> settings);

        // Settings History
        Task<SettingsHistory> AddHistoryAsync(SettingsHistory history);
        Task<List<SettingsHistory>> GetHistoryAsync(int systemSettingId, int limit = 50);
        Task<List<SettingsHistory>> GetRecentHistoryAsync(int limit = 100);

        // User Preferences
        Task<UserPreference?> GetUserPreferenceAsync(int userId, string key);
        Task<List<UserPreference>> GetAllUserPreferencesAsync(int userId);
        Task<UserPreference> SetUserPreferenceAsync(UserPreference preference);
        Task<bool> DeleteUserPreferenceAsync(int userId, string key);

        // Utilities
        Task<bool> SettingExistsAsync(string category, string key);
        Task<int> GetSettingsCountByCategoryAsync(string category);
    }
}

