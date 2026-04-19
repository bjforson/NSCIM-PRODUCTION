using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    /// <summary>
    /// Seeds initial system settings into the database
    /// </summary>
    public partial class SettingsSeeder
    {
        private readonly ApplicationDbContext _context;

        public SettingsSeeder(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task SeedDefaultSettingsAsync()
        {
            // Check if settings already exist
            if (await _context.SystemSettings.AnyAsync())
            {
                return; // Settings already seeded
            }

            var settings = new List<SystemSetting>
            {
                // ===== GENERAL SETTINGS =====
                new SystemSetting
                {
                    Category = "General",
                    SettingKey = "SystemName",
                    SettingValue = "NickScan Central Imaging Portal",
                    DataType = "string",
                    Description = "Name of the system displayed in UI",
                    DefaultValue = "NickScan Central Imaging Portal",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "General",
                    SettingKey = "Organization",
                    SettingValue = "Ghana Revenue Authority",
                    DataType = "string",
                    Description = "Organization name",
                    DefaultValue = "Ghana Revenue Authority",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "General",
                    SettingKey = "Port",
                    SettingValue = "Takoradi",
                    DataType = "string",
                    Description = "Default port location",
                    DefaultValue = "Takoradi",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 3,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== ICUMS SETTINGS =====
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "BaseUrl",
                    SettingValue = "https://www.icumsghana.com/nickscanintegrationservice",
                    DataType = "string",
                    Description = "ICUMS API base URL",
                    DefaultValue = "https://www.icumsghana.com/nickscanintegrationservice",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 1,
                    AllowedRoles = "Admin,SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "FetchBatchUrl",
                    SettingValue = "/api/BOEScanData/FetchBatchBOEScanDocument",
                    DataType = "string",
                    Description = "ICUMS batch fetch endpoint",
                    DefaultValue = "/api/BOEScanData/FetchBatchBOEScanDocument",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "FetchUrl",
                    SettingValue = "/api/BOEScanData/FetchBOEScanDocument",
                    DataType = "string",
                    Description = "ICUMS single container fetch endpoint",
                    DefaultValue = "/api/BOEScanData/FetchBOEScanDocument",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 3,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "SubmitResultUrl",
                    SettingValue = "/api/BOEScanData/SubmitScanResult",
                    DataType = "string",
                    Description = "ICUMS scan result submission endpoint",
                    DefaultValue = "/api/BOEScanData/SubmitScanResult",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 4,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "TimeoutSeconds",
                    SettingValue = "120",
                    DataType = "int",
                    Description = "API request timeout in seconds",
                    DefaultValue = "120",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 5,
                    ValidationRules = "{\"min\": 30, \"max\": 600}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "BatchIntervalMinutes",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "Interval between batch fetches in minutes",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 6,
                    ValidationRules = "{\"min\": 1, \"max\": 60}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "ICUMS",
                    SettingKey = "DownloadsPath",
                    SettingValue = @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads",
                    DataType = "string",
                    Description = "Path for ICUMS downloads",
                    DefaultValue = @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 7,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== EMAIL SETTINGS =====
                new SystemSetting
                {
                    Category = "Email",
                    SettingKey = "SmtpServer",
                    SettingValue = "smtp.office365.com",
                    DataType = "string",
                    Description = "SMTP server address",
                    DefaultValue = "smtp.office365.com",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Email",
                    SettingKey = "SmtpPort",
                    SettingValue = "587",
                    DataType = "int",
                    Description = "SMTP server port",
                    DefaultValue = "587",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    ValidationRules = "{\"min\": 1, \"max\": 65535}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Email",
                    SettingKey = "SmtpUsername",
                    SettingValue = "nickscan@example.com",
                    DataType = "string",
                    Description = "SMTP authentication username",
                    DefaultValue = "",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 3,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Email",
                    SettingKey = "SmtpPassword",
                    SettingValue = "",
                    DataType = "string",
                    Description = "SMTP authentication password",
                    DefaultValue = "",
                    IsEncrypted = true,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 4,
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Email",
                    SettingKey = "EnableSsl",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable SSL/TLS for email",
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
                    Category = "Email",
                    SettingKey = "FromEmail",
                    SettingValue = "nickscan@gra.gov.gh",
                    DataType = "string",
                    Description = "Email sender address",
                    DefaultValue = "nickscan@gra.gov.gh",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 6,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== NICKCOMMS GATEWAY SETTINGS =====
                new SystemSetting
                {
                    Category = "NickComms",
                    SettingKey = "BaseUrl",
                    SettingValue = "http://localhost:5220",
                    DataType = "string",
                    Description = "Base URL of the NickComms.Gateway service",
                    DefaultValue = "http://localhost:5220",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "NickComms",
                    SettingKey = "ApiKey",
                    SettingValue = "",
                    DataType = "string",
                    Description = "X-Api-Key value for the nscim-image client (set via env var or here)",
                    DefaultValue = "",
                    IsEncrypted = true,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    AllowedRoles = "SuperAdmin",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "NickComms",
                    SettingKey = "Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "When true, NSCIS routes all email/SMS through NickComms.Gateway",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 3,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "NickComms",
                    SettingKey = "TimeoutSeconds",
                    SettingValue = "15",
                    DataType = "int",
                    Description = "HTTP timeout when calling NickComms",
                    DefaultValue = "15",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 4,
                    ValidationRules = "{\"min\": 1, \"max\": 120}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== SECURITY SETTINGS =====
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "PasswordMinLength",
                    SettingValue = "8",
                    DataType = "int",
                    Description = "Minimum password length",
                    DefaultValue = "8",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 1,
                    ValidationRules = "{\"min\": 6, \"max\": 128}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "SessionTimeoutMinutes",
                    SettingValue = "30",
                    DataType = "int",
                    Description = "User session timeout in minutes",
                    DefaultValue = "30",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    ValidationRules = "{\"min\": 5, \"max\": 1440}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "JwtExpirationHours",
                    SettingValue = "24",
                    DataType = "int",
                    Description = "JWT token expiration in hours",
                    DefaultValue = "24",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 3,
                    ValidationRules = "{\"min\": 1, \"max\": 168}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Security",
                    SettingKey = "MaxFailedLoginAttempts",
                    SettingValue = "5",
                    DataType = "int",
                    Description = "Maximum failed login attempts before lockout",
                    DefaultValue = "5",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 4,
                    ValidationRules = "{\"min\": 3, \"max\": 10}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== SCANNER SETTINGS =====
                new SystemSetting
                {
                    Category = "Scanners",
                    SettingKey = "FS6000.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable FS6000 scanner integration",
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
                    Category = "Scanners",
                    SettingKey = "FS6000.SourcePath",
                    SettingValue = @"C:\FS6000\Export",
                    DataType = "string",
                    Description = "FS6000 export folder path",
                    DefaultValue = @"C:\FS6000\Export",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Scanners",
                    SettingKey = "ASE.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable ASE scanner integration",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 3,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Scanners",
                    SettingKey = "Heimann.Enabled",
                    SettingValue = "true",
                    DataType = "bool",
                    Description = "Enable Heimann scanner integration",
                    DefaultValue = "true",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 4,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== PERFORMANCE SETTINGS =====
                new SystemSetting
                {
                    Category = "Performance",
                    SettingKey = "CacheDurationSeconds",
                    SettingValue = "300",
                    DataType = "int",
                    Description = "Cache duration in seconds",
                    DefaultValue = "300",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 1,
                    ValidationRules = "{\"min\": 0, \"max\": 86400}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Performance",
                    SettingKey = "MaxImageSizeMB",
                    SettingValue = "10",
                    DataType = "int",
                    Description = "Maximum image size in MB",
                    DefaultValue = "10",
                    IsEncrypted = false,
                    RequiresRestart = false,
                    IsActive = true,
                    DisplayOrder = 2,
                    ValidationRules = "{\"min\": 1, \"max\": 50}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },

                // ===== AUTHENTICATION/JWT SETTINGS =====
                new SystemSetting
                {
                    Category = "Authentication",
                    SettingKey = "JWT.Issuer",
                    SettingValue = "NickScanCentralImagingPortal",
                    DataType = "string",
                    Description = "JWT token issuer identifier",
                    DefaultValue = "NickScanCentralImagingPortal",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 1,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Authentication",
                    SettingKey = "JWT.Audience",
                    SettingValue = "NickScanPortalUsers",
                    DataType = "string",
                    Description = "JWT token audience identifier",
                    DefaultValue = "NickScanPortalUsers",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 2,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Authentication",
                    SettingKey = "JWT.ExpirationHours",
                    SettingValue = "8",
                    DataType = "int",
                    Description = "JWT access token expiration in hours",
                    DefaultValue = "8",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 3,
                    ValidationRules = "{\"min\": 1, \"max\": 24}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Authentication",
                    SettingKey = "JWT.RefreshTokenExpirationDays",
                    SettingValue = "30",
                    DataType = "int",
                    Description = "JWT refresh token expiration in days",
                    DefaultValue = "30",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 4,
                    ValidationRules = "{\"min\": 7, \"max\": 90}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                },
                new SystemSetting
                {
                    Category = "Authentication",
                    SettingKey = "JWT.ClockSkewSeconds",
                    SettingValue = "0",
                    DataType = "int",
                    Description = "Tolerance for token expiration validation (seconds)",
                    DefaultValue = "0",
                    IsEncrypted = false,
                    RequiresRestart = true,
                    IsActive = true,
                    DisplayOrder = 5,
                    ValidationRules = "{\"min\": 0, \"max\": 300}",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                }
            };

            await _context.SystemSettings.AddRangeAsync(settings);
            await _context.SaveChangesAsync();
        }
    }
}

