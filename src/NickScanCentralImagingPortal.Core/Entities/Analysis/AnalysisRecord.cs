using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    [Table("AnalysisRecords")]
    public class AnalysisRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid GroupId { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [StringLength(20)]
        public string? ScannerType { get; set; }

        [StringLength(500)]
        public string? ImageUrl { get; set; }

        [StringLength(200)]
        public string? MetadataRef { get; set; }

        [StringLength(200)]
        public string? CompletenessRef { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "Ready";

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        // ── Image split integration (2-container scans) ──

        /// <summary>Whether this container's scan image contains multiple containers.</summary>
        public bool IsMultiContainerScan { get; set; }

        /// <summary>References image_split_jobs.id in the splitter's PostgreSQL database (by value, no FK).</summary>
        public Guid? SplitJobId { get; set; }

        /// <summary>"left" (Container 1) or "right" (Container 2) within the split image.</summary>
        [StringLength(10)]
        public string? SplitPosition { get; set; }

        /// <summary>null | "Pending" | "Ready" | "Chosen" | "Skipped"</summary>
        [StringLength(20)]
        public string? SplitStatus { get; set; }

        /// <summary>The split result the analyst chose (references image_split_results.id by value).</summary>
        public Guid? SplitResultId { get; set; }

        /// <summary>Top-1 split candidate result ID.</summary>
        public Guid? SplitOptionA_ResultId { get; set; }

        /// <summary>Top-2 split candidate result ID.</summary>
        public Guid? SplitOptionB_ResultId { get; set; }
    }
}


