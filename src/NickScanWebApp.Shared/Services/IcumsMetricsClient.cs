namespace NickScanWebApp.Shared.Services;

public sealed class IcumsMetricsClient
{
    public const string BasePath = "/api/ICUMSMetrics";
    public const string SnapshotPath = BasePath + "/snapshot";

    private readonly ApiService _apiService;

    public IcumsMetricsClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TSnapshot?> GetSnapshotAsync<TSnapshot>()
    {
        return _apiService.GetAsync<TSnapshot>(SnapshotPath);
    }
}
