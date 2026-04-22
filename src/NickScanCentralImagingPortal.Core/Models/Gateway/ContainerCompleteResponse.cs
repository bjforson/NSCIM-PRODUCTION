using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Core.Models.Gateway
{
    /// <summary>
    /// Unified response containing all container-related data
    /// Aggregates data from multiple sources in a single API call
    /// </summary>
    public class ContainerCompleteResponse
    {
        /// <summary>
        /// Container identifier
        /// </summary>
        public string ContainerNumber { get; set; } = string.Empty;

        /// <summary>
        /// When the request was made
        /// </summary>
        public DateTime RequestedAt { get; set; }

        /// <summary>
        /// Total response time in milliseconds
        /// </summary>
        public int ResponseTimeMs { get; set; }

        // ===== Data Sections =====

        /// <summary>
        /// Scanner data section (image + FS6000/ASE records)
        /// Null if not requested or unavailable
        /// </summary>
        public ScannerDataSection? Scanner { get; set; }

        /// <summary>
        /// ICUMS/BOE data section
        /// Null if not requested or unavailable
        /// </summary>
        public ICUMSDataSection? ICUMS { get; set; }

        /// <summary>
        /// Validation and completeness data
        /// Null if not requested or unavailable
        /// </summary>
        public ValidationDataSection? Validation { get; set; }

        /// <summary>
        /// Vehicle data section (if container has vehicles)
        /// Null if not requested or unavailable
        /// </summary>
        public VehicleDataSection? Vehicles { get; set; }

        /// <summary>
        /// Processing history/audit trail
        /// Null if not requested or unavailable
        /// </summary>
        public HistoryDataSection? History { get; set; }

        // ===== Metadata =====

        /// <summary>
        /// Indicates what data is available
        /// </summary>
        public DataAvailability Available { get; set; } = new();

        /// <summary>
        /// Non-critical warnings during data gathering
        /// </summary>
        public List<string> Warnings { get; set; } = new();

        /// <summary>
        /// Errors encountered during data gathering
        /// </summary>
        public List<string> Errors { get; set; } = new();
    }

    /// <summary>
    /// Scanner data including image and scanner-specific records
    /// </summary>
    public class ScannerDataSection
    {
        public ScannerType DetectedScanner { get; set; }
        public string ImageBase64 { get; set; } = string.Empty;
        public byte[]? ImageBytes { get; set; }
        public string MimeType { get; set; } = "image/jpeg";
        public DateTime ScanTime { get; set; }
        public int ImageSizeBytes { get; set; }
        public int? Width { get; set; }
        public int? Height { get; set; }
        public string Quality { get; set; } = "Unknown";
        public bool FromCache { get; set; }
        public string ProcessingPipeline { get; set; } = string.Empty;

        // Scanner-specific data
        public FS6000ScanData? FS6000Data { get; set; }
        public ASEScanData? ASEData { get; set; }
    }

    /// <summary>
    /// ICUMS/BOE data section — full BOE column inventory exposed via Gateway.
    /// Legacy aliases (BOENumber, ManifestNumber, Consignee, OriginPort, DestinationPort,
    /// CargoDescription, DeclaredValue, CustomsStatus) retained for backward compatibility
    /// with existing consumers; prefer the explicit columns.
    /// </summary>
    public class ICUMSDataSection
    {
        // Identifiers
        public string? BOENumber { get; set; }           // alias: DeclarationNumber
        public string? DeclarationNumber { get; set; }
        public string? RotationNumber { get; set; }
        public string? ManifestNumber { get; set; }      // alias: BlNumber
        public string? BlNumber { get; set; }
        public string? HouseBl { get; set; }
        public string? MasterBlNumber { get; set; }

        // Parties
        public string? Consignee { get; set; }           // alias: ConsigneeName
        public string? ConsigneeName { get; set; }
        public string? ConsigneeAddress { get; set; }
        public string? ShipperName { get; set; }
        public string? ShipperAddress { get; set; }
        public string? ImpName { get; set; }
        public string? ImpAddress { get; set; }
        public string? ExpName { get; set; }
        public string? ExpAddress { get; set; }
        public string? DeclarantName { get; set; }
        public string? DeclarantAddress { get; set; }

        // Location / shipping
        public string? OriginPort { get; set; }
        public string? DestinationPort { get; set; }     // alias: DeliveryPlace
        public string? DeliveryPlace { get; set; }
        public string? CountryOfOrigin { get; set; }
        public string? VesselName { get; set; }
        public DateTime? ArrivalDate { get; set; }

        // Cargo / declaration
        public string? CargoDescription { get; set; }    // alias: GoodsDescription
        public string? GoodsDescription { get; set; }
        public string? MarksNumbers { get; set; }
        public string? ClearanceType { get; set; }
        public string? OriginalClearanceType { get; set; }
        public DateTime? CmrUpgradedAt { get; set; }
        public string? RegimeCode { get; set; }
        public string? DeclarationDate { get; set; }
        public int? DeclarationVersion { get; set; }
        public int? NoOfContainers { get; set; }

        // Container details
        public string? ContainerDescription { get; set; }
        public string? ContainerISO { get; set; }
        public string? ContainerSize { get; set; }
        public int? ContainerQuantity { get; set; }
        public decimal? ContainerWeight { get; set; }
        public string? ContainerStatus { get; set; }
        public string? ContainerRemarks { get; set; }
        public string? SealNumber { get; set; }
        public string? TruckPlateNumber { get; set; }
        public string? DriverName { get; set; }
        public string? DriverLicense { get; set; }

        // Financial & risk
        public decimal? DeclaredValue { get; set; }      // alias: TotalDutyPaid
        public decimal? TotalDutyPaid { get; set; }
        public string? CrmsLevel { get; set; }
        public string? CompOffRemarks { get; set; }
        public string? CcvrIntelRemarks { get; set; }

        // State
        public string? CustomsStatus { get; set; }       // alias: ProcessingStatus
        public string? ProcessingStatus { get; set; }
        public string? ErrorMessage { get; set; }
        public bool IsConsolidated { get; set; }
        public bool HasIngestionWarnings { get; set; }
        public string? IngestionWarnings { get; set; }
        public DateTime? ProcessedAt { get; set; }
        public DateTime? UpdatedAt { get; set; }
        public DateTime? DownloadedAt { get; set; }
        public int LineItemCount { get; set; }
    }

    /// <summary>
    /// Validation and completeness data
    /// </summary>
    public class ValidationDataSection
    {
        public string ValidationStatus { get; set; } = "Unknown";
        public int CompletenessScore { get; set; }
        public DateTime? LastValidatedAt { get; set; }
        public string? ValidatedBy { get; set; }
        public List<string> MissingFields { get; set; } = new();
        public List<string> ValidationErrors { get; set; } = new();
        public bool IsReadyForSubmission { get; set; }
    }

    /// <summary>
    /// Vehicle data section
    /// </summary>
    public class VehicleDataSection
    {
        public int VehicleCount { get; set; }
        public List<VehicleInfo> Vehicles { get; set; } = new();
    }

    public class VehicleInfo
    {
        public string VIN { get; set; } = string.Empty;
        public string? Make { get; set; }
        public string? Model { get; set; }
        public int? Year { get; set; }
        public string? Color { get; set; }
        public string? EngineNumber { get; set; }
    }

    /// <summary>
    /// Processing history/audit trail
    /// </summary>
    public class HistoryDataSection
    {
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public List<HistoryEvent> Events { get; set; } = new();
    }

    public class HistoryEvent
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? PerformedBy { get; set; }
    }
}

