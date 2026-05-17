using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities.CameraEvidence
{
    public class CameraEvidencePromotionRequest
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [MaxLength(100)]
        public string RequestedByUserId { get; set; } = string.Empty;

        public DateTime RequestedAtUtc { get; set; } = DateTime.UtcNow;

        [Required]
        [MaxLength(200)]
        public string DataField { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string CoreConsumer { get; set; } = string.Empty;

        public string ProposedUse { get; set; } = string.Empty;
        public string RiskAssessment { get; set; } = string.Empty;
        public string AccuracyEvidence { get; set; } = string.Empty;
        public string RollbackPlan { get; set; } = string.Empty;

        [Required]
        [MaxLength(200)]
        public string FeatureFlag { get; set; } = string.Empty;

        [Required]
        [MaxLength(40)]
        public string Status { get; set; } = "Draft";

        [MaxLength(100)]
        public string? ApprovedByUserId { get; set; }

        public DateTime? ApprovedAtUtc { get; set; }
    }
}
