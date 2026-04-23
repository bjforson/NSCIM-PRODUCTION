using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Tracks scanner images that contain containers from DIFFERENT records (BOEs/Importers).
    /// Only created when multi-container scans have containers from different BOEs.
    /// Normal multi-container scans (same BOE) are NOT tracked here.
    /// </summary>
    [Table("CrossRecordScans")]
    public class CrossRecordScan
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Original scanner record (e.g., "CONT001, CONT002")
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string OriginalScanRecord { get; set; } = string.Empty;

        /// <summary>
        /// Links to FS6000Scan or AseScan GUID
        /// </summary>
        [Required]
        public Guid ScannerRecordId { get; set; }

        /// <summary>
        /// Scanner type: FS6000, ASE
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        /// <summary>
        /// When the scan was performed
        /// </summary>
        public DateTime ScanDateTime { get; set; }

        // ============================================
        // Container 1 (Position 1 in image)
        // ============================================

        [Required]
        [MaxLength(50)]
        public string Container1 { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Container1_BOE { get; set; }

        [MaxLength(200)]
        public string? Container1_Consignee { get; set; }

        [MaxLength(10)]
        public string? Container1_CRMS { get; set; }

        [MaxLength(10)]
        public string? Container1_ClearanceType { get; set; }

        [MaxLength(100)]
        public string? Container1_MasterBL { get; set; }

        [MaxLength(50)]
        public string? Container1_Rotation { get; set; }

        // ============================================
        // Container 2 (Position 2 in image)
        // ============================================

        [Required]
        [MaxLength(50)]
        public string Container2 { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Container2_BOE { get; set; }

        [MaxLength(200)]
        public string? Container2_Consignee { get; set; }

        [MaxLength(10)]
        public string? Container2_CRMS { get; set; }

        [MaxLength(10)]
        public string? Container2_ClearanceType { get; set; }

        [MaxLength(100)]
        public string? Container2_MasterBL { get; set; }

        [MaxLength(50)]
        public string? Container2_Rotation { get; set; }

        // ============================================
        // Classification & Severity
        // ============================================

        /// <summary>
        /// Type of cross-record issue:
        /// - DifferentImporters (most severe)
        /// - DifferentRiskLevels (CRMS mismatch)
        /// - DifferentClearanceTypes (Import vs Export)
        /// - DifferentBOEs (different declarations, same importer)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string CrossRecordType { get; set; } = string.Empty;

        /// <summary>
        /// Severity: High, Medium, Low
        /// </summary>
        [MaxLength(20)]
        public string Severity { get; set; } = "Medium";

        /// <summary>
        /// Whether this requires manual review
        /// </summary>
        public bool RequiresReview { get; set; } = false;

        // ============================================
        // Comparison Results
        // ============================================

        public bool SameDeclaration { get; set; }
        public bool SameConsignee { get; set; }
        public bool SameMasterBL { get; set; }
        public bool SameRotation { get; set; }
        public bool SameCRMS { get; set; }
        public bool SameClearanceType { get; set; }

        // ============================================
        // Audit & Review
        // ============================================

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? ReviewedAt { get; set; }

        [MaxLength(100)]
        public string? ReviewedBy { get; set; }

        [MaxLength(1000)]
        public string? ReviewNotes { get; set; }

        /// <summary>
        /// Review status: Pending, Reviewed, Escalated
        /// </summary>
        [MaxLength(20)]
        public string ReviewStatus { get; set; } = "Pending";

        /// <summary>
        /// 2.15.4 — ID of the image_split_jobs row submitted to the Python
        /// image-splitter for this scan. Populated automatically by
        /// <c>MultiContainerValidationService.CreateCrossRecordTrackingAsync</c>
        /// right after detection, so analysts land in the viewer with both
        /// candidate splits already computed.
        /// <para>
        /// Value-only reference — the jobs table lives in the same Postgres
        /// schema but is managed by the Python service, so no FK.
        /// Nullable because submission can fail (splitter down, image fetch
        /// failed, etc.); detection still succeeds in that case and a
        /// retry sweep can backfill later.
        /// </para>
        /// </summary>
        public Guid? SplitJobId { get; set; }
    }
}

