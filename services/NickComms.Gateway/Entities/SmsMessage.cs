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
}
