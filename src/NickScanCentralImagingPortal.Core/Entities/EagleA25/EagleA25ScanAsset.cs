using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.EagleA25
{
    public class EagleA25ScanAsset
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid EagleA25ScanId { get; set; }

        public int SourceExtFileId { get; set; }

        public Guid SourceExtFileGuid { get; set; }

        public int SourceExtFileTypeId { get; set; }

        [Required]
        [MaxLength(30)]
        public string FileType { get; set; } = string.Empty;

        public bool IsXray { get; set; }

        [MaxLength(256)]
        public string? MimeType { get; set; }

        [MaxLength(80)]
        public string? Description { get; set; }

        [Required]
        [MaxLength(1000)]
        public string SourcePath { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? ResolvedSourcePath { get; set; }

        [MaxLength(1000)]
        public string? SourceUrl { get; set; }

        [MaxLength(1000)]
        public string? LocalPath { get; set; }

        public long? FileSizeBytes { get; set; }

        public DateTime? SourceCreateDateUtc { get; set; }

        public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;

        [ForeignKey(nameof(EagleA25ScanId))]
        public virtual EagleA25Scan Scan { get; set; } = null!;
    }
}
