using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using NickScanCentralImagingPortal.API.Authorization;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Core.DTOs.Settings;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// API controller for system settings management
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [HasPermission(Permissions.ControllersSystemSettings)]
    public class SettingsController : ControllerBase
    {
        private const string RedactedValue = "***REDACTED***";

        private static readonly string[] SecretPathTokens =
        {
            "password",
            "pwd",
            "secret",
            "apikey",
            "serviceapikey",
            "accesstoken",
            "refreshtoken",
            "token",
            "connectionstring",
            "connectionstrings",
            "privatekey",
            "clientsecret",
            "sharedaccesskey",
            "credential",
            "certificate",
            "jwtsecret"
        };

        private readonly ISettingsService _settingsService;
        private readonly ILogger<SettingsController> _logger;

        public SettingsController(ISettingsService settingsService, ILogger<SettingsController> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        /// <summary>
        /// Get all settings categories
        /// </summary>
        [HttpGet("categories")]
        public async Task<ActionResult<List<CategorySettingsDto>>> GetCategories()
        {
            try
            {
                var categories = await _settingsService.GetAllCategoriesAsync();
                return Ok(categories.Select(RedactCategory).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting categories");
                return StatusCode(500, "Failed to retrieve categories");
            }
        }

        /// <summary>
        /// Get settings by category
        /// </summary>
        [HttpGet("category/{category}")]
        public async Task<ActionResult<CategorySettingsDto>> GetCategorySettings(string category)
        {
            try
            {
                var settings = await _settingsService.GetCategorySettingsAsync(category);
                if (settings == null)
                    return NotFound($"Category '{category}' not found");

                return Ok(RedactCategory(settings));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting settings for category {Category}", category);
                return StatusCode(500, "Failed to retrieve category settings");
            }
        }

        /// <summary>
        /// Get a specific setting
        /// </summary>
        [HttpGet("{category}/{key}")]
        public async Task<ActionResult<SystemSettingDto>> GetSetting(string category, string key)
        {
            try
            {
                var setting = await _settingsService.GetSettingAsync(category, key);
                if (setting == null)
                    return NotFound($"Setting '{category}.{key}' not found");

                return Ok(RedactSetting(setting));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting setting {Category}.{Key}", category, key);
                return StatusCode(500, "Failed to retrieve setting");
            }
        }

        /// <summary>
        /// Update a setting
        /// </summary>
        [HttpPut("update")]
        public async Task<ActionResult<SystemSettingDto>> UpdateSetting([FromBody] UpdateSettingDto update)
        {
            try
            {
                if (IsRedactedPlaceholder(update.SettingValue))
                {
                    var existing = await _settingsService.GetSettingAsync(update.Category, update.SettingKey);
                    if (existing != null && ShouldRedactSetting(existing))
                    {
                        _logger.LogInformation(
                            "Skipped unchanged redacted setting {Category}.{Key} posted by {User}",
                            update.Category,
                            update.SettingKey,
                            update.ChangedBy);
                        return Ok(RedactSetting(existing));
                    }

                    return BadRequest(new { Error = "Redacted placeholders cannot be saved as setting values." });
                }

                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var updated = await _settingsService.UpdateSettingAsync(update, ipAddress);

                _logger.LogInformation("Setting {Category}.{Key} updated by {User}",
                    update.Category, update.SettingKey, update.ChangedBy);

                return Ok(RedactSetting(updated));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating setting {Category}.{Key}", update.Category, update.SettingKey);
                return StatusCode(500, new { Error = "Failed to update setting", Details = ex.Message });
            }
        }

        /// <summary>
        /// Bulk update settings for a category
        /// </summary>
        [HttpPut("bulk-update")]
        public async Task<ActionResult<List<SystemSettingDto>>> BulkUpdate([FromBody] BulkSettingsUpdateDto bulkUpdate)
        {
            try
            {
                var settingsToUpdate = new Dictionary<string, string>();
                var skippedRedactedSettings = new List<SystemSettingDto>();

                foreach (var setting in bulkUpdate.Settings)
                {
                    if (!IsRedactedPlaceholder(setting.Value))
                    {
                        settingsToUpdate[setting.Key] = setting.Value;
                        continue;
                    }

                    var existing = await _settingsService.GetSettingAsync(bulkUpdate.Category, setting.Key);
                    if (existing != null && ShouldRedactSetting(existing))
                    {
                        skippedRedactedSettings.Add(RedactSetting(existing));
                        continue;
                    }

                    return BadRequest(new
                    {
                        Error = "Redacted placeholders cannot be saved as setting values.",
                        SettingKey = setting.Key
                    });
                }

                var ipAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                var updated = new List<SystemSettingDto>();

                if (settingsToUpdate.Count > 0)
                {
                    var sanitizedBulkUpdate = new BulkSettingsUpdateDto
                    {
                        Category = bulkUpdate.Category,
                        Settings = settingsToUpdate,
                        Reason = bulkUpdate.Reason,
                        ChangedBy = bulkUpdate.ChangedBy
                    };

                    updated = await _settingsService.BulkUpdateSettingsAsync(sanitizedBulkUpdate, ipAddress);
                }

                _logger.LogInformation("Bulk updated {Count} settings in category {Category} by {User}",
                    settingsToUpdate.Count, bulkUpdate.Category, bulkUpdate.ChangedBy);

                return Ok(updated.Select(RedactSetting).Concat(skippedRedactedSettings).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during bulk update for category {Category}", bulkUpdate.Category);
                return StatusCode(500, new { Error = "Failed to bulk update settings", Details = ex.Message });
            }
        }

        /// <summary>
        /// Get settings history
        /// </summary>
        [HttpGet("history/{category}/{key}")]
        public async Task<ActionResult<List<SettingsHistoryDto>>> GetHistory(string category, string key, [FromQuery] int limit = 50)
        {
            try
            {
                var history = await _settingsService.GetSettingHistoryAsync(category, key, limit);
                return Ok(history.Select(RedactHistory).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting history for {Category}.{Key}", category, key);
                return StatusCode(500, "Failed to retrieve history");
            }
        }

        /// <summary>
        /// Get recent settings changes across all categories
        /// </summary>
        [HttpGet("recent-changes")]
        public async Task<ActionResult<List<SettingsHistoryDto>>> GetRecentChanges([FromQuery] int limit = 100)
        {
            try
            {
                var changes = await _settingsService.GetRecentChangesAsync(limit);
                return Ok(changes.Select(RedactHistory).ToList());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting recent changes");
                return StatusCode(500, "Failed to retrieve recent changes");
            }
        }

        /// <summary>
        /// Validate a setting value
        /// </summary>
        [HttpPost("validate")]
        public async Task<ActionResult<SettingsValidationResult>> ValidateSetting([FromBody] ValidateSettingRequest request)
        {
            try
            {
                var result = await _settingsService.ValidateSettingAsync(request.Category, request.Key, request.Value);
                RedactValidationResult(result, request.Category);
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating setting");
                return StatusCode(500, "Failed to validate setting");
            }
        }

        /// <summary>
        /// Test connection for a category
        /// </summary>
        [HttpPost("test-connection/{category}")]
        public async Task<ActionResult<ConnectionTestResult>> TestConnection(string category)
        {
            try
            {
                var result = await _settingsService.TestConnectionAsync(category);
                return Ok(RedactConnectionTestResult(category, result));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing connection for {Category}", category);
                return StatusCode(500, "Failed to test connection");
            }
        }

        /// <summary>
        /// Reset category to defaults
        /// </summary>
        [HttpPost("reset/{category}")]
        public async Task<ActionResult> ResetToDefaults(string category, [FromBody] ResetRequest request)
        {
            try
            {
                var success = await _settingsService.ResetToDefaultsAsync(category, request.ResetBy);
                return Ok(new { Success = success, Message = $"Category '{category}' reset to defaults" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting category {Category}", category);
                return StatusCode(500, "Failed to reset to defaults");
            }
        }

        /// <summary>
        /// Export settings
        /// </summary>
        [HttpGet("export")]
        public async Task<ActionResult<SettingsExportDto>> ExportSettings([FromQuery] string? category = null)
        {
            try
            {
                var export = await _settingsService.ExportSettingsAsync(category);
                return Ok(RedactExport(export));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting settings");
                return StatusCode(500, "Failed to export settings");
            }
        }

        /// <summary>
        /// Import settings
        /// </summary>
        [HttpPost("import")]
        public async Task<ActionResult> ImportSettings([FromBody] ImportSettingsRequest request)
        {
            try
            {
                var sanitizedExport = RemoveRedactedExportValues(request.Export);
                var success = await _settingsService.ImportSettingsAsync(
                    sanitizedExport,
                    request.ImportedBy,
                    request.OverwriteExisting);

                return Ok(new { Success = success, Message = "Settings imported successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error importing settings");
                return StatusCode(500, new { Error = "Failed to import settings", Details = ex.Message });
            }
        }

        /// <summary>
        /// Clear settings cache
        /// </summary>
        [HttpPost("clear-cache")]
        public async Task<ActionResult> ClearCache()
        {
            try
            {
                await _settingsService.ClearCacheAsync();
                return Ok(new { Message = "Cache cleared successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing cache");
                return StatusCode(500, "Failed to clear cache");
            }
        }

        /// <summary>
        /// Seed initial settings (for first-time setup)
        /// </summary>
        [HttpPost("seed")]
        [HasPermission(Permissions.SystemSettingsEdit)]
        public async Task<ActionResult> SeedSettings()
        {
            try
            {
                _logger.LogInformation("🌱 Starting settings seed process...");

                // Get the ApplicationDbContext from the service provider
                var dbContext = HttpContext.RequestServices.GetRequiredService<NickScanCentralImagingPortal.Infrastructure.Data.ApplicationDbContext>();

                // Create seeder instance
                var seeder = new NickScanCentralImagingPortal.Infrastructure.Data.SettingsSeeder(dbContext);

                // Check current settings count
                var existingCategories = await _settingsService.GetAllCategoriesAsync();
                var beforeCount = existingCategories.Sum(c => c.Settings.Count);

                _logger.LogInformation("Settings before seeding: {Count}", beforeCount);

                // Seed all settings (default + extended + comprehensive)
                await seeder.SeedAllSettingsAsync();

                // Get updated count
                var updatedCategories = await _settingsService.GetAllCategoriesAsync();
                var afterCount = updatedCategories.Sum(c => c.Settings.Count);
                var newSettings = afterCount - beforeCount;

                _logger.LogInformation("✅ Settings seeded successfully! Before: {Before}, After: {After}, New: {New}",
                    beforeCount, afterCount, newSettings);

                return Ok(new
                {
                    Message = "Settings seeded successfully",
                    BeforeCount = beforeCount,
                    AfterCount = afterCount,
                    NewSettingsAdded = newSettings,
                    Categories = updatedCategories.Select(c => c.Category).OrderBy(c => c).ToList()
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error seeding settings");
                return StatusCode(500, new { Error = "Failed to seed settings", Details = ex.Message });
            }
        }

        // User Preferences Endpoints
        [HttpGet("preferences/{userId}")]
        public async Task<ActionResult<List<UserPreferenceDto>>> GetUserPreferences(int userId)
        {
            try
            {
                var preferences = await _settingsService.GetAllUserPreferencesAsync(userId);
                return Ok(preferences);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting preferences for user {UserId}", userId);
                return StatusCode(500, "Failed to retrieve user preferences");
            }
        }

        [HttpPut("preferences/{userId}/{key}")]
        public async Task<ActionResult<UserPreferenceDto>> SetUserPreference(
            int userId,
            string key,
            [FromBody] SetPreferenceRequest request)
        {
            try
            {
                var preference = await _settingsService.SetUserPreferenceAsync(
                    userId,
                    key,
                    request.Value,
                    request.DataType);

                return Ok(preference);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting preference {Key} for user {UserId}", key, userId);
                return StatusCode(500, "Failed to set user preference");
            }
        }

        /// <summary>
        /// Get all appsettings.json sections
        /// </summary>
        [HttpGet("appsettings/sections")]
        public ActionResult<List<string>> GetAppSettingsSections()
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var sections = GetTopLevelSections(configuration);
                return Ok(sections);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting appsettings sections");
                return StatusCode(500, "Failed to retrieve appsettings sections");
            }
        }

        /// <summary>
        /// Get a specific appsettings.json section
        /// </summary>
        [HttpGet("appsettings/section/{section}")]
        public ActionResult<Dictionary<string, object>> GetAppSettingsSection(string section)
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var sectionData = GetSectionData(configuration, section);
                return Ok(sectionData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting appsettings section {Section}", section);
                return StatusCode(500, $"Failed to retrieve section '{section}'");
            }
        }

        /// <summary>
        /// Update appsettings.json section (writes back to file)
        /// </summary>
        [HttpPut("appsettings/section/{section}")]
        [HasPermission(Permissions.SystemSettingsEdit)]
        public async Task<ActionResult> UpdateAppSettingsSection(string section, [FromBody] UpdateAppSettingsRequest request)
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var appSettingsPath = GetAppSettingsPath();

                if (string.IsNullOrEmpty(appSettingsPath) || !System.IO.File.Exists(appSettingsPath))
                {
                    return StatusCode(500, "appsettings.json file not found");
                }

                await UpdateAppSettingsFileAsync(appSettingsPath, section, request.Settings);

                _logger.LogInformation("AppSettings section {Section} updated by {User}", section, request.ChangedBy);

                return Ok(new { Message = $"Section '{section}' updated successfully. Application restart required." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating appsettings section {Section}", section);
                return StatusCode(500, new { Error = "Failed to update section", Details = ex.Message });
            }
        }

        /// <summary>
        /// Get all appsettings.json configuration as a flat key-value list
        /// </summary>
        [HttpGet("appsettings/all")]
        public ActionResult<AppSettingsDto> GetAllAppSettings()
        {
            try
            {
                var configuration = HttpContext.RequestServices.GetRequiredService<IConfiguration>();
                var allSettings = GetAllSettings(configuration);
                return Ok(allSettings);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting all appsettings");
                return StatusCode(500, "Failed to retrieve appsettings");
            }
        }

        #region Helper Methods for AppSettings

        private List<string> GetTopLevelSections(IConfiguration configuration)
        {
            var sections = new List<string>();
            foreach (var child in configuration.GetChildren())
            {
                sections.Add(child.Key);
            }
            return sections.OrderBy(s => s).ToList();
        }

        private Dictionary<string, object> GetSectionData(IConfiguration configuration, string sectionName)
        {
            var section = configuration.GetSection(sectionName);
            var data = new Dictionary<string, object>();

            if (!section.Exists())
            {
                return data;
            }

            foreach (var child in section.GetChildren())
            {
                if (child.Value == null)
                {
                    // Nested section
                    var nested = GetSectionData(configuration, $"{sectionName}:{child.Key}");
                    if (nested.Any())
                    {
                        data[child.Key] = nested;
                    }
                }
                else
                {
                    data[child.Key] = RedactConfigValue($"{sectionName}:{child.Key}", child.Value);
                }
            }

            return data;
        }

        private AppSettingsDto GetAllSettings(IConfiguration configuration)
        {
            var settings = new AppSettingsDto
            {
                Sections = new Dictionary<string, Dictionary<string, object>>()
            };

            foreach (var section in GetTopLevelSections(configuration))
            {
                settings.Sections[section] = GetSectionData(configuration, section);
            }

            return settings;
        }

        private string? GetAppSettingsPath()
        {
            var basePath = Directory.GetCurrentDirectory();
            var env = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production";

            var paths = new[]
            {
                Path.Combine(basePath, "appsettings.json"),
                Path.Combine(basePath, $"appsettings.{env}.json"),
                Path.Combine(AppContext.BaseDirectory, "appsettings.json"),
                Path.Combine(AppContext.BaseDirectory, $"appsettings.{env}.json")
            };

            return paths.FirstOrDefault(p => System.IO.File.Exists(p));
        }

        private async Task UpdateAppSettingsFileAsync(string filePath, string section, Dictionary<string, object> settings)
        {
            // Read existing file
            var json = await System.IO.File.ReadAllTextAsync(filePath);
            var jsonDoc = System.Text.Json.JsonDocument.Parse(json);
            var root = jsonDoc.RootElement;

            // Create a new JSON object
            var newRoot = new Dictionary<string, object>();

            // Copy all existing sections
            foreach (var property in root.EnumerateObject())
            {
                if (property.Name != section)
                {
                    var deserialized = System.Text.Json.JsonSerializer.Deserialize<object>(property.Value.GetRawText());
                    if (deserialized != null)
                    {
                        newRoot[property.Name] = deserialized;
                    }
                }
            }

            // Update the target section
            var sectionDict = new Dictionary<string, object>();
            foreach (var setting in settings)
            {
                var settingPath = $"{section}:{setting.Key}";
                if (IsRedactedObjectValue(setting.Value) && IsSecretLikePath(settingPath))
                {
                    var existingValue = GetExistingSectionValue(root, section, setting.Key);
                    if (existingValue == null)
                    {
                        throw new InvalidOperationException(
                            $"Cannot save redacted placeholder for missing appsettings key '{settingPath}'.");
                    }

                    sectionDict[setting.Key] = existingValue;
                }
                else
                {
                    sectionDict[setting.Key] = setting.Value;
                }
            }
            newRoot[section] = sectionDict;

            // Write back to file with proper formatting
            var options = new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = null // Keep original property names
            };

            var newJson = System.Text.Json.JsonSerializer.Serialize(newRoot, options);
            await System.IO.File.WriteAllTextAsync(filePath, newJson);
        }

        private static CategorySettingsDto RedactCategory(CategorySettingsDto category)
        {
            return new CategorySettingsDto
            {
                Category = category.Category,
                DisplayName = category.DisplayName,
                Description = category.Description,
                Settings = category.Settings.Select(RedactSetting).ToList(),
                RequiresRestart = category.RequiresRestart,
                SettingCount = category.SettingCount
            };
        }

        private static SystemSettingDto RedactSetting(SystemSettingDto setting)
        {
            var redactValue = ShouldRedactSetting(setting);
            var redactDefault = !string.IsNullOrWhiteSpace(setting.DefaultValue) &&
                                ShouldRedactValue($"{setting.Category}:{setting.SettingKey}:DefaultValue", setting.DefaultValue);

            return new SystemSettingDto
            {
                Id = setting.Id,
                Category = setting.Category,
                SettingKey = setting.SettingKey,
                SettingValue = redactValue ? RedactedValue : setting.SettingValue,
                DataType = setting.DataType,
                Description = setting.Description,
                DefaultValue = redactDefault ? RedactedValue : setting.DefaultValue,
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

        private static SettingsHistoryDto RedactHistory(SettingsHistoryDto history)
        {
            var path = $"{history.Category}:{history.SettingKey}";
            return new SettingsHistoryDto
            {
                Id = history.Id,
                Category = history.Category,
                SettingKey = history.SettingKey,
                OldValue = ShouldRedactValue(path, history.OldValue) ? RedactedValue : history.OldValue,
                NewValue = ShouldRedactValue(path, history.NewValue) ? RedactedValue : history.NewValue,
                ChangedBy = history.ChangedBy,
                Reason = history.Reason,
                IpAddress = history.IpAddress,
                ChangedAt = history.ChangedAt
            };
        }

        private static SettingsExportDto RedactExport(SettingsExportDto export)
        {
            var redacted = new SettingsExportDto
            {
                ExportedAt = export.ExportedAt,
                ExportedBy = export.ExportedBy,
                Version = export.Version
            };

            foreach (var category in export.Settings)
            {
                redacted.Settings[category.Key] = category.Value.ToDictionary(
                    setting => setting.Key,
                    setting => ShouldRedactValue($"{category.Key}:{setting.Key}", setting.Value)
                        ? RedactedValue
                        : setting.Value);
            }

            return redacted;
        }

        private static SettingsExportDto RemoveRedactedExportValues(SettingsExportDto export)
        {
            var sanitized = new SettingsExportDto
            {
                ExportedAt = export.ExportedAt,
                ExportedBy = export.ExportedBy,
                Version = export.Version
            };

            foreach (var category in export.Settings)
            {
                var settings = category.Value
                    .Where(setting => !IsRedactedPlaceholder(setting.Value))
                    .ToDictionary(setting => setting.Key, setting => setting.Value);

                if (settings.Count > 0)
                {
                    sanitized.Settings[category.Key] = settings;
                }
            }

            return sanitized;
        }

        private static Core.DTOs.Settings.ConnectionTestResult RedactConnectionTestResult(
            string category,
            Core.DTOs.Settings.ConnectionTestResult result)
        {
            return new Core.DTOs.Settings.ConnectionTestResult
            {
                Success = result.Success,
                Message = ShouldRedactValue($"{category}:Message", result.Message) ? RedactedValue : result.Message,
                ResponseTime = result.ResponseTime,
                Details = RedactObjectDictionary(result.Details, category)
            };
        }

        private static Dictionary<string, object> RedactObjectDictionary(Dictionary<string, object> values, string pathPrefix)
        {
            var redacted = new Dictionary<string, object>();
            foreach (var value in values)
            {
                var path = $"{pathPrefix}:{value.Key}";
                if (value.Value is Dictionary<string, object> nested)
                {
                    redacted[value.Key] = RedactObjectDictionary(nested, path);
                }
                else
                {
                    redacted[value.Key] = RedactConfigValue(path, value.Value);
                }
            }

            return redacted;
        }

        private static void RedactValidationResult(SettingsValidationResult result, string category)
        {
            foreach (var key in result.ValidatedValues.Keys.ToList())
            {
                var value = result.ValidatedValues[key];
                if (ShouldRedactValue($"{category}:{key}", value))
                {
                    result.ValidatedValues[key] = RedactedValue;
                }
            }
        }

        private static object RedactConfigValue(string path, object? value)
        {
            if (value is null)
            {
                return string.Empty;
            }

            if (value is JsonElement jsonElement)
            {
                if (jsonElement.ValueKind == JsonValueKind.String)
                {
                    var jsonStringValue = jsonElement.GetString();
                    return ShouldRedactValue(path, jsonStringValue) ? RedactedValue : jsonStringValue ?? string.Empty;
                }

                return jsonElement;
            }

            var scalarStringValue = value.ToString();
            return ShouldRedactValue(path, scalarStringValue) ? RedactedValue : value;
        }

        private static bool ShouldRedactSetting(SystemSettingDto setting)
        {
            return setting.IsEncrypted ||
                   ShouldRedactValue($"{setting.Category}:{setting.SettingKey}", setting.SettingValue);
        }

        private static bool ShouldRedactValue(string path, string? value)
        {
            if (string.IsNullOrWhiteSpace(value) || IsSafePlaceholder(value))
            {
                return false;
            }

            return IsSecretLikePath(path) || LooksSecretLikeValue(value);
        }

        private static bool IsSecretLikePath(string path)
        {
            var normalized = NormalizeSecretText(path);
            return SecretPathTokens.Any(normalized.Contains);
        }

        private static bool LooksSecretLikeValue(string value)
        {
            if (IsSafePlaceholder(value))
            {
                return false;
            }

            var normalized = value.Trim().ToLowerInvariant();
            return normalized.Contains("password=") ||
                   normalized.Contains("pwd=") ||
                   normalized.Contains("sharedaccesskey=") ||
                   normalized.Contains("accountkey=") ||
                   normalized.Contains("client_secret") ||
                   normalized.Contains("apikey=") ||
                   normalized.Contains("api_key") ||
                   normalized.Contains("access_token") ||
                   normalized.Contains("refresh_token") ||
                   normalized.Contains("bearer ");
        }

        private static bool IsSafePlaceholder(string value)
        {
            return value.Contains("***USE_ENV_VAR", StringComparison.OrdinalIgnoreCase) ||
                   value.Contains("USE_ENV_VAR_", StringComparison.OrdinalIgnoreCase);
        }

        private static bool IsRedactedPlaceholder(string? value)
        {
            return string.Equals(value, RedactedValue, StringComparison.Ordinal);
        }

        private static bool IsRedactedObjectValue(object? value)
        {
            return value switch
            {
                string stringValue => IsRedactedPlaceholder(stringValue),
                JsonElement { ValueKind: JsonValueKind.String } jsonElement => IsRedactedPlaceholder(jsonElement.GetString()),
                _ => false
            };
        }

        private static string NormalizeSecretText(string value)
        {
            return new string(value
                .Where(char.IsLetterOrDigit)
                .Select(char.ToLowerInvariant)
                .ToArray());
        }

        private static object? GetExistingSectionValue(JsonElement root, string section, string key)
        {
            if (!root.TryGetProperty(section, out var sectionElement) ||
                sectionElement.ValueKind != JsonValueKind.Object ||
                !sectionElement.TryGetProperty(key, out var existingValue))
            {
                return null;
            }

            return JsonSerializer.Deserialize<object>(existingValue.GetRawText());
        }

        #endregion
    }

    // Request DTOs
    public class ValidateSettingRequest
    {
        public string Category { get; set; } = string.Empty;
        public string Key { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    public class ResetRequest
    {
        public string ResetBy { get; set; } = string.Empty;
    }

    public class ImportSettingsRequest
    {
        public SettingsExportDto Export { get; set; } = new();
        public string ImportedBy { get; set; } = string.Empty;
        public bool OverwriteExisting { get; set; } = false;
    }

    public class SetPreferenceRequest
    {
        public string Value { get; set; } = string.Empty;
        public string DataType { get; set; } = "string";
    }

    public class UpdateAppSettingsRequest
    {
        public Dictionary<string, object> Settings { get; set; } = new();
        public string ChangedBy { get; set; } = string.Empty;
        public string? Reason { get; set; }
    }

    public class AppSettingsDto
    {
        public Dictionary<string, Dictionary<string, object>> Sections { get; set; } = new();
    }
}

