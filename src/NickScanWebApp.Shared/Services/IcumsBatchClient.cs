using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class IcumsBatchClient
{
    private const string BatchBasePath = "/api/icums/batch";
    private const string TransferBasePath = "/api/icums/transfer";
    private readonly ApiService _apiService;

    public IcumsBatchClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TStatus?> GetStatusAsync<TStatus>()
    {
        return _apiService.GetAsync<TStatus>($"{BatchBasePath}/status");
    }

    public Task<TStats?> GetStatsAsync<TStats>()
    {
        return _apiService.GetAsync<TStats>($"{BatchBasePath}/stats");
    }

    public Task<List<TFile>?> GetFilesAsync<TFile>(
        string? status = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var query = BuildQuery(
            ("status", status),
            ("fromDate", FormatDate(fromDate)),
            ("toDate", FormatDate(toDate)));

        return _apiService.GetAsync<List<TFile>>($"{BatchBasePath}/files{query}");
    }

    public Task<List<TRecord>?> GetFileRecordsAsync<TRecord>(int fileId)
    {
        return _apiService.GetAsync<List<TRecord>>($"{BatchBasePath}/files/{fileId}/records");
    }

    public Task<TVerification?> GetFileVerificationAsync<TVerification>(int fileId)
    {
        return _apiService.GetAsync<TVerification>($"{BatchBasePath}/files/{fileId}/verification");
    }

    public Task<List<TContainer>?> GetContainersAsync<TContainer>(
        string? search = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var query = BuildQuery(
            ("fromDate", FormatDate(fromDate)),
            ("toDate", FormatDate(toDate)),
            ("search", search));

        return _apiService.GetAsync<List<TContainer>>($"{BatchBasePath}/containers{query}");
    }

    public Task<List<TLog>?> GetLogsAsync<TLog>(
        string? level = null,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var query = BuildQuery(
            ("level", level),
            ("fromDate", FormatDate(fromDate)),
            ("toDate", FormatDate(toDate)));

        return _apiService.GetAsync<List<TLog>>($"{BatchBasePath}/logs{query}");
    }

    public async Task ToggleAsync(object request)
    {
        await _apiService.PostAsync<object, object>($"{BatchBasePath}/toggle", request);
    }

    public async Task SaveConfigAsync(object request)
    {
        await _apiService.PostAsync<object, object>($"{BatchBasePath}/config", request);
    }

    public async Task TriggerAsync()
    {
        await _apiService.PostAsync<object, object>($"{BatchBasePath}/trigger", new { });
    }

    public async Task RetryFileAsync(int fileId)
    {
        await _apiService.PostAsync<object, object>($"{BatchBasePath}/files/{fileId}/retry", new { });
    }

    public Task DeleteFileAsync(int fileId)
    {
        return _apiService.DeleteAsync($"{BatchBasePath}/files/{fileId}");
    }

    public Task<TStatus?> GetTransferStatusAsync<TStatus>()
    {
        return _apiService.GetAsync<TStatus>($"{TransferBasePath}/status");
    }

    public Task<TStats?> GetTransferStatisticsAsync<TStats>()
    {
        return _apiService.GetAsync<TStats>($"{TransferBasePath}/statistics");
    }

    public Task<List<THistory>?> GetTransferHistoryAsync<THistory>(
        int page = 1,
        int pageSize = 100,
        DateTime? fromDate = null,
        DateTime? toDate = null)
    {
        var query = BuildQuery(
            ("page", page.ToString(CultureInfo.InvariantCulture)),
            ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            ("fromDate", FormatDate(fromDate)),
            ("toDate", FormatDate(toDate)));

        return _apiService.GetAsync<List<THistory>>($"{TransferBasePath}/history{query}");
    }

    public Task<TResult?> TriggerTransferAsync<TResult>()
    {
        return _apiService.PostAsync<object, TResult>($"{TransferBasePath}/trigger", new { });
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
