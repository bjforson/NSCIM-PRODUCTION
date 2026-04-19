namespace NickScanCentralImagingPortal.Core.Models
{
    public class ContainerBasicInfo
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public int ScannerDataId { get; set; }
        public int ICUMSDataId { get; set; }
        public int ScannerRecordCount { get; set; }
        public int ICUMSRecordCount { get; set; }
        public int ImageCount { get; set; }
        public DateTime LastUpdated { get; set; }
    }

    public class ScannerDataRecord
    {
        public Guid Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanDate { get; set; }
        public string? ImagePath { get; set; }
        public string? FileName { get; set; }
        public long? FileSize { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    public class FullScannerDataRecord
    {
        public Guid Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public string PicNumber { get; set; } = string.Empty;
        public string? FycoPresent { get; set; }
        public string? VesselName { get; set; }
        public string? OperatorId { get; set; }
        public string? ScanResult { get; set; }
        public string? GoodsDescription { get; set; }
        public string? ShippingCompany { get; set; }
        public string? Consignee { get; set; }
        public string? FilePath { get; set; }
        public string SyncStatus { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime? ProcessedAt { get; set; }

        // Additional scanner-specific fields
        public string ScannerType { get; set; } = string.Empty; // FS6000, ASE, HeimannSmith, Nuctech
        public string ScannerId { get; set; } = string.Empty;
        public string? RawData { get; set; }
        public string? ImagePath { get; set; }
        public string ProcessingStatus { get; set; } = string.Empty;

        // Image information
        public int ImageCount { get; set; }
        public List<ScannerImageInfo> Images { get; set; } = new List<ScannerImageInfo>();

        // ASE-specific fields
        public int? InspectionId { get; set; }
        public string? InspectionUuid { get; set; }
        public string? TruckPlate { get; set; }
        public string? ImageDisplayName { get; set; }
        public bool HasScanImage { get; set; }
        public DateTime? SyncedAt { get; set; }
    }

    public class ScannerImageInfo
    {
        public Guid Id { get; set; }
        public string ImageType { get; set; } = string.Empty; // Main, Icon, CCR, LPR, Manifest
        public string FileName { get; set; } = string.Empty;
        public int? FileSizeBytes { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class ICUMSDataRecord
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string BOENumber { get; set; } = string.Empty;
        public string DeclarationNumber { get; set; } = string.Empty;
        public DateTime ImportDate { get; set; }
        public DateTime CreatedAt { get; set; }
        public string Status { get; set; } = string.Empty;
        public decimal? GrossWeight { get; set; }
        public decimal? NetWeight { get; set; }
    }

    public class FullBOEDataRecord
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string? ContainerDescription { get; set; }
        public string? ContainerISO { get; set; }
        public int? ContainerQuantity { get; set; }
        public decimal? ContainerWeight { get; set; }

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

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class ImageMetadata
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long? FileSize { get; set; }
        public DateTime ScanDate { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public string ProcessingStatus { get; set; } = string.Empty;
    }

    public class ImageWithTools
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ImagePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long? FileSize { get; set; }
        public DateTime ScanDate { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public string ProcessingStatus { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public List<string> AvailableTools { get; set; } = new();
    }

    public class SearchResult
    {
        public string Type { get; set; } = string.Empty;
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public DateTime Date { get; set; }
        public int Relevance { get; set; }
    }

    public class UnifiedSearchResults
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public List<SearchResult> Results { get; set; } = new();
        public int TotalResults { get; set; }
    }

    public class SearchRequest
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
    }

    public enum ContainerDetailsTab
    {
        Scanner,
        ICUMS,
        Images,
        Search
    }
}
