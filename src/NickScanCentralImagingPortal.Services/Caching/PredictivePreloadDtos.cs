namespace NickScanCentralImagingPortal.Services.Caching;

public sealed class PredictiveAssignmentContext
{
    public Guid GroupId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string EligibleStatus { get; set; } = string.Empty;
    public string GroupIdentifier { get; set; } = string.Empty;
    public string? NormalizedGroupIdentifier { get; set; }
    public string? GroupType { get; set; }
    public string? ScannerType { get; set; }
    public string Status { get; set; } = string.Empty;
    public int Priority { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public IReadOnlyList<string> ContainerNumbers { get; set; } = Array.Empty<string>();
    public int TotalContainerCount { get; set; }
    public DateTime CachedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string Source { get; set; } = "predictive-preload-v1";
}

public sealed class PredictiveRoleAssignments
{
    public string Role { get; set; } = string.Empty;
    public string EligibleStatus { get; set; } = string.Empty;
    public IReadOnlyList<Guid> GroupIds { get; set; } = Array.Empty<Guid>();
    public DateTime CachedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class PredictivePreloadRunResult
{
    public bool Enabled { get; set; }
    public DateTime StartedAtUtc { get; set; }
    public DateTime FinishedAtUtc { get; set; }
    public int CandidateCount { get; set; }
    public int SuccessCount { get; set; }
    public int FailureCount { get; set; }
    public int SkippedCount { get; set; }
    public string? SkippedReason { get; set; }
    public List<PredictivePreloadAssignmentResult> Assignments { get; set; } = new();
}

public sealed class PredictivePreloadAssignmentResult
{
    public Guid GroupId { get; set; }
    public string Role { get; set; } = string.Empty;
    public string GroupIdentifier { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ContainerCount { get; set; }
    public int ContainerPreloadSuccessCount { get; set; }
    public int ContainerPreloadFailureCount { get; set; }
    public List<PredictivePreloadContainerResult> Containers { get; set; } = new();
    public DateTime CompletedAtUtc { get; set; }
}

public sealed class PredictivePreloadContainerResult
{
    public string ContainerNumber { get; set; } = string.Empty;
    public bool Success { get; set; }
    public string? Error { get; set; }
    public int ScannerFieldCount { get; set; }
    public int IcumFieldCount { get; set; }
    public int ImageMetadataCount { get; set; }
    public DateTime CompletedAtUtc { get; set; }
}

public sealed class PredictiveContainerContext
{
    public string ContainerNumber { get; set; } = string.Empty;
    public PredictiveContainerSummary? Summary { get; set; }
    public PredictiveScannerDataPage? ScannerFirstPage { get; set; }
    public PredictiveIcumDataPage? IcumsFirstPage { get; set; }
    public PredictiveBoeSummary? BoeSummary { get; set; }
    public IReadOnlyList<PredictiveImageMetadata> ImageMetadata { get; set; } = Array.Empty<PredictiveImageMetadata>();
    public bool FullImagesPreloaded { get; set; }
    public DateTime CachedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public string Source { get; set; } = "predictive-preload-v1";
}

public sealed class PredictiveContainerSummary
{
    public string ContainerNumber { get; set; } = string.Empty;
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

public sealed class PredictiveScannerDataPage
{
    public string ContainerNumber { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public string Status { get; set; } = "Found";
    public IReadOnlyList<PredictiveFieldValue> Data { get; set; } = Array.Empty<PredictiveFieldValue>();
}

public sealed class PredictiveIcumDataPage
{
    public string ContainerNumber { get; set; } = string.Empty;
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalCount { get; set; }
    public string Status { get; set; } = "Found";
    public IReadOnlyList<PredictiveFieldValue> Data { get; set; } = Array.Empty<PredictiveFieldValue>();
    public IReadOnlyList<PredictiveManifestPreview> ManifestPreview { get; set; } = Array.Empty<PredictiveManifestPreview>();
}

public sealed class PredictiveFieldValue
{
    public string Field { get; set; } = string.Empty;
    public string? Value { get; set; }
    public string Category { get; set; } = string.Empty;
    public DateTime? Timestamp { get; set; }
}

public sealed class PredictiveBoeSummary
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

public sealed class PredictiveManifestPreview
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

public sealed class PredictiveImageMetadata
{
    public string ImageId { get; set; } = string.Empty;
    public string ScannerType { get; set; } = string.Empty;
    public string ImageType { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public long FileSizeBytes { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public bool HasBinaryData { get; set; }
    public string? ThumbnailUrl { get; set; }
    public string? FullImageUrl { get; set; }
}
