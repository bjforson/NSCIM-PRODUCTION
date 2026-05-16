namespace NickScanWebApp.Shared.Services;

public sealed class ModuleMonitoringClient
{
    public const string BasePath = "/api/_module";
    public const string QueuesPath = BasePath + "/queues";
    public const string DiagnosticsBasePath = BasePath + "/diagnostics";
    public const string CompletenessDiagnosticsPath = DiagnosticsBasePath + "/completeness";
    public const string DaPostureDiagnosticsPath = DiagnosticsBasePath + "/da-posture";
    public const string DriftCountsDiagnosticsPath = DiagnosticsBasePath + "/drift-counts";

    private readonly ApiService _apiService;

    public ModuleMonitoringClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TQueues?> GetQueuesAsync<TQueues>()
    {
        return _apiService.GetAsync<TQueues>(QueuesPath);
    }

    public Task<TThroughput?> GetQueueThroughputAsync<TThroughput>(int minutes)
    {
        return _apiService.GetAsync<TThroughput>(BuildQueueThroughputPath(minutes));
    }

    public Task<TRecent?> GetRecentQueueItemsAsync<TRecent>(int limit)
    {
        return _apiService.GetAsync<TRecent>(BuildRecentQueueItemsPath(limit));
    }

    public Task<TCompleteness?> GetCompletenessDiagnosticsAsync<TCompleteness>()
    {
        return _apiService.GetAsync<TCompleteness>(CompletenessDiagnosticsPath);
    }

    public Task<TDaPosture?> GetDaPostureDiagnosticsAsync<TDaPosture>()
    {
        return _apiService.GetAsync<TDaPosture>(DaPostureDiagnosticsPath);
    }

    public Task<TDrift?> GetDriftCountsDiagnosticsAsync<TDrift>()
    {
        return _apiService.GetAsync<TDrift>(DriftCountsDiagnosticsPath);
    }

    public static string BuildQueueThroughputPath(int minutes)
    {
        return $"{QueuesPath}/throughput?minutes={minutes}";
    }

    public static string BuildRecentQueueItemsPath(int limit)
    {
        return $"{QueuesPath}/recent?limit={limit}";
    }
}
