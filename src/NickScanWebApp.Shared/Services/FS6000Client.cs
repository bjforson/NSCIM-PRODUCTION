using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class FS6000Client
{
    public const string BasePath = "/api/FS6000";
    public const string StatisticsPath = BasePath + "/statistics";
    public const string ScansPath = BasePath + "/scans";
    public const string StatsPath = BasePath + "/stats";
    public const string SyncStatusPath = BasePath + "/sync/status";
    public const string IngestionStatusPath = BasePath + "/ingestion/status";
    public const string TelemetryPath = BasePath + "/telemetry";

    private readonly ApiService _apiService;

    public FS6000Client(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TStatistics?> GetStatisticsAsync<TStatistics>()
    {
        return _apiService.GetAsync<TStatistics>(StatisticsPath);
    }

    public Task<TScans?> GetScansAsync<TScans>(FS6000ScanQuery? query = null)
    {
        return _apiService.GetAsync<TScans>(BuildScansPath(query));
    }

    public Task<TStats?> GetStatsAsync<TStats>(ScannerDailyStatsQuery? query = null)
    {
        return _apiService.GetAsync<TStats>(BuildStatsPath(query));
    }

    public Task<TStatus?> GetSyncStatusAsync<TStatus>()
    {
        return _apiService.GetAsync<TStatus>(SyncStatusPath);
    }

    public Task<TStatus?> GetIngestionStatusAsync<TStatus>()
    {
        return _apiService.GetAsync<TStatus>(IngestionStatusPath);
    }

    public Task<TTelemetry?> GetTelemetryAsync<TTelemetry>()
    {
        return _apiService.GetAsync<TTelemetry>(TelemetryPath);
    }

    public static string BuildScansPath(FS6000ScanQuery? query = null)
    {
        query ??= new FS6000ScanQuery();

        var parts = new List<string>();
        Add(parts, "page", query.Page);
        Add(parts, "pageSize", query.PageSize);
        Add(parts, "containerNumber", query.ContainerNumber?.Trim());
        Add(parts, "syncStatus", query.SyncStatus?.Trim());
        Add(parts, "startDate", query.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parts, "endDate", query.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return ScansPath + ToQueryString(parts);
    }

    public static string BuildStatsPath(ScannerDailyStatsQuery? query = null)
    {
        var parts = new List<string>();
        Add(parts, "startDate", query?.StartDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parts, "endDate", query?.EndDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return StatsPath + ToQueryString(parts);
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

public sealed class FS6000ScanQuery
{
    public int? Page { get; set; }
    public int? PageSize { get; set; }
    public string? ContainerNumber { get; set; }
    public string? SyncStatus { get; set; }
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}

public sealed class ScannerDailyStatsQuery
{
    public DateTime? StartDate { get; set; }
    public DateTime? EndDate { get; set; }
}
