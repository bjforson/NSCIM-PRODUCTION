using System.Text.Json;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.Settings;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Settings
{
    /// <summary>
    /// Service for managing system settings with encryption and validation
    /// </summary>
    public class SettingsService : ISettingsService
    {
        private readonly ISettingsRepository _repository;
        private readonly ILogger<SettingsService> _logger;
        private readonly ISettingsEncryptionService _encryptionService;
        private readonly ISettingsValidationService _validationService;
        private readonly ISettingsProvider _settingsProvider;

        public SettingsService(
            ISettingsRepository repository,
            ILogger<SettingsService> logger,
            ISettingsEncryptionService encryptionService,
            ISettingsValidationService validationService,
            ISettingsProvider settingsProvider)
        {
            _repository = repository;
            _logger = logger;
            _encryptionService = encryptionService;
            _validationService = validationService;
            _settingsProvider = settingsProvider;
        }

        public async Task<SystemSettingDto?> GetSettingAsync(string category, string key)
        {
            try
            {
                var setting = await _repository.GetSettingAsync(category, key);
                if (setting == null) return null;

                return await MapToDto(setting);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting setting {Category}.{Key}", category, key);
                throw;
            }
        }

        public async Task<List<SystemSettingDto>> GetSettingsByCategoryAsync(string category)
        {
            try
            {
                var settings = await _repository.GetSettingsByCategoryAsync(category);
                var dtos = new List<SystemSettingDto>();

                foreach (var setting in settings)
                {
                    dtos.Add(await MapToDto(setting));
                }

                return dtos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting settings for category {Category}", category);
                throw;
            }
        }

        public async Task<CategorySettingsDto?> GetCategorySettingsAsync(string category)
        {
            try
            {
                var settings = await GetSettingsByCategoryAsync(category);

                if (!settings.Any())
                    return null;

                return new CategorySettingsDto
                {
                    Category = category,
                    DisplayName = GetCategoryDisplayName(category),
                    Description = GetCategoryDescription(category),
                    Settings = settings,
                    RequiresRestart = settings.Any(s => s.RequiresRestart),
                    SettingCount = settings.Count
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting category settings for {Category}", category);
                throw;
            }
        }

        public async Task<List<CategorySettingsDto>> GetAllCategoriesAsync()
        {
            try
            {
                var categories = await _repository.GetCategoriesAsync();
                var categoryDtos = new List<CategorySettingsDto>();

                foreach (var category in categories)
                {
                    var categorySettings = await GetCategorySettingsAsync(category);
                    if (categorySettings != null)
                    {
                        categoryDtos.Add(categorySettings);
                    }
                }

                return categoryDtos;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all categories");
                throw;
            }
        }

        public async Task<SystemSettingDto> CreateSettingAsync(SystemSettingDto settingDto, string createdBy)
        {
            try
            {
                var setting = new SystemSetting
                {
                    Category = settingDto.Category,
                    SettingKey = settingDto.SettingKey,
                    SettingValue = settingDto.IsEncrypted
                        ? await _encryptionService.EncryptAsync(settingDto.SettingValue)
                        : settingDto.SettingValue,
                    DataType = settingDto.DataType,
                    Description = settingDto.Description,
                    DefaultValue = settingDto.DefaultValue,
                    IsEncrypted = settingDto.IsEncrypted,
                    RequiresRestart = settingDto.RequiresRestart,
                    AllowedRoles = settingDto.AllowedRoles,
                    IsActive = settingDto.IsActive,
                    DisplayOrder = settingDto.DisplayOrder,
                    ValidationRules = settingDto.ValidationRules,
                    LastModifiedBy = createdBy,
                    LastModifiedAt = DateTime.UtcNow
                };

                var created = await _repository.CreateSettingAsync(setting);
                _logger.LogInformation("Created setting {Category}.{Key} by {User}",
                    settingDto.Category, settingDto.SettingKey, createdBy);

                return await MapToDto(created);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating setting {Category}.{Key}", settingDto.Category, settingDto.SettingKey);
                throw;
            }
        }

        public async Task<SystemSettingDto> UpdateSettingAsync(UpdateSettingDto update, string? ipAddress = null)
        {
            try
            {
                var setting = await _repository.GetSettingAsync(update.Category, update.SettingKey);
                if (setting == null)
                {
                    throw new InvalidOperationException($"Setting {update.Category}.{update.SettingKey} not found");
                }

                // Validate the new value
                var validation = await ValidateSettingAsync(update.Category, update.SettingKey, update.SettingValue);
                if (!validation.IsValid)
                {
                    throw new InvalidOperationException($"Validation failed: {string.Join(", ", validation.Errors)}");
                }

                // Record history before updating
                var history = new SettingsHistory
                {
                    SystemSettingId = setting.Id,
                    Category = setting.Category,
                    SettingKey = setting.SettingKey,
                    OldValue = setting.IsEncrypted ? "[ENCRYPTED]" : setting.SettingValue,
                    NewValue = setting.IsEncrypted ? "[ENCRYPTED]" : update.SettingValue,
                    ChangedBy = update.ChangedBy,
                    Reason = update.Reason,
                    IpAddress = ipAddress,
                    ChangedAt = DateTime.UtcNow
                };

                await _repository.AddHistoryAsync(history);

                // Update the setting
                setting.SettingValue = setting.IsEncrypted
                    ? await _encryptionService.EncryptAsync(update.SettingValue)
                    : update.SettingValue;
                setting.LastModifiedBy = update.ChangedBy;
                setting.LastModifiedAt = DateTime.UtcNow;

                var updated = await _repository.UpdateSettingAsync(setting);

                // ✅ Invalidate cache to ensure live data is returned
                _settingsProvider.InvalidateCache(update.Category, update.SettingKey);

                _logger.LogInformation("Updated setting {Category}.{Key} by {User}",
                    update.Category, update.SettingKey, update.ChangedBy);

                return await MapToDto(updated);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating setting {Category}.{Key}", update.Category, update.SettingKey);
                throw;
            }
        }

        public async Task<bool> DeleteSettingAsync(string category, string key, string deletedBy)
        {
            try
            {
                var setting = await _repository.GetSettingAsync(category, key);
                if (setting == null) return false;

                // Record deletion in history
                var history = new SettingsHistory
                {
                    SystemSettingId = setting.Id,
                    Category = category,
                    SettingKey = key,
                    OldValue = setting.SettingValue,
                    NewValue = "[DELETED]",
                    ChangedBy = deletedBy,
                    ChangedAt = DateTime.UtcNow
                };

                await _repository.AddHistoryAsync(history);

                var deleted = await _repository.DeleteSettingAsync(setting.Id);

                _logger.LogInformation("Deleted setting {Category}.{Key} by {User}", category, key, deletedBy);

                return deleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting setting {Category}.{Key}", category, key);
                throw;
            }
        }

        public async Task<List<SystemSettingDto>> BulkUpdateSettingsAsync(BulkSettingsUpdateDto bulkUpdate, string? ipAddress = null)
        {
            try
            {
                _logger.LogInformation("Bulk updating {Count} settings in category {Category} by {User}",
                    bulkUpdate.Settings.Count, bulkUpdate.Category, bulkUpdate.ChangedBy);

                var updatedSettings = new List<SystemSettingDto>();

                foreach (var kvp in bulkUpdate.Settings)
                {
                    var update = new UpdateSettingDto
                    {
                        Category = bulkUpdate.Category,
                        SettingKey = kvp.Key,
                        SettingValue = kvp.Value,
                        Reason = bulkUpdate.Reason,
                        ChangedBy = bulkUpdate.ChangedBy
                    };

                    var updated = await UpdateSettingAsync(update, ipAddress);
                    updatedSettings.Add(updated);
                }

                // ✅ Invalidate entire category cache after bulk update
                _settingsProvider.InvalidateCache(bulkUpdate.Category);

                return updatedSettings;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk update for category {Category}", bulkUpdate.Category);
                throw;
            }
        }

        // Settings History
        public async Task<List<SettingsHistoryDto>> GetSettingHistoryAsync(string category, string key, int limit = 50)
        {
            try
            {
                var setting = await _repository.GetSettingAsync(category, key);
                if (setting == null) return new List<SettingsHistoryDto>();

                var history = await _repository.GetHistoryAsync(setting.Id, limit);
                return history.Select(h => new SettingsHistoryDto
                {
                    Id = h.Id,
                    Category = h.Category,
                    SettingKey = h.SettingKey,
                    OldValue = h.OldValue,
                    NewValue = h.NewValue,
                    ChangedBy = h.ChangedBy,
                    Reason = h.Reason,
                    IpAddress = h.IpAddress,
                    ChangedAt = h.ChangedAt
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting history for {Category}.{Key}", category, key);
                throw;
            }
        }

        public async Task<List<SettingsHistoryDto>> GetRecentChangesAsync(int limit = 100)
        {
            try
            {
                var history = await _repository.GetRecentHistoryAsync(limit);
                return history.Select(h => new SettingsHistoryDto
                {
                    Id = h.Id,
                    Category = h.Category,
                    SettingKey = h.SettingKey,
                    OldValue = h.OldValue,
                    NewValue = h.NewValue,
                    ChangedBy = h.ChangedBy,
                    Reason = h.Reason,
                    IpAddress = h.IpAddress,
                    ChangedAt = h.ChangedAt
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent changes");
                throw;
            }
        }

        // User Preferences
        public async Task<UserPreferenceDto?> GetUserPreferenceAsync(int userId, string key)
        {
            try
            {
                var preference = await _repository.GetUserPreferenceAsync(userId, key);
                if (preference == null) return null;

                return new UserPreferenceDto
                {
                    Id = preference.Id,
                    UserId = preference.UserId,
                    PreferenceKey = preference.PreferenceKey,
                    PreferenceValue = preference.PreferenceValue,
                    DataType = preference.DataType,
                    Description = preference.Description,
                    UpdatedAt = preference.UpdatedAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user preference {Key} for user {UserId}", key, userId);
                throw;
            }
        }

        public async Task<List<UserPreferenceDto>> GetAllUserPreferencesAsync(int userId)
        {
            try
            {
                var preferences = await _repository.GetAllUserPreferencesAsync(userId);
                return preferences.Select(p => new UserPreferenceDto
                {
                    Id = p.Id,
                    UserId = p.UserId,
                    PreferenceKey = p.PreferenceKey,
                    PreferenceValue = p.PreferenceValue,
                    DataType = p.DataType,
                    Description = p.Description,
                    UpdatedAt = p.UpdatedAt
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all preferences for user {UserId}", userId);
                throw;
            }
        }

        public async Task<UserPreferenceDto> SetUserPreferenceAsync(int userId, string key, string value, string dataType = "string")
        {
            try
            {
                var preference = new UserPreference
                {
                    UserId = userId,
                    PreferenceKey = key,
                    PreferenceValue = value,
                    DataType = dataType,
                    UpdatedAt = DateTime.UtcNow
                };

                var saved = await _repository.SetUserPreferenceAsync(preference);

                _logger.LogInformation("Set user preference {Key} for user {UserId}", key, userId);

                return new UserPreferenceDto
                {
                    Id = saved.Id,
                    UserId = saved.UserId,
                    PreferenceKey = saved.PreferenceKey,
                    PreferenceValue = saved.PreferenceValue,
                    DataType = saved.DataType,
                    Description = saved.Description,
                    UpdatedAt = saved.UpdatedAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting user preference {Key} for user {UserId}", key, userId);
                throw;
            }
        }

        public async Task<bool> DeleteUserPreferenceAsync(int userId, string key)
        {
            try
            {
                var deleted = await _repository.DeleteUserPreferenceAsync(userId, key);
                _logger.LogInformation("Deleted user preference {Key} for user {UserId}", key, userId);
                return deleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting user preference {Key} for user {UserId}", key, userId);
                throw;
            }
        }

        // Validation
        public async Task<SettingsValidationResult> ValidateSettingAsync(string category, string key, string value)
        {
            return await _validationService.ValidateSettingAsync(category, key, value);
        }

        public async Task<SettingsValidationResult> ValidateCategorySettingsAsync(Dictionary<string, string> settings, string category)
        {
            return await _validationService.ValidateCategorySettingsAsync(settings, category);
        }

        // Encryption
        public async Task<string> EncryptValueAsync(string value)
        {
            return await _encryptionService.EncryptAsync(value);
        }

        public async Task<string> DecryptValueAsync(string encryptedValue)
        {
            return await _encryptionService.DecryptAsync(encryptedValue);
        }

        // Import/Export
        public async Task<SettingsExportDto> ExportSettingsAsync(string? category = null)
        {
            try
            {
                _logger.LogInformation("Exporting settings - Category: {Category}", category ?? "All");

                var settings = string.IsNullOrEmpty(category)
                    ? await _repository.GetAllSettingsAsync()
                    : await _repository.GetSettingsByCategoryAsync(category);

                var export = new SettingsExportDto
                {
                    ExportedAt = DateTime.UtcNow.ToString("o"),
                    ExportedBy = "System", // TODO: Get current user
                    Version = "1.0"
                };

                foreach (var setting in settings)
                {
                    if (!export.Settings.ContainsKey(setting.Category))
                    {
                        export.Settings[setting.Category] = new Dictionary<string, string>();
                    }

                    var value = setting.IsEncrypted
                        ? await _encryptionService.DecryptAsync(setting.SettingValue)
                        : setting.SettingValue;

                    export.Settings[setting.Category][setting.SettingKey] = value;
                }

                _logger.LogInformation("Exported {Count} settings across {Categories} categories",
                    settings.Count, export.Settings.Keys.Count);

                return export;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting settings");
                throw;
            }
        }

        public async Task<bool> ImportSettingsAsync(SettingsExportDto export, string importedBy, bool overwriteExisting = false)
        {
            try
            {
                _logger.LogInformation("Importing settings by {User} - Overwrite: {Overwrite}", importedBy, overwriteExisting);

                var importedCount = 0;
                var skippedCount = 0;

                foreach (var category in export.Settings)
                {
                    foreach (var setting in category.Value)
                    {
                        var exists = await _repository.SettingExistsAsync(category.Key, setting.Key);

                        if (exists && !overwriteExisting)
                        {
                            skippedCount++;
                            continue;
                        }

                        var update = new UpdateSettingDto
                        {
                            Category = category.Key,
                            SettingKey = setting.Key,
                            SettingValue = setting.Value,
                            Reason = $"Imported from backup by {importedBy}",
                            ChangedBy = importedBy
                        };

                        if (exists)
                        {
                            await UpdateSettingAsync(update);
                        }
                        else
                        {
                            // Create new setting if doesn't exist
                            var existingSetting = await _repository.GetSettingAsync(category.Key, setting.Key);
                            if (existingSetting != null)
                            {
                                await UpdateSettingAsync(update);
                            }
                        }

                        importedCount++;
                    }
                }

                _logger.LogInformation("Import completed - Imported: {Imported}, Skipped: {Skipped}",
                    importedCount, skippedCount);

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing settings");
                throw;
            }
        }

        // Utilities
        public async Task<bool> ResetToDefaultsAsync(string category, string resetBy)
        {
            try
            {
                _logger.LogInformation("Resetting category {Category} to defaults by {User}", category, resetBy);

                var settings = await _repository.GetSettingsByCategoryAsync(category);
                var resetCount = 0;

                foreach (var setting in settings)
                {
                    if (!string.IsNullOrEmpty(setting.DefaultValue))
                    {
                        var update = new UpdateSettingDto
                        {
                            Category = category,
                            SettingKey = setting.SettingKey,
                            SettingValue = setting.DefaultValue,
                            Reason = $"Reset to default by {resetBy}",
                            ChangedBy = resetBy
                        };

                        await UpdateSettingAsync(update);
                        resetCount++;
                    }
                }

                _logger.LogInformation("Reset {Count} settings in category {Category}", resetCount, category);
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting category {Category} to defaults", category);
                throw;
            }
        }

        public async Task<Dictionary<string, object>> GetSettingsAsConfigurationAsync(string category)
        {
            try
            {
                var settings = await _repository.GetSettingsByCategoryAsync(category);
                var config = new Dictionary<string, object>();

                foreach (var setting in settings)
                {
                    var value = setting.IsEncrypted
                        ? await _encryptionService.DecryptAsync(setting.SettingValue)
                        : setting.SettingValue;

                    config[setting.SettingKey] = ParseValue(value, setting.DataType);
                }

                return config;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting settings as configuration for {Category}", category);
                throw;
            }
        }

        public async Task<ConnectionTestResult> TestConnectionAsync(string category)
        {
            // This will be implemented with category-specific logic
            return await _validationService.TestConnectionAsync(category);
        }

        public async Task ClearCacheAsync()
        {
            // Clear any cached settings
            _settingsProvider.ClearCache();
            _logger.LogInformation("Cleared all settings cache");
            await Task.CompletedTask;
        }

        // Helper methods
        private async Task<SystemSettingDto> MapToDto(SystemSetting setting)
        {
            var value = setting.IsEncrypted
                ? await _encryptionService.DecryptAsync(setting.SettingValue)
                : setting.SettingValue;

            return new SystemSettingDto
            {
                Id = setting.Id,
                Category = setting.Category,
                SettingKey = setting.SettingKey,
                SettingValue = value,
                DataType = setting.DataType,
                Description = setting.Description,
                DefaultValue = setting.DefaultValue,
                IsEncrypted = setting.IsEncrypted,
                RequiresRestart = setting.RequiresRestart,
                AllowedRoles = setting.AllowedRoles,
                IsActive = setting.IsActive,
                DisplayOrder = setting.DisplayOrder,
                ValidationRules = setting.ValidationRules,
                LastModifiedBy = setting.LastModifiedBy,
                LastModifiedAt = setting.LastModifiedAt
            };
        }

        private object ParseValue(string value, string dataType)
        {
            return dataType.ToLower() switch
            {
                "int" => int.TryParse(value, out var intVal) ? intVal : 0,
                "bool" => bool.TryParse(value, out var boolVal) && boolVal,
                "decimal" => decimal.TryParse(value, out var decVal) ? decVal : 0m,
                "json" => JsonSerializer.Deserialize<object>(value) ?? new object(),
                "array" => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries),
                _ => value
            };
        }

        private string GetCategoryDisplayName(string category)
        {
            return category switch
            {
                "General" => "General Settings",
                "Security" => "Security & Authentication",
                "ICUMS" => "ICUMS Integration",
                "Scanners" => "Scanner Configuration",
                "Email" => "Email & Notifications",
                "DataQuality" => "Data Quality & Validation",
                "Performance" => "Performance & Optimization",
                "Backup" => "Backup & Recovery",
                "Monitoring" => "Monitoring & Logging",
                "UserPreferences" => "User Preferences",
                "Advanced" => "Advanced Settings",
                _ => category
            };
        }

        private string? GetCategoryDescription(string category)
        {
            return category switch
            {
                "General" => "System-wide general configurations",
                "Security" => "Authentication, authorization, and security policies",
                "ICUMS" => "ICUMS API integration and synchronization settings",
                "Scanners" => "Scanner integration configurations (FS6000, ASE, Heimann)",
                "Email" => "Email server and notification settings",
                "DataQuality" => "CMR validation and data quality management",
                "Performance" => "Performance tuning and optimization settings",
                "Backup" => "Backup schedules and recovery options",
                "Monitoring" => "Logging, monitoring, and health check settings",
                "UserPreferences" => "Per-user customization options",
                "Advanced" => "Advanced developer and system settings",
                _ => null
            };
        }
    }
}

