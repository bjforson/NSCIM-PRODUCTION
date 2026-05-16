namespace NickScanWebApp.Shared.Services;

public sealed class InspectionFindingCategoryClient
{
    public const string BasePath = "/api/inspection-finding-categories";
    public const string ThreatPath = BasePath + "/threat";
    public const string RevenueAnomalyPath = BasePath + "/revenue-anomaly";

    private readonly ApiService _apiService;

    public InspectionFindingCategoryClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<List<TCategory>?> GetThreatCategoriesAsync<TCategory>()
    {
        return _apiService.GetAsync<List<TCategory>>(ThreatPath);
    }

    public Task<List<TCategory>?> GetRevenueAnomalyCategoriesAsync<TCategory>()
    {
        return _apiService.GetAsync<List<TCategory>>(RevenueAnomalyPath);
    }
}
