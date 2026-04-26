using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickComms.Gateway.Entities;

[Table("sms_messages")]
public class SmsMessage
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("sender_id")]
    [MaxLength(20)]
    public string SenderId { get; set; } = string.Empty;

    [Column("recipient")]
    [MaxLength(20)]
    public string Recipient { get; set; } = string.Empty;

    [Column("content")]
    public string Content { get; set; } = string.Empty;

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "queued";

    [Column("hubtel_message_id")]
    [MaxLength(50)]
    public string? HubtelMessageId { get; set; }

    [Column("hubtel_rate")]
    public decimal? HubtelRate { get; set; }

    [Column("batch_id")]
    public Guid? BatchId { get; set; }

    [Column("client_app")]
    [MaxLength(50)]
    public string ClientApp { get; set; } = string.Empty;

    [Column("client_reference")]
    [MaxLength(100)]
    public string? ClientReference { get; set; }

    [Column("error_message")]
    public string? ErrorMessage { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("sent_at")]
    public DateTime? SentAt { get; set; }

    // ---------------------------------------------------------------------
    // Outbox columns (added 2026-04-25). Replaces the in-memory Channel that
    // would lose queued messages on crash. Lifecycle:
    //   queued (next_attempt_at <= NOW)
    //     → processing (worker claims it, sets processing_started_at, ++attempts)
    //         → sent / failed (terminal)
    //         → queued again with next_attempt_at = NOW + backoff (transient retry)
    //   processing rows older than the stuck-row cutoff get reset to queued
    //   on worker startup.
    // ---------------------------------------------------------------------

    [Column("attempt_count")]
    public int AttemptCount { get; set; }

    /// <summary>
    /// Earliest UTC time the worker is allowed to pick this row up. Set to
    /// <c>NOW()</c> on insert (ready immediately) and to <c>NOW + backoff</c>
    /// on transient retry.
    /// </summary>
    [Column("next_attempt_at")]
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Set when the worker claims the row. Drives the stuck-row sweeper —
    /// any row in <c>processing</c> older than the cutoff is assumed to have
    /// been orphaned by a crash and gets reset to <c>queued</c>.
    /// </summary>
    [Column("processing_started_at")]
    public DateTime? ProcessingStartedAt { get; set; }
}
