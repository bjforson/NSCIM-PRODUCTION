using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// 🛡️ LAYER 6: MONITORING AND ALERTS
    /// Continuously monitors for duplicate downloads and raises alerts
    /// This is the LAST LINE OF DEFENSE - if duplicates somehow occur, we catch them immediately
    /// </summary>
    public class DuplicateDownloadMonitoringService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DuplicateDownloadMonitoringService> _logger;
        private readonly string _connectionString;
        private const string SERVICE_ID = "[DUPLICATE-MONITOR]";
        private int _consecutiveDuplicateChecks = 0;
        private DateTime _lastAlertSent = DateTime.MinValue;

        public DuplicateDownloadMonitoringService(
            IServiceProvider serviceProvider,
            ILogger<DuplicateDownloadMonitoringService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _connectionString = configuration.GetConnectionString("ICUMS_Downloads_Connection")
                ?? throw new InvalidOperationException("Connection string 'ICUMS_Downloads_Connection' is required but not configured.");
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Wait 5 minutes after startup
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                // Service is being stopped during startup - exit gracefully
                _logger.LogInformation("{ServiceId} Service cancelled during startup delay", SERVICE_ID);
                return;
            }

            _logger.LogWarning("{ServiceId} 🛡️ Duplicate Download Monitoring Service ACTIVE - Checking every 30 minutes", SERVICE_ID);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckForDuplicatesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} Error during duplicate monitoring", SERVICE_ID);
                }

                // Check every 30 minutes
                await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken);
            }
        }

        private async Task CheckForDuplicatesAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var icumDownloadsRepository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();

            try
            {
                _logger.LogDebug("{ServiceId} 🔍 Checking for duplicate downloads...", SERVICE_ID);

                using (var connection = new NpgsqlConnection(_connectionString))
                {
                    await connection.OpenAsync(stoppingToken);

                    // Check for TRUE duplicates (same ContainerNumber AND same DeclarationNumber)
                    // Note: Multiple BOE documents with different DeclarationNumbers for the same container
                    // is VALID (container can have multiple declarations), so we only flag true duplicates
                    var checkSql = @"
                        SELECT 
                            containernumber,
                            declarationnumber,
                            COUNT(*) AS downloadcount,
                            MIN(createdat) AS firstdownload,
                            MAX(createdat) AS lastdownload
                        FROM boedocuments
                        WHERE createdat >= now() AT TIME ZONE 'UTC' - INTERVAL '24 hours'
                        GROUP BY containernumber, declarationnumber
                        HAVING COUNT(*) > 1
                        ORDER BY MAX(createdat) DESC";

                    var duplicates = new List<(string Container, string? Declaration, int Count, DateTime First, DateTime Last)>();

                    using (var command = new NpgsqlCommand(checkSql, connection))
                    {
                        using (var reader = await command.ExecuteReaderAsync(stoppingToken))
                        {
                            while (await reader.ReadAsync(stoppingToken))
                            {
                                duplicates.Add((
                                    reader.GetString(0),
                                    reader.IsDBNull(1) ? null : reader.GetString(1),
                                    reader.GetInt32(2),
                                    reader.GetDateTime(3),
                                    reader.GetDateTime(4)
                                ));
                            }
                        }
                    }

                    // ✅ FIX: DataReader is now closed, safe to use connection for INSERT
                    if (duplicates.Count > 0)
                    {
                        // 🚨 DUPLICATES DETECTED! RAISE ALERT!
                        _consecutiveDuplicateChecks++;

                        _logger.LogError(
                            "{ServiceId} 🚨 CRITICAL ALERT: {Count} DUPLICATE DOWNLOADS DETECTED IN LAST 24 HOURS! " +
                            "Re-download issue has occurred again!",
                            SERVICE_ID, duplicates.Count);

                        foreach (var dup in duplicates.Take(10)) // Show first 10
                        {
                            var hoursBetween = (dup.Last - dup.First).TotalHours;
                            var declInfo = string.IsNullOrEmpty(dup.Declaration) ? "NULL Declaration" : $"Declaration: {dup.Declaration}";
                            _logger.LogError(
                                "{ServiceId} 🔴 DUPLICATE: {Container} ({DeclInfo}) - Downloaded {Count} times, " +
                                "{Hours:F1} hours apart (First: {First:yyyy-MM-dd HH:mm}, Last: {Last:yyyy-MM-dd HH:mm})",
                                SERVICE_ID, dup.Container, declInfo, dup.Count, hoursBetween, dup.First, dup.Last);
                        }

                        // Send alert if we haven't sent one recently
                        if ((DateTime.UtcNow - _lastAlertSent).TotalHours > 6)
                        {
                            await SendCriticalAlertAsync(duplicates, stoppingToken);
                            _lastAlertSent = DateTime.UtcNow;
                        }

                        // AUTOMATIC REMEDIATION: Log to audit table (reader is closed, connection is safe to use)
                        await LogDuplicateIncidentAsync(connection, duplicates, stoppingToken);
                    }
                    else
                    {
                        // No duplicates found - system healthy
                        _consecutiveDuplicateChecks = 0;

                        _logger.LogInformation(
                            "{ServiceId} ✅ No duplicates detected in last 24 hours - system healthy",
                            SERVICE_ID);
                    }

                    // Also check queue health
                    await CheckQueueHealthAsync(connection, stoppingToken);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error checking for duplicates", SERVICE_ID);
            }
        }

        private async Task CheckQueueHealthAsync(
            NpgsqlConnection connection,
            CancellationToken stoppingToken)
        {
            try
            {
                var queueSql = @"
                    SELECT 
                        status,
                        COUNT(*) AS count,
                        AVG(retrycount::double precision) AS avgretries
                    FROM icumsdownloadqueue
                    GROUP BY status";

                using (var command = new NpgsqlCommand(queueSql, connection))
                {
                    using (var reader = await command.ExecuteReaderAsync(stoppingToken))
                    {
                        while (await reader.ReadAsync(stoppingToken))
                        {
                            var status = reader.GetString(0);
                            var count = reader.GetInt32(1);
                            var avgRetries = reader.IsDBNull(2) ? 0 : reader.GetDouble(2);

                            // Alert on abnormal queue states
                            if (status == "Pending" && count > 1000)
                            {
                                _logger.LogWarning(
                                    "{ServiceId} ⚠️ Queue backlog: {Count} pending items",
                                    SERVICE_ID, count);
                            }

                            if (status == "Processing" && count > 10)
                            {
                                _logger.LogWarning(
                                    "{ServiceId} ⚠️ Unusual processing count: {Count} items stuck in Processing",
                                    SERVICE_ID, count);
                            }

                            if (avgRetries > 2)
                            {
                                _logger.LogWarning(
                                    "{ServiceId} ⚠️ High retry rate for {Status} items: {AvgRetries:F1} average retries",
                                    SERVICE_ID, status, avgRetries);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{ServiceId} Error checking queue health", SERVICE_ID);
            }
        }

        private async Task LogDuplicateIncidentAsync(
            NpgsqlConnection connection,
            List<(string Container, string? Declaration, int Count, DateTime First, DateTime Last)> duplicates,
            CancellationToken stoppingToken)
        {
            try
            {
                var insertSql = @"
                    INSERT INTO icumsdownloadaudit (
                        containernumber, 
                        attemptedAt, 
                        action, 
                        result, 
                        errormessage
                    ) VALUES (
                        @ContainerNumber,
                        now() AT TIME ZONE 'UTC',
                        'Duplicate-Detected',
                        'Failed',
                        @ErrorMessage
                    )";

                foreach (var dup in duplicates)
                {
                    using (var command = new NpgsqlCommand(insertSql, connection))
                    {
                        command.Parameters.AddWithValue("@ContainerNumber", dup.Container);
                        var declInfo = string.IsNullOrEmpty(dup.Declaration) ? "NULL Declaration" : $"Declaration: {dup.Declaration}";
                        command.Parameters.AddWithValue("@ErrorMessage",
                            $"Downloaded {dup.Count} times for {declInfo}. First: {dup.First:yyyy-MM-dd HH:mm}, Last: {dup.Last:yyyy-MM-dd HH:mm}");

                        await command.ExecuteNonQueryAsync(stoppingToken);
                    }
                }

                _logger.LogInformation("{ServiceId} Logged {Count} duplicate incidents to audit table",
                    SERVICE_ID, duplicates.Count);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "{ServiceId} Error logging duplicate incidents", SERVICE_ID);
            }
        }

        private async Task SendCriticalAlertAsync(
            List<(string Container, string? Declaration, int Count, DateTime First, DateTime Last)> duplicates,
            CancellationToken stoppingToken)
        {
            try
            {
                // Log critical alert that will be visible in logs
                _logger.LogCritical(
                    "{ServiceId} " + new string('=', 80),
                    SERVICE_ID);
                _logger.LogCritical(
                    "{ServiceId} 🚨🚨🚨 CRITICAL ALERT: RE-DOWNLOAD ISSUE DETECTED 🚨🚨🚨",
                    SERVICE_ID);
                _logger.LogCritical(
                    "{ServiceId} {Count} containers were re-downloaded in the last 24 hours",
                    SERVICE_ID, duplicates.Count);
                _logger.LogCritical(
                    "{ServiceId} This indicates the fix is not working properly",
                    SERVICE_ID);
                _logger.LogCritical(
                    "{ServiceId} ACTION REQUIRED: Review logs and run diagnostic SQL",
                    SERVICE_ID);
                _logger.LogCritical(
                    "{ServiceId} " + new string('=', 80),
                    SERVICE_ID);

                // TODO: Add email/SMS notification integration here
                // TODO: Add Slack/Teams webhook notification here
                // TODO: Add PagerDuty/incident management integration here

                await Task.CompletedTask;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error sending critical alert", SERVICE_ID);
            }
        }
    }
}

