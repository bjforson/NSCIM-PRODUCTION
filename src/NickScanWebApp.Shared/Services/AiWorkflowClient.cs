using System.Net.Http.Json;
using System.Text.Json;

namespace NickScanWebApp.Shared.Services;

public sealed class AiWorkflowClient
{
    public const string BasePath = "/api/aiworkflow";
    public const string ImageSuggestionsPath = BasePath + "/image/suggestions";
    public const string ShadowMetricsPath = BasePath + "/shadow/metrics";

    private const string ApiClientName = "NickScanAPI";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IHttpClientFactory _httpClientFactory;

    public AiWorkflowClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<HttpResponseMessage> GetImageSuggestionsResponseAsync(
        string? status = null,
        string? containerNumber = null,
        int? pageSize = null)
    {
        return CreateClient().GetAsync(BuildImageSuggestionsPath(status, containerNumber, pageSize));
    }

    public async Task<List<TSuggestion>?> GetImageSuggestionsAsync<TSuggestion>(
        string? status = null,
        string? containerNumber = null,
        int? pageSize = null)
    {
        using var response = await GetImageSuggestionsResponseAsync(status, containerNumber, pageSize);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<TSuggestion>>(JsonOptions);
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
        using var response = await ResolveImageSuggestionResponseAsync(id, request);
        return response.IsSuccessStatusCode;
    }

    public Task<HttpResponseMessage> ResolveImageSuggestionResponseAsync<TRequest>(
        long id,
        TRequest request)
    {
        return CreateClient().PostAsJsonAsync(BuildResolveImageSuggestionPath(id), request);
    }

    public Task<HttpResponseMessage> GetShadowMetricsResponseAsync()
    {
        return CreateClient().GetAsync(ShadowMetricsPath);
    }

    public async Task<TMetrics?> GetShadowMetricsAsync<TMetrics>()
    {
        using var response = await GetShadowMetricsResponseAsync();
        if (!response.IsSuccessStatusCode)
        {
            return default;
        }

        return await response.Content.ReadFromJsonAsync<TMetrics>(JsonOptions);
    }

    public async Task<JsonElement?> GetShadowMetricsElementAsync()
    {
        using var response = await GetShadowMetricsResponseAsync();
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        await using var stream = await response.Content.ReadAsStreamAsync();
        using var document = await JsonDocument.ParseAsync(stream);
        return document.RootElement.Clone();
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

    private HttpClient CreateClient()
    {
        return _httpClientFactory.CreateClient(ApiClientName);
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
