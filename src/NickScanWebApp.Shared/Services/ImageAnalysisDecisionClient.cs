namespace NickScanWebApp.Shared.Services;

public sealed class ImageAnalysisDecisionClient
{
    public const string BasePath = "/api/ImageAnalysisDecision";
    public const string DecisionsPath = BasePath;
    public const string RectanglesPath = BasePath + "/rectangles";

    private readonly ApiService _apiService;

    public ImageAnalysisDecisionClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<List<TDecision>?> GetContainerDecisionsAsync<TDecision>(string containerNumber)
    {
        return _apiService.GetAsync<List<TDecision>>(BuildContainerDecisionsPath(containerNumber));
    }

    public Task<TDecision?> GetGroupOverallDecisionAsync<TDecision>(string groupIdentifier)
    {
        return _apiService.GetAsync<TDecision>(BuildGroupOverallDecisionPath(groupIdentifier));
    }

    public Task<Dictionary<string, TStatus>?> GetGroupStatusBatchAsync<TStatus>(IEnumerable<string> groupIdentifiers)
    {
        return _apiService.GetAsync<Dictionary<string, TStatus>>(BuildGroupStatusBatchPath(groupIdentifiers));
    }

    public Task<TResponse?> SaveDecisionAsync<TRequest, TResponse>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(DecisionsPath, request);
    }

    public Task<object?> SaveDecisionAsync<TRequest>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, object>(DecisionsPath, request);
    }

    public Task<object?> SaveRectanglesAsync<TRequest>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, object>(RectanglesPath, request);
    }

    public static string BuildContainerDecisionsPath(string containerNumber)
    {
        return $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}";
    }

    public static string BuildGroupOverallDecisionPath(string groupIdentifier)
    {
        return $"{BasePath}/group/{Uri.EscapeDataString(groupIdentifier)}/overall";
    }

    public static string BuildGroupStatusBatchPath(IEnumerable<string> groupIdentifiers)
    {
        var groups = string.Join(",", groupIdentifiers.Select(Uri.EscapeDataString));
        return $"{BasePath}/groups/batch?groups={groups}";
    }
}
