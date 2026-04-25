using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NickComms.Gateway.Configuration;
using NickComms.Gateway.Data;
using NickComms.Gateway.Entities;

namespace NickComms.Gateway.Services;

/// <summary>
/// SMS outbox worker — replaces the previous in-memory <c>Channel&lt;T&gt;</c>
/// implementation that lost queued messages on crash. Lifecycle and persistence
/// live entirely on the <c>sms_messages</c> row:
/// <list type="number">
///   <item>API writes a row with <c>status='queued'</c> and <c>next_attempt_at = NOW</c>.</item>
///   <item>This worker polls every <c>OutboxOptions.PollIntervalSeconds</c>.
///         On each tick it claims up to <c>BatchSize</c> rows by flipping their
///         status to <c>processing</c>, stamping <c>processing_started_at</c>,
///         and incrementing <c>attempt_count</c>.</item>
///   <item>For each claimed row, it calls Hubtel and either marks the row
///         <c>sent</c> / permanent <c>failed</c>, or returns it to <c>queued</c>
///         with <c>next_attempt_at = NOW + exponential backoff</c>.</item>
///   <item>On startup the worker also resets any <c>processing</c> rows whose
///         <c>processing_started_at</c> is older than the stuck-row cutoff —
///         that's the only way a crashed worker's claim gets released.</item>
/// </list>
/// Single-instance enforcement (the Mutex in <c>Program.cs</c>) means we don't
/// need <c>FOR UPDATE SKIP LOCKED</c>; the simple LINQ <c>Where + Take</c>
/// path is enough. If we ever go multi-instance, swap the claim block for raw
/// SQL with that lock hint.
///
/// The pre-existing <c>Enqueue()</c> + <c>QueuedCount</c> API surface remains
/// for backward compatibility with <c>SmsService</c>, but they're now no-ops
/// — the DB row commit IS the queue signal, and the in-memory channel is gone.
/// </summary>
public class SmsQueueService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<SmsQueueService> _logger;
    private readonly OutboxOptions _options;

    public SmsQueueService(
        IServiceScopeFactory scopeFactory,
        ILogger<SmsQueueService> logger,
        IOptions<OutboxOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options.Value;
    }

    /// <summary>Compatibility shim — DB-row commit IS the queue signal now.</summary>
    public void Enqueue(SmsQueueItem _) { /* no-op, kept for caller compat */ }

    /// <summary>Live count of <c>queued</c> rows ready to send right now.</summary>
    public int QueuedCount
    {
        get
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();
            return db.SmsMessages.Count(m => m.Status == "queued" && m.NextAttemptAt <= DateTime.UtcNow);
        }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "SmsQueueService (outbox) started — poll: {Poll}s batch: {Batch} maxAttempts: {Max}",
            _options.PollIntervalSeconds, _options.BatchSize, _options.MaxAttempts);

        // One-shot startup recovery: any 'processing' rows from a previous
        // crashed run get released back to 'queued' so they're picked up.
        await ReleaseStuckRowsAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var processed = await DrainBatchAsync(stoppingToken);
                if (processed == 0)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_options.PollIntervalSeconds), stoppingToken);
                }
                // If we did process something, loop immediately so a deep backlog drains fast.
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger.LogError(ex, "SMS outbox loop error — backing off 5s");
                await Task.Delay(5000, stoppingToken);
            }
        }
    }

    private async Task<int> DrainBatchAsync(CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();

        var now = DateTime.UtcNow;
        // Single-instance: a non-locking LINQ claim is sufficient. We mutate
        // status BEFORE leaving the using-scope so SaveChanges flushes the
        // claim transactionally with the increment.
        var batch = await db.SmsMessages
            .Where(m => m.Status == "queued" && m.NextAttemptAt <= now)
            .OrderBy(m => m.CreatedAt)
            .Take(_options.BatchSize)
            .ToListAsync(ct);

        if (batch.Count == 0) return 0;

        foreach (var m in batch)
        {
            m.Status = "processing";
            m.ProcessingStartedAt = now;
            m.AttemptCount += 1;
        }
        await db.SaveChangesAsync(ct);

        // Process outside the claim transaction; each result writes its own
        // status update. The Hubtel call is the slow leg — keep it off the
        // shared DbContext used for the claim.
        foreach (var m in batch)
        {
            await ProcessOneAsync(m, ct);
        }
        return batch.Count;
    }

    private async Task ProcessOneAsync(SmsMessage row, CancellationToken ct)
    {
        using var scope = _scopeFactory.CreateScope();
        var hubtel = scope.ServiceProvider.GetRequiredService<IHubtelClient>();
        var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();

        try
        {
            var result = await hubtel.SendSmsAsync(row.SenderId, row.Recipient, row.Content, ct);

            if (result != null && result.Status == 0)
            {
                await db.SmsMessages.Where(m => m.Id == row.Id)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(m => m.Status, "sent")
                        .SetProperty(m => m.HubtelMessageId, result.MessageId)
                        .SetProperty(m => m.HubtelRate, result.Rate)
                        .SetProperty(m => m.SentAt, DateTime.UtcNow), ct);

                _logger.LogInformation("SMS sent to {To}: hubtelId={HubtelId} rate={Rate} attempt={Attempt}",
                    row.Recipient, result.MessageId, result.Rate, row.AttemptCount);
            }
            else
            {
                var err = result == null ? "Hubtel API returned null response" : $"Hubtel status={result.Status}";
                await HandleFailureAsync(db, row, err, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "SMS attempt {Attempt} failed for {Id} → {To}",
                row.AttemptCount, row.Id, row.Recipient);
            await HandleFailureAsync(db, row, ex.Message, ct);
        }
    }

    private async Task HandleFailureAsync(CommsDbContext db, SmsMessage row, string error, CancellationToken ct)
    {
        if (row.AttemptCount >= _options.MaxAttempts)
        {
            await db.SmsMessages.Where(m => m.Id == row.Id)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, "failed")
                    .SetProperty(m => m.ErrorMessage, error)
                    .SetProperty(m => m.SentAt, DateTime.UtcNow), ct);

            _logger.LogError("SMS {Id} permanently failed after {Attempts} attempts: {Error}",
                row.Id, row.AttemptCount, error);
            return;
        }

        // Transient — return to queued with exponential backoff.
        var delay = Math.Min(
            _options.BackoffBaseSeconds * (int)Math.Pow(2, Math.Max(0, row.AttemptCount - 1)),
            _options.MaxBackoffSeconds);

        await db.SmsMessages.Where(m => m.Id == row.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(m => m.Status, "queued")
                .SetProperty(m => m.NextAttemptAt, DateTime.UtcNow.AddSeconds(delay))
                .SetProperty(m => m.ErrorMessage, error), ct);
    }

    private async Task ReleaseStuckRowsAsync(CancellationToken ct)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();
            var cutoff = DateTime.UtcNow.AddMinutes(-_options.StuckRowCutoffMinutes);

            var released = await db.SmsMessages
                .Where(m => m.Status == "processing" && m.ProcessingStartedAt < cutoff)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(m => m.Status, "queued")
                    .SetProperty(m => m.NextAttemptAt, DateTime.UtcNow)
                    .SetProperty(m => m.ProcessingStartedAt, (DateTime?)null), ct);

            if (released > 0)
            {
                _logger.LogWarning("Released {Count} stuck SMS rows (>{Cutoff}min in processing)",
                    released, _options.StuckRowCutoffMinutes);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to release stuck SMS rows on startup");
        }
    }
}

/// <summary>
/// Legacy DTO kept as the parameter type of <see cref="SmsQueueService.Enqueue"/>
/// for source compatibility with <c>SmsService</c>. The fields are no longer
/// read — the worker hydrates everything from the DB row.
/// </summary>
public class SmsQueueItem
{
    public Guid MessageId { get; set; }
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}
