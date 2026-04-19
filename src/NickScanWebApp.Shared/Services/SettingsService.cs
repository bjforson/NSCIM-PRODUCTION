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
                return await _apiService.GetAsync<List<CategorySettingsDto>>("/api/Settings/categories")
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
                return await _apiService.GetAsync<CategorySettingsDto>($"/api/Settings/category/{category}");
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
                return await _apiService.PutAsync<UpdateSettingDto, SystemSettingDto>("/api/Settings/update", update);
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
                return await _apiService.PutAsync<BulkSettingsUpdateDto, List<SystemSettingDto>>("/api/Settings/bulk-update", bulkUpdate)
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
                return await _apiService.PostAsync<object, SettingsValidationResult>("/api/Settings/validate", request)
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
                return await _apiService.PostAsync<object?, ConnectionTestResult>($"/api/Settings/test-connection/{category}", null)
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
                await _apiService.PostAsync<object, object>($"/api/Settings/reset/{category}", request);
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
                var endpoint = string.IsNullOrEmpty(category)
                    ? "/api/Settings/export"
                    : $"/api/Settings/export?category={category}";

                return await _apiService.GetAsync<SettingsExportDto>(endpoint);
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
                return await _apiService.GetAsync<List<SettingsHistoryDto>>($"/api/Settings/history/{category}/{key}?limit={limit}")
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
                return await _apiService.GetAsync<List<SettingsHistoryDto>>($"/api/Settings/recent-changes?limit={limit}")
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
                return await _apiService.GetAsync<List<string>>("/api/Settings/appsettings/sections")
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
                return await _apiService.GetAsync<Dictionary<string, object>>($"/api/Settings/appsettings/section/{section}")
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
                return await _apiService.GetAsync<AppSettingsDto>("/api/Settings/appsettings/all")
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
                await _apiService.PutAsync<UpdateAppSettingsRequest, object>($"/api/Settings/appsettings/section/{section}", request);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating appsettings section {Section}", section);
                throw;
            }
        }
    }
}

