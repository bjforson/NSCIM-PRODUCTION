using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities.EagleA25
{
    public class EagleA25SyncLog
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public DateTime StartedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime? CompletedAtUtc { get; set; }

        [MaxLength(32)]
        public string Status { get; set; } = "Running";

        public long? LastSyncedAccession { get; set; }

        public int ScansRead { get; set; }

        public int ScansInserted { get; set; }

        public int ScansUpdated { get; set; }

        public int AssetsRead { get; set; }

        public int AssetsInserted { get; set; }

        public int AssetsUpdated { get; set; }

        [MaxLength(2000)]
        public string? ErrorMessage { get; set; }
    }
}
