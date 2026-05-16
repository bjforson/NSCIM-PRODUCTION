namespace NickScanWebApp.Shared.Services;

public sealed class ContainerValidationClient
{
    private const string BasePath = "/api/containervalidation";
    private readonly ApiService _apiService;

    public ContainerValidationClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TResponse?> GetPendingAsync<TResponse>(string queryString)
    {
        var separator = string.IsNullOrWhiteSpace(queryString) ? string.Empty : "?";
        return _apiService.GetAsync<TResponse>($"{BasePath}/pending{separator}{queryString}");
    }

    public Task<TDetails?> GetDetailsAsync<TDetails>(string containerNumber)
    {
        return _apiService.GetAsync<TDetails>(
            $"{BasePath}/details/{Uri.EscapeDataString(containerNumber)}");
    }

    public Task<object?> ApproveAsync(string containerNumber)
    {
        return _apiService.PostAsync<object, object>(
            $"{BasePath}/approve/{Uri.EscapeDataString(containerNumber)}",
            new { });
    }

    public Task<object?> RejectAsync(string containerNumber)
    {
        return _apiService.PostAsync<object, object>(
            $"{BasePath}/reject/{Uri.EscapeDataString(containerNumber)}",
            new { });
    }
}
