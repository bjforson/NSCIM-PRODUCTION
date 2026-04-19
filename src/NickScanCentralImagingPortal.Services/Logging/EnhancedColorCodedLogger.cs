using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.Logging
{
    /// <summary>
    /// Enhanced color-coded logger with emojis and better visual differentiation
    /// </summary>
    public class EnhancedColorCodedLogger
    {
        private readonly ILogger _logger;
        private readonly string _serviceCategory;
        private readonly string _serviceId;
        private readonly string _emoji;
        private readonly string _colorCode;
        private readonly bool _enableColors;

        public EnhancedColorCodedLogger(ILogger logger, string serviceCategory, string serviceId = "")
        {
            _logger = logger;
            _serviceCategory = serviceCategory;
            _serviceId = string.IsNullOrEmpty(serviceId) ? serviceCategory : serviceId;
            _emoji = GetEmojiForService(serviceCategory);
            _colorCode = GetColorForService(serviceCategory);
            _enableColors = Environment.GetEnvironmentVariable("NO_COLOR") == null &&
                           Environment.GetEnvironmentVariable("TERM") != "dumb" &&
                           Console.IsOutputRedirected == false;
        }

        /// <summary>
        /// Get emoji for service category
        /// </summary>
        private static string GetEmojiForService(string serviceCategory)
        {
            return serviceCategory.ToUpperInvariant() switch
            {
                "ICUMS" => "🔵",
                "FS6000" => "🟢",
                "ASE" => "🟢",
                "NUCTECH" => "🟢",
                "SCANNER" => "🟢",
                "CONTAINER" => "📦",
                "CONTAINER-COMPLETENESS" => "📦",
                "CONTAINER-VALIDATION" => "✅",
                "VALIDATION" => "✅",
                "HEALTH" or "HEALTH-CHECK" => "❤️",
                "MONITORING" or "PERFORMANCE-MONITORING" => "📊",
                "PERFORMANCE" => "⚡",
                "API" => "🌐",
                "CONTROLLER" or "API-CONTROLLER" => "🎮",
                "MIDDLEWARE" => "🔧",
                "BACKGROUND" or "BACKGROUND-SERVICE" => "⚙️",
                "SERVICE" => "🔧",
                "REPOSITORY" => "🗄️",
                "DATABASE" => "💾",
                _ => "📝"
            };
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
                "CONTAINER" or "CONTAINER-COMPLETENESS" or "VALIDATION" or "CONTAINER-VALIDATION" => AnsiColors.BrightYellow,
                "HEALTH" or "HEALTH-CHECK" or "MONITORING" or "PERFORMANCE-MONITORING" or "PERFORMANCE" => AnsiColors.BrightMagenta,
                "API" or "CONTROLLER" or "API-CONTROLLER" or "MIDDLEWARE" => AnsiColors.White,
                "BACKGROUND" or "BACKGROUND-SERVICE" or "SERVICE" => AnsiColors.BrightBlue,
                "REPOSITORY" or "DATABASE" => AnsiColors.BrightBlack,
                _ => AnsiColors.White
            };
        }

        /// <summary>
        /// Format message with enhanced visual elements
        /// </summary>
        private string FormatMessage(string message, LogLevel level, string? methodName = null)
        {
            var levelEmoji = GetLevelEmoji(level);
            var prefix = $"{_emoji} [{_serviceId}]";

            if (!string.IsNullOrEmpty(methodName))
            {
                prefix += $" {methodName}";
            }

            var formattedMessage = $"{levelEmoji} {prefix} {message}";

            // Apply color if enabled
            if (_enableColors)
            {
                var levelColor = GetLevelColor(level);
                return $"{levelColor}{formattedMessage}{AnsiColors.Reset}";
            }

            return formattedMessage;
        }

        /// <summary>
        /// Get emoji for log level
        /// </summary>
        private static string GetLevelEmoji(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => "🔍",
                LogLevel.Debug => "🐛",
                LogLevel.Information => "ℹ️",
                LogLevel.Warning => "⚠️",
                LogLevel.Error => "❌",
                LogLevel.Critical => "🚨",
                _ => "📝"
            };
        }

        /// <summary>
        /// Get color for log level
        /// </summary>
        private static string GetLevelColor(LogLevel level)
        {
            return level switch
            {
                LogLevel.Trace => AnsiColors.Dim,
                LogLevel.Debug => AnsiColors.BrightBlack,
                LogLevel.Information => AnsiColors.White,
                LogLevel.Warning => AnsiColors.BrightYellow,
                LogLevel.Error => AnsiColors.BrightRed,
                LogLevel.Critical => AnsiColors.BrightRed + AnsiColors.Bold,
                _ => AnsiColors.White
            };
        }

        /// <summary>
        /// Log information with enhanced formatting
        /// </summary>
        public void LogInformation(string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Information, methodName);
            _logger.LogInformation(formattedMessage);
        }

        /// <summary>
        /// Log information with enhanced formatting and parameters
        /// </summary>
        public void LogInformation(string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Information);
            _logger.LogInformation(formattedMessage, args);
        }

        /// <summary>
        /// Log warning with enhanced formatting
        /// </summary>
        public void LogWarning(string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Warning, methodName);
            _logger.LogWarning(formattedMessage);
        }

        /// <summary>
        /// Log warning with enhanced formatting and parameters
        /// </summary>
        public void LogWarning(string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Warning);
            _logger.LogWarning(formattedMessage, args);
        }

        /// <summary>
        /// Log warning with enhanced formatting and exception
        /// </summary>
        public void LogWarning(Exception exception, string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Warning, methodName);
            _logger.LogWarning(exception, formattedMessage);
        }

        /// <summary>
        /// Log warning with enhanced formatting, exception, and parameters
        /// </summary>
        public void LogWarning(Exception exception, string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Warning);
            _logger.LogWarning(exception, formattedMessage, args);
        }

        /// <summary>
        /// Log error with enhanced formatting
        /// </summary>
        public void LogError(string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Error, methodName);
            _logger.LogError(formattedMessage);
        }

        /// <summary>
        /// Log error with enhanced formatting and parameters
        /// </summary>
        public void LogError(string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Error);
            _logger.LogError(formattedMessage, args);
        }

        /// <summary>
        /// Log error with enhanced formatting and exception
        /// </summary>
        public void LogError(Exception exception, string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Error, methodName);
            _logger.LogError(exception, formattedMessage);
        }

        /// <summary>
        /// Log error with enhanced formatting, exception, and parameters
        /// </summary>
        public void LogError(Exception exception, string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Error);
            _logger.LogError(exception, formattedMessage, args);
        }

        /// <summary>
        /// Log debug with enhanced formatting
        /// </summary>
        public void LogDebug(string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Debug, methodName);
            _logger.LogDebug(formattedMessage);
        }

        /// <summary>
        /// Log debug with enhanced formatting and parameters
        /// </summary>
        public void LogDebug(string message, params object[] args)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Debug);
            _logger.LogDebug(formattedMessage, args);
        }

        /// <summary>
        /// Log critical with enhanced formatting
        /// </summary>
        public void LogCritical(string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Critical, methodName);
            _logger.LogCritical(formattedMessage);
        }

        /// <summary>
        /// Log critical with enhanced formatting and exception
        /// </summary>
        public void LogCritical(Exception exception, string message, [CallerMemberName] string? methodName = null)
        {
            var formattedMessage = FormatMessage(message, LogLevel.Critical, methodName);
            _logger.LogCritical(exception, formattedMessage);
        }

        /// <summary>
        /// Log success message (custom level)
        /// </summary>
        public void LogSuccess(string message, [CallerMemberName] string? methodName = null)
        {
            var prefix = $"✅ [{_serviceId}]";
            if (!string.IsNullOrEmpty(methodName))
            {
                prefix += $" {methodName}";
            }

            var formattedMessage = $"{prefix} {message}";

            if (_enableColors)
            {
                formattedMessage = $"{AnsiColors.BrightGreen}{formattedMessage}{AnsiColors.Reset}";
            }

            _logger.LogInformation(formattedMessage);
        }

        /// <summary>
        /// Log progress message (custom level)
        /// </summary>
        public void LogProgress(string message, [CallerMemberName] string? methodName = null)
        {
            var prefix = $"🔄 [{_serviceId}]";
            if (!string.IsNullOrEmpty(methodName))
            {
                prefix += $" {methodName}";
            }

            var formattedMessage = $"{prefix} {message}";

            if (_enableColors)
            {
                formattedMessage = $"{AnsiColors.BrightBlue}{formattedMessage}{AnsiColors.Reset}";
            }

            _logger.LogInformation(formattedMessage);
        }
    }
}
