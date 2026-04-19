using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Monitors service health metrics: execution times, memory usage, database connections
    /// Provides visibility into service performance and early detection of issues
    /// </summary>
    public class ServiceHealthMonitor
    {
        private readonly ILogger _logger;
        private readonly ConcurrentDictionary<string, WorkflowMetrics> _workflowMetrics = new();
        private readonly ConcurrentDictionary<string, ServiceMetrics> _serviceMetrics = new();
        private readonly object _lockObject = new();

        public ServiceHealthMonitor(ILogger<ServiceHealthMonitor> logger)
        {
            _logger = logger;
        }

        /// <summary>
        /// Record workflow execution time
        /// </summary>
        public void RecordWorkflowExecution(string serviceName, string workflowName, TimeSpan executionTime, bool success, int itemsProcessed = 0)
        {
            var key = $"{serviceName}:{workflowName}";
            var metrics = _workflowMetrics.GetOrAdd(key, _ => new WorkflowMetrics
            {
                ServiceName = serviceName,
                WorkflowName = workflowName
            });

            lock (metrics)
            {
                metrics.TotalExecutions++;
                metrics.TotalExecutionTime += executionTime;
                metrics.AverageExecutionTime = TimeSpan.FromMilliseconds(
                    metrics.TotalExecutionTime.TotalMilliseconds / metrics.TotalExecutions);

                if (executionTime > metrics.MaxExecutionTime)
                    metrics.MaxExecutionTime = executionTime;

                if (metrics.MinExecutionTime == TimeSpan.Zero || executionTime < metrics.MinExecutionTime)
                    metrics.MinExecutionTime = executionTime;

                if (success)
                    metrics.SuccessfulExecutions++;
                else
                    metrics.FailedExecutions++;

                metrics.TotalItemsProcessed += itemsProcessed;
                metrics.LastExecutionTime = DateTime.UtcNow;
                metrics.LastExecutionDuration = executionTime;
            }

            // Log if execution time is unusually high
            if (executionTime.TotalSeconds > 30)
            {
                _logger.LogWarning(
                    "[HEALTH-MONITOR] Slow workflow detected: {Service}:{Workflow} took {Duration}s",
                    serviceName, workflowName, executionTime.TotalSeconds);
            }
        }

        /// <summary>
        /// Record service-level metrics
        /// </summary>
        public void RecordServiceMetrics(string serviceName, long memoryUsageBytes, int activeConnections = 0)
        {
            var metrics = _serviceMetrics.GetOrAdd(serviceName, _ => new ServiceMetrics
            {
                ServiceName = serviceName
            });

            lock (metrics)
            {
                metrics.MemoryUsageBytes = memoryUsageBytes;
                metrics.ActiveConnections = activeConnections;
                metrics.LastUpdateTime = DateTime.UtcNow;

                // Track memory trends
                if (metrics.PeakMemoryUsageBytes < memoryUsageBytes)
                    metrics.PeakMemoryUsageBytes = memoryUsageBytes;
            }
        }

        /// <summary>
        /// Get workflow metrics
        /// </summary>
        public WorkflowMetrics? GetWorkflowMetrics(string serviceName, string workflowName)
        {
            var key = $"{serviceName}:{workflowName}";
            return _workflowMetrics.TryGetValue(key, out var metrics) ? metrics : null;
        }

        /// <summary>
        /// Get service metrics
        /// </summary>
        public ServiceMetrics? GetServiceMetrics(string serviceName)
        {
            return _serviceMetrics.TryGetValue(serviceName, out var metrics) ? metrics : null;
        }

        /// <summary>
        /// Get all metrics summary
        /// </summary>
        public Dictionary<string, object> GetMetricsSummary()
        {
            var summary = new Dictionary<string, object>();

            foreach (var kvp in _workflowMetrics)
            {
                var metrics = kvp.Value;
                lock (metrics)
                {
                    summary[$"workflow:{kvp.Key}"] = new
                    {
                        TotalExecutions = metrics.TotalExecutions,
                        SuccessfulExecutions = metrics.SuccessfulExecutions,
                        FailedExecutions = metrics.FailedExecutions,
                        AverageExecutionTimeMs = metrics.AverageExecutionTime.TotalMilliseconds,
                        MaxExecutionTimeMs = metrics.MaxExecutionTime.TotalMilliseconds,
                        LastExecutionTime = metrics.LastExecutionTime,
                        TotalItemsProcessed = metrics.TotalItemsProcessed
                    };
                }
            }

            foreach (var kvp in _serviceMetrics)
            {
                var metrics = kvp.Value;
                lock (metrics)
                {
                    summary[$"service:{kvp.Key}"] = new
                    {
                        MemoryUsageMB = metrics.MemoryUsageBytes / (1024.0 * 1024.0),
                        PeakMemoryUsageMB = metrics.PeakMemoryUsageBytes / (1024.0 * 1024.0),
                        ActiveConnections = metrics.ActiveConnections,
                        LastUpdateTime = metrics.LastUpdateTime
                    };
                }
            }

            return summary;
        }

        /// <summary>
        /// Log metrics summary periodically
        /// </summary>
        public void LogMetricsSummary()
        {
            _logger.LogInformation("[HEALTH-MONITOR] === Service Health Metrics Summary ===");

            foreach (var kvp in _workflowMetrics)
            {
                var metrics = kvp.Value;
                lock (metrics)
                {
                    _logger.LogInformation(
                        "[HEALTH-MONITOR] Workflow {Service}:{Workflow} - " +
                        "Executions: {Total} (Success: {Success}, Failed: {Failed}), " +
                        "Avg Time: {AvgMs}ms, Max: {MaxMs}ms, Items: {Items}",
                        metrics.ServiceName, metrics.WorkflowName,
                        metrics.TotalExecutions, metrics.SuccessfulExecutions, metrics.FailedExecutions,
                        metrics.AverageExecutionTime.TotalMilliseconds,
                        metrics.MaxExecutionTime.TotalMilliseconds,
                        metrics.TotalItemsProcessed);
                }
            }

            foreach (var kvp in _serviceMetrics)
            {
                var metrics = kvp.Value;
                lock (metrics)
                {
                    _logger.LogInformation(
                        "[HEALTH-MONITOR] Service {Service} - " +
                        "Memory: {MemoryMB}MB (Peak: {PeakMB}MB), Connections: {Connections}",
                        metrics.ServiceName,
                        metrics.MemoryUsageBytes / (1024.0 * 1024.0),
                        metrics.PeakMemoryUsageBytes / (1024.0 * 1024.0),
                        metrics.ActiveConnections);
                }
            }
        }

        /// <summary>
        /// Measure execution time of a workflow
        /// </summary>
        public async Task<T> MeasureExecutionAsync<T>(
            string serviceName,
            string workflowName,
            Func<Task<T>> workflow,
            Func<T, int>? getItemCount = null)
        {
            var stopwatch = Stopwatch.StartNew();
            bool success = false;
            int itemsProcessed = 0;

            try
            {
                var result = await workflow();
                success = true;
                itemsProcessed = getItemCount?.Invoke(result) ?? 0;
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HEALTH-MONITOR] Workflow {Service}:{Workflow} failed",
                    serviceName, workflowName);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                RecordWorkflowExecution(serviceName, workflowName, stopwatch.Elapsed, success, itemsProcessed);
            }
        }

        /// <summary>
        /// Measure execution time of a workflow (void)
        /// </summary>
        public async Task MeasureExecutionAsync(
            string serviceName,
            string workflowName,
            Func<Task> workflow,
            Func<int>? getItemCount = null)
        {
            var stopwatch = Stopwatch.StartNew();
            bool success = false;
            int itemsProcessed = 0;

            try
            {
                await workflow();
                success = true;
                itemsProcessed = getItemCount?.Invoke() ?? 0;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[HEALTH-MONITOR] Workflow {Service}:{Workflow} failed",
                    serviceName, workflowName);
                throw;
            }
            finally
            {
                stopwatch.Stop();
                RecordWorkflowExecution(serviceName, workflowName, stopwatch.Elapsed, success, itemsProcessed);
            }
        }

        /// <summary>
        /// Get current memory usage for a process
        /// </summary>
        public long GetCurrentMemoryUsage()
        {
            return GC.GetTotalMemory(false);
        }

        /// <summary>
        /// ✅ MEMORY FIX: Cleanup old metrics to prevent unbounded growth
        /// Removes workflow and service metrics older than the specified age
        /// </summary>
        public void CleanupOldMetrics(TimeSpan maxAge)
        {
            var cutoff = DateTime.UtcNow.Subtract(maxAge);
            var removedCount = 0;

            // Cleanup workflow metrics
            var workflowKeysToRemove = _workflowMetrics
                .Where(kvp => kvp.Value.LastExecutionTime < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in workflowKeysToRemove)
            {
                if (_workflowMetrics.TryRemove(key, out _))
                {
                    removedCount++;
                }
            }

            // Cleanup service metrics (keep recent ones, remove only very old)
            var serviceKeysToRemove = _serviceMetrics
                .Where(kvp => kvp.Value.LastUpdateTime < cutoff)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in serviceKeysToRemove)
            {
                if (_serviceMetrics.TryRemove(key, out _))
                {
                    removedCount++;
                }
            }

            if (removedCount > 0)
            {
                _logger.LogInformation("[HEALTH-MONITOR] Cleaned up {Count} old metrics (older than {Age})",
                    removedCount, maxAge);
            }
        }
    }

    /// <summary>
    /// Metrics for a specific workflow
    /// </summary>
    public class WorkflowMetrics
    {
        public string ServiceName { get; set; } = string.Empty;
        public string WorkflowName { get; set; } = string.Empty;
        public int TotalExecutions { get; set; }
        public int SuccessfulExecutions { get; set; }
        public int FailedExecutions { get; set; }
        public TimeSpan TotalExecutionTime { get; set; }
        public TimeSpan AverageExecutionTime { get; set; }
        public TimeSpan MaxExecutionTime { get; set; }
        public TimeSpan MinExecutionTime { get; set; }
        public DateTime LastExecutionTime { get; set; }
        public TimeSpan LastExecutionDuration { get; set; }
        public int TotalItemsProcessed { get; set; }
    }

    /// <summary>
    /// Metrics for a service
    /// </summary>
    public class ServiceMetrics
    {
        public string ServiceName { get; set; } = string.Empty;
        public long MemoryUsageBytes { get; set; }
        public long PeakMemoryUsageBytes { get; set; }
        public int ActiveConnections { get; set; }
        public DateTime LastUpdateTime { get; set; }
    }
}

