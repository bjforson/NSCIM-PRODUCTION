using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    [Table("AnalysisSubmissions")]
    public class AnalysisSubmission
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid GroupId { get; set; }

        [StringLength(500)]
        public string? PayloadPath { get; set; }

        [StringLength(128)]
        public string? PayloadHash { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "TestSaved"; // TestSaved | Submitted | Failed

        public int Retries { get; set; } = 0;

        [StringLength(1000)]
        public string? LastError { get; set; }

        // ✅ NEW: Fields for tracking partial submission
        public int TotalContainerCount { get; set; } // Total containers in group
        public int SubmittedContainerCount { get; set; } // Containers submitted to ICUMS
        public int PendingContainerCount { get; set; } // Containers waiting for images
        public bool IsPartiallyCompleted { get; set; } // Flag for partial completion
        public DateTime? PartiallyCompletedDate { get; set; } // When partial completion occurred

        // Track which specific containers were submitted vs. pending (JSON arrays)
        [StringLength(2000)]
        public string? SubmittedContainerNumbers { get; set; } // JSON: ["C1", "C2", "C3", "C4"]

        [StringLength(2000)]
        public string? PendingContainerNumbers { get; set; } // JSON: ["C5"]

        public DateTime? SubmittedAtUtc { get; set; }
        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    }
}


