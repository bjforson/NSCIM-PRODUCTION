using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Canonical identity for a physical scanner image or derived image asset.
    /// Container numbers are linked metadata, not the asset identity.
    /// </summary>
    [Table("ScanImageAssets")]
    public class ScanImageAsset
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public int? OriginalScanRecordId { get; set; }

        [Required]
        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ScannerNativeId { get; set; }

        [StringLength(500)]
        public string? SourceContainerLabel { get; set; }

        [Required]
        [StringLength(30)]
        public string AssetKind { get; set; } = ScanImageAssetKinds.Source;

        [Required]
        [StringLength(30)]
        public string StorageKind { get; set; } = ScanImageAssetStorageKinds.Database;

        [StringLength(1000)]
        public string? SourcePath { get; set; }

        [StringLength(1000)]
        public string? LocalPath { get; set; }

        [StringLength(100)]
        public string? MimeType { get; set; }

        [StringLength(300)]
        public string? ImageDisplayName { get; set; }

        public long? FileSizeBytes { get; set; }

        [StringLength(128)]
        public string? ContentHash { get; set; }

        public DateTime? ScanTimeUtc { get; set; }

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public virtual OriginalScanRecord? OriginalScanRecord { get; set; }

        public virtual ICollection<SourceScanContainerLink> ContainerLinks { get; set; } = new List<SourceScanContainerLink>();
    }

    public static class ScanImageAssetKinds
    {
        public const string Source = "source";
        public const string Thumbnail = "thumbnail";
        public const string SplitCrop = "split-crop";
        public const string Fallback = "fallback";
        public const string Annotated = "annotated";
        public const string Submission = "submission";
    }

    public static class ScanImageAssetStorageKinds
    {
        public const string Database = "database";
        public const string File = "file";
        public const string Proxy = "proxy";
        public const string External = "external";
    }
}
