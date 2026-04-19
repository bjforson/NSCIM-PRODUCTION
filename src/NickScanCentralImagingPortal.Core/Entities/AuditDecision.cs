using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Stores audit decisions for containers that have completed image analysis
    /// Second-tier review/verification system
    /// </summary>
    [Table("AuditDecisions")]
    public class AuditDecision
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string GroupIdentifier { get; set; } = string.Empty; // BOE or BL number

        [Required]
        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty; // "FS6000" or "ASE"

        // Link to original image analysis decision
        public int ImageAnalysisDecisionId { get; set; }

        [ForeignKey("ImageAnalysisDecisionId")]
        public ImageAnalysisDecision? OriginalDecision { get; set; }

        // Audit decision per container
        [Required]
        [StringLength(20)]
        public string Decision { get; set; } = string.Empty; // "Approved" or "Rejected"

        [StringLength(500)]
        public string? AuditNotes { get; set; }

        [Required]
        [StringLength(100)]
        public string AuditedBy { get; set; } = string.Empty;

        public DateTime AuditedAt { get; set; } = DateTime.UtcNow;

        // Overall decision for the entire group (computed)
        [StringLength(20)]
        public string? OverallGroupDecision { get; set; } // "Approved" or "Rejected"

        public bool IsCompleted { get; set; } = false; // All containers in group audited
        public DateTime? CompletedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAt { get; set; }
    }
}

