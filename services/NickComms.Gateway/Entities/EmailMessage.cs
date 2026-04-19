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
}
