using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickComms.Gateway.Entities;

[Table("api_keys")]
public class ApiKey
{
    [Key]
    [Column("id")]
    public Guid Id { get; set; } = Guid.NewGuid();

    [Column("app_name")]
    [MaxLength(50)]
    public string AppName { get; set; } = string.Empty;

    [Column("key_hash")]
    [MaxLength(128)]
    public string KeyHash { get; set; } = string.Empty;

    [Column("key_prefix")]
    [MaxLength(8)]
    public string KeyPrefix { get; set; } = string.Empty;

    [Column("is_active")]
    public bool IsActive { get; set; } = true;

    [Column("created_at")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("last_used_at")]
    public DateTime? LastUsedAt { get; set; }
}
