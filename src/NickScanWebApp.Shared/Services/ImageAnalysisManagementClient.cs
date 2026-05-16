namespace NickScanWebApp.Shared.Services;

public sealed class ImageAnalysisManagementClient
{
    public const string BasePath = "/api/image-analysis-management";
    public const string ServiceStatePath = BasePath + "/service-state";
    public const string WaveMonitorPath = BasePath + "/wave-monitor";

    private readonly ApiService _apiService;

    public ImageAnalysisManagementClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TState?> GetServiceStateAsync<TState>()
    {
        return _apiService.GetAsync<TState>(ServiceStatePath);
    }

    public Task<TResponse?> UpdateServiceStateAsync<TRequest, TResponse>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(ServiceStatePath, request);
    }

    public Task<TWaveMonitor?> GetWaveMonitorAsync<TWaveMonitor>()
    {
        return _apiService.GetAsync<TWaveMonitor>(WaveMonitorPath);
    }
}
