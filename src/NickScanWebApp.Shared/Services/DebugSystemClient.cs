namespace NickScanWebApp.Shared.Services;

public sealed class DebugSystemClient
{
    public const string BasePath = "/api/debug";
    public const string SystemPath = BasePath + "/system";

    private readonly ApiService _apiService;

    public DebugSystemClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TInfo?> GetSystemInfoAsync<TInfo>()
    {
        return _apiService.GetAsync<TInfo>(SystemPath);
    }
}
