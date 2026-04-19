using System.Text.Json;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Monitoring
{
    /// <summary>
    /// Background service that monitors ApplicationLogs for new errors
    /// Detects errors, groups similar ones, and triggers investigation
    /// </summary>
    public class ErrorMonitoringBackgroundService : BackgroundService
    {
        private readonly ILogger<ErrorMonitoringBackgroundService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly string _connectionString;
        private const string SERVICE_ID = "[ERROR-MONITOR]";
        private const int MaxErrorPatternLength = 2000;
        private bool _applicationLogsTableExists = true;
        private int _monitorCycleCount;

        public ErrorMonitoringBackgroundService(
            ILogger<ErrorMonitoringBackgroundService> logger,
            IServiceProvider serviceProvider,
            IConfiguration configuration)
        {
            _logger = logger;
            _serviceProvider = serviceProvider;
            _connectionString = configuration.GetConnectionString("NS_CIS_Connection") ??
                               "Data Source=localhost;Initial Catalog=NS_CIS;Integrated Security=true;TrustServerCertificate=true;";
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 Error Monitoring Background Service starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();

                    // Check if error monitoring is enabled
                    var isEnabled = await settingsProvider.GetBoolAsync("ErrorMonitoring", "Enabled", true);
                    if (!isEnabled)
                    {
                        _logger.LogDebug("Error monitoring is disabled, skipping check");
                        await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                        continue;
                    }

                    var checkIntervalMinutes = await settingsProvider.GetIntAsync("ErrorMonitoring", "CheckIntervalMinutes", 2);
                    var lookbackMinutes = await settingsProvider.GetIntAsync("ErrorMonitoring", "LookbackMinutes", 5);

                    // Detect new errors
                    await DetectAndProcessNewErrorsAsync(lookbackMinutes, stoppingToken);

                    _monitorCycleCount++;
                    if (_monitorCycleCount % 10 == 0)
                    {
                        await ResetStuckInvestigationsAsync(stoppingToken);
                    }

                    // Wait before next check
                    await Task.Delay(TimeSpan.FromMinutes(checkIntervalMinutes), stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error in error monitoring service");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }
        }

        private async Task DetectAndProcessNewErrorsAsync(int lookbackMinutes, CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var investigationService = scope.ServiceProvider.GetRequiredService<IErrorInvestigationService>();

                // Query ApplicationLogs for new errors
                var cutoffTime = DateTime.UtcNow.AddMinutes(-lookbackMinutes);

                var newErrors = await GetNewErrorsFromLogsAsync(cutoffTime, stoppingToken);

                if (newErrors.Count == 0)
                {
                    _logger.LogDebug("No new errors detected in the last {Minutes} minutes", lookbackMinutes);
                    return;
                }

                _logger.LogInformation("🔍 Detected {Count} new error(s) in logs", newErrors.Count);

                // Group similar errors and create/update investigations
                var errorGroups = GroupSimilarErrors(newErrors);

                foreach (var group in errorGroups)
                {
                    try
                    {
                        await investigationService.ProcessErrorGroupAsync(group, stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error processing error group: {Pattern}", group.ErrorPattern);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting new errors");
            }
        }

        private async Task<List<ErrorLogEntry>> GetNewErrorsFromLogsAsync(DateTime cutoffTime, CancellationToken stoppingToken)
        {
            var errors = new List<ErrorLogEntry>();

            if (!_applicationLogsTableExists)
                return errors;

            try
            {
                using var connection = new NpgsqlConnection(_connectionString);
                await connection.OpenAsync(stoppingToken);

                var query = @"
                    SELECT 
                        l.id,
                        l.timestamp,
                        l.level,
                        l.serviceid,
                        l.logger AS operation,
                        l.message,
                        l.exception,
                        l.properties::text AS properties
                    FROM applicationlogs l
                    WHERE l.level = 'Error'
                        AND l.timestamp >= @CutoffTime
                        AND (l.logger IS NULL OR (
                            l.logger NOT ILIKE '%ErrorMonitoring%'
                            AND l.logger NOT ILIKE '%ErrorInvestigation%'))
                        AND NOT EXISTS (
                            SELECT 1 
                            FROM errorinvestigations ei
                            WHERE ei.relatedlogids LIKE '%' || CAST(l.id AS TEXT) || '%'
                        )
                    ORDER BY l.timestamp DESC
                    LIMIT 100";

                using var command = new NpgsqlCommand(query, connection);
                command.Parameters.AddWithValue("@CutoffTime", cutoffTime);

                using var reader = await command.ExecuteReaderAsync(stoppingToken);
                while (await reader.ReadAsync(stoppingToken))
                {
                    errors.Add(new ErrorLogEntry
                    {
                        Id = reader.GetInt64(reader.GetOrdinal("id")),
                        Timestamp = reader.GetDateTime(reader.GetOrdinal("timestamp")),
                        Level = reader.GetString(reader.GetOrdinal("level")),
                        ServiceId = reader.IsDBNull(reader.GetOrdinal("serviceid")) ? null : reader.GetString(reader.GetOrdinal("serviceid")),
                        Operation = reader.IsDBNull(reader.GetOrdinal("operation")) ? null : reader.GetString(reader.GetOrdinal("operation")),
                        Message = reader.GetString(reader.GetOrdinal("message")),
                        Exception = reader.IsDBNull(reader.GetOrdinal("exception")) ? null : reader.GetString(reader.GetOrdinal("exception")),
                        Properties = reader.IsDBNull(reader.GetOrdinal("properties")) ? null : reader.GetString(reader.GetOrdinal("properties"))
                    });
                }
            }
            catch (PostgresException ex) when (ex.SqlState == "42P01")
            {
                _applicationLogsTableExists = false;
                _logger.LogWarning("ApplicationLogs/ErrorInvestigations table does not exist - skipping error monitoring queries until restart");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error querying ApplicationLogs for new errors");
            }

            return errors;
        }

        private async Task ResetStuckInvestigationsAsync(CancellationToken stoppingToken)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var cutoff = DateTime.UtcNow.AddMinutes(-30);
                var stuck = await db.ErrorInvestigations
                    .AsTracking()
                    .Where(ei => ei.Status == "Investigating" && ei.UpdatedAt < cutoff)
                    .ToListAsync(stoppingToken);

                foreach (var ei in stuck)
                {
                    ei.Status = "New";
                    ei.UpdatedAt = DateTime.UtcNow;
                }

                if (stuck.Count > 0)
                {
                    await db.SaveChangesAsync(stoppingToken);
                    _logger.LogInformation("Reset {Count} stuck investigation(s) from Investigating to New", stuck.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Could not reset stuck investigations");
            }
        }

        private List<ErrorGroupDto> GroupSimilarErrors(List<ErrorLogEntry> errors)
        {
            var groups = new Dictionary<string, ErrorGroupDto>();

            foreach (var error in errors)
            {
                // Create a fingerprint/pattern for grouping similar errors
                var pattern = CreateErrorPattern(error);

                if (!groups.ContainsKey(pattern))
                {
                    groups[pattern] = new ErrorGroupDto
                    {
                        ErrorPattern = pattern,
                        ErrorCode = ExtractErrorCode(error.Message),
                        ServiceId = error.ServiceId,
                        Operation = error.Operation,
                        ExceptionType = ExtractExceptionType(error.Exception),
                        Errors = new List<ErrorLogEntryDto>()
                    };
                }

                groups[pattern].Errors.Add(new ErrorLogEntryDto
                {
                    Id = error.Id,
                    Timestamp = error.Timestamp,
                    Level = error.Level,
                    ServiceId = error.ServiceId,
                    Operation = error.Operation,
                    Message = error.Message,
                    Exception = error.Exception,
                    Properties = error.Properties
                });
            }

            return groups.Values.ToList();
        }

        private string CreateErrorPattern(ErrorLogEntry error)
        {
            // Create a pattern based on:
            // 1. Exception type
            // 2. Service/Operation
            // 3. Error message keywords (normalized)

            var exceptionType = ExtractExceptionType(error.Exception) ?? "UnknownException";
            var serviceOp = $"{error.ServiceId ?? "Unknown"}:{error.Operation ?? "Unknown"}";
            var messageKey = NormalizeErrorMessage(error.Message);

            var raw = $"{exceptionType}|{serviceOp}|{messageKey}";
            if (raw.Length <= MaxErrorPatternLength)
                return raw;
            return raw[..MaxErrorPatternLength];
        }

        private string? ExtractErrorCode(string message)
        {
            // Look for error codes like ERR_1000, ERR_2001, etc.
            var match = System.Text.RegularExpressions.Regex.Match(message, @"ERR_\d{4}");
            return match.Success ? match.Value : null;
        }

        private string? ExtractExceptionType(string? exception)
        {
            if (string.IsNullOrEmpty(exception)) return null;

            // Extract exception type from stack trace
            var lines = exception.Split('\n');
            if (lines.Length > 0)
            {
                var firstLine = lines[0].Trim();
                var match = System.Text.RegularExpressions.Regex.Match(firstLine, @"^(\w+(?:\.\w+)*):");
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }

        private string NormalizeErrorMessage(string message)
        {
            // Normalize error message by:
            // 1. Removing variable values (IDs, timestamps, etc.)
            // 2. Keeping structure and keywords

            var normalized = message;

            // Remove GUIDs
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}", "GUID");

            // Remove numbers (IDs, counts, etc.)
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\b\d+\b", "N");

            // Remove timestamps
            normalized = System.Text.RegularExpressions.Regex.Replace(normalized, @"\d{4}-\d{2}-\d{2}[\sT]\d{2}:\d{2}:\d{2}", "TIMESTAMP");

            return normalized.Trim();
        }

        private class ErrorLogEntry
        {
            public long Id { get; set; }
            public DateTime Timestamp { get; set; }
            public string Level { get; set; } = string.Empty;
            public string? ServiceId { get; set; }
            public string? Operation { get; set; }
            public string Message { get; set; } = string.Empty;
            public string? Exception { get; set; }
            public string? Properties { get; set; }
        }

        private class ErrorGroup
        {
            public string ErrorPattern { get; set; } = string.Empty;
            public string? ErrorCode { get; set; }
            public string? ServiceId { get; set; }
            public string? Operation { get; set; }
            public string? ExceptionType { get; set; }
            public List<ErrorLogEntry> Errors { get; set; } = new();
        }
    }
}

