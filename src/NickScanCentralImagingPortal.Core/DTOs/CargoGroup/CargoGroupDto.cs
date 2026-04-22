namespace NickScanCentralImagingPortal.Core.DTOs.CargoGroup
{
    /// <summary>
    /// Standard unified model for displaying cargo data across all pages
    /// Supports both consolidated and non-consolidated cargo
    /// </summary>
    public class CargoGroupDto
    {
        // Group Identification
        public string GroupIdentifier { get; set; } = string.Empty; // Master BL (consolidated) or Declaration Number (non-consolidated)
        public CargoType Type { get; set; } // Consolidated or NonConsolidated
        public string GroupingKey { get; set; } = string.Empty; // "MasterBL" or "Declaration"

        // Summary Information
        public string ClearanceType { get; set; } = string.Empty; // IM, EX, CMR
        public int TotalContainers { get; set; }
        public int TotalHouseBLs { get; set; } // For consolidated: count of House BLs
        public int TotalBOEs { get; set; } // Total BOE documents in this group
        public DateTime? LatestUpdateDate { get; set; }

        // Consolidated Cargo Details (when Type = Consolidated)
        public string? MasterBL { get; set; } // The Master BL that groups all House BLs
        public List<HouseBLGroupDto>? HouseBLGroups { get; set; } // All House BLs with their BOEs

        // Non-Consolidated Cargo Details (when Type = NonConsolidated)
        public string? DeclarationNumber { get; set; } // The BOE/Declaration number
        public string? ConsigneeName { get; set; }

        // Common: All containers under this group (works for both types)
        public List<string> ContainerNumbers { get; set; } = new(); // All containers under this group

        // Complete Data Sets (standardized across all groups)
        public CargoGroupDataDto Data { get; set; } = new();
    }

    public enum CargoType
    {
        Consolidated,      // Master BL → Multiple House BLs → One or Multiple Containers
        NonConsolidated    // Declaration/Master BL → Multiple Containers
    }

    /// <summary>
    /// House BL group within a consolidated Master BL
    /// </summary>
    public class HouseBLGroupDto
    {
        public string HouseBL { get; set; } = string.Empty;
        public string? MasterBL { get; set; }
        public string? DeclarationNumber { get; set; }
        public string? ConsigneeName { get; set; }
        public string? ClearanceType { get; set; }
        public List<int> BOEIds { get; set; } = new(); // All BOE IDs for this House BL
        public List<BOEDetailDto> BOEDetails { get; set; } = new(); // Complete BOE details
        public List<string> ContainerNumbers { get; set; } = new(); // Containers this House BL is in
    }

    /// <summary>
    /// Complete data sets for a cargo group (standardized structure)
    /// </summary>
    public class CargoGroupDataDto
    {
        // ICUMS Data
        public List<ICUMSDataGroupDto> ICUMSData { get; set; } = new();

        // Scanner Data
        public List<ScannerDataGroupDto> ScannerData { get; set; } = new();

        // Image Data
        public List<ImageDataGroupDto> ImageData { get; set; } = new();
    }

    /// <summary>
    /// ICUMS data grouped by House BL (for consolidated) or by Container (for non-consolidated)
    /// </summary>
    public class ICUMSDataGroupDto
    {
        public string GroupKey { get; set; } = string.Empty; // House BL (consolidated) or Container Number (non-consolidated)
        public string? HouseBL { get; set; } // Only for consolidated
        public string? ContainerNumber { get; set; } // Only for non-consolidated
        public List<ICUMSDataRecordDto> Records { get; set; } = new(); // All ICUMS fields
        public List<BOEDetailDto> BOEDetails { get; set; } = new(); // All BOE documents
    }

    /// <summary>
    /// Scanner data grouped by container
    /// </summary>
    public class ScannerDataGroupDto
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public List<ScannerDataRecordDto> Records { get; set; } = new();
        public DateTime? ScanDate { get; set; }
    }

    /// <summary>
    /// Image data grouped by container
    /// </summary>
    public class ImageDataGroupDto
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public List<ImageMetadataDto> Images { get; set; } = new();
        public int ImageCount => Images.Count;
    }

    /// <summary>
    /// Complete BOE document details — full field inventory from boedocuments.
    /// UI layer decides which fields to render (hide if null/empty under "show conditionally" policy).
    /// Legacy fields (BOEId, DeclarationNumber, HouseBL, MasterBL, ConsigneeName, ClearanceType,
    /// ContainerNumber, RotationNumber, GoodsDescription, TotalDutyPaid, DeclarationDate, AllFields,
    /// RawJsonData) retained for backward compatibility.
    /// </summary>
    public class BOEDetailDto
    {
        public int BOEId { get; set; }

        // Container Details
        public string? ContainerNumber { get; set; }
        public string? ContainerDescription { get; set; }
        public string? ContainerISO { get; set; }
        public int? ContainerQuantity { get; set; }
        public decimal? ContainerWeight { get; set; }
        public string? ContainerSize { get; set; }
        public string? ContainerStatus { get; set; }
        public string? ContainerRemarks { get; set; }
        public string? SealNumber { get; set; }
        public string? TruckPlateNumber { get; set; }
        public string? DriverName { get; set; }
        public string? DriverLicense { get; set; }

        // Header / Declaration
        public string? DeclarationNumber { get; set; }
        public string? DeclarationDate { get; set; }
        public int? DeclarationVersion { get; set; }
        public string? RegimeCode { get; set; }
        public string? ClearanceType { get; set; }
        public string? OriginalClearanceType { get; set; }
        public DateTime? CmrUpgradedAt { get; set; }
        public int? NoOfContainers { get; set; }
        public decimal? TotalDutyPaid { get; set; }
        public string? CrmsLevel { get; set; }
        public string? CompOffRemarks { get; set; }
        public string? CcvrIntelRemarks { get; set; }

        // Parties
        public string? ImpName { get; set; }
        public string? ImpAddress { get; set; }
        public string? ExpName { get; set; }
        public string? ExpAddress { get; set; }
        public string? ImpExpName { get; set; }
        public string? ImpExpAddress { get; set; }
        public string? DeclarantName { get; set; }
        public string? DeclarantAddress { get; set; }

        // Manifest / BL / Shipping
        public string? BlNumber { get; set; }
        public string? HouseBL { get; set; }
        public string? MasterBL { get; set; }            // legacy alias: populated from BlNumber at mapping time
        public string? MasterBlNumber { get; set; }      // carrier master BL — matches BOEDocument.MasterBlNumber
        public string? RotationNumber { get; set; }
        public string? DeliveryPlace { get; set; }
        public string? GoodsDescription { get; set; }
        public string? CountryOfOrigin { get; set; }
        public string? MarksNumbers { get; set; }
        public string? ConsigneeName { get; set; }
        public string? ConsigneeAddress { get; set; }
        public string? ShipperName { get; set; }
        public string? ShipperAddress { get; set; }

        // Flags
        public bool IsConsolidated { get; set; }

        // Line items (often needed alongside BOE header)
        public List<ManifestItemDto> ManifestItems { get; set; } = new();

        // Legacy: dictionary of dynamically extracted fields from RawJsonData (existing consumers may rely on it)
        public Dictionary<string, string> AllFields { get; set; } = new();

        // Legacy: full JSON backup. For new admin code prefer IngestionMetadata.RawJsonData.
        public string? RawJsonData { get; set; }

        // Admin-only ingestion metadata — analyst UI should hide by default.
        public BOEIngestionMetadataDto? IngestionMetadata { get; set; }
    }

    /// <summary>
    /// Ingestion-side metadata — for admin/debug views only. Not intended for analyst screens.
    /// </summary>
    public class BOEIngestionMetadataDto
    {
        public string? ProcessingStatus { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public bool HasIngestionWarnings { get; set; }
        public List<string> IngestionWarnings { get; set; } = new();
        public int? UnmappedFieldsCount { get; set; }
        public bool UnmappedFieldsOverflow { get; set; }
        public List<UnmappedFieldDto> UnmappedFields { get; set; } = new();
        public string? RawJsonData { get; set; }
        public int DownloadedFileId { get; set; }
        public int DocumentIndex { get; set; }
    }

    public class UnmappedFieldDto
    {
        public string Label { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
    }

    /// <summary>
    /// Line item on a BOE (one row per ManifestItem).
    /// </summary>
    public class ManifestItemDto
    {
        public int Id { get; set; }
        public int ItemIndex { get; set; }
        public int? ItemNo { get; set; }
        public string? HsCode { get; set; }
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public decimal? Weight { get; set; }
        public decimal? ItemFob { get; set; }
        public decimal? ItemDutyPaid { get; set; }
        public string? FobCurrency { get; set; }
        public string? CountryOfOrigin { get; set; }
        public string? Cpc { get; set; }
    }

    /// <summary>
    /// ICUMS data record (field-value pair)
    /// </summary>
    public class ICUMSDataRecordDto
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public string? HouseBL { get; set; } // For grouping consolidated cargo by House BL
    }

    /// <summary>
    /// Scanner data record (field-value pair)
    /// </summary>
    public class ScannerDataRecordDto
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
    }

    /// <summary>
    /// Image metadata
    /// </summary>
    public class ImageMetadataDto
    {
        public int Id { get; set; }
        public string ImageType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
        public string ThumbnailUrl { get; set; } = string.Empty;
        public string FullImageUrl { get; set; } = string.Empty;
    }

    /// <summary>
    /// Summary DTO for cargo group list/summary pages
    /// </summary>
    public class CargoGroupSummaryDto
    {
        public string GroupIdentifier { get; set; } = string.Empty;
        public CargoType Type { get; set; }
        public string DisplayName { get; set; } = string.Empty; // Master BL or Declaration Number
        public string ClearanceType { get; set; } = string.Empty;
        public int TotalContainers { get; set; }
        public int TotalHouseBLs { get; set; }
        public int TotalBOEs { get; set; }
        public int ImageCount { get; set; }
        public int ScannerRecordCount { get; set; }
        public int ICUMSRecordCount { get; set; }
        public int CompletenessScore { get; set; }
        public string Status { get; set; } = string.Empty;
        public DateTime? LatestUpdateDate { get; set; }
    }

    /// <summary>
    /// DTO for returning group identifier from container number
    /// </summary>
    public class CargoGroupIdentifierDto
    {
        public string GroupIdentifier { get; set; } = string.Empty;
        public CargoType Type { get; set; }
    }
}

