using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.CameraEvidence
{
    public class CameraEvidenceOcrResult
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid FrameId { get; set; }

        public Guid SiteId { get; set; }

        public Guid SourceId { get; set; }

        [Required]
        [MaxLength(80)]
        public string Engine { get; set; } = "local-tesseract";

        [MaxLength(80)]
        public string? EngineVersion { get; set; }

        public string RawText { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? NormalizedText { get; set; }

        [Required]
        [MaxLength(50)]
        public string CandidateType { get; set; } = "unknown";

        public double Confidence { get; set; }

        [Required]
        [MaxLength(50)]
        public string ValidationStatus { get; set; } = "NotValidated";

        public string? ValidationReasonsJson { get; set; }

        public string? BoundingBoxesJson { get; set; }

        [Required]
        [MaxLength(40)]
        public string ReviewStatus { get; set; } = "Pending";

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(FrameId))]
        public virtual CameraEvidenceFrame Frame { get; set; } = null!;

        public virtual ICollection<CameraEvidenceReviewDecision> ReviewDecisions { get; set; } = new List<CameraEvidenceReviewDecision>();
    }
}
