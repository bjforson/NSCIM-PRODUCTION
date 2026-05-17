using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.CameraEvidence
{
    public class CameraEvidenceReviewDecision
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid OcrResultId { get; set; }

        [Required]
        [MaxLength(100)]
        public string ReviewerUserId { get; set; } = string.Empty;

        [Required]
        [MaxLength(40)]
        public string Decision { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? CorrectedText { get; set; }

        [MaxLength(50)]
        public string? CorrectedCandidateType { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(OcrResultId))]
        public virtual CameraEvidenceOcrResult OcrResult { get; set; } = null!;
    }
}
