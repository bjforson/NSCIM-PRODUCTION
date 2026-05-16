using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class ContainerProcessingClient
{
    public const string BasePath = "/api/ContainerProcessing";
    public const string SummaryPath = BasePath + "/summary";

    private readonly ApiService _apiService;

    public ContainerProcessingClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TGroups?> GetGroupsAsync<TGroups>(
        string? clearanceType = null,
        int page = 1,
        int pageSize = 50)
    {
        return _apiService.GetAsync<TGroups>(BuildGroupsPath(clearanceType, page, pageSize));
    }

    public Task<TSummary?> GetSummaryAsync<TSummary>()
    {
        return _apiService.GetAsync<TSummary>(SummaryPath);
    }

    public static string BuildGroupsPath(string? clearanceType = null, int page = 1, int pageSize = 50)
    {
        var parts = new List<string>
        {
            $"page={page.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}"
        };

        if (!string.IsNullOrWhiteSpace(clearanceType))
        {
            parts.Add($"clearanceType={Uri.EscapeDataString(clearanceType)}");
        }

        return $"{BasePath}/groups?{string.Join("&", parts)}";
    }
}
