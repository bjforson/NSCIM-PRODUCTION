namespace NickScanWebApp.Shared.Services;

public sealed class ImageAnalysisReadinessClient
{
    private const string BasePath = "/api/image-analysis/user";
    private readonly ApiService _apiService;

    public ImageAnalysisReadinessClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TSnapshot?> GetReadinessAsync<TSnapshot>(string role)
    {
        return _apiService.GetAsync<TSnapshot>(
            $"{BasePath}/readiness?role={Uri.EscapeDataString(role)}");
    }

    public Task<TSnapshot?> GetReadinessSnapshotAsync<TSnapshot>()
    {
        return _apiService.GetAsync<TSnapshot>($"{BasePath}/readiness-snapshot");
    }

    public Task<object?> SetReadyAsync(string role, bool isReady, string? sessionId = null)
    {
        return _apiService.PostAsync<object, object>($"{BasePath}/ready", new
        {
            Role = role,
            IsReady = isReady,
            SessionId = sessionId
        });
    }

    public Task<object?> SendHeartbeatAsync(string role)
    {
        return _apiService.PostAsync<object, object>($"{BasePath}/heartbeat", new { Role = role });
    }
}
