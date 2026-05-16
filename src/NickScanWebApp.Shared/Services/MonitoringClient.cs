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
    public const string DeprecatedEndpointsSummaryPath = BasePath + "/deprecated-endpoints/summary";
    public const string Phase3RoutesSummaryPath = BasePath + "/phase3-routes/summary";
    public const string AllEndpointsSummaryPath = BasePath + "/all-endpoints/summary";
    public const string QueueHealthPath = QueueHealthClient.BasePath;

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

    public Task<TDeprecated?> GetDeprecatedEndpointsSummaryAsync<TDeprecated>()
    {
        return _apiService.GetAsync<TDeprecated>(DeprecatedEndpointsSummaryPath);
    }

    public Task<TPhase3?> GetPhase3RoutesSummaryAsync<TPhase3>()
    {
        return _apiService.GetAsync<TPhase3>(Phase3RoutesSummaryPath);
    }

    public Task<TRoutes?> GetSafeToRemoveAsync<TRoutes>(int daysWithZeroUsage = 30)
    {
        return _apiService.GetAsync<TRoutes>(BuildSafeToRemovePath(daysWithZeroUsage));
    }

    public Task<TEndpoints?> GetAllEndpointsSummaryAsync<TEndpoints>()
    {
        return _apiService.GetAsync<TEndpoints>(AllEndpointsSummaryPath);
    }

    public Task<TCallers?> GetEndpointCallersAsync<TCallers>(string endpoint)
    {
        return _apiService.GetAsync<TCallers>(BuildEndpointCallersPath(endpoint));
    }

    public Task<TQueue?> GetQueueHealthAsync<TQueue>()
    {
        return _apiService.GetAsync<TQueue>(QueueHealthPath);
    }

    public static string BuildSafeToRemovePath(int daysWithZeroUsage)
    {
        return $"{BasePath}/safe-to-remove?daysWithZeroUsage={daysWithZeroUsage}";
    }

    public static string BuildEndpointCallersPath(string endpoint)
    {
        return $"{BasePath}/endpoint-callers?ep={Uri.EscapeDataString(endpoint)}";
    }
}
