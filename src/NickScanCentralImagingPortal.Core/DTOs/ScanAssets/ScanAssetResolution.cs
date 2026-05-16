using System;
using System.Collections.Generic;

namespace NickScanCentralImagingPortal.Core.DTOs.ScanAssets;

public sealed class ScanAssetResolution
{
    public string? Status { get; set; }
    public string? Reason { get; set; }
    public bool Found { get; set; }
    public bool IsAmbiguous { get; set; }
    public string? RequestedContainerNumber { get; set; } = string.Empty;
    public string? NormalizedContainerNumber { get; set; }
    public string? ContainerNumber { get; set; } = string.Empty;
    public string? GroupIdentifier { get; set; }
    public int? AnalysisRecordId { get; set; }
    public string? SourceScannerType { get; set; }
    public string? SourceScanId { get; set; }
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
    public string? ResolvedBy { get; set; }
    public string? MatchKind { get; set; }
    public string? ResolutionReason { get; set; }
    public Guid? SplitJobId { get; set; }
    public Guid? SplitResultId { get; set; }
    public string? SplitPosition { get; set; }
    public DateTime? ScanTime { get; set; }
    public long? ImageSizeBytes { get; set; }
    public long? FileSizeBytes
    {
        get => ImageSizeBytes;
        set => ImageSizeBytes = value;
    }
    public string? ImageDisplayName { get; set; }
    public bool HasImage { get; set; }
    public ScanAssetCacheKey? CacheKey { get; set; }
    public SplitOptionContext? SplitContext { get; set; }
    public IReadOnlyList<ScanAssetResolutionCandidate> Candidates { get; set; } = Array.Empty<ScanAssetResolutionCandidate>();
    public bool Resolved => Found || string.Equals(Status, ScanAssetResolutionStatuses.Resolved, StringComparison.OrdinalIgnoreCase);
    public bool IsExactMatch => string.Equals(MatchKind, ScanAssetMatchKinds.Exact, StringComparison.OrdinalIgnoreCase)
        || (ResolvedBy?.Contains("Exact", StringComparison.OrdinalIgnoreCase) == true);
    public bool IsTokenizedMatch => string.Equals(MatchKind, ScanAssetMatchKinds.Tokenized, StringComparison.OrdinalIgnoreCase)
        || (ResolvedBy?.Contains("Tokenized", StringComparison.OrdinalIgnoreCase) == true);

    public static ScanAssetResolution NotFound(string requestedContainerNumber, string reason) => new()
    {
        Status = ScanAssetResolutionStatuses.NotFound,
        RequestedContainerNumber = requestedContainerNumber,
        ContainerNumber = requestedContainerNumber,
        NormalizedContainerNumber = requestedContainerNumber,
        Reason = reason,
        ResolutionReason = reason
    };
}

public sealed class ScanAssetResolutionRequest
{
    public string? ContainerNumber { get; set; }
    public string? GroupIdentifier { get; set; }
    public int? AnalysisRecordId { get; set; }
    public Guid? SplitJobId { get; set; }
    public string? ScannerType { get; set; }
}

public record class ScanAssetResolutionCandidate
{
    public string? SourceScannerType { get; set; }
    public string? SourceScanId { get; set; }
    public int? OriginalScanRecordId { get; set; }
    public Guid? ScannerScanId { get; set; }
    public string? SourceContainerNumbers { get; set; }
    public string? ResolvedBy { get; set; }
    public DateTime? ScanTime { get; set; }
    public long? ImageSizeBytes { get; set; }
    public string? ImageDisplayName { get; set; }
    public ScanAssetCacheKey? CacheKey { get; set; }
}

public sealed record class ScanAssetCandidate : ScanAssetResolutionCandidate
{
    public string? MatchKind { get; set; }
    public Guid? ScannerRecordId
    {
        get => ScannerScanId;
        set => ScannerScanId = value;
    }
    public string? SourceContainerLabel
    {
        get => SourceContainerNumbers;
        set => SourceContainerNumbers = value;
    }
    public bool HasImage { get; set; }
    public Guid? SplitJobId { get; set; }
    public Guid? SplitResultId { get; set; }
    public int? AnalysisRecordId { get; set; }
    public string? SplitPosition { get; set; }
    public string? SplitStatus { get; set; }
}

public sealed class ScanAssetCacheKey
{
    public string? SourceScannerType { get; set; }
    public string? SourceScanId { get; set; }
    public Guid? SplitJobId { get; set; }
    public Guid? SplitResultId { get; set; }

    public string Value
    {
        get
        {
            var scanner = string.IsNullOrWhiteSpace(SourceScannerType)
                ? "unknown"
                : SourceScannerType.Trim().ToUpperInvariant();
            var source = string.IsNullOrWhiteSpace(SourceScanId)
                ? "none"
                : SourceScanId.Trim();
            var splitJob = SplitJobId?.ToString("N") ?? "none";
            var splitResult = SplitResultId?.ToString("N") ?? "none";
            return $"scan-asset:{scanner}:{source}:split-job:{splitJob}:split-result:{splitResult}";
        }
    }
}

public sealed class SplitOptionContext
{
    public int AnalysisRecordId { get; set; }
    public Guid GroupId { get; set; }
    public string? GroupIdentifier { get; set; }
    public string ContainerNumber { get; set; } = string.Empty;
    public string? ScannerType { get; set; }
    public Guid? SplitJobId { get; set; }
    public Guid? SplitResultId { get; set; }
    public Guid? SplitOptionAResultId { get; set; }
    public Guid? SplitOptionBResultId { get; set; }
    public string? SplitPosition { get; set; }
    public string? SplitStatus { get; set; }
    public ScanAssetResolution? Source { get; set; }
}

public static class ScanAssetResolutionStatuses
{
    public const string InvalidRequest = "InvalidRequest";
    public const string NotFound = "NotFound";
    public const string Resolved = "Resolved";
    public const string Ambiguous = "Ambiguous";
}

public static class ScanAssetResolvedBy
{
    public const string ExactFS6000 = "ExactFS6000";
    public const string ExactASE = "ExactASE";
    public const string TokenizedASE = "TokenizedASE";
    public const string TokenizedOriginalScan = "TokenizedOriginalScan";
    public const string SplitJobContext = "SplitJobContext";
}

public static class ScanAssetMatchKinds
{
    public const string Exact = "Exact";
    public const string Tokenized = "Tokenized";
    public const string SplitContext = "SplitContext";
}
