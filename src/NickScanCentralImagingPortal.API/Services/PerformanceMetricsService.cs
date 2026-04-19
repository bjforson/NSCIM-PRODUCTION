using System.Collections.Concurrent;
using System.Diagnostics;

namespace NickScanCentralImagingPortal.API.Services
{
    /// <summary>
    /// Service for tracking and reporting API performance metrics
    /// </summary>
    public interface IPerformanceMetricsService
    {
        void RecordRequest(string endpoint, string method, long durationMs, int statusCode);
        PerformanceMetrics GetMetrics();
        EndpointMetrics GetEndpointMetrics(string endpoint);
        void Reset();
    }

    /// <summary>
    /// Performance metrics data
    /// </summary>
    public class PerformanceMetrics
    {
        public DateTime StartTime { get; set; } = DateTime.UtcNow;
        public long TotalRequests { get; set; }
        public long TotalErrors { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public long MaxResponseTimeMs { get; set; }
        public long MinResponseTimeMs { get; set; }
        public double RequestsPerSecond { get; set; }
        public double ErrorRate { get; set; }
        public Dictionary<string, EndpointMetrics> EndpointMetrics { get; set; } = new();
        public Dictionary<int, long> StatusCodeDistribution { get; set; } = new();
        public PercentileData Percentiles { get; set; } = new();
    }

    /// <summary>
    /// Per-endpoint metrics
    /// </summary>
    public class EndpointMetrics
    {
        public string Endpoint { get; set; } = string.Empty;
        public string Method { get; set; } = string.Empty;
        public long RequestCount { get; set; }
        public long ErrorCount { get; set; }
        public double AverageResponseTimeMs { get; set; }
        public long MaxResponseTimeMs { get; set; }
        public long MinResponseTimeMs { get; set; }
        public double ErrorRate { get; set; }
    }

    /// <summary>
    /// Response time percentiles
    /// </summary>
    public class PercentileData
    {
        public long P50 { get; set; }
        public long P75 { get; set; }
        public long P90 { get; set; }
        public long P95 { get; set; }
        public long P99 { get; set; }
    }

    /// <summary>
    /// Implementation of performance metrics service
    /// </summary>
    public class PerformanceMetricsService : IPerformanceMetricsService
    {
        private readonly ConcurrentDictionary<string, EndpointStats> _endpointStats = new();
        private readonly ConcurrentBag<long> _responseTimes = new();
        private readonly ConcurrentDictionary<int, long> _statusCodes = new();
        private readonly DateTime _startTime = DateTime.UtcNow;
        private long _totalRequests = 0;
        private long _totalErrors = 0;

        public void RecordRequest(string endpoint, string method, long durationMs, int statusCode)
        {
            Interlocked.Increment(ref _totalRequests);

            if (statusCode >= 400)
            {
                Interlocked.Increment(ref _totalErrors);
            }

            // Record response time
            _responseTimes.Add(durationMs);

            // Record status code
            _statusCodes.AddOrUpdate(statusCode, 1, (key, count) => count + 1);

            // Record endpoint-specific stats
            var key = $"{method}:{endpoint}";
            _endpointStats.AddOrUpdate(key,
                new EndpointStats
                {
                    Endpoint = endpoint,
                    Method = method,
                    RequestCount = 1,
                    ErrorCount = statusCode >= 400 ? 1 : 0,
                    TotalDurationMs = durationMs,
                    MinDurationMs = durationMs,
                    MaxDurationMs = durationMs
                },
                (k, stats) =>
                {
                    stats.RequestCount++;
                    if (statusCode >= 400) stats.ErrorCount++;
                    stats.TotalDurationMs += durationMs;
                    if (durationMs < stats.MinDurationMs) stats.MinDurationMs = durationMs;
                    if (durationMs > stats.MaxDurationMs) stats.MaxDurationMs = durationMs;
                    return stats;
                });
        }

        public PerformanceMetrics GetMetrics()
        {
            var uptime = DateTime.UtcNow - _startTime;
            var responseTimes = _responseTimes.ToArray();

            return new PerformanceMetrics
            {
                StartTime = _startTime,
                TotalRequests = _totalRequests,
                TotalErrors = _totalErrors,
                AverageResponseTimeMs = responseTimes.Length > 0 ? responseTimes.Average() : 0,
                MaxResponseTimeMs = responseTimes.Length > 0 ? responseTimes.Max() : 0,
                MinResponseTimeMs = responseTimes.Length > 0 ? responseTimes.Min() : 0,
                RequestsPerSecond = uptime.TotalSeconds > 0 ? _totalRequests / uptime.TotalSeconds : 0,
                ErrorRate = _totalRequests > 0 ? (_totalErrors * 100.0) / _totalRequests : 0,
                EndpointMetrics = _endpointStats.ToDictionary(
                    kvp => kvp.Key,
                    kvp => new EndpointMetrics
                    {
                        Endpoint = kvp.Value.Endpoint,
                        Method = kvp.Value.Method,
                        RequestCount = kvp.Value.RequestCount,
                        ErrorCount = kvp.Value.ErrorCount,
                        AverageResponseTimeMs = kvp.Value.RequestCount > 0
                            ? kvp.Value.TotalDurationMs / (double)kvp.Value.RequestCount
                            : 0,
                        MaxResponseTimeMs = kvp.Value.MaxDurationMs,
                        MinResponseTimeMs = kvp.Value.MinDurationMs,
                        ErrorRate = kvp.Value.RequestCount > 0
                            ? (kvp.Value.ErrorCount * 100.0) / kvp.Value.RequestCount
                            : 0
                    }),
                StatusCodeDistribution = _statusCodes.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
                Percentiles = CalculatePercentiles(responseTimes)
            };
        }

        public EndpointMetrics GetEndpointMetrics(string endpoint)
        {
            if (_endpointStats.TryGetValue(endpoint, out var stats))
            {
                return new EndpointMetrics
                {
                    Endpoint = stats.Endpoint,
                    Method = stats.Method,
                    RequestCount = stats.RequestCount,
                    ErrorCount = stats.ErrorCount,
                    AverageResponseTimeMs = stats.RequestCount > 0
                        ? stats.TotalDurationMs / (double)stats.RequestCount
                        : 0,
                    MaxResponseTimeMs = stats.MaxDurationMs,
                    MinResponseTimeMs = stats.MinDurationMs,
                    ErrorRate = stats.RequestCount > 0
                        ? (stats.ErrorCount * 100.0) / stats.RequestCount
                        : 0
                };
            }

            return new EndpointMetrics { Endpoint = endpoint };
        }

        public void Reset()
        {
            _endpointStats.Clear();
            _responseTimes.Clear();
            _statusCodes.Clear();
            Interlocked.Exchange(ref _totalRequests, 0);
            Interlocked.Exchange(ref _totalErrors, 0);
        }

        private PercentileData CalculatePercentiles(long[] values)
        {
            if (values.Length == 0)
            {
                return new PercentileData();
            }

            var sorted = values.OrderBy(v => v).ToArray();

            return new PercentileData
            {
                P50 = GetPercentile(sorted, 0.50),
                P75 = GetPercentile(sorted, 0.75),
                P90 = GetPercentile(sorted, 0.90),
                P95 = GetPercentile(sorted, 0.95),
                P99 = GetPercentile(sorted, 0.99)
            };
        }

        private long GetPercentile(long[] sortedValues, double percentile)
        {
            if (sortedValues.Length == 0) return 0;

            var index = (int)Math.Ceiling(percentile * sortedValues.Length) - 1;
            index = Math.Max(0, Math.Min(index, sortedValues.Length - 1));

            return sortedValues[index];
        }

        private class EndpointStats
        {
            public string Endpoint { get; set; } = string.Empty;
            public string Method { get; set; } = string.Empty;
            public long RequestCount { get; set; }
            public long ErrorCount { get; set; }
            public long TotalDurationMs { get; set; }
            public long MinDurationMs { get; set; }
            public long MaxDurationMs { get; set; }
        }
    }
}

