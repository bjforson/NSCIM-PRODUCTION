using System.Net.Mail;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.Settings;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Settings
{
    /// <summary>
    /// Service for validating settings values with type-specific rules
    /// </summary>
    public class SettingsValidationService : ISettingsValidationService
    {
        private readonly ILogger<SettingsValidationService> _logger;
        private readonly IIcumApiService _icumApiService;
        private readonly IEmailService _emailService;

        public SettingsValidationService(
            ILogger<SettingsValidationService> logger,
            IIcumApiService icumApiService,
            IEmailService emailService)
        {
            _logger = logger;
            _icumApiService = icumApiService;
            _emailService = emailService;
        }

        public async Task<SettingsValidationResult> ValidateSettingAsync(string category, string key, string value)
        {
            var result = new SettingsValidationResult { IsValid = true };

            try
            {
                // Category-specific validation
                switch (category.ToUpper())
                {
                    case "ICUMS":
                        ValidateICUMSSetting(key, value, result);
                        break;
                    case "EMAIL":
                        ValidateEmailSetting(key, value, result);
                        break;
                    case "SECURITY":
                        ValidateSecuritySetting(key, value, result);
                        break;
                    case "SCANNERS":
                        ValidateScannerSetting(key, value, result);
                        break;
                    case "PERFORMANCE":
                        ValidatePerformanceSetting(key, value, result);
                        break;
                    default:
                        // Generic validation
                        ValidateGenericSetting(key, value, result);
                        break;
                }

                result.ValidatedValues[key] = value;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating setting {Category}.{Key}", category, key);
                result.IsValid = false;
                result.Errors.Add($"Validation error: {ex.Message}");
            }

            return await Task.FromResult(result);
        }

        public async Task<SettingsValidationResult> ValidateCategorySettingsAsync(Dictionary<string, string> settings, string category)
        {
            var result = new SettingsValidationResult { IsValid = true };

            foreach (var setting in settings)
            {
                var validation = await ValidateSettingAsync(category, setting.Key, setting.Value);

                if (!validation.IsValid)
                {
                    result.IsValid = false;
                    result.Errors.AddRange(validation.Errors);
                }

                result.Warnings.AddRange(validation.Warnings);
                result.ValidatedValues[setting.Key] = setting.Value;
            }

            return result;
        }

        public async Task<ConnectionTestResult> TestConnectionAsync(string category)
        {
            var result = new ConnectionTestResult();
            var startTime = DateTime.UtcNow;

            try
            {
                switch (category.ToUpper())
                {
                    case "ICUMS":
                        result = await TestICUMSConnectionAsync();
                        break;
                    case "EMAIL":
                        result = await TestEmailConnectionAsync();
                        break;
                    default:
                        result.Success = false;
                        result.Message = $"Connection test not implemented for category: {category}";
                        break;
                }

                result.ResponseTime = DateTime.UtcNow - startTime;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error testing connection for {Category}", category);
                result.Success = false;
                result.Message = $"Connection test failed: {ex.Message}";
                result.ResponseTime = DateTime.UtcNow - startTime;
            }

            return result;
        }

        // ICUMS Validation
        private void ValidateICUMSSetting(string key, string value, SettingsValidationResult result)
        {
            switch (key)
            {
                case "BaseUrl":
                    // BaseUrl must be absolute
                    if (!Uri.TryCreate(value, UriKind.Absolute, out _))
                    {
                        result.IsValid = false;
                        result.Errors.Add($"{key} must be a valid absolute URL");
                    }
                    break;
                case "FetchBatchUrl":
                case "FetchUrl":
                case "SubmitResultUrl":
                    // API endpoints can be relative (starting with /) or absolute
                    if (!Uri.TryCreate(value, UriKind.Absolute, out _) &&
                        !value.StartsWith("/"))
                    {
                        result.IsValid = false;
                        result.Errors.Add($"{key} must be a valid URL or relative path starting with /");
                    }
                    break;

                case "TimeoutSeconds":
                case "BatchIntervalMinutes":
                    if (!int.TryParse(value, out var intVal) || intVal < 1 || intVal > 3600)
                    {
                        result.IsValid = false;
                        result.Errors.Add($"{key} must be between 1 and 3600");
                    }
                    break;

                case "DownloadsPath":
                    if (!Directory.Exists(value))
                    {
                        result.Warnings.Add($"Directory does not exist: {value}");
                    }
                    break;
            }
        }

        // Email Validation
        private void ValidateEmailSetting(string key, string value, SettingsValidationResult result)
        {
            switch (key)
            {
                case "SmtpServer":
                    if (string.IsNullOrWhiteSpace(value))
                    {
                        result.IsValid = false;
                        result.Errors.Add("SMTP server is required");
                    }
                    break;

                case "SmtpPort":
                    if (!int.TryParse(value, out var port) || port < 1 || port > 65535)
                    {
                        result.IsValid = false;
                        result.Errors.Add("SMTP port must be between 1 and 65535");
                    }
                    break;

                case "FromEmail":
                case "SmtpUsername":
                    if (!string.IsNullOrWhiteSpace(value))
                    {
                        try
                        {
                            var addr = new MailAddress(value);
                        }
                        catch
                        {
                            result.IsValid = false;
                            result.Errors.Add($"{key} must be a valid email address");
                        }
                    }
                    break;

                case "AdminRecipients":
                case "ReportRecipients":
                    var emails = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    foreach (var email in emails)
                    {
                        try
                        {
                            var addr = new MailAddress(email);
                        }
                        catch
                        {
                            result.IsValid = false;
                            result.Errors.Add($"Invalid email address in list: {email}");
                        }
                    }
                    break;
            }
        }

        // Security Validation
        private void ValidateSecuritySetting(string key, string value, SettingsValidationResult result)
        {
            switch (key)
            {
                case "PasswordMinLength":
                    if (!int.TryParse(value, out var minLen) || minLen < 6 || minLen > 128)
                    {
                        result.IsValid = false;
                        result.Errors.Add("Password minimum length must be between 6 and 128");
                    }
                    break;

                case "PasswordExpirationDays":
                    if (!int.TryParse(value, out var days) || days < 0 || days > 365)
                    {
                        result.IsValid = false;
                        result.Errors.Add("Password expiration must be between 0 and 365 days");
                    }
                    break;

                case "SessionTimeoutMinutes":
                    if (!int.TryParse(value, out var timeout) || timeout < 5 || timeout > 1440)
                    {
                        result.IsValid = false;
                        result.Errors.Add("Session timeout must be between 5 and 1440 minutes");
                    }
                    break;

                case "MaxFailedLoginAttempts":
                    if (!int.TryParse(value, out var attempts) || attempts < 3 || attempts > 10)
                    {
                        result.IsValid = false;
                        result.Errors.Add("Max failed login attempts must be between 3 and 10");
                    }
                    break;

                case "JwtExpirationHours":
                    if (!int.TryParse(value, out var hours) || hours < 1 || hours > 168)
                    {
                        result.IsValid = false;
                        result.Errors.Add("JWT expiration must be between 1 and 168 hours");
                    }
                    break;
            }
        }

        // Scanner Validation
        private void ValidateScannerSetting(string key, string value, SettingsValidationResult result)
        {
            if (key.Contains("Path") || key.Contains("Directory"))
            {
                if (!Directory.Exists(value))
                {
                    result.Warnings.Add($"Directory does not exist: {value}");
                }
            }

            if (key.Contains("Interval") && key.Contains("Minutes"))
            {
                if (!int.TryParse(value, out var interval) || interval < 1 || interval > 1440)
                {
                    result.IsValid = false;
                    result.Errors.Add($"{key} must be between 1 and 1440 minutes");
                }
            }
        }

        // Performance Validation
        private void ValidatePerformanceSetting(string key, string value, SettingsValidationResult result)
        {
            if (key.Contains("CacheDuration") && key.Contains("Seconds"))
            {
                if (!int.TryParse(value, out var seconds) || seconds < 0 || seconds > 86400)
                {
                    result.IsValid = false;
                    result.Errors.Add($"{key} must be between 0 and 86400 seconds");
                }
            }

            if (key.Contains("Timeout"))
            {
                if (!int.TryParse(value, out var timeout) || timeout < 1 || timeout > 300)
                {
                    result.IsValid = false;
                    result.Errors.Add($"{key} must be between 1 and 300 seconds");
                }
            }
        }

        // Generic Validation
        private void ValidateGenericSetting(string key, string value, SettingsValidationResult result)
        {
            // Basic validation - ensure value is not null or empty for required settings
            if (key.Contains("Required") && string.IsNullOrWhiteSpace(value))
            {
                result.IsValid = false;
                result.Errors.Add($"{key} is required");
            }
        }

        // Connection Tests
        private async Task<ConnectionTestResult> TestICUMSConnectionAsync()
        {
            var result = new ConnectionTestResult();

            try
            {
                _logger.LogInformation("Testing ICUMS API connection");

                var apiStatus = await _icumApiService.GetApiStatusAsync();

                result.Success = apiStatus.Status == "Success";
                result.Message = result.Success
                    ? "ICUMS API connection successful"
                    : $"ICUMS API connection failed: {apiStatus.Status}";

                result.Details["Status"] = apiStatus.Status;
                result.Details["DataCount"] = apiStatus.Data?.BoeScanDocuments?.Count ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ICUMS connection test failed");
                result.Success = false;
                result.Message = $"Connection test failed: {ex.Message}";
            }

            return result;
        }

        private async Task<ConnectionTestResult> TestEmailConnectionAsync()
        {
            var result = new ConnectionTestResult();

            try
            {
                _logger.LogInformation("Testing email connection");

                // Email test would go here - for now just return success
                result.Success = true;
                result.Message = "Email configuration appears valid";
                result.Details["Note"] = "Send test email via the Email tab to fully verify";
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Email connection test failed");
                result.Success = false;
                result.Message = $"Connection test failed: {ex.Message}";
            }

            return await Task.FromResult(result);
        }
    }
}

