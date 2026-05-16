namespace NickScanWebApp.Shared.Services;

public sealed class MatchCorrectionAdminClient
{
    public const string BasePath = "/api/admin/match-corrections";
    public const string UnmatchPath = BasePath + "/unmatch";
    public const string RematchPath = BasePath + "/rematch";
    public const string ManualFlagPath = BasePath + "/flag";

    private readonly ApiService _apiService;

    public MatchCorrectionAdminClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TResponse?> GetFlagsAsync<TResponse>(
        int page,
        int pageSize,
        bool includeResolved,
        string? flagType = null,
        string? severity = null,
        string? containerSearch = null)
    {
        return _apiService.GetAsync<TResponse>(
            BuildListPath(page, pageSize, includeResolved, flagType, severity, containerSearch));
    }

    public Task<TDetail?> GetDetailAsync<TDetail>(string containerNumber)
    {
        return _apiService.GetAsync<TDetail>(
            $"{BasePath}/{Uri.EscapeDataString(containerNumber)}/detail");
    }

    public Task<TResponse?> ResolveFlagAsync<TRequest, TResponse>(int flagId, TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>($"{BasePath}/{flagId}/resolve", request);
    }

    public Task<TResponse?> CreateManualFlagAsync<TRequest, TResponse>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(ManualFlagPath, request);
    }

    public Task<TResponse?> RematchAsync<TRequest, TResponse>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(RematchPath, request);
    }

    public Task<TResponse?> UnmatchAsync<TRequest, TResponse>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(UnmatchPath, request);
    }

    public static string BuildListPath(
        int page,
        int pageSize,
        bool includeResolved,
        string? flagType = null,
        string? severity = null,
        string? containerSearch = null)
    {
        var parts = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}",
            $"includeResolved={includeResolved.ToString().ToLowerInvariant()}"
        };

        if (!string.IsNullOrWhiteSpace(flagType))
        {
            parts.Add($"flagType={Uri.EscapeDataString(flagType)}");
        }

        if (!string.IsNullOrWhiteSpace(severity))
        {
            parts.Add($"severity={Uri.EscapeDataString(severity)}");
        }

        if (!string.IsNullOrWhiteSpace(containerSearch))
        {
            parts.Add($"containerSearch={Uri.EscapeDataString(containerSearch.Trim())}");
        }

        return $"{BasePath}?{string.Join("&", parts)}";
    }
}
