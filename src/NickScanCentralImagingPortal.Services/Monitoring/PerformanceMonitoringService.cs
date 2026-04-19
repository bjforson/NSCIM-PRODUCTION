using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Real-time performance monitoring service that tracks system metrics
    /// </summary>
    public class PerformanceMonitoringService : BackgroundService, IPerformanceMonitoringService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PerformanceMonitoringService> _logger;
        private readonly IMemoryCache _memoryCache;
        private readonly ConcurrentDictionary<string, PerformanceMetric> _metrics;
        private readonly Process _currentProcess;
        private const string SERVICE_ID = "[PERFORMANCE-MONITOR]";

        public PerformanceMonitoringService(
            IServiceProvider serviceProvider,
            ILogger<PerformanceMonitoringService> logger,
            IMemoryCache memoryCache)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _memoryCache = memoryCache;
            _metrics = new ConcurrentDictionary<string, PerformanceMetric>();
            _currentProcess = Process.GetCurrentProcess();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 {ServiceId} Performance monitoring service starting", SERVICE_ID);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CollectPerformanceMetrics();
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error in {ServiceId} performance monitoring", SERVICE_ID);
                    try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
                    catch (OperationCanceledException) { break; }
                }
            }

            _logger.LogInformation("{ServiceId} Performance monitoring service stopped", SERVICE_ID);
        }

        private async Task CollectPerformanceMetrics()
        {
            var timestamp = DateTime.UtcNow;

            // System Resource Metrics
            await CollectSystemResourceMetrics(timestamp);

            // Database Performance Metrics
            await CollectDatabasePerformanceMetrics(timestamp);

            // Memory Usage Metrics
            await CollectMemoryUsageMetrics(timestamp);

            // Background Service Metrics
            await CollectBackgroundServiceMetrics(timestamp);

            // API Performance Metrics
            await CollectApiPerformanceMetrics(timestamp);

            // Cleanup old metrics (keep last 24 hours)
            CleanupOldMetrics();
        }

        private async Task CollectSystemResourceMetrics(DateTime timestamp)
        {
            try
            {
                // CPU Usage
                var cpuUsage = await GetCpuUsageAsync();
                RecordMetric("System.CPU.Usage", cpuUsage, timestamp, "percentage");

                // Memory Usage
                var memoryUsage = _currentProcess.WorkingSet64 / 1024 / 1024; // MB
                RecordMetric("System.Memory.WorkingSet", memoryUsage, timestamp, "MB");

                // Private Memory
                var privateMemory = _currentProcess.PrivateMemorySize64 / 1024 / 1024; // MB
                RecordMetric("System.Memory.Private", privateMemory, timestamp, "MB");

                // GC Collections
                var gen0Collections = GC.CollectionCount(0);
                var gen1Collections = GC.CollectionCount(1);
                var gen2Collections = GC.CollectionCount(2);

                RecordMetric("System.GC.Gen0Collections", gen0Collections, timestamp, "count");
                RecordMetric("System.GC.Gen1Collections", gen1Collections, timestamp, "count");
                RecordMetric("System.GC.Gen2Collections", gen2Collections, timestamp, "count");

                // Thread Count
                var threadCount = _currentProcess.Threads.Count;
                RecordMetric("System.Threads.Count", threadCount, timestamp, "count");

                _logger.LogDebug("{ServiceId} Collected system resource metrics", SERVICE_ID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error collecting system resource metrics");
            }
        }

        private async Task CollectDatabasePerformanceMetrics(DateTime timestamp)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var appDbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var icumDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

                // Database Response Time Test
                var stopwatch = Stopwatch.StartNew();
                await appDbContext.Database.ExecuteSqlRawAsync("SELECT 1");
                stopwatch.Stop();
                RecordMetric("Database.App.ResponseTime", stopwatch.ElapsedMilliseconds, timestamp, "ms");

                stopwatch.Restart();
                await icumDbContext.Database.ExecuteSqlRawAsync("SELECT 1");
                stopwatch.Stop();
                RecordMetric("Database.ICUMS.ResponseTime", stopwatch.ElapsedMilliseconds, timestamp, "ms");

                _logger.LogDebug("{ServiceId} Collected database performance metrics", SERVICE_ID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error collecting database performance metrics");
            }
        }

        private Task CollectMemoryUsageMetrics(DateTime timestamp)
        {
            try
            {
                // Managed Memory
                var managedMemory = GC.GetTotalMemory(false) / 1024 / 1024; // MB
                RecordMetric("Memory.Managed.Total", managedMemory, timestamp, "MB");

                // Available Memory
                var availableMemory = GC.GetTotalMemory(true) / 1024 / 1024; // MB
                RecordMetric("Memory.Managed.Available", availableMemory, timestamp, "MB");

                // Cache Memory (if using IMemoryCache)
                var cacheSize = GetCacheSize();
                RecordMetric("Memory.Cache.Size", cacheSize, timestamp, "items");

                _logger.LogDebug("{ServiceId} Collected memory usage metrics", SERVICE_ID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error collecting memory usage metrics");
            }

            return Task.CompletedTask;
        }

        private async Task CollectBackgroundServiceMetrics(DateTime timestamp)
        {
            try
            {
                // This would integrate with your existing health check service
                // For now, we'll track basic service status
                var services = new[]
                {
                    "ServiceOrchestratorBackgroundService",
                    "FS6000BackgroundService",
                    "AseBackgroundService",
                    "IcumBackgroundService",
                    "ICUMSDownloadBackgroundService",
                    "IcumFileScannerService",
                    "IcumJsonIngestionService",
                    "IcumDataTransferService",
                    "ContainerCompletenessService",
                    "ComprehensiveHealthCheckService"
                };

                foreach (var serviceName in services)
                {
                    // Check if service is running (this would need to be implemented)
                    var isRunning = await IsServiceRunningAsync(serviceName);
                    RecordMetric($"Service.{serviceName}.IsRunning", isRunning ? 1 : 0, timestamp, "boolean");
                }

                _logger.LogDebug("{ServiceId} Collected background service metrics", SERVICE_ID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error collecting background service metrics");
            }
        }

        private async Task CollectApiPerformanceMetrics(DateTime timestamp)
        {
            try
            {
                // This would integrate with your API middleware to track response times
                // For now, we'll track basic API health
                var apiHealth = await CheckApiHealthAsync();
                RecordMetric("API.Health.Status", apiHealth ? 1 : 0, timestamp, "boolean");

                _logger.LogDebug("{ServiceId} Collected API performance metrics", SERVICE_ID);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "❌ Error collecting API performance metrics");
            }
        }

        private void RecordMetric(string name, double value, DateTime timestamp, string unit)
        {
            var metric = new PerformanceMetric
            {
                Name = name,
                Value = value,
                Timestamp = timestamp,
                Unit = unit
            };

            _metrics.AddOrUpdate(name, metric, (key, existing) => metric);

            try
            {
                var cacheKey = $"PerformanceMetric:{name}";
                var cacheOptions = new MemoryCacheEntryOptions
                {
                    AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5),
                    Size = 1
                };
                _memoryCache.Set(cacheKey, metric, cacheOptions);
            }
            catch (ObjectDisposedException) { }
        }

        private void CleanupOldMetrics()
        {
            var cutoffTime = DateTime.UtcNow.AddHours(-24);
            var keysToRemove = _metrics
                .Where(kvp => kvp.Value.Timestamp < cutoffTime)
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in keysToRemove)
            {
                _metrics.TryRemove(key, out _);
            }
        }

        // Helper methods
        private async Task<double> GetCpuUsageAsync()
        {
            try
            {
                var startTime = DateTime.UtcNow;
                var startCpuUsage = _currentProcess.TotalProcessorTime;

                await Task.Delay(100); // Wait 100ms

                var endTime = DateTime.UtcNow;
                var endCpuUsage = _currentProcess.TotalProcessorTime;

                var cpuUsedMs = (endCpuUsage - startCpuUsage).TotalMilliseconds;
                var totalMsPassed = (endTime - startTime).TotalMilliseconds;
                var cpuUsageTotal = cpuUsedMs / (Environment.ProcessorCount * totalMsPassed);

                return Math.Round(cpuUsageTotal * 100, 2);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("CPU usage measurement failed: {Error}", ex.Message);
                return 0;
            }
        }

        private int GetCacheSize()
        {
            try
            {
                return _memoryCache is MemoryCache mc ? mc.Count : 0;
            }
            catch (ObjectDisposedException)
            {
                return 0;
            }
        }

        private async Task<bool> IsServiceRunningAsync(string serviceName)
        {
            // This would integrate with your service orchestrator
            // For now, return true as a placeholder
            return await Task.FromResult(true);
        }

        private async Task<bool> CheckApiHealthAsync()
        {
            try
            {
                // This would make a health check call to your API
                // For now, return true as a placeholder
                return await Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogDebug("API health check failed: {Error}", ex.Message);
                return false;
            }
        }

        // Public methods for API access
        public Dictionary<string, PerformanceMetric> GetCurrentMetrics()
        {
            return _metrics.ToDictionary(kvp => kvp.Key, kvp => kvp.Value);
        }

        public List<PerformanceMetric> GetMetricsForService(string serviceName)
        {
            return _metrics
                .Where(kvp => kvp.Key.StartsWith($"Service.{serviceName}"))
                .Select(kvp => kvp.Value)
                .ToList();
        }

        public PerformanceSummary GetPerformanceSummary()
        {
            var currentTime = DateTime.UtcNow;
            var last5Minutes = currentTime.AddMinutes(-5);
            var lastHour = currentTime.AddHours(-1);

            var recentMetrics = _metrics.Values
                .Where(m => m.Timestamp >= last5Minutes)
                .ToList();

            var hourlyMetrics = _metrics.Values
                .Where(m => m.Timestamp >= lastHour)
                .ToList();

            return new PerformanceSummary
            {
                Timestamp = currentTime,
                TotalMetrics = _metrics.Count,
                RecentMetricsCount = recentMetrics.Count,
                HourlyMetricsCount = hourlyMetrics.Count,
                SystemHealth = CalculateSystemHealth(recentMetrics),
                TopMemoryConsumers = GetTopMemoryConsumers(hourlyMetrics),
                PerformanceAlerts = GetPerformanceAlerts(recentMetrics)
            };
        }

        private string CalculateSystemHealth(List<PerformanceMetric> metrics)
        {
            // Simple health calculation based on key metrics
            var cpuUsage = metrics.FirstOrDefault(m => m.Name == "System.CPU.Usage")?.Value ?? 0;
            var memoryUsage = metrics.FirstOrDefault(m => m.Name == "System.Memory.WorkingSet")?.Value ?? 0;
            var dbResponseTime = metrics.FirstOrDefault(m => m.Name == "Database.App.ResponseTime")?.Value ?? 0;

            if (cpuUsage > 95 || memoryUsage > 3000 || dbResponseTime > 5000)
                return "Critical";
            if (cpuUsage > 80 || memoryUsage > 2000 || dbResponseTime > 1000)
                return "Degraded";

            return "Healthy";
        }

        private List<string> GetTopMemoryConsumers(List<PerformanceMetric> metrics)
        {
            return metrics
                .Where(m => m.Name.Contains("Memory") && m.Unit == "MB")
                .OrderByDescending(m => m.Value)
                .Take(5)
                .Select(m => $"{m.Name}: {m.Value:F1}MB")
                .ToList();
        }

        private List<string> GetPerformanceAlerts(List<PerformanceMetric> metrics)
        {
            var alerts = new List<string>();

            var cpuUsage = metrics.FirstOrDefault(m => m.Name == "System.CPU.Usage")?.Value ?? 0;
            if (cpuUsage > 80)
                alerts.Add($"High CPU Usage: {cpuUsage:F1}%");

            var memoryUsage = metrics.FirstOrDefault(m => m.Name == "System.Memory.WorkingSet")?.Value ?? 0;
            if (memoryUsage > 2000)
                alerts.Add($"High Memory Usage: {memoryUsage:F1}MB");

            var dbResponseTime = metrics.FirstOrDefault(m => m.Name == "Database.App.ResponseTime")?.Value ?? 0;
            if (dbResponseTime > 1000)
                alerts.Add($"Slow Database Response: {dbResponseTime}ms");

            return alerts;
        }
    }
}
