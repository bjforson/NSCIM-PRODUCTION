namespace NickScanWebApp.Shared.Services;

public sealed class NotificationClient
{
    public const string BasePath = "/api/Notifications";

    private readonly ApiService _apiService;

    public NotificationClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public async Task<int> GetUnreadCountAsync(string username)
    {
        return await _apiService.TryGetAsync<int>(BuildUserCountPath(username));
    }

    public Task<TNotifications?> GetUserNotificationsAsync<TNotifications>(
        string username,
        bool includeRead = false,
        int limit = 10)
    {
        return _apiService.TryGetAsync<TNotifications>(BuildUserNotificationsPath(username, includeRead, limit));
    }

    public Task<TResult?> ClearAllForUserAsync<TResult>(string username)
    {
        return _apiService.DeleteAsync<TResult>(BuildClearAllPath(username));
    }

    public Task<TResult?> DeleteNotificationAsync<TResult>(int notificationId)
    {
        return _apiService.DeleteAsync<TResult>($"{BasePath}/{notificationId}");
    }

    public static string BuildUserCountPath(string username)
    {
        return $"{BasePath}/user/{Uri.EscapeDataString(username)}/count";
    }

    public static string BuildUserNotificationsPath(string username, bool includeRead, int limit)
    {
        return $"{BasePath}/user/{Uri.EscapeDataString(username)}?includeRead={includeRead.ToString().ToLowerInvariant()}&limit={limit}";
    }

    public static string BuildClearAllPath(string username)
    {
        return $"{BasePath}/user/{Uri.EscapeDataString(username)}/clear-all";
    }
}
