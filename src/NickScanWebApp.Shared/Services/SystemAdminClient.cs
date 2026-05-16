namespace NickScanWebApp.Shared.Services;

public sealed class SystemAdminClient
{
    public const string BasePath = "/api/SystemAdmin";
    public const string ShutdownPath = BasePath + "/shutdown";
    public const string RestartPath = BasePath + "/restart";
    public const string ShutdownWebAppPath = BasePath + "/shutdown-webapp";
    public const string RestartWebAppPath = BasePath + "/restart-webapp";
    public const string ServicesPath = BasePath + "/services";
    public const string SystemInfoPath = BasePath + "/system-info";
    public const string PerformancePath = BasePath + "/performance";

    private readonly ApiService _apiService;

    public SystemAdminClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TResult?> ShutdownAsync<TResult>()
    {
        return _apiService.PostAsync<object, TResult>(ShutdownPath, new { });
    }

    public Task<TResult?> RestartAsync<TResult>()
    {
        return _apiService.PostAsync<object, TResult>(RestartPath, new { });
    }

    public Task<TResult?> ShutdownWebAppAsync<TResult>()
    {
        return _apiService.PostAsync<object, TResult>(ShutdownWebAppPath, new { });
    }

    public Task<TResult?> RestartWebAppAsync<TResult>()
    {
        return _apiService.PostAsync<object, TResult>(RestartWebAppPath, new { });
    }

    public Task<TServices?> GetServicesAsync<TServices>()
    {
        return _apiService.GetAsync<TServices>(ServicesPath);
    }

    public Task<TInfo?> GetSystemInfoAsync<TInfo>()
    {
        return _apiService.GetAsync<TInfo>(SystemInfoPath);
    }

    public Task<TPerformance?> GetPerformanceAsync<TPerformance>()
    {
        return _apiService.GetAsync<TPerformance>(PerformancePath);
    }

    public Task<TResult?> RestartServiceAsync<TResult>(string serviceName)
    {
        return _apiService.PostAsync<object, TResult>(BuildServiceCommandPath(serviceName, "restart"), new { });
    }

    public Task<TResult?> StopServiceAsync<TResult>(string serviceName)
    {
        return _apiService.PostAsync<object, TResult>(BuildServiceCommandPath(serviceName, "stop"), new { });
    }

    public Task<TResult?> StartServiceAsync<TResult>(string serviceName)
    {
        return _apiService.PostAsync<object, TResult>(BuildServiceCommandPath(serviceName, "start"), new { });
    }

    public static string BuildServiceCommandPath(string serviceName, string command)
    {
        return $"{BasePath}/service/{Uri.EscapeDataString(serviceName)}/{command}";
    }
}
