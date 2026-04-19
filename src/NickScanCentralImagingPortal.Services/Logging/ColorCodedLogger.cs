using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.Logging
{
    /// <summary>
    /// ANSI Color codes for consistent cross-platform color support
    /// </summary>
    public static class AnsiColors
    {
        // Reset
        public const string Reset = "\x1b[0m";

        // Text Colors
        public const string Black = "\x1b[30m";
        public const string Red = "\x1b[31m";
        public const string Green = "\x1b[32m";
        public const string Yellow = "\x1b[33m";
        public const string Blue = "\x1b[34m";
        public const string Magenta = "\x1b[35m";
        public const string Cyan = "\x1b[36m";
        public const string White = "\x1b[37m";

        // Bright Colors
        public const string BrightBlack = "\x1b[90m";
        public const string BrightRed = "\x1b[91m";
        public const string BrightGreen = "\x1b[92m";
        public const string BrightYellow = "\x1b[93m";
        public const string BrightBlue = "\x1b[94m";
        public const string BrightMagenta = "\x1b[95m";
        public const string BrightCyan = "\x1b[96m";
        public const string BrightWhite = "\x1b[97m";

        // Background Colors
        public const string BgRed = "\x1b[41m";
        public const string BgGreen = "\x1b[42m";
        public const string BgYellow = "\x1b[43m";
        public const string BgBlue = "\x1b[44m";
        public const string BgMagenta = "\x1b[45m";
        public const string BgCyan = "\x1b[46m";

        // Styles
        public const string Bold = "\x1b[1m";
        public const string Dim = "\x1b[2m";
        public const string Italic = "\x1b[3m";
        public const string Underline = "\x1b[4m";
    }

    /// <summary>
    /// Color-coded logger wrapper that provides consistent color coding for different service categories
    /// </summary>
    public class ColorCodedLogger
    {
        private readonly ILogger _logger;
        private readonly string _serviceCategory;
        private readonly string _serviceId;
        private readonly string _colorCode;
        private readonly bool _enableColors;

        public ColorCodedLogger(ILogger logger, string serviceCategory, string serviceId = "")
        {
            _logger = logger;
            _serviceCategory = serviceCategory;
            _serviceId = string.IsNullOrEmpty(serviceId) ? serviceCategory : serviceId;
            _colorCode = GetColorForService(serviceCategory);
            _enableColors = Environment.GetEnvironmentVariable("NO_COLOR") == null &&
                           Environment.GetEnvironmentVariable("TERM") != "dumb" &&
                           Console.IsOutputRedirected == false;
        }

        /// <summary>
        /// Log information with service category prefix
        /// </summary>
        public void LogInformation(string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, methodName);
            _logger.LogInformation(formattedMessage);
        }

        /// <summary>
        /// Log information with service category prefix and parameters
        /// </summary>
        public void LogInformation(string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message);
            _logger.LogInformation(formattedMessage, args);
        }

        /// <summary>
        /// Log warning with service category prefix
        /// </summary>
        public void LogWarning(string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, methodName);
            _logger.LogWarning(formattedMessage);
        }

        /// <summary>
        /// Log warning with service category prefix and parameters
        /// </summary>
        public void LogWarning(string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message);
            _logger.LogWarning(formattedMessage, args);
        }

        /// <summary>
        /// Log error with service category prefix
        /// </summary>
        public void LogError(string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, methodName);
            _logger.LogError(formattedMessage);
        }

        /// <summary>
        /// Log error with service category prefix and parameters
        /// </summary>
        public void LogError(string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message);
            _logger.LogError(formattedMessage, args);
        }

        /// <summary>
        /// Log error with service category prefix and exception
        /// </summary>
        public void LogError(Exception exception, string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, methodName);
            _logger.LogError(exception, formattedMessage);
        }

        /// <summary>
        /// Log error with service category prefix, exception, and parameters
        /// </summary>
        public void LogError(Exception exception, string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message);
            _logger.LogError(exception, formattedMessage, args);
        }

        /// <summary>
        /// Log debug with service category prefix
        /// </summary>
        public void LogDebug(string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, methodName);
            _logger.LogDebug(formattedMessage);
        }

        /// <summary>
        /// Log debug with service category prefix and parameters
        /// </summary>
        public void LogDebug(string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message);
            _logger.LogDebug(formattedMessage, args);
        }

        /// <summary>
        /// Log critical with service category prefix
        /// </summary>
        public void LogCritical(string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, methodName);
            _logger.LogCritical(formattedMessage);
        }

        /// <summary>
        /// Log critical with service category prefix and exception
        /// </summary>
        public void LogCritical(Exception exception, string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, methodName);
            _logger.LogCritical(exception, formattedMessage);
        }

        /// <summary>
        /// Get ANSI color code for service category
        /// </summary>
        private static string GetColorForService(string serviceCategory)
        {
            return serviceCategory.ToUpperInvariant() switch
            {
                "ICUMS" => AnsiColors.BrightCyan,
                "FS6000" or "ASE" or "NUCTECH" or "SCANNER" => AnsiColors.BrightGreen,
                "CONTAINER" or "VALIDATION" => AnsiColors.BrightYellow,
                "HEALTH" or "MONITORING" or "PERFORMANCE" => AnsiColors.BrightMagenta,
                "API" or "CONTROLLER" or "MIDDLEWARE" => AnsiColors.White,
                "BACKGROUND" or "SERVICE" => AnsiColors.BrightBlue,
                "REPOSITORY" or "DATABASE" => AnsiColors.BrightBlack,
                _ => AnsiColors.White
            };
        }

        /// <summary>
        /// Format message with service category prefix and color coding
        /// </summary>
        private string FormatMessage(string message, string? methodName = null)
        {
            var prefix = $"[{_serviceId}]";

            if (!string.IsNullOrEmpty(methodName))
            {
                prefix += $" {methodName}";
            }

            var formattedMessage = $"{prefix} {message}";

            // Apply color if enabled
            if (_enableColors)
            {
                return $"{_colorCode}{formattedMessage}{AnsiColors.Reset}";
            }

            return formattedMessage;
        }
    }

    /// <summary>
    /// Service categories for color coding
    /// </summary>
    public static class ServiceCategories
    {
        public const string ICUMS = "ICUMS";
        public const string SCANNER_FS6000 = "FS6000";
        public const string SCANNER_ASE = "ASE";
        public const string SCANNER_NUCTECH = "NUCTECH";
        public const string CONTAINER_COMPLETENESS = "CONTAINER-COMPLETENESS";
        public const string CONTAINER_VALIDATION = "CONTAINER-VALIDATION";
        public const string HEALTH_CHECK = "HEALTH-CHECK";
        public const string PERFORMANCE_MONITORING = "PERFORMANCE-MONITORING";
        public const string BACKGROUND_SERVICE = "BACKGROUND-SERVICE";
        public const string API_CONTROLLER = "API-CONTROLLER";
        public const string REPOSITORY = "REPOSITORY";
        public const string DATABASE = "DATABASE";
        public const string MIDDLEWARE = "MIDDLEWARE";
    }
}
