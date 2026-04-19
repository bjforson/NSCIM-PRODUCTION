using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    /// <summary>
    /// Extended settings seeder - adds additional comprehensive system settings
    /// This class extends the base SettingsSeeder with all the new settings
    /// </summary>
    public partial class SettingsSeeder
    {
        /// <summary>
        /// Seeds extended comprehensive settings (Phase 2-4)
        /// Call this after SeedDefaultSettingsAsync() or independently
        /// </summary>
        public async Task SeedExtendedSettingsAsync()
        {
            // Only seed if extended settings don't exist
            if (await _context.SystemSettings.AnyAsync(s => s.Category == "CORS" || s.Category == "RateLimiting"))
            {
                return; // Extended settings already seeded
            }

            var extendedSettings = GetExtendedSettings();
            await _context.SystemSettings.AddRangeAsync(extendedSettings);
            await _context.SaveChangesAsync();
        }

        private List<SystemSetting> GetExtendedSettings()
        {
            return new List<SystemSetting>
            {
                // ===== ADDITIONAL SECURITY SETTINGS =====
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "PasswordRequireUppercase",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Require uppercase letters in passwords",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 5,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "PasswordRequireDigit",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Require digits in passwords",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 6,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "BcryptWorkFactor",
                    SettingValue = "12",
                    DataType = "int",
                    Description = "BCrypt work factor (higher = more secure but slower)",
                    DefaultValue = "12",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 7,
                    ValidationRules = "{\"min\": 10, \"max\": 15}",
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "EnableHsts",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable HTTP Strict Transport Security",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 8,
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "HstsMaxAgeSeconds",
                    SettingValue = "31536000",
                    DataType = "int",
                    Description = "HSTS max age in seconds (1 year = 31536000)",
                    DefaultValue = "31536000",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 9,
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "MaxRequestBodySizeMB",
                    SettingValue = "100",
                    DataType = "int",
                    Description = "Maximum request body size in MB",
                    DefaultValue = "100",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 10,
                    ValidationRules = "{\"min\": 1, \"max\": 500}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "MinimumTlsVersion",
                    SettingValue = "Tls12",
                    DataType = "string",
                    Description = "Minimum TLS version (Tls12 or Tls13)",
                    DefaultValue = "Tls12",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 11,
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== CORS SETTINGS =====
                new SystemSetting
                {
                    Category = "CORS",
                    SettingKey = "AllowedOrigins",
                    SettingValue = "http://localhost:5000,https://localhost:5001,http://localhost:5126",
                    DataType = "string",
                    Description = "Comma-separated list of allowed origins",
                    DefaultValue = "http://localhost:5000",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 1,
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "CORS",
                    SettingKey = "AllowCredentials",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Allow credentials in CORS requests",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 2,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== RATE LIMITING SETTINGS =====
                new SystemSetting
                {
                    Category = "RateLimiting",
                    SettingKey = "Login.PerMinute",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "Login attempts per minute per IP",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 1,
                    ValidationRules = "{\"min\": 1, \"max\": 20}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "RateLimiting",
                    SettingKey = "API.PerMinute",
                    SettingValue = "500",
                    DataType = "int",
                    Description = "General API requests per minute",
                    DefaultValue = "500",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    ValidationRules = "{\"min\": 10, \"max\": 10000}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "RateLimiting",
                    SettingKey = "Dashboard.PerMinute",
                    SettingValue = "200",
                    DataType = "int",
                    Description = "Dashboard requests per minute",
                    DefaultValue = "200",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 3,
                    ValidationRules = "{\"min\": 10, \"max\": 1000}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== CACHING SETTINGS =====
                new SystemSetting
                {
                    Category = "Caching",
                    SettingKey = "EnableResponseCaching",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable response caching",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Caching",
                    SettingKey = "Containers.DurationSeconds",
                    SettingValue = "300",
                    DataType = "int",
                    Description = "Cache duration for containers in seconds",
                    DefaultValue = "300",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    ValidationRules = "{\"min\": 0, \"max\": 3600}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Caching",
                    SettingKey = "ICUMS.DurationSeconds",
                    SettingValue = "1800",
                    DataType = "int",
                    Description = "Cache duration for ICUMS data in seconds",
                    DefaultValue = "1800",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 3,
                    ValidationRules = "{\"min\": 0, \"max\": 7200}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== REDIS SETTINGS =====
                new SystemSetting
                {
                    Category = "Redis",
                    SettingKey = "Enabled",
                    SettingValue = "false",
                    DataType = "bool",
                    Description = "Enable Redis distributed cache",
                    DefaultValue = "false",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Redis",
                    SettingKey = "ConnectionString",
                    SettingValue = "localhost:6379",
                    DataType = "string",
                    Description = "Redis connection string",
                    DefaultValue = "localhost:6379",
                    IsEncrypted = true,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 2,
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== PERFORMANCE SETTINGS (ADDITIONAL) =====
                new SystemSetting
                {
                    Category = "Performance",
                    SettingKey = "SlowRequestThresholdMs",
                    SettingValue = "1000",
                    DataType = "int",
                    Description = "Threshold for slow request logging (milliseconds)",
                    DefaultValue = "1000",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 3,
                    ValidationRules = "{\"min\": 100, \"max\": 10000}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Performance",
                    SettingKey = "EnableMetricsCollection",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable performance metrics collection",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 4,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Performance",
                    SettingKey = "MetricsRetentionHours",
                    SettingValue = "24",
                    DataType = "int",
                    Description = "How long to retain performance metrics (hours)",
                    DefaultValue = "24",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 5,
                    ValidationRules = "{\"min\": 1, \"max\": 168}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== BACKGROUND SERVICES - FS6000 =====
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "FS6000.SyncIntervalMinutes",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "FS6000 file sync interval in minutes",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 1,
                    ValidationRules = "{\"min\": 1, \"max\": 60}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "FS6000.ProcessingIntervalMinutes",
                    SettingValue = "1",
                    DataType = "int",
                    Description = "FS6000 processing interval in minutes",
                    DefaultValue = "1",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    ValidationRules = "{\"min\": 1, \"max\": 30}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "FS6000.MaxConcurrentFiles",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "Maximum concurrent file processing",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 3,
                    ValidationRules = "{\"min\": 1, \"max\": 20}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== BACKGROUND SERVICES - ASE =====
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ASE.SyncIntervalMinutes",
                    SettingValue = "15",
                    DataType = "int",
                    Description = "ASE database sync interval in minutes",
                    DefaultValue = "15",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 10,
                    ValidationRules = "{\"min\": 1, \"max\": 120}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ASE.BatchSize",
                    SettingValue = "50",
                    DataType = "int",
                    Description = "ASE batch processing size",
                    DefaultValue = "50",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 11,
                    ValidationRules = "{\"min\": 10, \"max\": 500}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== BACKGROUND SERVICES - ICUMS =====
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ICUMS.BatchIntervalMinutes",
                    SettingValue = "30",
                    DataType = "int",
                    Description = "ICUMS batch processing interval in minutes",
                    DefaultValue = "30",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 20,
                    ValidationRules = "{\"min\": 1, \"max\": 120}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ICUMS.DownloadProcessIntervalMinutes",
                    SettingValue = "2",
                    DataType = "int",
                    Description = "ICUMS download processing interval in minutes",
                    DefaultValue = "2",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 21,
                    ValidationRules = "{\"min\": 1, \"max\": 30}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ICUMS.SubmissionIntervalMinutes",
                    SettingValue = "10",
                    DataType = "int",
                    Description = "ICUMS submission interval in minutes",
                    DefaultValue = "10",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 22,
                    ValidationRules = "{\"min\": 1, \"max\": 60}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== BACKGROUND SERVICES - HEALTH & MONITORING =====
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "HealthCheck.IntervalMinutes",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "Health check interval in minutes",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 30,
                    ValidationRules = "{\"min\": 1, \"max\": 30}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "DashboardBroadcast.IntervalSeconds",
                    SettingValue = "30",
                    DataType = "int",
                    Description = "Dashboard broadcast interval in seconds",
                    DefaultValue = "30",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 31,
                    ValidationRules = "{\"min\": 5, \"max\": 300}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== LOGGING SETTINGS =====
                new SystemSetting
                {
                    Category = "Logging",
                    SettingKey = "DefaultLevel",
                    SettingValue = "Information",
                    DataType = "string",
                    Description = "Default logging level (Trace, Debug, Information, Warning, Error, Critical)",
                    DefaultValue = "Information",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Logging",
                    SettingKey = "RetentionDays",
                    SettingValue = "30",
                    DataType = "int",
                    Description = "Log file retention period in days",
                    DefaultValue = "30",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    ValidationRules = "{\"min\": 7, \"max\": 365}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== NOTIFICATION SETTINGS =====
                new SystemSetting
                {
                    Category = "Notifications",
                    SettingKey = "EnableEmailNotifications",
                    SettingValue = "false",
                    DataType = "bool",
                    Description = "Enable email notifications for errors",
                    DefaultValue = "false",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Notifications",
                    SettingKey = "EnableSlackNotifications",
                    SettingValue = "false",
                    DataType = "bool",
                    Description = "Enable Slack notifications for errors",
                    DefaultValue = "false",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== DATABASE SETTINGS =====
                new SystemSetting
                {
                    Category = "Database",
                    SettingKey = "CommandTimeoutSeconds",
                    SettingValue = "30",
                    DataType = "int",
                    Description = "Database command timeout in seconds",
                    DefaultValue = "30",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 1,
                    ValidationRules = "{\"min\": 10, \"max\": 300}",
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Database",
                    SettingKey = "MaxRetryCount",
                    SettingValue = "3",
                    DataType = "int",
                    Description = "Maximum retry count for transient failures",
                    DefaultValue = "3",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 2,
                    ValidationRules = "{\"min\": 0, \"max\": 10}",
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== IMAGE ANALYSIS SETTINGS =====
                new SystemSetting
                {
                    Category = "ImageAnalysis",
                    SettingKey = "PartiallyCompletedRetentionDays",
                    SettingValue = "90",
                    DataType = "int",
                    Description = "Number of days to retain PartiallyCompleted records before auto-completing. Records with missing container images will be automatically marked as Completed after this period.",
                    DefaultValue = "90",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 1,
                    ValidationRules = "{\"min\": 1, \"max\": 365}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            };
        }
    }
}

