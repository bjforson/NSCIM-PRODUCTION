using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class ImageAnalysisDashboardClient
{
    public const string BasePath = "/api/image-analysis/dashboard";
    public const string OverviewPath = BasePath + "/overview";
    public const string UserProductivityPath = BasePath + "/user-productivity";
    public const string QualityPath = BasePath + "/quality";
    public const string BottlenecksPath = BasePath + "/bottlenecks";
    public const string PredictionsPath = BasePath + "/predictions";
    public const string AlertsPath = BasePath + "/alerts";
    public const string SafeguardSummaryPath = BasePath + "/safeguard-summary";
    public const string SystemHealthPath = BasePath + "/system-health";
    public const string ExportPendingPath = BasePath + "/export-pending";

    private readonly ApiService _apiService;

    public ImageAnalysisDashboardClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TDashboard?> GetOverviewAsync<TDashboard>()
    {
        return _apiService.GetAsync<TDashboard>(OverviewPath);
    }

    public Task<TActivity?> GetUserProductivityAsync<TActivity>()
    {
        return _apiService.GetAsync<TActivity>(UserProductivityPath);
    }

    public Task<TQuality?> GetQualityAsync<TQuality>()
    {
        return _apiService.GetAsync<TQuality>(QualityPath);
    }

    public Task<TBottlenecks?> GetBottlenecksAsync<TBottlenecks>()
    {
        return _apiService.GetAsync<TBottlenecks>(BottlenecksPath);
    }

    public Task<TTrends?> GetTrendsAsync<TTrends>(string period)
    {
        return _apiService.GetAsync<TTrends>(BuildTrendsPath(period));
    }

    public Task<TSafeguard?> GetSafeguardSummaryAsync<TSafeguard>()
    {
        return _apiService.GetAsync<TSafeguard>(SafeguardSummaryPath);
    }

    public Task<TPredictions?> GetPredictionsAsync<TPredictions>()
    {
        return _apiService.GetAsync<TPredictions>(PredictionsPath);
    }

    public Task<TAlerts?> GetAlertsAsync<TAlerts>()
    {
        return _apiService.GetAsync<TAlerts>(AlertsPath);
    }

    public Task<object?> AcknowledgeAlertAsync(int alertId)
    {
        return _apiService.PostAsync<object, object>(
            BuildAlertAcknowledgementPath(alertId),
            new { });
    }

    public Task<byte[]?> GetExportBytesAsync(string format, DateTime? startDate, DateTime? endDate)
    {
        return _apiService.GetBytesAsync(BuildExportPath(format, startDate, endDate));
    }

    public Task<THealth?> GetSystemHealthAsync<THealth>()
    {
        return _apiService.GetAsync<THealth>(SystemHealthPath);
    }

    public Task<TExportPending?> GetExportPendingAsync<TExportPending>(ImageAnalysisExportPendingQuery query)
    {
        return _apiService.GetAsync<TExportPending>(BuildExportPendingPath(query));
    }

    public static string BuildTrendsPath(string? period)
    {
        var effectivePeriod = string.IsNullOrWhiteSpace(period) ? "24h" : period.Trim();
        return $"{BasePath}/trends?period={Uri.EscapeDataString(effectivePeriod)}";
    }

    public static string BuildAlertAcknowledgementPath(int alertId)
    {
        return $"{AlertsPath}/{alertId}/acknowledge";
    }

    public static string BuildExportPath(string format, DateTime? startDate, DateTime? endDate)
    {
        var safeFormat = string.IsNullOrWhiteSpace(format) ? "csv" : format.Trim().ToLowerInvariant();
        var effectiveStartDate = startDate ?? DateTime.UtcNow.AddDays(-7);
        var effectiveEndDate = endDate ?? DateTime.UtcNow;
        var parts = new List<string>();
        Add(parts, "startDate", effectiveStartDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        Add(parts, "endDate", effectiveEndDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));

        return $"{BasePath}/export/{Uri.EscapeDataString(safeFormat)}{ToQueryString(parts)}";
    }

    public static string BuildExportPendingPath(ImageAnalysisExportPendingQuery query)
    {
        var parts = new List<string>();
        Add(parts, "search", query.Search?.Trim());
        Add(parts, "scannerType", NormalizeScannerType(query.ScannerType));
        Add(parts, "sortBy", query.SortBy);
        Add(parts, "sortDir", query.SortDir);
        Add(parts, "page", query.Page <= 0 ? 1 : query.Page);
        Add(parts, "pageSize", query.PageSize <= 0 ? 50 : query.PageSize);

        return ExportPendingPath + ToQueryString(parts);
    }

    private static string? NormalizeScannerType(string? scannerType)
    {
        return string.IsNullOrWhiteSpace(scannerType)
            || string.Equals(scannerType, "All", StringComparison.OrdinalIgnoreCase)
                ? null
                : scannerType.Trim();
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

public sealed class ImageAnalysisExportPendingQuery
{
    public string? Search { get; set; }
    public string? ScannerType { get; set; }
    public string? SortBy { get; set; }
    public string? SortDir { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 50;
}
