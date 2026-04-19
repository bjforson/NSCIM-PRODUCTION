using System;
using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Preserves the raw, unmodified scanner output before any splitting or normalization.
    /// Serves as the single source of truth for audit and re-processing.
    /// </summary>
    public class OriginalScanRecord
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [MaxLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        [Required]
        [MaxLength(500)]
        public string OriginalContainerNumbers { get; set; } = string.Empty;

        public int DerivedRecordCount { get; set; }

        [MaxLength(100)]
        public string? PicNumber { get; set; }

        [MaxLength(100)]
        public string? InspectionId { get; set; }

        public string? RawData { get; set; }

        [MaxLength(1000)]
        public string? SourceFilePath { get; set; }

        public DateTime ScanTime { get; set; }

        public DateTime IngestedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// 1.20.0 — Timestamp when RunPostICUMSValidationWorkflowAsync last processed
        /// this row. The validator uses this as a cursor: rows with NULL or stale
        /// (> 1 hour old) LastValidatedAtUtc are eligible for re-processing; rows
        /// validated more recently are skipped. Prevents the worker from looping
        /// over the same oldest 100 rows forever while leaving 218 unprocessed.
        /// </summary>
        public DateTime? LastValidatedAtUtc { get; set; }
    }
}
