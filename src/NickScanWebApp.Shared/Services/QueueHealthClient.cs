namespace NickScanWebApp.Shared.Services;

public sealed class QueueHealthClient
{
    public const string BasePath = "/api/QueueHealth";
    public const string StatisticsPath = BasePath + "/statistics";
    public const string PublishingPath = BasePath + "/publishing";

    private readonly ApiService _apiService;

    public QueueHealthClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TItems?> GetItemsAsync<TItems>(
        int page,
        int pageSize,
        string sortBy,
        bool sortDescending,
        string? scannerType = null,
        string? status = null,
        string? search = null)
    {
        return _apiService.GetAsync<TItems>(
            BuildItemsPath(page, pageSize, sortBy, sortDescending, scannerType, status, search));
    }

    public Task<TStatistics?> GetStatisticsAsync<TStatistics>()
    {
        return _apiService.GetAsync<TStatistics>(StatisticsPath);
    }

    public Task<TPublishing?> GetPublishingHealthAsync<TPublishing>()
    {
        return _apiService.GetAsync<TPublishing>(PublishingPath);
    }

    public static string BuildItemsPath(
        int page,
        int pageSize,
        string sortBy,
        bool sortDescending,
        string? scannerType = null,
        string? status = null,
        string? search = null)
    {
        var parts = new List<string>
        {
            $"page={page}",
            $"pageSize={pageSize}",
            $"sortBy={Uri.EscapeDataString(sortBy)}",
            $"sortDescending={sortDescending.ToString().ToLowerInvariant()}"
        };

        if (!string.IsNullOrWhiteSpace(scannerType) && scannerType != "All")
        {
            parts.Add($"scannerType={Uri.EscapeDataString(scannerType)}");
        }

        if (!string.IsNullOrWhiteSpace(status) && status != "All")
        {
            parts.Add($"status={Uri.EscapeDataString(status)}");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parts.Add($"search={Uri.EscapeDataString(search)}");
        }

        return $"{BasePath}/items?{string.Join("&", parts)}";
    }
}
