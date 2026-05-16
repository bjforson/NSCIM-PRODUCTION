namespace NickScanWebApp.Shared.Services;

public sealed class AccessReviewClient
{
    public const string BasePath = "/api/accessreview";

    private readonly ApiService _apiService;

    public AccessReviewClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TResponse?> GetUsersAsync<TResponse>(bool includeInactive = false, int page = 1, int pageSize = 100)
    {
        return _apiService.GetAsync<TResponse>(BuildUsersPath(includeInactive, page, pageSize));
    }

    public Task<TResult?> ApproveUserAsync<TResult>(int userId, object request)
    {
        return _apiService.PostAsync<object, TResult>($"{BuildUserPath(userId)}/approve", request);
    }

    public Task<TResult?> RevokeUserAsync<TResult>(int userId, object request)
    {
        return _apiService.PostAsync<object, TResult>($"{BuildUserPath(userId)}/revoke", request);
    }

    public static string BuildUsersPath(bool includeInactive, int page, int pageSize)
    {
        return $"{BasePath}/users?includeInactive={includeInactive.ToString().ToLowerInvariant()}&page={page}&pageSize={pageSize}";
    }

    public static string BuildUserPath(int userId)
    {
        return $"{BasePath}/users/{userId}";
    }
}
