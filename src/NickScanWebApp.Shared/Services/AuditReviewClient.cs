namespace NickScanWebApp.Shared.Services;

public sealed class AuditReviewClient
{
    public const string BasePath = "/api/AuditReview";
    public const string ReadyPath = BasePath + "/ready";
    public const string StatsPath = BasePath + "/stats";
    public const string AutoAuditorStatusPath = BasePath + "/auto-auditor/status";
    public const string AutoAuditorTogglePath = BasePath + "/auto-auditor/toggle";
    public const string SubmitPath = BasePath + "/submit";
    public const string CompletedPath = BasePath + "/completed";

    private readonly ApiService _apiService;

    public AuditReviewClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TGroup?> GetGroupAsync<TGroup>(string groupIdentifier, string? scannerType = null)
    {
        return _apiService.GetAsync<TGroup>(BuildGroupPath(groupIdentifier, scannerType));
    }

    public Task<TCompleted?> GetCompletedAsync<TCompleted>(string? scannerType = null, string? decision = null)
    {
        return _apiService.GetAsync<TCompleted>(BuildCompletedPath(scannerType, decision));
    }

    public Task<TResponse?> SubmitAsync<TRequest, TResponse>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(SubmitPath, request);
    }

    public static string BuildGroupPath(string groupIdentifier, string? scannerType = null)
    {
        var path = $"{BasePath}/group/{Uri.EscapeDataString(groupIdentifier)}";
        return string.IsNullOrEmpty(scannerType)
            ? path
            : $"{path}?scannerType={Uri.EscapeDataString(scannerType)}";
    }

    public static string BuildCompletedPath(string? scannerType = null, string? decision = null)
    {
        var parts = new List<string>();

        if (!IsAllOrBlank(scannerType))
        {
            parts.Add($"scannerType={Uri.EscapeDataString(scannerType!)}");
        }

        if (!IsAllOrBlank(decision))
        {
            parts.Add($"decision={Uri.EscapeDataString(decision!)}");
        }

        return parts.Count == 0 ? CompletedPath : $"{CompletedPath}?{string.Join("&", parts)}";
    }

    private static bool IsAllOrBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase);
    }
}
