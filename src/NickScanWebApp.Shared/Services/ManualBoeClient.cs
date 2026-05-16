namespace NickScanWebApp.Shared.Services;

public class ManualBoeClient
{
    private const string BasePath = "/api/ICUMSManual";

    private readonly ApiService _apiService;

    public ManualBoeClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<ManualBoeRequestResult?> SubmitRequestAsync(string containerNumber)
    {
        var escapedContainerNumber = Uri.EscapeDataString(containerNumber);
        return _apiService.PostAsync<object, ManualBoeRequestResult>(
            $"{BasePath}/trigger-download/{escapedContainerNumber}",
            new { });
    }
}

public class ManualBoeRequestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string ContainerNumber { get; set; } = string.Empty;
    public DateTime? QueuedAt { get; set; }
}
