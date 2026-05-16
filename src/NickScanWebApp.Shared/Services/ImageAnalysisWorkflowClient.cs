namespace NickScanWebApp.Shared.Services;

public sealed class ImageAnalysisWorkflowClient
{
    public const string BasePath = "/api/image-analysis";
    public const string MetricsPath = BasePath + "/metrics";

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
        return _apiService.GetAsync<TMetrics>(MetricsPath);
    }

    public Task<TOcr?> GetOcrAsync<TOcr>(string containerNumber)
    {
        return _apiService.GetAsync<TOcr>(BuildOcrPath(containerNumber));
    }

    public Task<TDetection?> GetDetectionAsync<TDetection>(string containerNumber)
    {
        return _apiService.GetAsync<TDetection>(BuildDetectionPath(containerNumber));
    }

    public Task<TQuality?> GetQualityAsync<TQuality>(string containerNumber)
    {
        return _apiService.GetAsync<TQuality>(BuildQualityPath(containerNumber));
    }

    public Task<TGroup?> GetGroupByIdentifierAsync<TGroup>(string groupIdentifier, string? scannerType)
    {
        return _apiService.GetAsync<TGroup>(BuildGroupByIdentifierPath(groupIdentifier, scannerType));
    }

    public Task<TContext?> GetWaveContextAsync<TContext>(Guid groupId)
    {
        return _apiService.GetAsync<TContext>(BuildWaveContextPath(groupId));
    }

    public static string BuildOcrPath(string containerNumber)
    {
        return $"{BasePath}/{Uri.EscapeDataString(containerNumber)}/ocr";
    }

    public static string BuildDetectionPath(string containerNumber)
    {
        return $"{BasePath}/{Uri.EscapeDataString(containerNumber)}/detect";
    }

    public static string BuildQualityPath(string containerNumber)
    {
        return $"{BasePath}/{Uri.EscapeDataString(containerNumber)}/quality";
    }

    public static string BuildEnhancedImagePath(string containerNumber)
    {
        return $"{BasePath}/{Uri.EscapeDataString(containerNumber)}/enhanced";
    }

    public static string BuildAnnotationEnhancePath(
        string containerNumber,
        int x,
        int y,
        int width,
        int height)
    {
        return $"{BasePath}/{Uri.EscapeDataString(containerNumber)}/annotations/enhance" +
            $"?x={x}&y={y}&width={width}&height={height}";
    }

    public static string BuildGroupByIdentifierPath(string groupIdentifier, string? scannerType)
    {
        var parts = new List<string>
        {
            $"identifier={Uri.EscapeDataString(groupIdentifier)}"
        };

        if (!string.IsNullOrWhiteSpace(scannerType))
        {
            parts.Add($"scannerType={Uri.EscapeDataString(scannerType)}");
        }

        return $"{BasePath}/group-by-identifier?{string.Join("&", parts)}";
    }

    public static string BuildWaveContextPath(Guid groupId)
    {
        return $"{BasePath}/wave-context/{groupId}";
    }
}
