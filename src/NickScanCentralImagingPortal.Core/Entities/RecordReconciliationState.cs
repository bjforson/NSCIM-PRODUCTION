using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// 1.14.0 — Persistent watermark + counters for the RecordReconciliationWorker.
    /// Singleton row with Id=1. Mapped to the "RecordReconciliationState" table.
    /// </summary>
    [Table("RecordReconciliationState")]
    public class RecordReconciliationState
    {
        [Key]
        public int Id { get; set; } = 1;

        public DateTime? LastWatermarkUtc { get; set; }
        public DateTime? LastTickAtUtc { get; set; }
        public int? LastTickDurationMs { get; set; }

        public long RecordsCreatedTotal { get; set; }
        public long RecordsUpdatedTotal { get; set; }
        public long ContainersPromotedTotal { get; set; }
        public long RecordsArchivedTotal { get; set; }
    }
}
