using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.API.Logging
{
    public interface IStructuredLoggingService
    {
        void LogInfo(string serviceId, string operation, string message, params object[] args);
        void LogWarning(string serviceId, string operation, string message, params object[] args);
        void LogError(string serviceId, string operation, string message, Exception? exception = null, params object[] args);
        void LogDebug(string serviceId, string operation, string message, params object[] args);
    }

    public class StructuredLoggingService : IStructuredLoggingService
    {
        private readonly ILogger<StructuredLoggingService> _logger;

        public StructuredLoggingService(ILogger<StructuredLoggingService> logger)
        {
            _logger = logger;
        }

        private string FormatLogMessage(string serviceId, string operation, string message)
        {
            return $"[{serviceId}] [{operation}] {message}";
        }

        public void LogInfo(string serviceId, string operation, string message, params object[] args)
        {
            _logger.LogInformation(FormatLogMessage(serviceId, operation, message), args);
        }

        public void LogWarning(string serviceId, string operation, string message, params object[] args)
        {
            _logger.LogWarning(FormatLogMessage(serviceId, operation, message), args);
        }

        public void LogError(string serviceId, string operation, string message, Exception? exception = null, params object[] args)
        {
            _logger.LogError(exception, FormatLogMessage(serviceId, operation, message), args);
        }

        public void LogDebug(string serviceId, string operation, string message, params object[] args)
        {
            _logger.LogDebug(FormatLogMessage(serviceId, operation, message), args);
        }
    }

    public class ThrottledLogger
    {
        private readonly ILogger _baseLogger;
        private readonly string _serviceId;
        private readonly ConcurrentDictionary<string, DateTime> _lastLogTimes = new ConcurrentDictionary<string, DateTime>();
        private readonly TimeSpan _throttleInterval = TimeSpan.FromSeconds(30); // Default throttle interval

        public ThrottledLogger(ILogger baseLogger, string serviceId, TimeSpan? throttleInterval = null)
        {
            _baseLogger = baseLogger;
            _serviceId = serviceId;
            if (throttleInterval.HasValue)
            {
                _throttleInterval = throttleInterval.Value;
            }
        }

        private bool ShouldLog(string operation)
        {
            var now = DateTime.UtcNow;
            if (_lastLogTimes.TryGetValue(operation, out var lastLogTime))
            {
                if ((now - lastLogTime) < _throttleInterval)
                {
                    return false;
                }
            }
            _lastLogTimes[operation] = now;
            return true;
        }

        private string FormatLogMessage(string operation, string message)
        {
            return $"[{_serviceId}] [{operation}] {message}";
        }

        public void LogInfo(string operation, string message, params object[] args)
        {
            if (ShouldLog(operation))
            {
                _baseLogger.LogInformation(FormatLogMessage(operation, message), args);
            }
        }

        public void LogWarning(string operation, string message, params object[] args)
        {
            if (ShouldLog(operation))
            {
                _baseLogger.LogWarning(FormatLogMessage(operation, message), args);
            }
        }

        public void LogError(string operation, string message, Exception? exception = null, params object[] args)
        {
            // Errors should generally not be throttled, or throttled with a much longer interval
            _baseLogger.LogError(exception, FormatLogMessage(operation, message), args);
        }

        public void LogDebug(string operation, string message, params object[] args)
        {
            if (ShouldLog(operation))
            {
                _baseLogger.LogDebug(FormatLogMessage(operation, message), args);
            }
        }
    }
}
