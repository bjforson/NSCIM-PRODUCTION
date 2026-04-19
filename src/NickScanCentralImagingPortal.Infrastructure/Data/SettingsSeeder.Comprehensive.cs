using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    /// <summary>
    /// Comprehensive settings seeder - adds ALL detailed system settings
    /// Phase 3-4: Background Services, ICUMS Enhanced, and advanced configurations
    /// </summary>
    public partial class SettingsSeeder
    {
        /// <summary>
        /// Seeds comprehensive detailed settings (all remaining settings)
        /// Call this to complete the full 189 settings implementation
        /// </summary>
        public async Task SeedComprehensiveSettingsAsync()
        {
            // Only seed if comprehensive settings don't exist
            if (await _context.SystemSettings.AnyAsync(s => s.SettingKey.Contains("CircuitBreaker") || s.SettingKey.Contains("Proxy")))
            {
                return; // Comprehensive settings already seeded
            }

            var comprehensiveSettings = GetComprehensiveSettings();
            await _context.SystemSettings.AddRangeAsync(comprehensiveSettings);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Master method to seed ALL settings at once
        /// </summary>
        public async Task SeedAllSettingsAsync()
        {
            await SeedDefaultSettingsAsync();
            await SeedExtendedSettingsAsync();
            await SeedComprehensiveSettingsAsync();
        }

        private List<SystemSetting> GetComprehensiveSettings()
        {
            return new List<SystemSetting>
            {
                // ===== ICUMS PROXY SETTINGS =====
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "Proxy.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable proxy for ICUMS API calls",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 8,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "Proxy.Address",
                    SettingValue = "http://18.135.35.74:3128",
                    DataType = "string",
                    Description = "Proxy server address",
                    DefaultValue = "http://18.135.35.74:3128",
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
                    Category = "ICUMS",
                    SettingKey = "Proxy.BypassOnLocal",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Bypass proxy for local addresses",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 10,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== ICUMS ENHANCED - CIRCUIT BREAKER =====
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "CircuitBreaker.FailureThreshold",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "Number of failures before circuit opens",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 11,
                    ValidationRules = "{\"min\": 1, \"max\": 20}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "CircuitBreaker.TimeoutMinutes",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "Circuit breaker timeout period in minutes",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 12,
                    ValidationRules = "{\"min\": 1, \"max\": 60}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== ICUMS ENHANCED - RETRY POLICY =====
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "RetryPolicy.MaxRetries",
                    SettingValue = "3",
                    DataType = "int",
                    Description = "Maximum number of retry attempts",
                    DefaultValue = "3",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 13,
                    ValidationRules = "{\"min\": 0, \"max\": 10}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "RetryPolicy.BaseDelaySeconds",
                    SettingValue = "1",
                    DataType = "int",
                    Description = "Base delay between retries in seconds",
                    DefaultValue = "1",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 14,
                    ValidationRules = "{\"min\": 1, \"max\": 10}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "RetryPolicy.MaxDelaySeconds",
                    SettingValue = "30",
                    DataType = "int",
                    Description = "Maximum delay between retries in seconds",
                    DefaultValue = "30",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 15,
                    ValidationRules = "{\"min\": 5, \"max\": 300}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== ICUMS ENHANCED - PARALLEL PROCESSING =====
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "EnableParallelProcessing",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable parallel processing of ICUMS data",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 16,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "MaxParallelFiles",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "Maximum parallel file processing count",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 17,
                    ValidationRules = "{\"min\": 1, \"max\": 20}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== ICUMS ENHANCED - CACHING =====
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "Caching.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable ICUMS response caching",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 18,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "Caching.DefaultTimeoutMinutes",
                    SettingValue = "10",
                    DataType = "int",
                    Description = "Default cache timeout for ICUMS data in minutes",
                    DefaultValue = "10",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 19,
                    ValidationRules = "{\"min\": 1, \"max\": 120}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== ICUMS ENHANCED - BACKUP =====
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "Backup.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable automatic backup of ICUMS downloads",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 20,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "Backup.RetentionDays",
                    SettingValue = "30",
                    DataType = "int",
                    Description = "Backup retention period in days",
                    DefaultValue = "30",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 21,
                    ValidationRules = "{\"min\": 7, \"max\": 365}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== ICUMS ENHANCED - HEALTH MONITORING =====
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "HealthMonitoring.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable ICUMS API health monitoring",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 22,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "HealthMonitoring.CheckIntervalMinutes",
                    SettingValue = "2",
                    DataType = "int",
                    Description = "Health check interval in minutes",
                    DefaultValue = "2",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 23,
                    ValidationRules = "{\"min\": 1, \"max\": 60}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== BACKGROUND SERVICES - DETAILED TOGGLES =====
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ServiceOrchestrator.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable service orchestrator (master coordinator)",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 100,
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ComprehensiveHealthCheck.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable comprehensive health check service",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 101,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "IcumFileScannerService.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable ICUMS file scanner service",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 102,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "IcumFileScannerService.ScanIntervalMinutes",
                    SettingValue = "1",
                    DataType = "int",
                    Description = "ICUMS file scanner interval in minutes",
                    DefaultValue = "1",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 103,
                    ValidationRules = "{\"min\": 1, \"max\": 30}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "IcumJsonIngestionService.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable ICUMS JSON ingestion service",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 104,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "IcumDataTransferService.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable ICUMS data transfer service",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 105,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "IcumDataTransferService.TransferIntervalMinutes",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "ICUMS data transfer interval in minutes",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 106,
                    ValidationRules = "{\"min\": 1, \"max\": 60}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ContainerCompletenessService.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable container completeness checking service",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 107,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ContainerCompletenessService.CheckIntervalMinutes",
                    SettingValue = "10",
                    DataType = "int",
                    Description = "Container completeness check interval in minutes",
                    DefaultValue = "10",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 108,
                    ValidationRules = "{\"min\": 1, \"max\": 120}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ManualBOESelectivityService.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable manual BOE selectivity service",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 109,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ContainerDataMapperService.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable container data mapper service",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 110,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "ContainerDataMapperService.MappingIntervalMinutes",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "Container data mapping interval in minutes",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 111,
                    ValidationRules = "{\"min\": 1, \"max\": 60}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "CMRRedownloadService.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable CMR re-download service",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 112,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "BackgroundServices",
                    SettingKey = "CMRMetricsRecorderService.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable CMR metrics recorder service",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 113,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== FS6000 DETAILED SETTINGS =====
                new SystemSetting
                {
                    Category = "Scanners",
                    SettingKey = "FS6000.DestinationPath",
                    SettingValue = @"C:\Shared\NSCIM_PRODUCTION\Data\FS6000\Staging",
                    DataType = "string",
                    Description = "FS6000 staging directory path",
                    DefaultValue = @"C:\Shared\NSCIM_PRODUCTION\Data\FS6000\Staging",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 5,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Scanners",
                    SettingKey = "FS6000.ArchivePath",
                    SettingValue = @"C:\Shared\NSCIM_PRODUCTION\Data\FS6000\Archive",
                    DataType = "string",
                    Description = "FS6000 archive directory path",
                    DefaultValue = @"C:\Shared\NSCIM_PRODUCTION\Data\FS6000\Archive",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 6,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Scanners",
                    SettingKey = "FS6000.FailedPath",
                    SettingValue = @"C:\Shared\NSCIM_PRODUCTION\Data\FS6000\Failed",
                    DataType = "string",
                    Description = "FS6000 failed files directory path",
                    DefaultValue = @"C:\Shared\NSCIM_PRODUCTION\Data\FS6000\Failed",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 7,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Scanners",
                    SettingKey = "FS6000.MaxFileSizeMB",
                    SettingValue = "100",
                    DataType = "int",
                    Description = "Maximum file size for FS6000 processing in MB",
                    DefaultValue = "100",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 8,
                    ValidationRules = "{\"min\": 1, \"max\": 500}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Scanners",
                    SettingKey = "FS6000.RetryAttempts",
                    SettingValue = "3",
                    DataType = "int",
                    Description = "Number of retry attempts for failed file processing",
                    DefaultValue = "3",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 9,
                    ValidationRules = "{\"min\": 0, \"max\": 10}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== EMAIL ADDITIONAL SETTINGS =====
                new SystemSetting
                {
                    Category = "Email",
                    SettingKey = "Enabled",
                    SettingValue = "false",
                    DataType = "bool",
                    Description = "Enable email service",
                    DefaultValue = "false",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 7,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Email",
                    SettingKey = "FromName",
                    SettingValue = "NickScan Central Imaging Portal",
                    DataType = "string",
                    Description = "Email sender display name",
                    DefaultValue = "NickScan Central Imaging Portal",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 8,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== ADDITIONAL RATE LIMITING =====
                new SystemSetting
                {
                    Category = "RateLimiting",
                    SettingKey = "Export.PerMinute",
                    SettingValue = "50",
                    DataType = "int",
                    Description = "Export/bulk operation requests per minute",
                    DefaultValue = "50",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 4,
                    ValidationRules = "{\"min\": 1, \"max\": 100}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "RateLimiting",
                    SettingKey = "Admin.PerMinute",
                    SettingValue = "1000",
                    DataType = "int",
                    Description = "Admin operation requests per minute",
                    DefaultValue = "1000",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 5,
                    ValidationRules = "{\"min\": 100, \"max\": 10000}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            };
        }
    }
}

