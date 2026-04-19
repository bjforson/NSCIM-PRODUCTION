using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    [Table("AnalysisSettings")]
    public class AnalysisSettings
    {
        [Key]
        public int Id { get; set; }

        public bool Enabled { get; set; } = true;

        [StringLength(20)]
        public string AssignmentMode { get; set; } = "Manual"; // Auto | Manual | UserClaim

        [StringLength(20)]
        public string AutoAssignStrategy { get; set; } = "RoundRobin"; // RoundRobin | LeastLoaded

        [Obsolete("Use AssignmentMode instead")]
        public bool AutoAssign { get; set; } = false; // Keep for backward compatibility

        public int LeaseMinutes { get; set; } = 15;
        public int MaxConcurrentPerUser { get; set; } = 1;

        /// <summary>
        /// When set, IntakeWorker only processes ContainerCompletenessStatus records with ScanDate.Year >= this value.
        /// Null = no filter (process all years).
        /// </summary>
        public int? MinYearForIntake { get; set; } = 2026;

        // Wave Processing settings
        public bool EnableWaveProcessing { get; set; } = false;
        public int WaveMinBatchSize { get; set; } = 3;
        public int WaveTimeoutHours { get; set; } = 24;
        public int WaveAutoCloseDays { get; set; } = 30;

        // 1.14.0 — Record Completeness / Reconciliation settings
        public bool RecordReconciliationEnabled { get; set; } = true;
        public int RecordReconciliationIntervalMinutes { get; set; } = 30;
        public int RecordArchiveAfterDays { get; set; } = 30;
        public int RecordReconciliationBatchSize { get; set; } = 100;

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }
    }
}


