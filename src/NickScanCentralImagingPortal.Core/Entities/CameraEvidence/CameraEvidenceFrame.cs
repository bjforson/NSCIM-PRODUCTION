using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.CameraEvidence
{
    public class CameraEvidenceFrame
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid EventId { get; set; }

        public Guid SiteId { get; set; }

        public Guid SourceId { get; set; }

        [Required]
        [MaxLength(50)]
        public string CaptureMode { get; set; } = "snapshot";

        public DateTime FrameTimestampUtc { get; set; } = DateTime.UtcNow;

        public int? RelativeOffsetMs { get; set; }

        [Required]
        [MaxLength(1200)]
        public string StoragePath { get; set; } = string.Empty;

        [Required]
        [MaxLength(100)]
        public string ContentType { get; set; } = "application/octet-stream";

        [Required]
        [MaxLength(128)]
        public string Sha256 { get; set; } = string.Empty;

        public int? Width { get; set; }

        public int? Height { get; set; }

        public bool IsHighQuality { get; set; }

        public string? ProtectSnapshotParametersJson { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(EventId))]
        public virtual CameraEvidenceEvent Event { get; set; } = null!;

        [ForeignKey(nameof(SiteId))]
        public virtual CameraEvidenceSite Site { get; set; } = null!;

        [ForeignKey(nameof(SourceId))]
        public virtual CameraEvidenceSource Source { get; set; } = null!;

        public virtual ICollection<CameraEvidenceOcrResult> OcrResults { get; set; } = new List<CameraEvidenceOcrResult>();
    }
}
