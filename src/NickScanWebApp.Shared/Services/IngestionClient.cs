namespace NickScanWebApp.Shared.Services;

public sealed class IngestionClient
{
    public const string BasePath = "/api/ingestion";
    public const string PendingFilesPath = BasePath + "/pending-files";
    public const string ServiceStatusPath = BasePath + "/service-status";
    public const string ManualTriggerPath = BasePath + "/manual-trigger";

    private readonly ApiService _apiService;

    public IngestionClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TFiles?> GetPendingFilesAsync<TFiles>()
    {
        return _apiService.GetAsync<TFiles>(PendingFilesPath);
    }

    public Task<TStatus?> GetServiceStatusAsync<TStatus>()
    {
        return _apiService.GetAsync<TStatus>(ServiceStatusPath);
    }

    public Task<TResult?> ProcessFileAsync<TResult>(int fileId)
    {
        return _apiService.PostAsync<object, TResult>(BuildProcessFilePath(fileId), new { });
    }

    public Task<TResult?> ResetFileStatusAsync<TResult>(int fileId)
    {
        return _apiService.PostAsync<object, TResult>(BuildResetFileStatusPath(fileId), new { });
    }

    public Task<TResult?> TriggerManualIngestionAsync<TResult>()
    {
        return _apiService.PostAsync<object, TResult>(ManualTriggerPath, new { });
    }

    public static string BuildProcessFilePath(int fileId)
    {
        return $"{BasePath}/process-file/{fileId}";
    }

    public static string BuildResetFileStatusPath(int fileId)
    {
        return $"{BasePath}/reset-file-status/{fileId}";
    }
}
