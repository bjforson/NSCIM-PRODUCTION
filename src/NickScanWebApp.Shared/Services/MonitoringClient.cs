namespace NickScanWebApp.Shared.Services;

public sealed class MonitoringClient
{
    public const string BasePath = "/api/Monitoring";
    public const string HealthOverviewPath = BasePath + "/health/overview";
    public const string DatabaseStatisticsPath = BasePath + "/database/statistics";
    public const string PerformanceMetricsPath = BasePath + "/performance/metrics";
    public const string ServicesHealthPath = BasePath + "/health/services";
    public const string RecentEventsPath = BasePath + "/events/recent";
    public const string FileSystemStatusPath = BasePath + "/filesystem/status";
    public const string ApiMetricsPath = BasePath + "/api-metrics";

    private readonly ApiService _apiService;

    public MonitoringClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<THealth?> GetHealthOverviewAsync<THealth>()
    {
        return _apiService.GetAsync<THealth>(HealthOverviewPath);
    }

    public Task<TDatabase?> GetDatabaseStatisticsAsync<TDatabase>()
    {
        return _apiService.GetAsync<TDatabase>(DatabaseStatisticsPath);
    }

    public Task<TPerformance?> GetPerformanceMetricsAsync<TPerformance>()
    {
        return _apiService.GetAsync<TPerformance>(PerformanceMetricsPath);
    }

    public Task<TServices?> GetServicesHealthAsync<TServices>()
    {
        return _apiService.GetAsync<TServices>(ServicesHealthPath);
    }

    public Task<TEvents?> GetRecentEventsAsync<TEvents>()
    {
        return _apiService.GetAsync<TEvents>(RecentEventsPath);
    }

    public Task<TFileSystem?> GetFileSystemStatusAsync<TFileSystem>()
    {
        return _apiService.GetAsync<TFileSystem>(FileSystemStatusPath);
    }

    public Task<TMetrics?> GetApiMetricsAsync<TMetrics>()
    {
        return _apiService.GetAsync<TMetrics>(ApiMetricsPath);
    }
}
