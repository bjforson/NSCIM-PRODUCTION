namespace NickScanWebApp.Shared.Services;

public sealed class CmrValidationClient
{
    private const string BasePath = "/api/CMRValidation";
    private readonly ApiService _apiService;

    public CmrValidationClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TStatistics?> GetStatisticsAsync<TStatistics>()
    {
        return _apiService.GetAsync<TStatistics>($"{BasePath}/statistics");
    }

    public Task<List<TRecord>?> GetProblematicRecordsAsync<TRecord>()
    {
        return _apiService.GetAsync<List<TRecord>>($"{BasePath}/problematic-records");
    }

    public Task<TStatus?> GetQueueStatusAsync<TStatus>()
    {
        return _apiService.GetAsync<TStatus>($"{BasePath}/queue-status");
    }

    public Task<TStatistics?> GetQueueStatisticsAsync<TStatistics>()
    {
        return _apiService.GetAsync<TStatistics>($"{BasePath}/queue-statistics");
    }

    public Task<TResult?> QueueRedownloadAsync<TResult>(object request)
    {
        return _apiService.PostAsync<object, TResult>($"{BasePath}/queue-redownload", request);
    }

    public Task<TResult?> QueueBatchRedownloadAsync<TResult>(object request)
    {
        return _apiService.PostAsync<object, TResult>($"{BasePath}/queue-batch-redownload", request);
    }

    public Task<TResult?> ProcessQueueAsync<TResult>()
    {
        return _apiService.PostAsync<object, TResult>($"{BasePath}/process-queue", new { });
    }

    public Task<TResult?> ClearCompletedAsync<TResult>()
    {
        return _apiService.PostAsync<object, TResult>($"{BasePath}/clear-completed", new { });
    }
}
