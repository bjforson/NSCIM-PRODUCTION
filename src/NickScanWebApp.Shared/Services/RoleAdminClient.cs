namespace NickScanWebApp.Shared.Services;

public sealed class RoleAdminClient
{
    public const string BasePath = "/api/Roles";

    private readonly ApiService _apiService;

    public RoleAdminClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TRoles?> GetRolesAsync<TRoles>()
    {
        return _apiService.GetAsync<TRoles>(BasePath);
    }

    public Task<TPermissions?> GetRolePermissionsAsync<TPermissions>(int roleId)
    {
        return _apiService.GetAsync<TPermissions>($"{BuildRolePath(roleId)}/permissions");
    }

    public Task<TUsers?> GetRoleUsersAsync<TUsers>(int roleId)
    {
        return _apiService.GetAsync<TUsers>($"{BuildRolePath(roleId)}/users");
    }

    public Task<TResult?> CreateRoleAsync<TResult>(object request)
    {
        return _apiService.PostAsync<object, TResult>(BasePath, request);
    }

    public Task<TResult?> UpdateRoleAsync<TResult>(int roleId, object request)
    {
        return _apiService.PutAsync<object, TResult>(BuildRolePath(roleId), request);
    }

    public Task<TResult?> UpdateRolePermissionsAsync<TResult>(int roleId, object request)
    {
        return _apiService.PutAsync<object, TResult>($"{BuildRolePath(roleId)}/permissions", request);
    }

    public Task DeleteRoleAsync(int roleId, string deletedBy)
    {
        return _apiService.DeleteAsync(BuildDeleteRolePath(roleId, deletedBy));
    }

    public static string BuildRolePath(int roleId)
    {
        return $"{BasePath}/{roleId}";
    }

    public static string BuildDeleteRolePath(int roleId, string deletedBy)
    {
        return $"{BuildRolePath(roleId)}?deletedBy={Uri.EscapeDataString(deletedBy)}";
    }
}
