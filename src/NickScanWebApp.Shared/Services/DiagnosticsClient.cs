namespace NickScanWebApp.Shared.Services;

public sealed class DiagnosticsClient
{
    public const string BasePath = "/api/Diagnostics";
    public const string SystemPath = BasePath + "/system";
    public const string MemoryBasePath = "/api/MemoryDiagnostics";
    public const string MemoryStatusPath = MemoryBasePath + "/status";
    public const string GcStatsPath = MemoryBasePath + "/gc/stats";

    private readonly ApiService _apiService;

    public DiagnosticsClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TDiagnostics?> GetContainerDiagnosticsAsync<TDiagnostics>(string containerNumber)
    {
        return _apiService.GetAsync<TDiagnostics>(BuildContainerDiagnosticsPath(containerNumber));
    }

    public Task<TDiagnostics?> GetSystemDiagnosticsAsync<TDiagnostics>()
    {
        return _apiService.GetAsync<TDiagnostics>(SystemPath);
    }

    public Task<TStatus?> GetMemoryStatusAsync<TStatus>()
    {
        return _apiService.GetAsync<TStatus>(MemoryStatusPath);
    }

    public Task<TStats?> GetGcStatsAsync<TStats>()
    {
        return _apiService.GetAsync<TStats>(GcStatsPath);
    }

    public Task<TResult?> CollectGarbageAsync<TResult>(int generation = 2)
    {
        return _apiService.PostAsync<object, TResult>(BuildCollectGarbagePath(generation), new { });
    }

    public static string BuildContainerDiagnosticsPath(string containerNumber)
    {
        return $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}";
    }

    public static string BuildCollectGarbagePath(int generation)
    {
        return $"{MemoryBasePath}/gc/collect?generation={generation}";
    }
}
