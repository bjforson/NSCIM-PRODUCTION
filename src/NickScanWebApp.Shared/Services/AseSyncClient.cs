namespace NickScanWebApp.Shared.Services;

public sealed class AseSyncClient
{
    public const string BasePath = "/api/asesync";
    public const string StatisticsPath = BasePath + "/statistics";
    public const string HistoryPath = BasePath + "/history";
    public const string TriggerPath = BasePath + "/trigger";

    private readonly ApiService _apiService;

    public AseSyncClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TStatistics?> GetStatisticsAsync<TStatistics>()
    {
        return _apiService.GetAsync<TStatistics>(StatisticsPath);
    }

    public Task<THistory?> GetHistoryAsync<THistory>()
    {
        return _apiService.GetAsync<THistory>(HistoryPath);
    }

    public Task<TResult?> TriggerSyncAsync<TResult>()
    {
        return _apiService.PostAsync<object, TResult>(TriggerPath, new { });
    }
}
