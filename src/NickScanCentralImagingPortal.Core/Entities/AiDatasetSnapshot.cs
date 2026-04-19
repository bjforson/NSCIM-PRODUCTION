using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Registry row for curated training/export snapshots.
    /// </summary>
    [Table("AiDatasetSnapshots")]
    public class AiDatasetSnapshot
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string CreatedBy { get; set; } = string.Empty;

        /// <summary>JSON: filters used (date range, scanner, opt-in only, etc.).</summary>
        public string FilterJson { get; set; } = "{}";

        [StringLength(50)]
        public string SchemaVersion { get; set; } = "1";

        public int RowCountEstimate { get; set; }

        [StringLength(500)]
        public string? ExportPath { get; set; }

        [StringLength(64)]
        public string? ChecksumSha256 { get; set; }

        [StringLength(1000)]
        public string? Notes { get; set; }
    }
}
