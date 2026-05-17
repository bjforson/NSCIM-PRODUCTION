using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.CameraEvidence
{
    public class CameraEvidenceSource
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid SiteId { get; set; }

        [Required]
        [MaxLength(50)]
        public string Provider { get; set; } = "UniFiProtect";

        [MaxLength(200)]
        public string? ProtectCameraId { get; set; }

        [MaxLength(200)]
        public string? ProtectDeviceKey { get; set; }

        [MaxLength(100)]
        public string? MacAddress { get; set; }

        [Required]
        [MaxLength(200)]
        public string DisplayName { get; set; } = string.Empty;

        [MaxLength(200)]
        public string? LocationName { get; set; }

        [MaxLength(200)]
        public string? OperationalZone { get; set; }

        [MaxLength(50)]
        public string ExpectedTextType { get; set; } = "unknown";

        [MaxLength(50)]
        public string CaptureMode { get; set; } = "snapshot";

        [MaxLength(80)]
        public string OcrProfile { get; set; } = "default";

        public bool IsEnabled { get; set; } = true;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(SiteId))]
        public virtual CameraEvidenceSite Site { get; set; } = null!;
    }
}
