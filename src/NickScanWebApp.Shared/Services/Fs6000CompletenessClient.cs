namespace NickScanWebApp.Shared.Services;

public sealed class Fs6000CompletenessClient
{
    public const string BasePath = "/api/fs6000imagecompleteness";
    public const string StatsPath = BasePath + "/stats";
    public const string ScansWithoutImagesPath = BasePath + "/scans-without-images";

    private readonly ApiService _apiService;

    public Fs6000CompletenessClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TStats?> GetStatsAsync<TStats>()
    {
        return _apiService.GetAsync<TStats>(StatsPath);
    }

    public Task<TScans?> GetScansWithoutImagesAsync<TScans>(int limit = 100)
    {
        return _apiService.GetAsync<TScans>(BuildScansWithoutImagesPath(limit));
    }

    public static string BuildScansWithoutImagesPath(int limit = 100)
    {
        return $"{ScansWithoutImagesPath}?limit={limit}";
    }
}
