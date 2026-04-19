using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Infrastructure.Interceptors;

/// <summary>
/// EF Core interceptor that logs SQL commands exceeding a configurable threshold.
/// Helps identify slow queries for performance profiling.
/// </summary>
public class SlowQueryInterceptor : DbCommandInterceptor
{
    private readonly ILogger<SlowQueryInterceptor> _logger;
    private readonly int _thresholdMs;

    public SlowQueryInterceptor(ILogger<SlowQueryInterceptor> logger, IConfiguration configuration)
    {
        _logger = logger;
        if (int.TryParse(configuration["Performance:SlowQueryThresholdMs"], out var threshold) && threshold > 0)
            _thresholdMs = threshold;
        else
            _thresholdMs = 500;
    }

    public override ValueTask<int> NonQueryExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        int result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.NonQueryExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ReaderExecutedAsync(command, eventData, result, cancellationToken);
    }

    public override ValueTask<object?> ScalarExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        object? result,
        CancellationToken cancellationToken = default)
    {
        LogIfSlow(command, eventData.Duration);
        return base.ScalarExecutedAsync(command, eventData, result, cancellationToken);
    }

    private void LogIfSlow(DbCommand command, TimeSpan duration)
    {
        var elapsedMs = duration.TotalMilliseconds;
        if (elapsedMs >= _thresholdMs)
        {
            var sql = command.CommandText;
            var sqlPreview = sql?.Length > 500 ? sql[..500] + "..." : sql ?? "(null)";
            sqlPreview = string.Join(" ", (sqlPreview ?? "").Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

            _logger.LogWarning(
                "SLOW SQL ({ElapsedMs:F0}ms, threshold={ThresholdMs}ms): {SqlPreview}",
                elapsedMs, _thresholdMs, sqlPreview);
        }
    }
}
