using System;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.Logging
{
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
            if (exception != null)
            {
                _baseLogger.LogError(exception, FormatLogMessage(operation, message), args);
            }
            else
            {
                _baseLogger.LogError(FormatLogMessage(operation, message), args);
            }
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
