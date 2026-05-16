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
        Guid? splitJobId = null)
    {
        return _apiService.GetAsync<ScanAssetResolution>(
            BuildResolvePath(containerNumber, groupIdentifier, analysisRecordId, splitJobId));
    }

    public Task<ScanAssetResolution?> TryResolveAsync(
        string containerNumber,
        string? groupIdentifier = null,
        int? analysisRecordId = null,
        Guid? splitJobId = null)
    {
        return _apiService.TryGetAsync<ScanAssetResolution>(
            BuildResolvePath(containerNumber, groupIdentifier, analysisRecordId, splitJobId));
    }

    public Task<List<ImageMetadata>?> TryGetImagesAsync(
        string sourceScanId,
        ScanAssetImageQuery? query = null)
    {
        return _apiService.TryGetAsync<List<ImageMetadata>>(BuildImagesPath(sourceScanId, query));
    }

    public static string BuildResolvePath(
        string containerNumber,
        string? groupIdentifier = null,
        int? analysisRecordId = null,
        Guid? splitJobId = null)
    {
        var parts = new List<string>();
        Add(parts, "containerNumber", containerNumber);
        Add(parts, "groupIdentifier", groupIdentifier);
        Add(parts, "analysisRecordId", analysisRecordId);
        Add(parts, "splitJobId", splitJobId);

        return "/api/scan-assets/resolve" + ToQueryString(parts);
    }

    public static string BuildImagePath(
        string sourceScanId,
        ScanAssetImageQuery? query = null)
    {
        var parts = new List<string>();
        Add(parts, "containerNumber", query?.ContainerNumber);
        Add(parts, "imageType", query?.ImageType);
        Add(parts, "size", string.IsNullOrWhiteSpace(query?.Size) ? "full" : query.Size);
        Add(parts, "splitJobId", query?.SplitJobId);
        Add(parts, "splitResultId", query?.SplitResultId);
        Add(parts, "side", query?.Side);

        return $"/api/scan-assets/{Uri.EscapeDataString(sourceScanId)}/image{ToQueryString(parts)}";
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
        Add(parts, "side", query?.Side);

        return $"/api/scan-assets/{Uri.EscapeDataString(sourceScanId)}/images{ToQueryString(parts)}";
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

    private static string ToQueryString(List<string> parts)
    {
        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
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
    public string? Side { get; set; }
}
