namespace NickScanWebApp.Shared.Services;

public sealed class AuditLogClient
{
    public const string BasePath = "/api/Audit";

    private readonly ApiService _apiService;

    public AuditLogClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TLogs?> GetAuditLogsAsync<TLogs>(
        int limit = 100,
        int skip = 0,
        string? eventType = null,
        string? severity = null,
        string? username = null)
    {
        return _apiService.GetAsync<TLogs>(BuildAuditLogsPath(limit, skip, eventType, severity, username));
    }

    public static string BuildAuditLogsPath(
        int limit = 100,
        int skip = 0,
        string? eventType = null,
        string? severity = null,
        string? username = null)
    {
        var parts = new List<string>
        {
            $"limit={limit}"
        };

        if (skip != 0)
        {
            parts.Add($"skip={skip}");
        }

        if (!string.IsNullOrEmpty(eventType))
        {
            parts.Add($"eventType={Uri.EscapeDataString(eventType)}");
        }

        if (!string.IsNullOrEmpty(severity))
        {
            parts.Add($"severity={Uri.EscapeDataString(severity)}");
        }

        if (!string.IsNullOrEmpty(username))
        {
            parts.Add($"username={Uri.EscapeDataString(username)}");
        }

        return $"{BasePath}?{string.Join("&", parts)}";
    }
}
