using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Links one canonical scanner image/source scan to one physical container.
    /// A two-container source scan is represented as one ScanImageAsset with two links.
    /// </summary>
    [Table("SourceScanContainerLinks")]
    public class SourceScanContainerLink
    {
        [Key]
        public int Id { get; set; }

        public Guid ScanImageAssetId { get; set; }

        public int? OriginalScanRecordId { get; set; }

        [Required]
        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ScannerNativeId { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(50)]
        public string NormalizedContainerNumber { get; set; } = string.Empty;

        [StringLength(500)]
        public string? SourceContainerLabel { get; set; }

        [Required]
        [StringLength(20)]
        public string Position { get; set; } = SourceScanContainerLinkPositions.Unknown;

        [Required]
        [StringLength(30)]
        public string Confidence { get; set; } = SourceScanContainerLinkConfidence.SourceMetadata;

        public Guid? SplitJobId { get; set; }

        public Guid? SplitResultId { get; set; }

        public int? BoeDocumentId { get; set; }

        public int? RecordExpectedContainerId { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public virtual ScanImageAsset? ScanImageAsset { get; set; }

        public virtual OriginalScanRecord? OriginalScanRecord { get; set; }
    }

    public static class SourceScanContainerLinkPositions
    {
        public const string Single = "single";
        public const string Left = "left";
        public const string Right = "right";
        public const string Unknown = "unknown";
    }

    public static class SourceScanContainerLinkConfidence
    {
        public const string SourceMetadata = "source-metadata";
        public const string SplitModel = "split-model";
        public const string OperatorConfirmed = "operator-confirmed";
        public const string Backfill = "backfill";
    }
}
