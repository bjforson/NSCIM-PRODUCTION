namespace NickScanWebApp.Shared.Services;

public sealed class CommsGatewayClient
{
    public const string BasePath = "/api/CommsGateway";
    public const string PingPath = BasePath + "/ping";
    public const string TestEmailPath = BasePath + "/test-email";
    public const string TestSmsPath = BasePath + "/test-sms";

    private readonly ApiService _apiService;

    public CommsGatewayClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TPing?> PingAsync<TPing>()
    {
        return _apiService.GetAsync<TPing>(PingPath);
    }

    public Task<TResult?> SendTestEmailAsync<TRequest, TResult>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResult>(TestEmailPath, request);
    }

    public Task<TResult?> SendTestSmsAsync<TRequest, TResult>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResult>(TestSmsPath, request);
    }

    public Task<THistory?> GetHistoryAsync<THistory>(int page = 1, int pageSize = 25)
    {
        return _apiService.GetAsync<THistory>(BuildHistoryPath(page, pageSize));
    }

    public static string BuildHistoryPath(int page, int pageSize)
    {
        return $"{BasePath}/history?page={page}&pageSize={pageSize}";
    }
}
