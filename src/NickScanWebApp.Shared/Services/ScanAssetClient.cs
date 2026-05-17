using NickScanWebApp.Shared.Models;

namespace NickScanWebApp.Shared.Services;

public sealed class ScanAssetClient
{
    private readonly ApiService _apiService;

    public ScanAssetClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<ScanAssetResolution?> ResolveAsync(
        string containerNumber,
        string? groupIdentifier = null,
        int? analysisRecordId = null,
        Guid? splitJobId = null,
        Guid? scanImageAssetId = null)
    {
        return _apiService.GetAsync<ScanAssetResolution>(
            BuildResolvePath(containerNumber, groupIdentifier, analysisRecordId, splitJobId, scanImageAssetId));
    }

    public Task<ScanAssetResolution?> TryResolveAsync(
        string containerNumber,
        string? groupIdentifier = null,
        int? analysisRecordId = null,
        Guid? splitJobId = null,
        Guid? scanImageAssetId = null)
    {
        return _apiService.TryGetAsync<ScanAssetResolution>(
            BuildResolvePath(containerNumber, groupIdentifier, analysisRecordId, splitJobId, scanImageAssetId));
    }

    public Task<List<ImageMetadata>?> TryGetImagesAsync(
        string sourceScanId,
        ScanAssetImageQuery? query = null)
    {
        return _apiService.TryGetAsync<List<ImageMetadata>>(BuildImagesPath(sourceScanId, query));
    }

    public Task<PagedResult<ScannerDataRecord>?> TryGetScannerDataAsync(
        string sourceScanId,
        ScanAssetScannerDataQuery? query = null)
    {
        return _apiService.TryGetAsync<PagedResult<ScannerDataRecord>>(BuildScannerDataPath(sourceScanId, query));
    }

    public Task<FullScannerDataRecord?> TryGetFullScannerDataAsync(
        string sourceScanId,
        ScanAssetScannerDataQuery? query = null)
    {
        var fullQuery = new ScanAssetScannerDataQuery
        {
            ContainerNumber = query?.ContainerNumber,
            GroupIdentifier = query?.GroupIdentifier,
            AnalysisRecordId = query?.AnalysisRecordId,
            SplitJobId = query?.SplitJobId,
            SplitResultId = query?.SplitResultId,
            ScanImageAssetId = query?.ScanImageAssetId,
            Side = query?.Side,
            Page = query?.Page,
            PageSize = query?.PageSize,
            Full = true
        };

        return _apiService.TryGetAsync<FullScannerDataRecord>(BuildScannerDataPath(sourceScanId, fullQuery));
    }

    public static string BuildResolvePath(
        string containerNumber,
        string? groupIdentifier = null,
        int? analysisRecordId = null,
        Guid? splitJobId = null,
        Guid? scanImageAssetId = null)
    {
        var parts = new List<string>();
        Add(parts, "containerNumber", containerNumber);
        Add(parts, "groupIdentifier", groupIdentifier);
        Add(parts, "analysisRecordId", analysisRecordId);
        Add(parts, "splitJobId", splitJobId);
        Add(parts, "scanImageAssetId", scanImageAssetId);

        return "/api/scan-assets/resolve" + ToQueryString(parts);
    }

    public static string BuildImagePath(
        string sourceScanId,
        ScanAssetImageQuery? query = null)
    {
        var parts = new List<string>();
        Add(parts, "containerNumber", query?.ContainerNumber);
        Add(parts, "groupIdentifier", query?.GroupIdentifier);
        Add(parts, "analysisRecordId", query?.AnalysisRecordId);
        Add(parts, "imageType", query?.ImageType);
        Add(parts, "size", string.IsNullOrWhiteSpace(query?.Size) ? "full" : query.Size);
        Add(parts, "splitJobId", query?.SplitJobId);
        Add(parts, "splitResultId", query?.SplitResultId);
        Add(parts, "scanImageAssetId", query?.ScanImageAssetId);
        Add(parts, "side", query?.Side);

        return $"/api/scan-assets/{Uri.EscapeDataString(sourceScanId)}/image{ToQueryString(parts)}";
    }

    public static string? TryBuildImagePath(
        ScanAssetResolution? resolution,
        string? containerNumber = null,
        string? size = "full",
        string? imageType = null)
    {
        if (resolution?.HasUsableSourceScan != true || string.IsNullOrWhiteSpace(resolution.EffectiveSourceScanId))
        {
            return null;
        }

        return BuildImagePath(
            resolution.EffectiveSourceScanId!,
            new ScanAssetImageQuery
            {
                ContainerNumber = StrictSingleContainerToken(containerNumber)
                    ?? StrictSingleContainerToken(resolution.ContainerNumber)
                    ?? StrictSingleContainerToken(resolution.NormalizedContainerNumber)
                    ?? StrictSingleContainerToken(resolution.RequestedContainerNumber)
                    ?? StrictSingleContainerToken(resolution.SplitContext?.ContainerNumber),
                GroupIdentifier = resolution.GroupIdentifier,
                AnalysisRecordId = resolution.AnalysisRecordId,
                ImageType = NormalizeImageType(imageType),
                Size = string.IsNullOrWhiteSpace(size) ? "full" : size,
                SplitJobId = resolution.SplitJobId,
                SplitResultId = resolution.SplitResultId,
                ScanImageAssetId = resolution.ScanImageAssetId,
                Side = resolution.EffectiveSplitSide
            });
    }

    public static string BuildImagesPath(
        string sourceScanId,
        ScanAssetImageQuery? query = null)
    {
        var parts = new List<string>();
        Add(parts, "containerNumber", query?.ContainerNumber);
        Add(parts, "groupIdentifier", query?.GroupIdentifier);
        Add(parts, "analysisRecordId", query?.AnalysisRecordId);
        Add(parts, "splitJobId", query?.SplitJobId);
        Add(parts, "splitResultId", query?.SplitResultId);
        Add(parts, "scanImageAssetId", query?.ScanImageAssetId);
        Add(parts, "side", query?.Side);

        return $"/api/scan-assets/{Uri.EscapeDataString(sourceScanId)}/images{ToQueryString(parts)}";
    }

    public static string BuildScannerDataPath(
        string sourceScanId,
        ScanAssetScannerDataQuery? query = null)
    {
        var parts = new List<string>();
        Add(parts, "containerNumber", query?.ContainerNumber);
        Add(parts, "groupIdentifier", query?.GroupIdentifier);
        Add(parts, "analysisRecordId", query?.AnalysisRecordId);
        Add(parts, "splitJobId", query?.SplitJobId);
        Add(parts, "splitResultId", query?.SplitResultId);
        Add(parts, "scanImageAssetId", query?.ScanImageAssetId);
        Add(parts, "side", query?.Side);
        Add(parts, "page", query?.Page);
        Add(parts, "pageSize", query?.PageSize);
        Add(parts, "full", query?.Full);

        return $"/api/scan-assets/{Uri.EscapeDataString(sourceScanId)}/scanner-data{ToQueryString(parts)}";
    }

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static void Add(List<string> parts, string name, int? value)
    {
        if (value.HasValue)
        {
            parts.Add($"{name}={value.Value}");
        }
    }

    private static void Add(List<string> parts, string name, Guid? value)
    {
        if (value.HasValue)
        {
            parts.Add($"{name}={value.Value}");
        }
    }

    private static void Add(List<string> parts, string name, bool? value)
    {
        if (value.HasValue)
        {
            parts.Add($"{name}={value.Value.ToString().ToLowerInvariant()}");
        }
    }

    private static string ToQueryString(List<string> parts)
    {
        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }

    private static string? StrictSingleContainerToken(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var matches = System.Text.RegularExpressions.Regex.Matches(
            value,
            "[A-Z]{4}\\d{7}",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant);
        if (matches.Count == 1)
        {
            return matches[0].Value.ToUpperInvariant();
        }

        if (matches.Count > 1 || value.IndexOfAny(new[] { ',', ';', '|', '/', '\\', '\t', '\r', '\n' }) >= 0)
        {
            return null;
        }

        return null;
    }

    private static string? NormalizeImageType(string? imageType)
    {
        if (string.IsNullOrWhiteSpace(imageType))
        {
            return null;
        }

        var trimmed = imageType.Trim();
        return trimmed.StartsWith("FS6000-", StringComparison.OrdinalIgnoreCase)
            ? trimmed["FS6000-".Length..]
            : trimmed;
    }
}

public sealed class ScanAssetImageQuery
{
    public string? ContainerNumber { get; set; }
    public string? GroupIdentifier { get; set; }
    public int? AnalysisRecordId { get; set; }
    public string? ImageType { get; set; }
    public string? Size { get; set; } = "full";
    public Guid? SplitJobId { get; set; }
    public Guid? SplitResultId { get; set; }
    public Guid? ScanImageAssetId { get; set; }
    public string? Side { get; set; }
}

public sealed class ScanAssetScannerDataQuery
{
    public string? ContainerNumber { get; set; }
    public string? GroupIdentifier { get; set; }
    public int? AnalysisRecordId { get; set; }
    public Guid? SplitJobId { get; set; }
    public Guid? SplitResultId { get; set; }
    public Guid? ScanImageAssetId { get; set; }
    public string? Side { get; set; }
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public bool? Full { get; set; }
}
