using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickComms.Gateway.Entities;

[Table("email_messages")]
public class EmailMessage
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("from_email")]
    [MaxLength(200)]
    public string FromEmail { get; set; } = string.Empty;

    [Column("from_name")]
    [MaxLength(100)]
    public string? FromName { get; set; }

    [Column("to_email")]
    [MaxLength(200)]
    public string ToEmail { get; set; } = string.Empty;

    [Column("to_name")]
    [MaxLength(100)]
    public string? ToName { get; set; }

    [Column("subject")]
    [MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    [Column("body")]
    public string Body { get; set; } = string.Empty;

    [Column("is_html")]
    public bool IsHtml { get; set; }

    [Column("status")]
    [MaxLength(20)]
    public string Status { get; set; } = "queued";

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
    // Outbox columns (added 2026-04-25). See SmsMessage for the lifecycle
    // contract. Email also persists attachments here as base64 JSON so a
    // crashed worker can resume sending after restart instead of losing the
    // payload that previously lived in the in-memory Channel.
    // ---------------------------------------------------------------------

    [Column("attempt_count")]
    public int AttemptCount { get; set; }

    [Column("next_attempt_at")]
    public DateTime NextAttemptAt { get; set; } = DateTime.UtcNow;

    [Column("processing_started_at")]
    public DateTime? ProcessingStartedAt { get; set; }

    /// <summary>
    /// JSON array of <c>PersistedAttachment {filename, contentType, contentBase64}</c>
    /// captured when the request was accepted. Null/empty means no attachments.
    /// Stored as <c>jsonb</c>; the 10 MB API-side cap keeps row size manageable.
    /// </summary>
    [Column("attachments_json", TypeName = "jsonb")]
    public string? AttachmentsJson { get; set; }
}
