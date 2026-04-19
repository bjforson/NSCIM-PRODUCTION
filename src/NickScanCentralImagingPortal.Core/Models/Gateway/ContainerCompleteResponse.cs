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
    /// ICUMS/BOE data section
    /// </summary>
    public class ICUMSDataSection
    {
        public string? BOENumber { get; set; }
        public string? ManifestNumber { get; set; }
        public string? Consignee { get; set; }
        public string? ConsigneeAddress { get; set; }
        public string? OriginPort { get; set; }
        public string? DestinationPort { get; set; }
        public string? VesselName { get; set; }
        public DateTime? ArrivalDate { get; set; }
        public string? CargoDescription { get; set; }
        public decimal? DeclaredValue { get; set; }
        public string? CustomsStatus { get; set; }
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

