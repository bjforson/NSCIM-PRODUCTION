using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickComms.Gateway.Entities;

[Table("otp_sessions")]
public class OtpSession
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("phone_number")]
    [MaxLength(20)]
    public string PhoneNumber { get; set; } = string.Empty;

    [Column("hubtel_request_id")]
    [MaxLength(50)]
    public string HubtelRequestId { get; set; } = string.Empty;

    [Column("prefix")]
    [MaxLength(10)]
    public string Prefix { get; set; } = string.Empty;

    [Column("client_app")]
    [MaxLength(50)]
    public string ClientApp { get; set; } = string.Empty;

    [Column("verified")]
    public bool Verified { get; set; }

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
