using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities.EagleA25
{
    public class EagleA25Scan
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public int SourceScanId { get; set; }

        public Guid SourceScanGuid { get; set; }

        public int SourceScanEntryId { get; set; }

        public int SourceManifestId { get; set; }

        public Guid SourceManifestGuid { get; set; }

        public long Accession { get; set; }

        public long? ScanAccession { get; set; }

        public int? CargoSystemId { get; set; }

        public int? LocationId { get; set; }

        public DateTime ScanDateUtc { get; set; }

        public DateTime? ScanDateLocal { get; set; }

        public DateTime? ManifestCreateDateUtc { get; set; }

        public DateTime? ManifestCreateDateLocal { get; set; }

        [MaxLength(512)]
        public string? CargoIdentifier { get; set; }

        [MaxLength(512)]
        public string? AirWaybill { get; set; }

        [MaxLength(512)]
        public string? FlightNumber { get; set; }

        [MaxLength(512)]
        public string? TransitType { get; set; }

        [MaxLength(512)]
        public string? Weight { get; set; }

        [MaxLength(512)]
        public string? Company { get; set; }

        [MaxLength(512)]
        public string? Quantity { get; set; }

        [MaxLength(512)]
        public string? QuantityType { get; set; }

        [MaxLength(512)]
        public string? OriginFrom { get; set; }

        [MaxLength(512)]
        public string? OriginTo { get; set; }

        [MaxLength(512)]
        public string? Comments { get; set; }

        [MaxLength(256)]
        public string? DataPath { get; set; }

        [MaxLength(256)]
        public string? DataUrl { get; set; }

        public bool XRayDone { get; set; }

        public bool ReadyInspect { get; set; }

        public bool InspectDone { get; set; }

        public bool InspectSuspicious { get; set; }

        public bool SearchFound { get; set; }

        public bool SearchDone { get; set; }

        public bool Archived { get; set; }

        [MaxLength(32)]
        public string SyncStatus { get; set; } = "Synced";

        public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

        public virtual ICollection<EagleA25ScanAsset> Assets { get; set; } = new List<EagleA25ScanAsset>();
    }
}
