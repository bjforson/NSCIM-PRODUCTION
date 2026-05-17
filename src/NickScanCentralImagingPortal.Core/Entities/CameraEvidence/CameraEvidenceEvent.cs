using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.CameraEvidence
{
    public class CameraEvidenceEvent
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SiteId { get; set; }

        public Guid? SourceId { get; set; }

        [MaxLength(200)]
        public string? ProviderEventId { get; set; }

        [Required]
        [MaxLength(256)]
        public string IdempotencyKey { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? AlarmName { get; set; }

        [MaxLength(200)]
        public string? TriggerKey { get; set; }

        [MaxLength(100)]
        public string? TriggerType { get; set; }

        [MaxLength(200)]
        public string? ProtectDeviceKey { get; set; }

        public DateTime? EventTimestampUtc { get; set; }

        public DateTime ReceivedAtUtc { get; set; } = DateTime.UtcNow;

        public string RawPayloadJson { get; set; } = "{}";

        [Required]
        [MaxLength(40)]
        public string ProcessingStatus { get; set; } = "Received";

        [MaxLength(2000)]
        public string? ProcessingError { get; set; }

        [ForeignKey(nameof(SiteId))]
        public virtual CameraEvidenceSite Site { get; set; } = null!;

        [ForeignKey(nameof(SourceId))]
        public virtual CameraEvidenceSource? Source { get; set; }

        public virtual ICollection<CameraEvidenceFrame> Frames { get; set; } = new List<CameraEvidenceFrame>();
    }
}
