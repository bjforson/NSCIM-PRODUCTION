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

    public Task<TNotifications?> GetUserNotificationsRequiredAsync<TNotifications>(
        string username,
        bool includeRead = false,
        int limit = 10)
    {
        return _apiService.GetAsync<TNotifications>(BuildUserNotificationsPath(username, includeRead, limit));
    }

    public Task<TResult?> MarkAsReadAsync<TResult>(int notificationId)
    {
        return _apiService.PutAsync<object, TResult>(BuildReadPath(notificationId), new { });
    }

    public Task<object?> CreateNotificationAsync<TRequest>(TRequest request)
    {
        return CreateNotificationAsync<TRequest, object>(request);
    }

    public Task<TResult?> CreateNotificationAsync<TRequest, TResult>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResult>(BasePath, request);
    }

    public Task<TResult?> MarkAsUnreadAsync<TResult>(int notificationId)
    {
        return _apiService.PutAsync<object, TResult>(BuildUnreadPath(notificationId), new { });
    }

    public Task<TResult?> MarkAllAsReadForUserAsync<TResult>(string username)
    {
        return _apiService.PutAsync<object, TResult>(BuildReadAllPath(username), new { });
    }

    public Task<TResult?> ClearReadForUserAsync<TResult>(string username)
    {
        return _apiService.DeleteAsync<TResult>(BuildClearReadPath(username));
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

    public static string BuildReadPath(int notificationId)
    {
        return $"{BuildNotificationPath(notificationId)}/read";
    }

    public static string BuildUnreadPath(int notificationId)
    {
        return $"{BuildNotificationPath(notificationId)}/unread";
    }

    public static string BuildReadAllPath(string username)
    {
        return $"{BasePath}/user/{Uri.EscapeDataString(username)}/read-all";
    }

    public static string BuildClearReadPath(string username)
    {
        return $"{BasePath}/user/{Uri.EscapeDataString(username)}/clear-read";
    }

    public static string BuildClearAllPath(string username)
    {
        return $"{BasePath}/user/{Uri.EscapeDataString(username)}/clear-all";
    }

    public static string BuildNotificationPath(int notificationId)
    {
        return $"{BasePath}/{notificationId}";
    }
}
