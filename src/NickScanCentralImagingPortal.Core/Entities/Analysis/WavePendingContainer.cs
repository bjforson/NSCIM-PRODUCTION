using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    /// <summary>
    /// LEGACY (pre-1.14.0): wave-stage child row. Superseded by
    /// <see cref="NickScanCentralImagingPortal.Core.Entities.RecordExpectedContainer"/>
    /// which is pre-populated from the ICUMS download feed and carries the full
    /// state machine (AwaitingScan → Pending → Ready → Decided → Submitted).
    /// Still dual-written by wave processing for backward compat. Scheduled for
    /// removal in a future release.
    /// </summary>
    [Obsolete("1.17.0: superseded by RecordExpectedContainer. Dual-written for backward compat. Will be dropped in a future release.")]
    [Table("WavePendingContainers")]
    public class WavePendingContainer
    {
        [Key]
        public int Id { get; set; }

        public Guid ParentGroupId { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [StringLength(20)]
        public string? ScannerType { get; set; }

        public DateTime FirstSeenUtc { get; set; } = DateTime.UtcNow;

        public DateTime? BecameReadyUtc { get; set; }

        [Required]
        [StringLength(20)]
        public string Status { get; set; } = "Pending"; // Pending, Ready, Processed, NoImageAvailable
    }
}
