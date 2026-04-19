namespace NickScanWebApp.Shared.Models
{
    /// <summary>
    /// Basic container information summary
    /// </summary>
    public class ContainerBasicInfo
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public int ScannerRecordCount { get; set; }
        public int ICUMSRecordCount { get; set; }
        public int ImageCount { get; set; }
        public DateTime LastUpdated { get; set; }
        public string ValidationStatus { get; set; } = string.Empty;
        public int DataCompletenessScore { get; set; }
    }

    /// <summary>
    /// Full container summary from GET /api/containerdetails/full/{containerNumber}
    /// </summary>
    public class ContainerFullDetails
    {
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanDate { get; set; }
        public string ValidationStatus { get; set; } = string.Empty;
        public int CompletenessScore { get; set; }
        public string? ClearanceType { get; set; }
        public int ImageCount { get; set; }
        public bool HasScannerData { get; set; }
        public bool HasICUMSData { get; set; }
        public string? BOENumber { get; set; }
        public string? Consignee { get; set; }
        public string? OriginPort { get; set; }
        public string? Destination { get; set; }
        public string? VesselName { get; set; }
        public int VehicleCount { get; set; }
        public string? ScanLocation { get; set; }
        public string? Operator { get; set; }
        public string? ContainerSize { get; set; }
    }

    /// <summary>
    /// Scanner data record for display
    /// </summary>
    public class ScannerDataRecord
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
    }

    /// <summary>
    /// Full scanner data record with all fields
    /// </summary>
    public class FullScannerDataRecord
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanTime { get; set; }
        public Dictionary<string, object> AllFields { get; set; } = new();
        public List<string> AvailableFields { get; set; } = new();
        public List<string> MissingFields { get; set; } = new();
    }

    /// <summary>
    /// ICUMS data record for display
    /// </summary>
    public class ICUMSDataRecord
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public string? HouseBL { get; set; } // For grouping consolidated cargo by House BL
    }

    /// <summary>
    /// Full BOE data record with all fields
    /// </summary>
    public class FullBOEDataRecord
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string? DeclarationNumber { get; set; }
        public string? BOENumber { get; set; }
        public string? RotationNumber { get; set; }
        public string? ConsigneeName { get; set; }
        public string? BlNumber { get; set; }
        public string? HouseBl { get; set; }
        public string? ClearanceType { get; set; }
        public Dictionary<string, object> AllFields { get; set; } = new();
        public List<string> AvailableFields { get; set; } = new();
        public List<string> MissingFields { get; set; } = new();
    }

    /// <summary>
    /// Image metadata
    /// </summary>
    public class ImageMetadata
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
    /// Full image with manipulation tools
    /// </summary>
    public class ImageWithTools
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string ImageType { get; set; } = string.Empty;
        public string Base64Image { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
    }

    /// <summary>
    /// Unified search results across all container data
    /// </summary>
    public class UnifiedSearchResults
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string Query { get; set; } = string.Empty;
        public int TotalResults { get; set; }
        public List<SearchResultItem> ScannerResults { get; set; } = new();
        public List<SearchResultItem> ICUMSResults { get; set; } = new();
        public List<SearchResultItem> ImageResults { get; set; } = new();
    }

    /// <summary>
    /// Individual search result item
    /// </summary>
    public class SearchResultItem
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string MatchType { get; set; } = string.Empty; // Exact, Partial, Fuzzy
        public string Source { get; set; } = string.Empty; // Scanner, ICUMS, Image
    }

    /// <summary>
    /// Paginated result wrapper
    /// </summary>
    public class PagedResult<T>
    {
        public List<T> Data { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }

    /// <summary>
    /// Tab enum for container details modal
    /// </summary>
    public enum ContainerDetailsTab
    {
        Scanner,
        ICUMS,
        Images,
        Search
    }
}

