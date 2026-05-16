namespace NickScanWebApp.Shared.Services;

public sealed class ImageAnalysisWorkflowClient
{
    private const string BasePath = "/api/image-analysis";
    private readonly ApiService _apiService;

    public ImageAnalysisWorkflowClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<List<TAssignment>?> GetMyAssignmentsAsync<TAssignment>(string role)
    {
        return _apiService.GetAsync<List<TAssignment>>(
            $"{BasePath}/my-assignments?role={Uri.EscapeDataString(role)}");
    }

    public Task<List<TAssignment>?> TryGetAvailableAsync<TAssignment>(string role)
    {
        return _apiService.TryGetAsync<List<TAssignment>>(
            $"{BasePath}/available?role={Uri.EscapeDataString(role)}");
    }

    public Task<object?> ClaimGroupAsync(Guid groupId)
    {
        return _apiService.PostAsync<object, object>(
            $"{BasePath}/groups/{groupId}/claim",
            new { });
    }

    public Task<object?> RenewLeaseAsync(string groupIdentifier)
    {
        return _apiService.PostAsync<object, object>(
            $"{BasePath}/groups/{Uri.EscapeDataString(groupIdentifier)}/lease/renew",
            new { });
    }

    public Task<TMetrics?> GetMetricsAsync<TMetrics>()
    {
        return _apiService.GetAsync<TMetrics>($"{BasePath}/metrics");
    }
}
