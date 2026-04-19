namespace NickScanCentralImagingPortal.Core.DTOs.Settings
{
    /// <summary>
    /// DTO for system setting
    /// </summary>
    public class SystemSettingDto
    {
        public int Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string SettingKey { get; set; } = string.Empty;
        public string SettingValue { get; set; } = string.Empty;
        public string DataType { get; set; } = "string";
        public string? Description { get; set; }
        public string? DefaultValue { get; set; }
        public bool IsEncrypted { get; set; }
        public bool RequiresRestart { get; set; }
        public string? AllowedRoles { get; set; }
        public bool IsActive { get; set; }
        public int DisplayOrder { get; set; }
        public string? ValidationRules { get; set; }
        public string? LastModifiedBy { get; set; }
        public DateTime? LastModifiedAt { get; set; }
    }

    /// <summary>
    /// DTO for updating a setting
    /// </summary>
    public class UpdateSettingDto
    {
        public string Category { get; set; } = string.Empty;
        public string SettingKey { get; set; } = string.Empty;
        public string SettingValue { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string ChangedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO for bulk settings update
    /// </summary>
    public class BulkSettingsUpdateDto
    {
        public string Category { get; set; } = string.Empty;
        public Dictionary<string, string> Settings { get; set; } = new();
        public string? Reason { get; set; }
        public string ChangedBy { get; set; } = string.Empty;
    }

    /// <summary>
    /// DTO for settings by category
    /// </summary>
    public class CategorySettingsDto
    {
        public string Category { get; set; } = string.Empty;
        public string DisplayName { get; set; } = string.Empty;
        public string? Description { get; set; }
        public List<SystemSettingDto> Settings { get; set; } = new();
        public bool RequiresRestart { get; set; }
        public int SettingCount { get; set; }
    }

    /// <summary>
    /// DTO for settings history
    /// </summary>
    public class SettingsHistoryDto
    {
        public int Id { get; set; }
        public string Category { get; set; } = string.Empty;
        public string SettingKey { get; set; } = string.Empty;
        public string? OldValue { get; set; }
        public string NewValue { get; set; } = string.Empty;
        public string ChangedBy { get; set; } = string.Empty;
        public string? Reason { get; set; }
        public string? IpAddress { get; set; }
        public DateTime ChangedAt { get; set; }
    }

    /// <summary>
    /// DTO for user preference
    /// </summary>
    public class UserPreferenceDto
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string PreferenceKey { get; set; } = string.Empty;
        public string PreferenceValue { get; set; } = string.Empty;
        public string DataType { get; set; } = "string";
        public string? Description { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// DTO for settings validation result
    /// </summary>
    public class SettingsValidationResult
    {
        public bool IsValid { get; set; }
        public List<string> Errors { get; set; } = new();
        public List<string> Warnings { get; set; } = new();
        public Dictionary<string, string> ValidatedValues { get; set; } = new();
    }

    /// <summary>
    /// DTO for settings export/import
    /// </summary>
    public class SettingsExportDto
    {
        public string ExportedAt { get; set; } = DateTime.UtcNow.ToString("o");
        public string ExportedBy { get; set; } = string.Empty;
        public string Version { get; set; } = "1.0";
        public Dictionary<string, Dictionary<string, string>> Settings { get; set; } = new();
    }

    /// <summary>
    /// DTO for test connection result
    /// </summary>
    public class ConnectionTestResult
    {
        public bool Success { get; set; }
        public string Message { get; set; } = string.Empty;
        public TimeSpan ResponseTime { get; set; }
        public Dictionary<string, object> Details { get; set; } = new();
    }
}

