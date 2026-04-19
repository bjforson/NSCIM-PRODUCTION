using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    /// <summary>
    /// ICUMS-specific metrics collection for performance tracking
    /// Phase 3.1: Comprehensive metrics for monitoring ICUMS operations
    /// </summary>
    public class ICUMSMetrics
    {
        private readonly ILogger<ICUMSMetrics> _logger;
        private readonly ConcurrentDictionary<string, long> _counters;
        private readonly ConcurrentDictionary<string, List<double>> _histograms;
        private readonly ConcurrentDictionary<string, double> _gauges;
        private readonly object _lockObject = new();

        // Counter names
        public const string COUNTER_FILES_PROCESSED = "icums.files.processed";
        public const string COUNTER_FILES_FAILED = "icums.files.failed";
        public const string COUNTER_FILES_RETRIED = "icums.files.retried";
        public const string COUNTER_DOCUMENTS_PROCESTED = "icums.documents.processed";
        public const string COUNTER_MANIFEST_ITEMS_PROCESSED = "icums.manifest_items.processed";
        public const string COUNTER_API_CALLS = "icums.api.calls";
        public const string COUNTER_API_SUCCESS = "icums.api.success";
        public const string COUNTER_API_FAILURES = "icums.api.failures";
        public const string COUNTER_API_TIMEOUTS = "icums.api.timeouts";
        public const string COUNTER_DOWNLOADS = "icums.downloads";
        public const string COUNTER_DOWNLOADS_SKIPPED = "icums.downloads.skipped";
        public const string COUNTER_DUPLICATES_DETECTED = "icums.duplicates.detected";

        // Histogram names
        public const string HISTOGRAM_FILE_PROCESSING_TIME = "icums.file.processing_time_ms";
        public const string HISTOGRAM_DOCUMENT_PROCESSING_TIME = "icums.document.processing_time_ms";
        public const string HISTOGRAM_API_RESPONSE_TIME = "icums.api.response_time_ms";
        public const string HISTOGRAM_FILE_SIZE = "icums.file.size_bytes";
        public const string HISTOGRAM_DOCUMENTS_PER_FILE = "icums.documents.per_file";
        public const string HISTOGRAM_MANIFEST_ITEMS_PER_DOCUMENT = "icums.manifest_items.per_document";

        // Gauge names
        public const string GAUGE_QUEUE_DEPTH = "icums.queue.depth";
        public const string GAUGE_FAILED_QUEUE_DEPTH = "icums.failed_queue.depth";
        public const string GAUGE_PENDING_FILES = "icums.pending_files";
        public const string GAUGE_PROCESSING_FILES = "icums.processing_files";
        public const string GAUGE_MEMORY_USAGE_MB = "icums.memory.usage_mb";
        public const string GAUGE_AVG_THROUGHPUT_FILES_PER_MIN = "icums.throughput.files_per_min";
        public const string GAUGE_AVG_THROUGHPUT_DOCUMENTS_PER_MIN = "icums.throughput.documents_per_min";

        public ICUMSMetrics(ILogger<ICUMSMetrics> logger)
        {
            _logger = logger;
            _counters = new ConcurrentDictionary<string, long>();
            _histograms = new ConcurrentDictionary<string, List<double>>();
            _gauges = new ConcurrentDictionary<string, double>();

            // Initialize counters
            _counters.TryAdd(COUNTER_FILES_PROCESSED, 0);
            _counters.TryAdd(COUNTER_FILES_FAILED, 0);
            _counters.TryAdd(COUNTER_FILES_RETRIED, 0);
            _counters.TryAdd(COUNTER_DOCUMENTS_PROCESTED, 0);
            _counters.TryAdd(COUNTER_MANIFEST_ITEMS_PROCESSED, 0);
            _counters.TryAdd(COUNTER_API_CALLS, 0);
            _counters.TryAdd(COUNTER_API_SUCCESS, 0);
            _counters.TryAdd(COUNTER_API_FAILURES, 0);
            _counters.TryAdd(COUNTER_API_TIMEOUTS, 0);
            _counters.TryAdd(COUNTER_DOWNLOADS, 0);
            _counters.TryAdd(COUNTER_DOWNLOADS_SKIPPED, 0);
            _counters.TryAdd(COUNTER_DUPLICATES_DETECTED, 0);
        }

        /// <summary>
        /// Increment a counter
        /// </summary>
        public void IncrementCounter(string counterName, long value = 1)
        {
            _counters.AddOrUpdate(counterName, value, (key, oldValue) => oldValue + value);
        }

        /// <summary>
        /// Record a value in a histogram
        /// </summary>
        public void RecordHistogram(string histogramName, double value)
        {
            _histograms.AddOrUpdate(
                histogramName,
                new List<double> { value },
                (key, oldList) =>
                {
                    lock (_lockObject)
                    {
                        oldList.Add(value);
                        // Keep only last 1000 values to prevent memory growth
                        if (oldList.Count > 1000)
                        {
                            oldList.RemoveAt(0);
                        }
                        return oldList;
                    }
                });
        }

        /// <summary>
        /// Set a gauge value
        /// </summary>
        public void SetGauge(string gaugeName, double value)
        {
            _gauges.AddOrUpdate(gaugeName, value, (key, oldValue) => value);
        }

        /// <summary>
        /// Get counter value
        /// </summary>
        public long GetCounter(string counterName)
        {
            return _counters.TryGetValue(counterName, out var value) ? value : 0;
        }

        /// <summary>
        /// Get histogram statistics
        /// </summary>
        public HistogramStats GetHistogramStats(string histogramName)
        {
            if (!_histograms.TryGetValue(histogramName, out var values) || values.Count == 0)
            {
                return new HistogramStats { Count = 0 };
            }

            lock (_lockObject)
            {
                var sorted = values.OrderBy(v => v).ToList();
                return new HistogramStats
                {
                    Count = sorted.Count,
                    Min = sorted.First(),
                    Max = sorted.Last(),
                    Mean = sorted.Average(),
                    Median = sorted[sorted.Count / 2],
                    P50 = sorted[(int)(sorted.Count * 0.5)],
                    P95 = sorted[(int)(sorted.Count * 0.95)],
                    P99 = sorted[(int)(sorted.Count * 0.99)]
                };
            }
        }

        /// <summary>
        /// Get gauge value
        /// </summary>
        public double GetGauge(string gaugeName)
        {
            return _gauges.TryGetValue(gaugeName, out var value) ? value : 0;
        }

        /// <summary>
        /// Get all metrics snapshot
        /// </summary>
        public ICUMSMetricsSnapshot GetSnapshot()
        {
            return new ICUMSMetricsSnapshot
            {
                Timestamp = DateTime.UtcNow,
                Counters = new Dictionary<string, long>(_counters),
                Gauges = new Dictionary<string, double>(_gauges),
                Histograms = _histograms.Keys.ToDictionary(
                    key => key,
                    key => GetHistogramStats(key))
            };
        }

        /// <summary>
        /// Reset all metrics (useful for testing)
        /// </summary>
        public void Reset()
        {
            _counters.Clear();
            _histograms.Clear();
            _gauges.Clear();
        }

        // Convenience methods for common operations
        public void RecordFileProcessed(long processingTimeMs, long fileSizeBytes, int documentCount)
        {
            IncrementCounter(COUNTER_FILES_PROCESSED);
            RecordHistogram(HISTOGRAM_FILE_PROCESSING_TIME, processingTimeMs);
            RecordHistogram(HISTOGRAM_FILE_SIZE, fileSizeBytes);
            RecordHistogram(HISTOGRAM_DOCUMENTS_PER_FILE, documentCount);
        }

        public void RecordFileFailed()
        {
            IncrementCounter(COUNTER_FILES_FAILED);
        }

        public void RecordFileRetried()
        {
            IncrementCounter(COUNTER_FILES_RETRIED);
        }

        public void RecordDocumentProcessed(long processingTimeMs, int manifestItemCount)
        {
            IncrementCounter(COUNTER_DOCUMENTS_PROCESTED);
            RecordHistogram(HISTOGRAM_DOCUMENT_PROCESSING_TIME, processingTimeMs);
            RecordHistogram(HISTOGRAM_MANIFEST_ITEMS_PER_DOCUMENT, manifestItemCount);
        }

        public void RecordManifestItemsProcessed(int count)
        {
            IncrementCounter(COUNTER_MANIFEST_ITEMS_PROCESSED, count);
        }

        public void RecordApiCall(long responseTimeMs, bool success, bool timeout = false)
        {
            IncrementCounter(COUNTER_API_CALLS);
            RecordHistogram(HISTOGRAM_API_RESPONSE_TIME, responseTimeMs);

            if (timeout)
            {
                IncrementCounter(COUNTER_API_TIMEOUTS);
            }
            else if (success)
            {
                IncrementCounter(COUNTER_API_SUCCESS);
            }
            else
            {
                IncrementCounter(COUNTER_API_FAILURES);
            }
        }

        public void RecordDownload(bool skipped = false, bool duplicate = false)
        {
            if (skipped)
            {
                IncrementCounter(COUNTER_DOWNLOADS_SKIPPED);
            }
            else
            {
                IncrementCounter(COUNTER_DOWNLOADS);
            }

            if (duplicate)
            {
                IncrementCounter(COUNTER_DUPLICATES_DETECTED);
            }
        }
    }

    /// <summary>
    /// Histogram statistics
    /// </summary>
    public class HistogramStats
    {
        public int Count { get; set; }
        public double Min { get; set; }
        public double Max { get; set; }
        public double Mean { get; set; }
        public double Median { get; set; }
        public double P50 { get; set; }
        public double P95 { get; set; }
        public double P99 { get; set; }
    }

    /// <summary>
    /// Complete metrics snapshot
    /// </summary>
    public class ICUMSMetricsSnapshot
    {
        public DateTime Timestamp { get; set; }
        public Dictionary<string, long> Counters { get; set; } = new();
        public Dictionary<string, double> Gauges { get; set; } = new();
        public Dictionary<string, HistogramStats> Histograms { get; set; } = new();
    }
}

