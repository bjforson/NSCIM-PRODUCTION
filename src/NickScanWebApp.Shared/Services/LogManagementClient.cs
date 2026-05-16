using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class LogManagementClient
{
    public const string BasePath = "/api/LogManagement";
    public const string LogsPath = BasePath + "/logs";
    public const string StatisticsPath = BasePath + "/statistics";

    private readonly ApiService _apiService;

    public LogManagementClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TLogs?> GetLogsAsync<TLogs>(LogManagementQuery query)
    {
        return _apiService.GetAsync<TLogs>(BuildLogsPath(query));
    }

    public Task<TStatistics?> GetStatisticsAsync<TStatistics>(int hoursBack = 24)
    {
        return _apiService.GetAsync<TStatistics>(BuildStatisticsPath(hoursBack));
    }

    public static string BuildLogsPath(LogManagementQuery? query)
    {
        var effectiveQuery = query ?? new LogManagementQuery();
        var parts = new List<string>();
        Add(parts, "page", effectiveQuery.Page <= 0 ? 1 : effectiveQuery.Page);
        Add(parts, "pageSize", effectiveQuery.PageSize <= 0 ? 100 : effectiveQuery.PageSize);
        AddLevel(parts, effectiveQuery.Level);
        Add(parts, "search", effectiveQuery.Search?.Trim());
        Add(parts, "fromDate", effectiveQuery.FromDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parts, "toDate", effectiveQuery.ToDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return LogsPath + ToQueryString(parts);
    }

    public static string BuildStatisticsPath(int hoursBack = 24)
    {
        var effectiveHoursBack = hoursBack <= 0 ? 24 : hoursBack;
        return $"{StatisticsPath}?hoursBack={effectiveHoursBack}";
    }

    private static void AddLevel(List<string> parts, string? level)
    {
        if (!string.IsNullOrWhiteSpace(level)
            && !string.Equals(level.Trim(), "All", StringComparison.OrdinalIgnoreCase))
        {
            parts.Add($"level={Uri.EscapeDataString(level.Trim())}");
        }
    }

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{name}={Uri.EscapeDataString(value)}");
        }
    }

    private static void Add(List<string> parts, string name, int value)
    {
        parts.Add($"{name}={value}");
    }

    private static string ToQueryString(List<string> parts)
    {
        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }
}

public sealed class LogManagementQuery
{
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 100;
    public string? Level { get; set; }
    public string? Search { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
}
