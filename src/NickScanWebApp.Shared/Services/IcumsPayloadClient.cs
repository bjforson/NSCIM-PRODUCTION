namespace NickScanWebApp.Shared.Services;

public sealed class IcumsPayloadClient
{
    public const string BasePath = "/api/IcumsPayload";
    public const string SummaryPath = BasePath + "/summary";
    public const string ListPath = BasePath + "/list";
    public const string ReadPath = BasePath + "/read";
    public const string ImagePath = BasePath + "/image";
    public const string VerifyStatusPath = BasePath + "/verify-status";

    private readonly ApiService _apiService;

    public IcumsPayloadClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TSummary?> GetSummaryAsync<TSummary>()
    {
        return _apiService.GetAsync<TSummary>(SummaryPath);
    }

    public Task<TList?> ListPayloadsAsync<TList>(int limit = 50, string? subfolder = null)
    {
        return _apiService.GetAsync<TList>(BuildListPath(limit, subfolder));
    }

    public Task<TPayload?> ReadPayloadAsync<TPayload>(string fileName, string? subfolder = null)
    {
        return _apiService.GetAsync<TPayload>(BuildReadPath(fileName, subfolder));
    }

    public Task<TResponse?> VerifyStatusAsync<TRequest, TResponse>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(VerifyStatusPath, request);
    }

    public static string BuildListPath(int limit = 50, string? subfolder = null)
    {
        var parts = new List<string>();

        if (!string.IsNullOrEmpty(subfolder))
        {
            parts.Add($"subfolder={Uri.EscapeDataString(subfolder)}");
        }

        parts.Add($"limit={limit}");

        return $"{ListPath}?{string.Join("&", parts)}";
    }

    public static string BuildReadPath(string fileName, string? subfolder = null)
    {
        var parts = new List<string>
        {
            $"fileName={Uri.EscapeDataString(fileName)}"
        };

        if (!string.IsNullOrEmpty(subfolder))
        {
            parts.Add($"subfolder={Uri.EscapeDataString(subfolder)}");
        }

        return $"{ReadPath}?{string.Join("&", parts)}";
    }

    public static string BuildImagePath(string fileName, string? subfolder = null)
    {
        var parts = new List<string>
        {
            $"fileName={Uri.EscapeDataString(fileName)}"
        };

        if (!string.IsNullOrEmpty(subfolder))
        {
            parts.Add($"subfolder={Uri.EscapeDataString(subfolder)}");
        }

        return $"{ImagePath}?{string.Join("&", parts)}";
    }
}
