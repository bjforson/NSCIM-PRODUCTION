namespace NickScanWebApp.Shared.Services;

public sealed class UserAdminClient
{
    public const string BasePath = "/api/Users";

    private readonly ApiService _apiService;

    public UserAdminClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TUsers?> GetUsersAsync<TUsers>()
    {
        return _apiService.GetAsync<TUsers>(BasePath);
    }

    public Task<TResult?> CreateUserAsync<TResult>(object request)
    {
        return _apiService.PostAsync<object, TResult>(BasePath, request);
    }

    public Task<TResult?> UpdateUserAsync<TResult>(int userId, object request)
    {
        return _apiService.PutAsync<object, TResult>(BuildUserPath(userId), request);
    }

    public Task<TResult?> ResetPasswordAsync<TResult>(int userId)
    {
        return _apiService.PostAsync<object, TResult>($"{BuildUserPath(userId)}/reset-password", new { });
    }

    public Task DeleteUserAsync(int userId)
    {
        return _apiService.DeleteAsync(BuildUserPath(userId));
    }

    public static string BuildUserPath(int userId)
    {
        return $"{BasePath}/{userId}";
    }
}
