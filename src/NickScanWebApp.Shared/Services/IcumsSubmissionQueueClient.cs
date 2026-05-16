using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class IcumsSubmissionQueueClient
{
    private const string BasePath = "/api/ICUMSSubmissionQueue";
    private readonly ApiService _apiService;

    public IcumsSubmissionQueueClient(ApiService apiService)
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

    public async Task RetryAsync(int id)
    {
        await _apiService.PostAsync<object, object>($"{BasePath}/retry/{id}", new { });
    }

    public async Task CancelAsync(int id)
    {
        await _apiService.PostAsync<object, object>($"{BasePath}/cancel/{id}", new { });
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
