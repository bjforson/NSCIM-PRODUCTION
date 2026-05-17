using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities.CameraEvidence
{
    public class CameraEvidenceAuditLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid? SiteId { get; set; }

        public Guid? EventId { get; set; }

        public Guid? EntityId { get; set; }

        [Required]
        [MaxLength(80)]
        public string EntityType { get; set; } = string.Empty;

        [Required]
        [MaxLength(80)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(100)]
        public string? ActorUserId { get; set; }

        public string? DetailsJson { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
