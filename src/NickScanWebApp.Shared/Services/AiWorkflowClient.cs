using System.Text.Json;

namespace NickScanWebApp.Shared.Services;

public sealed class AiWorkflowClient
{
    public const string BasePath = "/api/aiworkflow";
    public const string ImageSuggestionsPath = BasePath + "/image/suggestions";
    public const string ShadowMetricsPath = BasePath + "/shadow/metrics";

    private readonly ApiService _apiService;

    public AiWorkflowClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task<List<TSuggestion>?> GetImageSuggestionsAsync<TSuggestion>(
        string? status = null,
        string? containerNumber = null,
        int? pageSize = null)
    {
        return await _apiService.TryGetAsync<List<TSuggestion>>(
            BuildImageSuggestionsPath(status, containerNumber, pageSize));
    }

    public Task<List<TSuggestion>?> GetContainerSuggestionProbeAsync<TSuggestion>(string containerNumber)
    {
        return GetImageSuggestionsAsync<TSuggestion>(
            status: "all",
            containerNumber: containerNumber,
            pageSize: 1);
    }

    public async Task<bool> ResolveImageSuggestionAsync<TRequest>(
        long id,
        TRequest request)
    {
        return await _apiService.TryPostAsync(BuildResolveImageSuggestionPath(id), request);
    }

    public async Task<TMetrics?> GetShadowMetricsAsync<TMetrics>()
    {
        return await _apiService.TryGetAsync<TMetrics>(ShadowMetricsPath);
    }

    public async Task<JsonElement?> GetShadowMetricsElementAsync()
    {
        return await GetShadowMetricsAsync<JsonElement>();
    }

    public static string BuildImageSuggestionsPath(
        string? status = null,
        string? containerNumber = null,
        int? pageSize = null)
    {
        var parts = new List<string>();
        Add(parts, "status", status);
        Add(parts, "containerNumber", containerNumber);
        Add(parts, "pageSize", pageSize);

        return ImageSuggestionsPath + BuildQueryString(parts);
    }

    public static string BuildResolveImageSuggestionPath(long id)
    {
        return $"{ImageSuggestionsPath}/{id}/resolve";
    }

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        }
    }

    private static void Add(List<string> parts, string name, int? value)
    {
        if (value.HasValue)
        {
            parts.Add($"{Uri.EscapeDataString(name)}={value.Value}");
        }
    }

    private static string BuildQueryString(List<string> parts)
    {
        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }
}
