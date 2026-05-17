using System.Text.Json.Serialization;

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
    /// Phase 2A source-scan resolver payload. The resolver/image contracts are
    /// present in some forks before the scanner-data and image-metadata routes,
    /// so WebApp callers keep compatibility fallbacks for optional endpoints.
    /// </summary>
    public class ScanAssetResolution
    {
        public string? Status { get; set; }
        public string? Reason { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string? RequestedContainerNumber { get; set; }
        public string? NormalizedContainerNumber { get; set; }
        public string? GroupIdentifier { get; set; }
        public int? AnalysisRecordId { get; set; }
        public string? SourceScanId { get; set; }
        public Guid? ScanImageAssetId { get; set; }
        public int? OriginalScanRecordId { get; set; }
        public Guid? ScannerScanId { get; set; }
        public Guid? ScannerRecordId
        {
            get => ScannerScanId;
            set => ScannerScanId = value;
        }
        public string? SourceContainerNumbers { get; set; }
        public string? SourceContainerLabel
        {
            get => SourceContainerNumbers;
            set => SourceContainerNumbers = value;
        }
        public string? SourceScannerType { get; set; }
        public Guid? SplitJobId { get; set; }
        public Guid? SplitResultId { get; set; }
        public string? SplitPosition { get; set; }
        public string? SplitSide { get; set; }
        public string? PositionHint { get; set; }
        public int? PositionInScan { get; set; }
        public string? ResolvedBy { get; set; }
        public string? MatchKind { get; set; }
        public string? ResolutionReason { get; set; }
        public DateTime? ScanTime { get; set; }
        public bool Found { get; set; }
        public bool IsResolved { get; set; }
        public bool Resolved { get; set; }
        public bool IsAmbiguous { get; set; }
        public bool HasImage { get; set; }
        public long? ImageSizeBytes { get; set; }
        public long? FileSizeBytes
        {
            get => ImageSizeBytes;
            set => ImageSizeBytes = value;
        }
        public string? ImageDisplayName { get; set; }
        public ScanAssetCacheKey? CacheKey { get; set; }
        public SplitOptionContext? SplitContext { get; set; }
        public List<ScanAssetResolutionCandidate> Candidates { get; set; } = new();
        public List<string> AmbiguousSourceScanIds { get; set; } = new();

        [JsonIgnore]
        public string? EffectiveSourceScanId =>
            GetStableSourceIdentifier(SourceScanId)
            ?? OriginalScanRecordId?.ToString()
            ?? ScanImageAssetId?.ToString()
            ?? ScannerScanId?.ToString();

        [JsonIgnore]
        public bool HasUsableSourceScan => !string.IsNullOrWhiteSpace(EffectiveSourceScanId) && !IsAmbiguous;

        [JsonIgnore]
        public string? EffectiveSplitSide
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(SplitSide))
                    return SplitSide;
                if (!string.IsNullOrWhiteSpace(SplitPosition))
                    return SplitPosition;
                if (!string.IsNullOrWhiteSpace(PositionHint))
                    return PositionHint;
                return PositionInScan switch
                {
                    1 => "left",
                    2 => "right",
                    _ => null
                };
            }
        }

        private static string? GetStableSourceIdentifier(string? sourceScanId)
        {
            if (string.IsNullOrWhiteSpace(sourceScanId))
                return null;

            var trimmed = sourceScanId.Trim();
            return LooksLikeContainerList(trimmed) ? null : trimmed;
        }

        private static bool LooksLikeContainerList(string value) =>
            value.IndexOfAny(new[] { ',', ';', '|', '\t', '\r', '\n' }) >= 0;
    }

    public class ScanAssetResolutionCandidate
    {
        public string? SourceScannerType { get; set; }
        public string? SourceScanId { get; set; }
        public Guid? ScanImageAssetId { get; set; }
        public int? OriginalScanRecordId { get; set; }
        public Guid? ScannerScanId { get; set; }
        public string? SourceContainerNumbers { get; set; }
        public string? ResolvedBy { get; set; }
        public DateTime? ScanTime { get; set; }
        public long? ImageSizeBytes { get; set; }
        public string? ImageDisplayName { get; set; }
        public ScanAssetCacheKey? CacheKey { get; set; }
    }

    public class ScanAssetCacheKey
    {
        public string? SourceScannerType { get; set; }
        public string? SourceScanId { get; set; }
        public Guid? SplitJobId { get; set; }
        public Guid? SplitResultId { get; set; }
        public string? Value { get; set; }
    }

    public class SplitOptionContext
    {
        public int AnalysisRecordId { get; set; }
        public Guid GroupId { get; set; }
        public string? GroupIdentifier { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string? ScannerType { get; set; }
        public bool IsMultiContainer { get; set; }
        public Guid? SplitJobId { get; set; }
        public Guid? SplitResultId { get; set; }
        public Guid? SplitOptionAResultId { get; set; }
        public Guid? SplitOptionBResultId { get; set; }
        public string? SplitPosition { get; set; }
        public string? SplitStatus { get; set; }
        public string? SourceScanId { get; set; }
        public int? OriginalScanRecordId { get; set; }
        public Guid? ScannerScanId { get; set; }
        public string? SourceScannerType { get; set; }
        public string? ResolverReason { get; set; }
        public ScanAssetResolution? Source { get; set; }
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
        public string? SourceScanId { get; set; }
        public Guid? ScanImageAssetId { get; set; }
        public int? OriginalScanRecordId { get; set; }
        public Guid? SplitJobId { get; set; }
        public Guid? SplitResultId { get; set; }
        public string? SplitSide { get; set; }
        public string? ResolutionReason { get; set; }
        public ScanAssetResolution? Resolution { get; set; }
    }

    /// <summary>
    /// Planned Phase 2A split-option payload for
    /// GET /api/image-analysis/records/{analysisRecordId}/split-options. It also
    /// accepts the current compatibility response from
    /// /api/image-splitter/container/{container}/split-options.
    /// </summary>
    public class ImageAnalysisSplitOptionsResponse
    {
        public bool IsMultiContainer { get; set; }
        public string? SplitStatus { get; set; }
        public string? Position { get; set; }
        public Guid? JobId { get; set; }
        public Guid? SplitJobId { get; set; }
        public int? AnalysisRecordId { get; set; }
        public string? SourceScanId { get; set; }
        public int? OriginalScanRecordId { get; set; }
        public Guid? ScannerScanId { get; set; }
        public string? SourceScannerType { get; set; }
        public string? OriginalImageUrl { get; set; }
        public string? ChosenResultId { get; set; }
        public Guid? SplitResultId { get; set; }
        public Guid? SplitOptionAResultId { get; set; }
        public Guid? SplitOptionBResultId { get; set; }
        public string? SplitPosition { get; set; }
        public string? SplitSide { get; set; }
        public string? PositionHint { get; set; }
        public int? PositionInScan { get; set; }
        public string? ResolverReason { get; set; }
        public ScanAssetResolution? Resolution { get; set; }
        public SplitOptionsResolverInfo? Resolver { get; set; }
        public List<ImageAnalysisSplitOption> Options { get; set; } = new();

        [JsonIgnore]
        public Guid? EffectiveSplitJobId => SplitJobId ?? JobId ?? Resolution?.SplitJobId;

        [JsonIgnore]
        public string? EffectiveSourceScanId =>
            GetStableSourceIdentifier(SourceScanId)
            ?? OriginalScanRecordId?.ToString()
            ?? ScannerScanId?.ToString()
            ?? Resolution?.EffectiveSourceScanId;

        [JsonIgnore]
        public string? EffectiveSplitSide
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Position))
                    return Position;
                if (!string.IsNullOrWhiteSpace(SplitSide))
                    return SplitSide;
                if (!string.IsNullOrWhiteSpace(SplitPosition))
                    return SplitPosition;
                if (!string.IsNullOrWhiteSpace(PositionHint))
                    return PositionHint;
                return PositionInScan switch
                {
                    1 => "left",
                    2 => "right",
                    _ => Resolution?.EffectiveSplitSide
                };
            }
        }

        private static string? GetStableSourceIdentifier(string? sourceScanId)
        {
            if (string.IsNullOrWhiteSpace(sourceScanId))
                return null;

            var trimmed = sourceScanId.Trim();
            return trimmed.IndexOfAny(new[] { ',', ';', '|', '\t', '\r', '\n' }) >= 0 ? null : trimmed;
        }
    }

    public class SplitOptionsResolverInfo
    {
        public string? ResolvedBy { get; set; }
        public string? Source { get; set; }
        public string? SourceScanId { get; set; }
        public string? SourceScannerType { get; set; }
    }

    public class ImageAnalysisSplitOption
    {
        public string ResultId { get; set; } = string.Empty;
        public string Strategy { get; set; } = string.Empty;
        public int SplitX { get; set; }
        public double Confidence { get; set; }
        public string? Side { get; set; }
        public string? CropImageUrl { get; set; }
        public string? PreviewImageUrl { get; set; }
        public string? Reasoning { get; set; }
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

        /// <summary>
        /// 6.02/6.03 (Sprint 4): empty-result disambiguation marker. See API
        /// PagedResult for value semantics. "Found" / "NoData" / "ContainerUnknown".
        /// Default "Found" keeps backwards compatibility for callers that ignore it.
        /// </summary>
        public string Status { get; set; } = "Found";

        public ScanAssetResolution? Resolution { get; set; }
    }

    /// <summary>
    /// System-wide predictive cache payload for a single container.
    /// Mirrors the API DTO without taking a dependency on the API assembly.
    /// </summary>
    public class PredictiveContainerContext
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public ScanAssetResolution? Resolution { get; set; }
        public PredictiveContainerSummary? Summary { get; set; }
        public PredictiveScannerDataPage? ScannerFirstPage { get; set; }
        public PredictiveIcumDataPage? IcumsFirstPage { get; set; }
        public PredictiveBoeSummary? BoeSummary { get; set; }
        public List<PredictiveImageMetadata> ImageMetadata { get; set; } = new();
        public bool FullImagesPreloaded { get; set; }
        public DateTime CachedAtUtc { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
        public string Source { get; set; } = string.Empty;
    }

    public class PredictiveContainerSummary
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string? SourceScanId { get; set; }
        public int? OriginalScanRecordId { get; set; }
        public Guid? SplitJobId { get; set; }
        public Guid? SplitResultId { get; set; }
        public string? ResolutionReason { get; set; }
        public string? ScannerType { get; set; }
        public string? Status { get; set; }
        public string? WorkflowStage { get; set; }
        public string? ClearanceType { get; set; }
        public string? GroupIdentifier { get; set; }
        public int? BoeDocumentId { get; set; }
        public bool HasScannerData { get; set; }
        public bool HasIcumsData { get; set; }
        public bool HasImageData { get; set; }
        public bool IsConsolidated { get; set; }
        public int? TotalHouseBLs { get; set; }
        public int? CompleteHouseBLs { get; set; }
        public int ScannerRecordCount { get; set; }
        public int IcumsRecordCount { get; set; }
        public int ImageCount { get; set; }
        public int CompletenessScore { get; set; }
        public DateTime? LatestScanDateUtc { get; set; }
        public DateTime? LastUpdatedUtc { get; set; }
    }

    public class PredictiveScannerDataPage
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public string Status { get; set; } = "Found";
        public List<PredictiveFieldValue> Data { get; set; } = new();
    }

    public class PredictiveIcumDataPage
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalCount { get; set; }
        public string Status { get; set; } = "Found";
        public List<PredictiveFieldValue> Data { get; set; } = new();
        public List<PredictiveManifestPreview> ManifestPreview { get; set; } = new();
    }

    public class PredictiveFieldValue
    {
        public string Field { get; set; } = string.Empty;
        public string? Value { get; set; }
        public string Category { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
    }

    public class PredictiveBoeSummary
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public int DocumentCount { get; set; }
        public int ManifestItemCount { get; set; }
        public int? PrimaryBoeDocumentId { get; set; }
        public string? DeclarationNumber { get; set; }
        public string? BlNumber { get; set; }
        public string? HouseBl { get; set; }
        public string? ClearanceType { get; set; }
        public string? DocumentType { get; set; }
        public string? CrmsLevel { get; set; }
        public string? ConsigneeName { get; set; }
        public string? ShipperName { get; set; }
        public string? GoodsDescription { get; set; }
        public decimal? TotalDutyPaid { get; set; }
        public bool HasIngestionWarnings { get; set; }
    }

    public class PredictiveManifestPreview
    {
        public int ManifestItemId { get; set; }
        public int ItemIndex { get; set; }
        public string? HsCode { get; set; }
        public string? Description { get; set; }
        public decimal? Quantity { get; set; }
        public string? Unit { get; set; }
        public decimal? Weight { get; set; }
        public decimal? ItemDutyPaid { get; set; }
        public string? CountryOfOrigin { get; set; }
    }

    public class PredictiveImageMetadata
    {
        public string ImageId { get; set; } = string.Empty;
        public string? SourceScanId { get; set; }
        public int? OriginalScanRecordId { get; set; }
        public Guid? SplitJobId { get; set; }
        public Guid? SplitResultId { get; set; }
        public string? SplitSide { get; set; }
        public string ScannerType { get; set; } = string.Empty;
        public string ImageType { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime CreatedAtUtc { get; set; }
        public bool HasBinaryData { get; set; }
        public string? ThumbnailUrl { get; set; }
        public string? FullImageUrl { get; set; }
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

