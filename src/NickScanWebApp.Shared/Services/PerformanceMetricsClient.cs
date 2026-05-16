namespace NickScanWebApp.Shared.Services;

public sealed class PerformanceMetricsClient
{
    public const string BasePath = "/api/PerformanceMetrics";
    public const string SlowestPath = BasePath + "/slowest";

    private readonly ApiService _apiService;

    public PerformanceMetricsClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TMetrics?> GetMetricsAsync<TMetrics>()
    {
        return _apiService.GetAsync<TMetrics>(BasePath);
    }

    public Task<TSlowest?> GetSlowestEndpointsAsync<TSlowest>()
    {
        return _apiService.GetAsync<TSlowest>(SlowestPath);
    }
}
