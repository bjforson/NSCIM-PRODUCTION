using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    /// <summary>
    /// LEGACY (pre-1.14.0): wave parent group. Superseded by
    /// <see cref="NickScanCentralImagingPortal.Core.Entities.RecordCompletenessStatus"/>
    /// which is populated proactively from ICUMS downloads by the
    /// RecordReconciliationWorker. Still dual-written by wave processing for
    /// backward compat. Scheduled for removal in a future release.
    /// </summary>
    [Obsolete("1.17.0: superseded by RecordCompletenessStatus. Dual-written for backward compat. Will be dropped in a future release.")]
    [Table("AnalysisParentGroups")]
    public class AnalysisParentGroup
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(150)]
        public string GroupIdentifier { get; set; } = string.Empty;

        [StringLength(20)]
        public string? ScannerType { get; set; }

        public int TotalExpectedContainers { get; set; }

        public int CompletedWaveCount { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Active"; // Active, Complete

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAtUtc { get; set; }
    }
}
