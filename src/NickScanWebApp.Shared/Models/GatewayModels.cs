namespace NickScanWebApp.Shared.Models
{
    public class ContainerCompleteData
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime RequestedAt { get; set; }
        public int ResponseTimeMs { get; set; }

        public ScannerDataSection? Scanner { get; set; }
        public ICUMSDataSection? ICUMS { get; set; }
        public ValidationDataSection? Validation { get; set; }
        public VehicleDataSection? Vehicles { get; set; }
        public HistoryDataSection? History { get; set; }

        public DataAvailability? Available { get; set; }
        public List<string> Warnings { get; set; } = new();
        public List<string> Errors { get; set; } = new();
    }

    public class ScannerDataSection
    {
        public string DetectedScanner { get; set; } = string.Empty;
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

        public FS6000ScanDataDto? FS6000Data { get; set; }
        public ASEScanDataDto? ASEData { get; set; }
    }

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

    public class VehicleDataSection
    {
        public int VehicleCount { get; set; }
        public List<VehicleInfoDto> Vehicles { get; set; } = new();
    }

    public class HistoryDataSection
    {
        public DateTime CreatedAt { get; set; }
        public DateTime? LastUpdatedAt { get; set; }
        public List<HistoryEventDto> Events { get; set; } = new();
    }

    public class DataAvailability
    {
        public bool HasScannerData { get; set; }
        public bool HasImage { get; set; }
        public bool HasICUMSData { get; set; }
        public bool HasValidationData { get; set; }
        public bool HasVehicleData { get; set; }
        public bool HasHistoryData { get; set; }
        public bool HasAnyData => HasScannerData || HasImage || HasICUMSData || HasValidationData || HasVehicleData || HasHistoryData;
    }

    public class FS6000ScanDataDto
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public string XmlFilePath { get; set; } = string.Empty;
        public string ImageFilePath { get; set; } = string.Empty;
        public string FolderPath { get; set; } = string.Empty;
        public DateTime ProcessedAt { get; set; }
        public string? ProcessingStatus { get; set; }
        public int ImageCount { get; set; }
    }

    public class ASEScanDataDto
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public string ScanId { get; set; } = string.Empty;
        public string? OperatorId { get; set; }
        public string? Location { get; set; }
        public string? ScanMode { get; set; }
        public int? EnergyLevel { get; set; }
        public decimal? DoseRate { get; set; }
        public bool? ThreatDetected { get; set; }
        public decimal? ThreatConfidence { get; set; }
        public string? DetectionNotes { get; set; }
    }

    public class VehicleInfoDto
    {
        public string VIN { get; set; } = string.Empty;
        public string? Make { get; set; }
        public string? Model { get; set; }
        public int? Year { get; set; }
        public string? Color { get; set; }
    }

    public class HistoryEventDto
    {
        public DateTime Timestamp { get; set; }
        public string EventType { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? PerformedBy { get; set; }
    }
}

