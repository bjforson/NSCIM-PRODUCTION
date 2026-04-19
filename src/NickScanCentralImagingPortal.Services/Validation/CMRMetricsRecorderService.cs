using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Validation
{
    /// <summary>
    /// Background service to record CMR validation metrics hourly
    /// </summary>
    public class CMRMetricsRecorderService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<CMRMetricsRecorderService> _logger;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _recordingInterval;

        public CMRMetricsRecorderService(
            IServiceProvider serviceProvider,
            ILogger<CMRMetricsRecorderService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;

            // Get recording interval from configuration (default: 1 hour)
            var intervalMinutes = _configuration.GetValue<int>("BackgroundServices:CMRMetricsRecorderService:IntervalMinutes", 60);
            _recordingInterval = TimeSpan.FromMinutes(intervalMinutes);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("CMR Metrics Recorder Service started. Recording interval: {Interval}", _recordingInterval);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RecordMetricsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in CMR Metrics Recorder Service");
                }

                await Task.Delay(_recordingInterval, stoppingToken);
            }

            _logger.LogInformation("CMR Metrics Recorder Service stopped");
        }

        private async Task RecordMetricsAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var validationService = scope.ServiceProvider.GetRequiredService<ICMRValidationService>();
            var redownloadService = scope.ServiceProvider.GetRequiredService<ICMRRedownloadService>();
            var context = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

            try
            {
                _logger.LogInformation("Recording CMR validation metrics");

                // Get current statistics
                var statistics = await validationService.GetCMRValidationStatisticsAsync();
                var queueStatus = await redownloadService.GetQueueStatusAsync();
                var queueStats = await redownloadService.GetQueueStatisticsAsync();

                // Get yesterday's metrics to calculate daily changes
                var yesterday = DateTime.UtcNow.AddDays(-1);
                var yesterdayMetrics = context.CMRValidationMetrics
                    .Where(m => m.RecordedAt >= yesterday && m.RecordedAt < DateTime.UtcNow.Date)
                    .OrderByDescending(m => m.RecordedAt)
                    .FirstOrDefault();

                var newRecordsToday = yesterdayMetrics != null
                    ? statistics.TotalCMRRecords - yesterdayMetrics.TotalCMRRecords
                    : 0;

                var fixedRecordsToday = yesterdayMetrics != null
                    ? yesterdayMetrics.InvalidCMRRecords - statistics.InvalidCMRRecords
                    : 0;

                var newIssuesDetectedToday = yesterdayMetrics != null
                    ? statistics.InvalidCMRRecords - yesterdayMetrics.InvalidCMRRecords
                    : 0;

                // Create metrics record
                var metricsRecord = new CMRValidationMetrics
                {
                    RecordedAt = DateTime.UtcNow,
                    TotalCMRRecords = statistics.TotalCMRRecords,
                    ValidCMRRecords = statistics.ValidCMRRecords,
                    InvalidCMRRecords = statistics.InvalidCMRRecords,
                    ValidationSuccessRate = statistics.ValidationSuccessRate,
                    MissingBlNumber = statistics.MissingBlNumber,
                    MissingRotationNumber = statistics.MissingRotationNumber,
                    MissingBothFields = statistics.MissingBothFields,
                    NewRecordsToday = newRecordsToday,
                    FixedRecordsToday = fixedRecordsToday,
                    NewIssuesDetectedToday = newIssuesDetectedToday,
                    QueuePendingCount = queueStatus.PendingItems,
                    QueueProcessingCount = queueStatus.ProcessingItems,
                    QueueCompletedCount = queueStatus.CompletedItems,
                    QueueFailedCount = queueStatus.FailedItems,
                    AverageRedownloadTimeMinutes = queueStats.AverageProcessingTimeMinutes,
                    QueueSuccessRate = queueStats.SuccessRate
                };

                context.CMRValidationMetrics.Add(metricsRecord);
                await context.SaveChangesAsync();

                _logger.LogInformation("✅ CMR validation metrics recorded - Total: {Total}, Valid: {Valid}, Success Rate: {Rate:F2}%",
                    statistics.TotalCMRRecords, statistics.ValidCMRRecords, statistics.ValidationSuccessRate);

                // Clean up old metrics (keep last 90 days)
                var cutoffDate = DateTime.UtcNow.AddDays(-90);
                var oldMetrics = context.CMRValidationMetrics.Where(m => m.RecordedAt < cutoffDate);
                context.CMRValidationMetrics.RemoveRange(oldMetrics);
                var deletedCount = await context.SaveChangesAsync();

                if (deletedCount > 0)
                {
                    _logger.LogInformation("Cleaned up {Count} old metrics records", deletedCount);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error recording CMR validation metrics");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("CMR Metrics Recorder Service is stopping...");
            await base.StopAsync(cancellationToken);
        }
    }
}

