using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities.CameraEvidence
{
    public class CameraEvidenceCoreLinkCandidate
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid EventId { get; set; }

        public Guid? OcrResultId { get; set; }

        [Required]
        [MaxLength(500)]
        public string CandidateValue { get; set; } = string.Empty;

        [Required]
        [MaxLength(50)]
        public string CandidateType { get; set; } = "unknown";

        [MaxLength(80)]
        public string? CoreEntityType { get; set; }

        [MaxLength(200)]
        public string? CoreEntityKey { get; set; }

        public double MatchConfidence { get; set; }

        [MaxLength(1000)]
        public string? MatchReason { get; set; }

        [Required]
        [MaxLength(60)]
        public string PromotionState { get; set; } = "Candidate";

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}
