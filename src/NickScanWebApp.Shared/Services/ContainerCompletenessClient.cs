namespace NickScanWebApp.Shared.Services;

public sealed class ContainerCompletenessClient
{
    private const string BasePath = "/api/containercompleteness";
    private readonly ApiService _apiService;

    public ContainerCompletenessClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TStats?> GetStatsAsync<TStats>()
    {
        return _apiService.GetAsync<TStats>($"{BasePath}/stats");
    }

    public Task<List<TContainer>?> GetMissingAsync<TContainer>()
    {
        return _apiService.GetAsync<List<TContainer>>($"{BasePath}/missing");
    }

    public Task<List<TContainer>?> GetCompleteAsync<TContainer>()
    {
        return _apiService.GetAsync<List<TContainer>>($"{BasePath}/complete");
    }

    public Task<object?> TriggerCheckAsync()
    {
        return _apiService.PostAsync<object, object>($"{BasePath}/trigger-check", new { });
    }

    public Task<object?> RequestBoeAsync(string containerNumber)
    {
        return _apiService.PostAsync<object, object>(
            $"{BasePath}/request-boe/{Uri.EscapeDataString(containerNumber)}",
            new { });
    }

    public Task<object?> RequestBoeBulkAsync(List<string> containerNumbers)
    {
        return _apiService.PostAsync<List<string>, object>($"{BasePath}/request-boe-bulk", containerNumbers);
    }
}
