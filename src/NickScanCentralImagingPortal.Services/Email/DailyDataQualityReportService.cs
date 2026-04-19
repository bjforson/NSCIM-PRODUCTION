using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Email
{
    /// <summary>
    /// Background service to generate and send daily data quality reports
    /// </summary>
    public class DailyDataQualityReportService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DailyDataQualityReportService> _logger;
        private readonly IConfiguration _configuration;
        private readonly bool _enabled;

        public DailyDataQualityReportService(
            IServiceProvider serviceProvider,
            ILogger<DailyDataQualityReportService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
            _enabled = bool.Parse(_configuration["BackgroundServices:DailyDataQualityReportService:Enabled"] ?? "true");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!_enabled)
            {
                _logger.LogInformation("Daily Data Quality Report Service is disabled");
                return;
            }

            _logger.LogInformation("Daily Data Quality Report Service started");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Calculate time until next 8:00 AM
                    var now = DateTime.Now;
                    var nextRunTime = now.Date.AddHours(8); // 8:00 AM today

                    if (now > nextRunTime)
                    {
                        // If it's past 8 AM today, schedule for tomorrow
                        nextRunTime = nextRunTime.AddDays(1);
                    }

                    var delay = nextRunTime - now;

                    _logger.LogInformation("Next daily report scheduled for {NextRunTime} (in {Hours} hours)",
                        nextRunTime, delay.TotalHours);

                    // Wait until 8:00 AM
                    try
                    {
                        await Task.Delay(delay, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // Service is being stopped - exit gracefully
                        _logger.LogInformation("Daily Data Quality Report Service cancelled");
                        return;
                    }

                    if (!stoppingToken.IsCancellationRequested)
                    {
                        await GenerateAndSendReportAsync();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in Daily Data Quality Report Service");
                    // Wait 1 hour before retrying if there's an error
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }

            _logger.LogInformation("Daily Data Quality Report Service stopped");
        }

        private async Task GenerateAndSendReportAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var validationService = scope.ServiceProvider.GetRequiredService<ICMRValidationService>();
            var redownloadService = scope.ServiceProvider.GetRequiredService<ICMRRedownloadService>();
            var emailService = scope.ServiceProvider.GetRequiredService<IEmailService>();

            try
            {
                _logger.LogInformation("Generating daily data quality report");

                // Get CMR validation statistics
                var statistics = await validationService.GetCMRValidationStatisticsAsync();

                // Get problematic records
                var problematicRecords = await validationService.GetProblematicCMRRecordsAsync();

                // Get re-download queue statistics
                var queueStats = await redownloadService.GetQueueStatisticsAsync();

                // Build report model
                var report = new DataQualityReportModel
                {
                    ReportDate = DateTime.Today.AddDays(-1), // Yesterday's data
                    TotalCMRRecords = statistics.TotalCMRRecords,
                    ValidRecords = statistics.ValidCMRRecords,
                    InvalidRecords = statistics.InvalidCMRRecords,
                    SuccessRate = statistics.ValidationSuccessRate,
                    NewRecordsToday = 0, // TODO: Track daily record counts
                    FixedRecordsToday = 0, // TODO: Track records fixed today
                    ProblematicContainers = problematicRecords.Select(r =>
                        $"{r.ContainerNumber} - Missing: {string.Join(", ", r.MissingFields)}").ToList(),
                    QueuedForRedownload = queueStats.PendingProcessing,
                    SuccessfulRedownloads = queueStats.TotalSuccessful,
                    FailedRedownloads = queueStats.TotalFailed
                };

                // Send the report
                var success = await emailService.SendDailyDataQualityReportAsync(report);

                if (success)
                {
                    _logger.LogInformation("✅ Daily data quality report sent successfully");
                }
                else
                {
                    _logger.LogWarning("⚠️ Failed to send daily data quality report");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating daily data quality report");
            }
        }

        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            _logger.LogInformation("Daily Data Quality Report Service is stopping...");
            await base.StopAsync(cancellationToken);
        }
    }
}

