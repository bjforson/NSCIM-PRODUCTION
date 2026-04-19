using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Helper class for adaptive polling intervals based on work availability
    /// Adjusts polling frequency dynamically to reduce idle polling while maintaining responsiveness
    /// </summary>
    public class AdaptivePollingHelper
    {
        private readonly ILogger? _logger;

        // Default thresholds (can be overridden via configuration)
        public int HighWorkThreshold { get; set; } = 50;      // 50+ items = high work
        public int MediumWorkThreshold { get; set; } = 10;   // 10-49 items = medium work
        public int LowWorkThreshold { get; set; } = 1;        // 1-9 items = low work
        // 0 items = no work

        // Polling intervals (in seconds)
        public int HighWorkIntervalSeconds { get; set; } = 10;  // was 5s — reduces wake-ups by 50%
        public int MediumWorkIntervalSeconds { get; set; } = 30;
        public int LowWorkIntervalSeconds { get; set; } = 120;  // 2 minutes
        public int NoWorkIntervalSeconds { get; set; } = 300;   // 5 minutes

        public AdaptivePollingHelper(ILogger? logger = null)
        {
            _logger = logger;
        }

        /// <summary>
        /// Calculate polling interval based on work count
        /// </summary>
        public TimeSpan CalculateInterval(int workCount)
        {
            int intervalSeconds;
            string workLevel;

            if (workCount >= HighWorkThreshold)
            {
                intervalSeconds = HighWorkIntervalSeconds;
                workLevel = "HIGH";
            }
            else if (workCount >= MediumWorkThreshold)
            {
                intervalSeconds = MediumWorkIntervalSeconds;
                workLevel = "MEDIUM";
            }
            else if (workCount >= LowWorkThreshold)
            {
                intervalSeconds = LowWorkIntervalSeconds;
                workLevel = "LOW";
            }
            else
            {
                intervalSeconds = NoWorkIntervalSeconds;
                workLevel = "NONE";
            }

            _logger?.LogDebug("[ADAPTIVE-POLLING] Work count: {Count}, Level: {Level}, Interval: {Interval}s",
                workCount, workLevel, intervalSeconds);

            return TimeSpan.FromSeconds(intervalSeconds);
        }

        /// <summary>
        /// Check if enough time has passed based on adaptive interval
        /// </summary>
        public bool ShouldExecute(DateTime lastRun, int workCount, DateTime now)
        {
            var interval = CalculateInterval(workCount);
            var timeSinceLastRun = now - lastRun;
            return timeSinceLastRun >= interval;
        }

        /// <summary>
        /// Get minimum interval (for main loop delay)
        /// </summary>
        public TimeSpan GetMinimumInterval()
        {
            return TimeSpan.FromSeconds(HighWorkIntervalSeconds);
        }
    }
}

