namespace NickScanCentralImagingPortal.Core.Models
{
    public class DownloadedFile
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long FileSize { get; set; }
        public string? FileHash { get; set; }
        public DateTime DownloadDate { get; set; }
        public DateTime? ProcessedDate { get; set; }
        public string ProcessingStatus { get; set; } = "Pending";
        public string? ErrorMessage { get; set; }
        public int? RecordCount { get; set; }

        // Ingestion verification
        public int? VerifiedDocumentCount { get; set; }
        public int? PerfectDocumentCount { get; set; }
        public int? PartialDocumentCount { get; set; }
        public double? AverageAccuracyPercent { get; set; }
        public double? LowestAccuracyPercent { get; set; }
        public string? LowestAccuracyContainer { get; set; }
        public string? VerificationDetails { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class BOEDocument
    {
        public int Id { get; set; }
        public int DownloadedFileId { get; set; }
        public int DocumentIndex { get; set; }

        // Container Details
        public string ContainerNumber { get; set; } = string.Empty;
        public string? ContainerDescription { get; set; }
        public string? ContainerISO { get; set; }
        public string? ContainerSize { get; set; }
        public int? ContainerQuantity { get; set; }
        public decimal? ContainerWeight { get; set; }
        public string? SealNumber { get; set; }
        public string? TruckPlateNumber { get; set; }
        public string? DriverName { get; set; }
        public string? DriverLicense { get; set; }
        public string? ContainerStatus { get; set; }
        public string? ContainerRemarks { get; set; }

        // Header Information
        public string? ImpName { get; set; }
        public decimal? TotalDutyPaid { get; set; }
        public string? CrmsLevel { get; set; }
        public string? ExpAddress { get; set; }
        public string? DeclarationNumber { get; set; }
        public string? RegimeCode { get; set; }
        public int? NoOfContainers { get; set; }
        public string? CompOffRemarks { get; set; }
        public string? DeclarantName { get; set; }
        public string? ExpName { get; set; }
        public string? ImpAddress { get; set; }
        public string? ImpExpName { get; set; }
        public string? CcvrIntelRemarks { get; set; }
        public int? DeclarationVersion { get; set; }
        public string? ImpExpAddress { get; set; }
        public string? DeclarationDate { get; set; }
        public string? ClearanceType { get; set; }
        public string? DeclarantAddress { get; set; }

        // Manifest Details
        public string? RotationNumber { get; set; }
        public string? ConsigneeName { get; set; }
        public string? CountryOfOrigin { get; set; }
        public string? MarksNumbers { get; set; }
        public string? ShipperName { get; set; }
        public string? ShipperAddress { get; set; }
        public string? BlNumber { get; set; }
        public string? DeliveryPlace { get; set; }
        public string? HouseBl { get; set; }
        public string? ConsigneeAddress { get; set; }
        public string? GoodsDescription { get; set; }
        public string? MasterBlNumber { get; set; }

        // ✅ CONSOLIDATED CARGO CLASSIFICATION
        // Consolidated = Has House BL (multiple consignees in one container)
        // Non-Consolidated = Master BL only (one consignment across multiple containers)
        public bool IsConsolidated { get; set; }

        // 🔍 BULLETPROOF: Dynamic unmapped fields storage - Tier 1: Structured columns (fast access)
        // Stores first 20 unmapped fields with section:field format (e.g., "Header:NewField")
        public string? UnmappedField1Label { get; set; }
        public string? UnmappedField1Value { get; set; }
        public string? UnmappedField2Label { get; set; }
        public string? UnmappedField2Value { get; set; }
        public string? UnmappedField3Label { get; set; }
        public string? UnmappedField3Value { get; set; }
        public string? UnmappedField4Label { get; set; }
        public string? UnmappedField4Value { get; set; }
        public string? UnmappedField5Label { get; set; }
        public string? UnmappedField5Value { get; set; }
        public string? UnmappedField6Label { get; set; }
        public string? UnmappedField6Value { get; set; }
        public string? UnmappedField7Label { get; set; }
        public string? UnmappedField7Value { get; set; }
        public string? UnmappedField8Label { get; set; }
        public string? UnmappedField8Value { get; set; }
        public string? UnmappedField9Label { get; set; }
        public string? UnmappedField9Value { get; set; }
        public string? UnmappedField10Label { get; set; }
        public string? UnmappedField10Value { get; set; }
        public string? UnmappedField11Label { get; set; }
        public string? UnmappedField11Value { get; set; }
        public string? UnmappedField12Label { get; set; }
        public string? UnmappedField12Value { get; set; }
        public string? UnmappedField13Label { get; set; }
        public string? UnmappedField13Value { get; set; }
        public string? UnmappedField14Label { get; set; }
        public string? UnmappedField14Value { get; set; }
        public string? UnmappedField15Label { get; set; }
        public string? UnmappedField15Value { get; set; }
        public string? UnmappedField16Label { get; set; }
        public string? UnmappedField16Value { get; set; }
        public string? UnmappedField17Label { get; set; }
        public string? UnmappedField17Value { get; set; }
        public string? UnmappedField18Label { get; set; }
        public string? UnmappedField18Value { get; set; }
        public string? UnmappedField19Label { get; set; }
        public string? UnmappedField19Value { get; set; }
        public string? UnmappedField20Label { get; set; }
        public string? UnmappedField20Value { get; set; }

        // 🔍 BULLETPROOF: Tier 2 - Complete backup (nothing lost)
        // Stores complete JSON document + all unmapped fields in structured format
        public string? RawJsonData { get; set; }

        // 🔍 BULLETPROOF: Metadata for unmapped fields
        public int? UnmappedFieldsCount { get; set; }  // Total count of unmapped fields detected
        public bool UnmappedFieldsOverflow { get; set; }  // true if count > 20

        // Processing metadata
        public DateTime? ProcessedAt { get; set; }
        public string ProcessingStatus { get; set; } = "Pending";
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // ── CMR upgrade provenance (1.13.0) ──────────────────────────────────
        // When a CMR row is upgraded to IM/EX (either via the explicit lifecycle
        // service when an IM/EX message arrives, or via the implicit upgrade handler
        // when ICUMS sends a CMR-typed message that already carries a declaration
        // number), we record the original clearance type and the upgrade timestamp
        // so the lifecycle is auditable. NULL on both = "current type is original,
        // never upgraded". Set once on first upgrade and immutable thereafter.
        [System.ComponentModel.DataAnnotations.StringLength(20)]
        public string? OriginalClearanceType { get; set; }

        public DateTime? CmrUpgradedAt { get; set; }

        // Ingestion integrity warnings — set by ValidateCriticalFieldsAsync during ingestion.
        // When true, IngestionWarnings holds a newline-delimited list of warnings.
        // Distinct from ProcessingStatus/ErrorMessage (those are for ingestion FAILURE; these are
        // for "record saved but has business-rule issues worth reviewing"). DB columns added
        // 2026-04-22 via scripts/add_ingestion_warnings_columns_pg.sql.
        public bool HasIngestionWarnings { get; set; }
        public string? IngestionWarnings { get; set; }

        // ── Document type, tagged from regimecode at ingest (audit option (b),
        // 2026-05-03). Values: 'BOE' (regular import/export), 'Transit'
        // (Bonded Transportation, regimes 80/88/89), 'Free Zone' (regimes
        // 90/94/95/97/99), or NULL (unknown / pre-declaration CMR with no
        // regime yet). Lets downstream consumers scope on documenttype
        // instead of hard-coding regime-set membership in every rule.
        // Populated by RegimeDirectionMap.ClassifyDocumentType — do not
        // hand-roll the mapping in callers.
        [System.ComponentModel.DataAnnotations.StringLength(20)]
        public string? DocumentType { get; set; }

        // Navigation properties
        public virtual DownloadedFile DownloadedFile { get; set; } = null!;
    }

    public class DownloadedManifestItem
    {
        public int Id { get; set; }
        public int BOEDocumentId { get; set; }
        public int ItemIndex { get; set; }

        // Item Details
        public string? HsCode { get; set; }
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public decimal? Weight { get; set; }
        public decimal? ItemFob { get; set; }
        public decimal? ItemDutyPaid { get; set; }
        public string? FobCurrency { get; set; }
        public string? CountryOfOrigin { get; set; }
        public int? ItemNo { get; set; }
        public string? Cpc { get; set; }

        // 🔍 BULLETPROOF: Dynamic unmapped fields storage for ManifestItems - Tier 1
        public string? UnmappedField1Label { get; set; }
        public string? UnmappedField1Value { get; set; }
        public string? UnmappedField2Label { get; set; }
        public string? UnmappedField2Value { get; set; }
        public string? UnmappedField3Label { get; set; }
        public string? UnmappedField3Value { get; set; }
        public string? UnmappedField4Label { get; set; }
        public string? UnmappedField4Value { get; set; }
        public string? UnmappedField5Label { get; set; }
        public string? UnmappedField5Value { get; set; }
        public string? UnmappedField6Label { get; set; }
        public string? UnmappedField6Value { get; set; }
        public string? UnmappedField7Label { get; set; }
        public string? UnmappedField7Value { get; set; }
        public string? UnmappedField8Label { get; set; }
        public string? UnmappedField8Value { get; set; }
        public string? UnmappedField9Label { get; set; }
        public string? UnmappedField9Value { get; set; }
        public string? UnmappedField10Label { get; set; }
        public string? UnmappedField10Value { get; set; }
        public string? UnmappedField11Label { get; set; }
        public string? UnmappedField11Value { get; set; }
        public string? UnmappedField12Label { get; set; }
        public string? UnmappedField12Value { get; set; }
        public string? UnmappedField13Label { get; set; }
        public string? UnmappedField13Value { get; set; }
        public string? UnmappedField14Label { get; set; }
        public string? UnmappedField14Value { get; set; }
        public string? UnmappedField15Label { get; set; }
        public string? UnmappedField15Value { get; set; }
        public string? UnmappedField16Label { get; set; }
        public string? UnmappedField16Value { get; set; }
        public string? UnmappedField17Label { get; set; }
        public string? UnmappedField17Value { get; set; }
        public string? UnmappedField18Label { get; set; }
        public string? UnmappedField18Value { get; set; }
        public string? UnmappedField19Label { get; set; }
        public string? UnmappedField19Value { get; set; }
        public string? UnmappedField20Label { get; set; }
        public string? UnmappedField20Value { get; set; }

        // 🔍 BULLETPROOF: Tier 2 - Complete backup for ManifestItems
        public string? RawJsonData { get; set; }
        public int? UnmappedFieldsCount { get; set; }
        public bool UnmappedFieldsOverflow { get; set; }

        // Processing metadata
        public DateTime? ProcessedAt { get; set; }
        public string ProcessingStatus { get; set; } = "Pending";
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation properties
        public virtual BOEDocument BOEDocument { get; set; } = null!;
    }

    public class IngestionLog
    {
        public int Id { get; set; }
        public int DownloadedFileId { get; set; }
        public string ProcessType { get; set; } = string.Empty; // FileDownload, JsonParse, DataIngestion, FinalProcessing
        public string Status { get; set; } = string.Empty; // Started, Completed, Failed
        public DateTime StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int? RecordsProcessed { get; set; }
        public string? ErrorMessage { get; set; }
        public string? Details { get; set; }
        public DateTime CreatedAt { get; set; }

        // Navigation properties
        public virtual DownloadedFile DownloadedFile { get; set; } = null!;
    }

    public class DownloadsStatistics
    {
        public int TotalFiles { get; set; }
        public int PendingFiles { get; set; }
        public int ProcessingFiles { get; set; }
        public int CompletedFiles { get; set; }
        public int FailedFiles { get; set; }
        public int TotalBOEDocuments { get; set; }
        public int TotalManifestItems { get; set; }
        public DateTime? LastProcessedDate { get; set; }
        public long TotalDataSize { get; set; }
    }

    /// <summary>
    /// Tracks container download history for deduplication
    /// Prevents downloading the same container multiple times within a time window
    /// </summary>
    public class ContainerDownloadHistory
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime DownloadedAt { get; set; }
        public string DownloadSource { get; set; } = string.Empty; // e.g., "ICUMSDownloadBackgroundService", "IcumBackgroundService"
        public bool HasValidData { get; set; }
        public string? ErrorMessage { get; set; }
        public int? DownloadedFileId { get; set; } // FK to DownloadedFiles (optional)
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Dead-letter queue for failed file processing
    /// Phase 2.2: Tracks files that failed processing and enables automatic retry with exponential backoff
    /// </summary>
    public class FailedProcessingQueue
    {
        public int Id { get; set; }
        public int DownloadedFileId { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FailureReason { get; set; } = string.Empty;
        public string? ErrorDetails { get; set; }
        public string FailureStage { get; set; } = string.Empty; // e.g., "JSON_Parse", "Data_Ingestion", "Database_Save"
        public int RetryCount { get; set; }
        public int MaxRetries { get; set; } = 5;
        public DateTime? NextRetryAt { get; set; }
        public string Status { get; set; } = "Pending"; // Pending, Retrying, Resolved, Abandoned
        public DateTime FailedAt { get; set; }
        public DateTime? ResolvedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    /// <summary>
    /// Tracks archived files for searchable index and restore capability
    /// Archive Solution: Manages archived files with compression, indexing, and retention
    /// </summary>
    public class ArchivedFile
    {
        public int Id { get; set; }
        public int DownloadedFileId { get; set; }
        public string OriginalFileName { get; set; } = string.Empty;
        public string OriginalFilePath { get; set; } = string.Empty;
        public string ArchiveFileName { get; set; } = string.Empty;
        public string ArchiveFilePath { get; set; } = string.Empty;
        public string ArchiveDirectory { get; set; } = string.Empty; // e.g., "2025/01/ContainerData"
        public long OriginalSizeBytes { get; set; }
        public long ArchivedSizeBytes { get; set; }
        public double CompressionRatio { get; set; } // Percentage reduction (0-100)
        public string CompressionType { get; set; } = "GZip"; // GZip, ZIP, None
        public DateTime ProcessedDate { get; set; } // When file was originally processed
        public DateTime ArchivedDate { get; set; }
        public string? ContainerNumbers { get; set; } // Comma-separated list for search
        public int DocumentCount { get; set; }
        public string FileType { get; set; } = string.Empty; // ContainerData, BatchData, ScanResults
        public bool IsRestored { get; set; }
        public DateTime? RestoredDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

}
