namespace NickComms.Gateway.Configuration;

/// <summary>
/// Tuning knobs for the SMS/email outbox workers. Defaults are conservative —
/// 2-second poll, 10 messages per batch, 5 attempts before permanent fail,
/// 60 s exponential-backoff base, 5 min stuck-row cutoff.
/// </summary>
public class OutboxOptions
{
    public const string SectionName = "Outbox";

    /// <summary>How often the worker scans for queued rows when idle.</summary>
    public int PollIntervalSeconds { get; set; } = 2;

    /// <summary>Maximum rows the worker claims per poll.</summary>
    public int BatchSize { get; set; } = 10;

    /// <summary>Maximum delivery attempts before a message is marked permanently failed.</summary>
    public int MaxAttempts { get; set; } = 5;

    /// <summary>
    /// Backoff base in seconds. Effective delay before retry is
    /// <c>BackoffBaseSeconds × 2^(attempt - 1)</c>, capped at
    /// <see cref="MaxBackoffSeconds"/>.
    /// </summary>
    public int BackoffBaseSeconds { get; set; } = 60;

    /// <summary>Cap on the exponential-backoff delay.</summary>
    public int MaxBackoffSeconds { get; set; } = 1800;

    /// <summary>
    /// Rows in <c>processing</c> state older than this are assumed orphaned
    /// by a crash and reset to <c>queued</c> on worker startup. Must be
    /// generously larger than the worst-case Hubtel/SMTP timeout.
    /// </summary>
    public int StuckRowCutoffMinutes { get; set; } = 5;
}
