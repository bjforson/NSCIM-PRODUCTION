namespace NickScanWebApp.Shared.Services;

public sealed class CrossRecordScansClient
{
    public const string BasePath = "/api/CrossRecordScans";

    private readonly ApiService _apiService;

    public CrossRecordScansClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TAnalytics?> GetAnalyticsAsync<TAnalytics>(DateTime? startDate, DateTime? endDate)
    {
        return _apiService.GetAsync<TAnalytics>(BuildAnalyticsPath(startDate, endDate));
    }

    public Task<TScan?> GetScanAsync<TScan>(int scanId)
    {
        return _apiService.GetAsync<TScan>($"{BasePath}/{scanId}");
    }

    public Task<TInfo?> GetContainerInfoAsync<TInfo>(string containerNumber)
    {
        return _apiService.GetAsync<TInfo>(
            $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}");
    }

    public static string BuildAnalyticsPath(DateTime? startDate, DateTime? endDate)
    {
        var parts = new List<string>();

        if (startDate.HasValue)
        {
            parts.Add($"startDate={Uri.EscapeDataString(startDate.Value.ToString("yyyy-MM-dd"))}");
        }

        if (endDate.HasValue)
        {
            parts.Add($"endDate={Uri.EscapeDataString(endDate.Value.ToString("yyyy-MM-dd"))}");
        }

        return parts.Count == 0
            ? $"{BasePath}/analytics"
            : $"{BasePath}/analytics?{string.Join("&", parts)}";
    }
}
