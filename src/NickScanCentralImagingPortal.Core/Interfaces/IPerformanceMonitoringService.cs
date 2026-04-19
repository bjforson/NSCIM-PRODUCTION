using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Interface for performance monitoring service
    /// </summary>
    public interface IPerformanceMonitoringService
    {
        /// <summary>
        /// Get all current performance metrics
        /// </summary>
        /// <returns>Dictionary of metric name to metric data</returns>
        Dictionary<string, PerformanceMetric> GetCurrentMetrics();

        /// <summary>
        /// Get performance metrics for a specific service
        /// </summary>
        /// <param name="serviceName">Name of the service</param>
        /// <returns>List of metrics for the service</returns>
        List<PerformanceMetric> GetMetricsForService(string serviceName);

        /// <summary>
        /// Get a summary of current system performance
        /// </summary>
        /// <returns>Performance summary with health status and alerts</returns>
        PerformanceSummary GetPerformanceSummary();
    }

    public class PerformanceMetric
    {
        public string Name { get; set; } = string.Empty;
        public double Value { get; set; }
        public DateTime Timestamp { get; set; }
        public string Unit { get; set; } = string.Empty;
    }

    public class PerformanceSummary
    {
        public DateTime Timestamp { get; set; }
        public int TotalMetrics { get; set; }
        public int RecentMetricsCount { get; set; }
        public int HourlyMetricsCount { get; set; }
        public string SystemHealth { get; set; } = string.Empty;
        public List<string> TopMemoryConsumers { get; set; } = new();
        public List<string> PerformanceAlerts { get; set; } = new();
    }
}
