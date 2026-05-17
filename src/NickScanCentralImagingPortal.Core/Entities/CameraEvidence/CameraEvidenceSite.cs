using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities.CameraEvidence
{
    public class CameraEvidenceSite
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(80)]
        public string SiteKey { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string DisplayName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? LocationName { get; set; }

        [Required]
        [MaxLength(1000)]
        public string BaseUrl { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? ApiKeySecretName { get; set; }

        [MaxLength(200)]
        public string? WebhookSecretName { get; set; }

        public string? AllowedWebhookSourceCidrsJson { get; set; }

        public bool VerifySsl { get; set; } = true;

        public int RequestTimeoutSeconds { get; set; } = 10;

        public bool IsEnabled { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public virtual ICollection<CameraEvidenceSource> Sources { get; set; } = new List<CameraEvidenceSource>();
    }
}
