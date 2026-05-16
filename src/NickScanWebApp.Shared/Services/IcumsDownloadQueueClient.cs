using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class IcumsDownloadQueueClient
{
    private const string BasePath = "/api/ICUMSDownloadQueue";
    private readonly ApiService _apiService;

    public IcumsDownloadQueueClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<List<TItem>?> GetItemsAsync<TItem>(
        int? limit = null,
        string? status = null,
        DateTime? startDate = null,
        DateTime? endDate = null)
    {
        var query = BuildQuery(
            ("limit", limit?.ToString(CultureInfo.InvariantCulture)),
            ("status", status),
            ("startDate", FormatDate(startDate)),
            ("endDate", FormatDate(endDate)));

        return _apiService.GetAsync<List<TItem>>($"{BasePath}{query}");
    }

    public Task<TStats?> GetStatsAsync<TStats>()
    {
        return _apiService.GetAsync<TStats>($"{BasePath}/stats");
    }

    public Task<TArchiveStats?> GetArchiveStatsAsync<TArchiveStats>()
    {
        return _apiService.GetAsync<TArchiveStats>($"{BasePath}/archive/stats");
    }

    public Task<TResponse?> EnqueueAsync<TResponse>(object request)
    {
        return _apiService.PostAsync<object, TResponse>($"{BasePath}/enqueue", request);
    }

    public async Task RetryAsync(int id)
    {
        await _apiService.PostAsync<object, object>($"{BasePath}/retry/{id}", new { });
    }

    public Task DeleteAsync(int id)
    {
        return _apiService.DeleteAsync($"{BasePath}/{id}");
    }

    public Task<TResponse?> RequeueFromArchiveAsync<TResponse>(object request)
    {
        return _apiService.PostAsync<object, TResponse>($"{BasePath}/requeue", request);
    }

    public Task<TResponse?> RequeuePendingAsync<TResponse>(object request)
    {
        return _apiService.PostAsync<object, TResponse>($"{BasePath}/requeue-pending", request);
    }

    public async Task UpdatePriorityAsync(object request)
    {
        await _apiService.PutAsync<object, object>($"{BasePath}/priority", request);
    }

    private static string? FormatDate(DateTime? date)
    {
        return date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }

    private static string BuildQuery(params (string Key, string? Value)[] parameters)
    {
        var parts = parameters
            .Where(parameter => !string.IsNullOrWhiteSpace(parameter.Value))
            .Select(parameter => $"{Uri.EscapeDataString(parameter.Key)}={Uri.EscapeDataString(parameter.Value!)}")
            .ToArray();

        return parts.Length == 0 ? string.Empty : "?" + string.Join("&", parts);
    }
}
