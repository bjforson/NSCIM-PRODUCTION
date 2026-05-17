using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities.CameraEvidence
{
    public class CameraEvidenceQueueItem
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SiteId { get; set; }

        public Guid EventId { get; set; }

        public Guid? FrameId { get; set; }

        [Required]
        [MaxLength(50)]
        public string WorkType { get; set; } = "MediaFetch";

        [Required]
        [MaxLength(40)]
        public string Status { get; set; } = "Pending";

        public int AttemptCount { get; set; }

        public DateTime? NextAttemptAtUtc { get; set; }

        public DateTime? LockedUntilUtc { get; set; }

        [MaxLength(2000)]
        public string? LastError { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
