namespace NickScanWebApp.Shared.Services;

public sealed class PermissionAdminClient
{
    public const string BasePath = "/api/Permissions";
    public const string AllPath = BasePath + "/all";
    public const string ByCategoryPath = BasePath + "/by-category";

    private readonly ApiService _apiService;

    public PermissionAdminClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TPermissions?> GetAllAsync<TPermissions>()
    {
        return _apiService.GetAsync<TPermissions>(AllPath);
    }

    public Task<TPermissions?> GetByCategoryAsync<TPermissions>()
    {
        return _apiService.GetAsync<TPermissions>(ByCategoryPath);
    }
}
