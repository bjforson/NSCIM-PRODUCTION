using Microsoft.Extensions.Logging;
using NickScanWebApp.Shared.Models;

namespace NickScanWebApp.Shared.Services
{
    /// <summary>
    /// Frontend service for settings management
    /// ✅ Unified service for both desktop and mobile applications
    /// Uses shared ApiService for authentication
    /// </summary>
    public class SettingsService
    {
        public const string BasePath = "/api/Settings";
        public const string CategoriesPath = BasePath + "/categories";
        public const string UpdatePath = BasePath + "/update";
        public const string BulkUpdatePath = BasePath + "/bulk-update";
        public const string ValidatePath = BasePath + "/validate";
        public const string ExportPath = BasePath + "/export";
        public const string RecentChangesPath = BasePath + "/recent-changes";
        public const string AppSettingsSectionsPath = BasePath + "/appsettings/sections";
        public const string AppSettingsAllPath = BasePath + "/appsettings/all";

        private readonly ApiService _apiService;
        private readonly ILogger<SettingsService> _logger;

        public SettingsService(
            ApiService apiService,
            ILogger<SettingsService> logger)
        {
            _apiService = apiService;
            _logger = logger;
        }

        // Get all categories
        public async Task<List<CategorySettingsDto>> GetAllCategoriesAsync()
        {
            try
            {
                return await _apiService.GetAsync<List<CategorySettingsDto>>(CategoriesPath)
                    ?? new List<CategorySettingsDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting settings categories");
                throw;
            }
        }

        // Get category settings
        public async Task<CategorySettingsDto?> GetCategorySettingsAsync(string category)
        {
            try
            {
                return await _apiService.GetAsync<CategorySettingsDto>(BuildCategoryPath(category));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting settings for category {Category}", category);
                throw;
            }
        }

        // Update single setting
        public async Task<SystemSettingDto?> UpdateSettingAsync(UpdateSettingDto update)
        {
            try
            {
                return await _apiService.PutAsync<UpdateSettingDto, SystemSettingDto>(UpdatePath, update);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating setting {Category}.{Key}", update.Category, update.SettingKey);
                throw;
            }
        }

        // Bulk update category
        public async Task<List<SystemSettingDto>> BulkUpdateAsync(BulkSettingsUpdateDto bulkUpdate)
        {
            try
            {
                return await _apiService.PutAsync<BulkSettingsUpdateDto, List<SystemSettingDto>>(BulkUpdatePath, bulkUpdate)
                    ?? new List<SystemSettingDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error bulk updating category {Category}", bulkUpdate.Category);
                throw;
            }
        }

        // Validate setting
        public async Task<SettingsValidationResult> ValidateSettingAsync(string category, string key, string value)
        {
            try
            {
                var request = new { Category = category, Key = key, Value = value };
                return await _apiService.PostAsync<object, SettingsValidationResult>(ValidatePath, request)
                    ?? new SettingsValidationResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating setting {Category}.{Key}", category, key);
                throw;
            }
        }

        // Test connection
        public async Task<ConnectionTestResult> TestConnectionAsync(string category)
        {
            try
            {
                return await _apiService.PostAsync<object?, ConnectionTestResult>(BuildTestConnectionPath(category), null)
                    ?? new ConnectionTestResult();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing connection for {Category}", category);
                throw;
            }
        }

        // Reset to defaults
        public async Task<bool> ResetToDefaultsAsync(string category, string resetBy)
        {
            try
            {
                var request = new { ResetBy = resetBy };
                await _apiService.PostAsync<object, object>(BuildResetPath(category), request);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting category {Category}", category);
                throw;
            }
        }

        // Export settings
        public async Task<SettingsExportDto?> ExportSettingsAsync(string? category = null)
        {
            try
            {
                return await _apiService.GetAsync<SettingsExportDto>(BuildExportPath(category));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting settings");
                throw;
            }
        }

        // Get setting history
        public async Task<List<SettingsHistoryDto>> GetHistoryAsync(string category, string key, int limit = 50)
        {
            try
            {
                return await _apiService.GetAsync<List<SettingsHistoryDto>>(BuildHistoryPath(category, key, limit))
                    ?? new List<SettingsHistoryDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting history for {Category}.{Key}", category, key);
                throw;
            }
        }

        // Get recent changes
        public async Task<List<SettingsHistoryDto>> GetRecentChangesAsync(int limit = 100)
        {
            try
            {
                return await _apiService.GetAsync<List<SettingsHistoryDto>>(BuildRecentChangesPath(limit))
                    ?? new List<SettingsHistoryDto>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent changes");
                throw;
            }
        }

        // AppSettings.json methods
        public async Task<List<string>> GetAppSettingsSectionsAsync()
        {
            try
            {
                return await _apiService.GetAsync<List<string>>(AppSettingsSectionsPath)
                    ?? new List<string>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting appsettings sections");
                throw;
            }
        }

        public async Task<Dictionary<string, object>> GetAppSettingsSectionAsync(string section)
        {
            try
            {
                return await _apiService.GetAsync<Dictionary<string, object>>(BuildAppSettingsSectionPath(section))
                    ?? new Dictionary<string, object>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting appsettings section {Section}", section);
                throw;
            }
        }

        public async Task<AppSettingsDto> GetAllAppSettingsAsync()
        {
            try
            {
                return await _apiService.GetAsync<AppSettingsDto>(AppSettingsAllPath)
                    ?? new AppSettingsDto();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all appsettings");
                throw;
            }
        }

        public async Task<bool> UpdateAppSettingsSectionAsync(string section, Dictionary<string, object> settings, string changedBy)
        {
            try
            {
                var request = new UpdateAppSettingsRequest
                {
                    Settings = settings,
                    ChangedBy = changedBy
                };
                await _apiService.PutAsync<UpdateAppSettingsRequest, object>(
                    BuildAppSettingsSectionPath(section),
                    request);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating appsettings section {Section}", section);
                throw;
            }
        }

        public static string BuildCategoryPath(string category)
        {
            return $"{BasePath}/category/{Uri.EscapeDataString(category)}";
        }

        public static string BuildTestConnectionPath(string category)
        {
            return $"{BasePath}/test-connection/{Uri.EscapeDataString(category)}";
        }

        public static string BuildResetPath(string category)
        {
            return $"{BasePath}/reset/{Uri.EscapeDataString(category)}";
        }

        public static string BuildExportPath(string? category = null)
        {
            return string.IsNullOrEmpty(category)
                ? ExportPath
                : $"{ExportPath}?category={Uri.EscapeDataString(category)}";
        }

        public static string BuildHistoryPath(string category, string key, int limit = 50)
        {
            return $"{BasePath}/history/{Uri.EscapeDataString(category)}/{Uri.EscapeDataString(key)}?limit={limit}";
        }

        public static string BuildRecentChangesPath(int limit = 100)
        {
            return $"{RecentChangesPath}?limit={limit}";
        }

        public static string BuildAppSettingsSectionPath(string section)
        {
            return $"{BasePath}/appsettings/section/{Uri.EscapeDataString(section)}";
        }
    }
}

