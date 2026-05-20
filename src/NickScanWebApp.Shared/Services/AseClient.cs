using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class AseClient
{
    public const string BasePath = "/api/Ase";
    public const string SyncStatusPath = BasePath + "/sync-status";
    public const string ScansPath = BasePath + "/scans";
    public const string StatsPath = BasePath + "/stats";
    public const string TelemetryPath = BasePath + "/telemetry";

    private readonly ApiService _apiService;

    public AseClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TStatus?> GetSyncStatusAsync<TStatus>()
    {
        return _apiService.GetAsync<TStatus>(SyncStatusPath);
    }

    public Task<TScans?> GetScansAsync<TScans>(AseScanQuery? query = null)
    {
        return _apiService.GetAsync<TScans>(BuildScansPath(query));
    }

    public Task<TStats?> GetStatsAsync<TStats>()
    {
        return _apiService.GetAsync<TStats>(StatsPath);
    }

    public Task<TTelemetry?> GetTelemetryAsync<TTelemetry>()
    {
        return _apiService.GetAsync<TTelemetry>(TelemetryPath);
    }

    public static string BuildScansPath(AseScanQuery? query = null)
    {
        query ??= new AseScanQuery();

        var parts = new List<string>();
        Add(parts, "page", query.Page);
        Add(parts, "pageSize", query.PageSize);
        Add(parts, "containerNumber", query.ContainerNumber?.Trim());
        Add(parts, "startDate", query.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parts, "endDate", query.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return ScansPath + ToQueryString(parts);
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
            parts.Add($"{name}={Math.Max(1, value.Value)}");
        }
    }

    private static string ToQueryString(List<string> parts)
    {
        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }
}

public sealed class AseScanQuery
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? ContainerNumber { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
