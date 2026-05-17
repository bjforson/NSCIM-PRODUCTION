using System.Globalization;
using NickScanCentralImagingPortal.Core.DTOs.CameraEvidence;

namespace NickScanWebApp.Shared.Services;

public sealed class CameraEvidenceClient
{
    private const string BasePath = "/api/camera-evidence";
    private readonly ApiService _apiService;

    public CameraEvidenceClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<CameraEvidenceHealthDto?> GetHealthAsync()
    {
        return _apiService.GetAsync<CameraEvidenceHealthDto>($"{BasePath}/health");
    }

    public Task<List<CameraEvidenceSiteDto>?> GetSitesAsync()
    {
        return _apiService.GetAsync<List<CameraEvidenceSiteDto>>($"{BasePath}/sites");
    }

    public Task<CameraEvidenceSiteDto?> CreateSiteAsync(CameraEvidenceSiteUpsertRequest request)
    {
        return _apiService.PostAsync<CameraEvidenceSiteUpsertRequest, CameraEvidenceSiteDto>($"{BasePath}/sites", request);
    }

    public Task<CameraEvidenceSiteDto?> UpdateSiteAsync(Guid siteId, CameraEvidenceSiteUpsertRequest request)
    {
        return _apiService.PatchAsync<CameraEvidenceSiteUpsertRequest, CameraEvidenceSiteDto>($"{BasePath}/sites/{siteId}", request);
    }

    public Task<List<CameraEvidenceSourceDto>?> GetSourcesAsync(Guid? siteId = null)
    {
        var path = siteId.HasValue
            ? $"{BasePath}/sources?siteId={siteId.Value}"
            : $"{BasePath}/sources";
        return _apiService.GetAsync<List<CameraEvidenceSourceDto>>(path);
    }

    public Task<CameraEvidenceSourceDto?> CreateSourceAsync(CameraEvidenceSourceUpsertRequest request)
    {
        return _apiService.PostAsync<CameraEvidenceSourceUpsertRequest, CameraEvidenceSourceDto>($"{BasePath}/sources", request);
    }

    public Task<CameraEvidenceSourceDto?> UpdateSourceAsync(Guid sourceId, CameraEvidenceSourceUpsertRequest request)
    {
        return _apiService.PatchAsync<CameraEvidenceSourceUpsertRequest, CameraEvidenceSourceDto>($"{BasePath}/sources/{sourceId}", request);
    }

    public Task<List<ProtectCameraDto>?> GetProtectCamerasAsync(Guid siteId)
    {
        return _apiService.GetAsync<List<ProtectCameraDto>>($"{BasePath}/sites/{siteId}/protect/cameras");
    }

    public Task<CameraEvidenceSnapshotTestResultDto?> TestSnapshotAsync(Guid sourceId)
    {
        return _apiService.PostAsync<object, CameraEvidenceSnapshotTestResultDto>($"{BasePath}/sources/{sourceId}/test-snapshot", new { });
    }

    public Task<CameraEvidenceEventPageDto?> GetEventsAsync(CameraEvidenceEventQuery? query = null)
    {
        return _apiService.GetAsync<CameraEvidenceEventPageDto>($"{BasePath}/events{BuildEventsQuery(query)}");
    }

    public Task<CameraEvidenceEventDetailDto?> GetEventAsync(Guid eventId)
    {
        return _apiService.GetAsync<CameraEvidenceEventDetailDto>($"{BasePath}/events/{eventId}");
    }

    public Task<byte[]?> GetFrameBytesAsync(Guid frameId)
    {
        return _apiService.GetBytesAsync($"{BasePath}/frames/{frameId}/image");
    }

    public Task<CameraEvidenceReviewDecisionDto?> ReviewOcrResultAsync(Guid ocrResultId, CameraEvidenceReviewRequest request)
    {
        return _apiService.PostAsync<CameraEvidenceReviewRequest, CameraEvidenceReviewDecisionDto>($"{BasePath}/ocr-results/{ocrResultId}/review", request);
    }

    private static string BuildEventsQuery(CameraEvidenceEventQuery? query)
    {
        query ??= new CameraEvidenceEventQuery();
        var parts = new List<string>
        {
            $"page={Math.Max(1, query.Page).ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={Math.Clamp(query.PageSize, 1, 200).ToString(CultureInfo.InvariantCulture)}"
        };

        Add(parts, "siteKey", query.SiteKey);
        Add(parts, "reviewStatus", query.ReviewStatus);
        if (query.SourceId.HasValue)
        {
            parts.Add($"sourceId={query.SourceId.Value}");
        }

        return $"?{string.Join("&", parts)}";
    }

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }
}

public sealed class CameraEvidenceEventQuery
{
    public string? SiteKey { get; set; }
    public Guid? SourceId { get; set; }
    public string? ReviewStatus { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
