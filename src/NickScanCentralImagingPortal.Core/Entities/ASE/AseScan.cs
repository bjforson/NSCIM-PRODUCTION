using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.ASE
{
    public class AseScan
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public int InspectionId { get; set; }

        [Required]
        public DateTime ScanTime { get; set; }

        [Required]
        public string InspectionUuid { get; set; } = string.Empty;

        public string? ContainerNumber { get; set; }

        public string? TruckPlate { get; set; }

        public byte[]? ScanImage { get; set; }

        public string? ImageDisplayName { get; set; }

        public DateTime SyncedAt { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        public int? OriginalScanRecordId { get; set; }
        public virtual OriginalScanRecord? OriginalScanRecord { get; set; }
    }

    public class AseSyncLog
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public int LastSyncedInspectionId { get; set; }

        [Required]
        public DateTime LastSyncTime { get; set; }

        [Required]
        public int RecordsProcessed { get; set; }

        [Required]
        public string SyncStatus { get; set; } = "Completed";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
}
