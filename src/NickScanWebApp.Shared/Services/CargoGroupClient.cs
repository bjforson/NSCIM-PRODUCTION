namespace NickScanWebApp.Shared.Services;

public sealed class CargoGroupClient
{
    public const string BasePath = "/api/cargogroup";

    private readonly ApiService _apiService;

    public CargoGroupClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TCargoGroup?> GetCargoGroupAsync<TCargoGroup>(
        string groupIdentifier,
        string? type = null,
        bool? loadScannerData = null,
        bool? loadImageData = null,
        bool? loadICUMSData = null)
    {
        return _apiService.GetAsync<TCargoGroup>(
            BuildCargoGroupPath(groupIdentifier, type, loadScannerData, loadImageData, loadICUMSData));
    }

    public Task<TCargoGroup?> TryGetCargoGroupAsync<TCargoGroup>(
        string groupIdentifier,
        string? type = null,
        bool? loadScannerData = null,
        bool? loadImageData = null,
        bool? loadICUMSData = null)
    {
        return _apiService.TryGetAsync<TCargoGroup>(
            BuildCargoGroupPath(groupIdentifier, type, loadScannerData, loadImageData, loadICUMSData));
    }

    public Task<TSummary?> GetAiSummaryAsync<TSummary>(string groupIdentifier)
    {
        return _apiService.GetAsync<TSummary>(BuildAiSummaryPath(groupIdentifier));
    }

    public Task<TSummary?> SaveAiSummaryAsync<TRequest, TSummary>(string groupIdentifier, TRequest request)
    {
        return _apiService.PostAsync<TRequest, TSummary>(BuildAiSummaryPath(groupIdentifier), request);
    }

    public Task<TLookup?> LookupAsync<TLookup>(string query, int limit = 50)
    {
        return _apiService.GetAsync<TLookup>(BuildLookupPath(query, limit));
    }

    public static string BuildCargoGroupPath(
        string groupIdentifier,
        string? type = null,
        bool? loadScannerData = null,
        bool? loadImageData = null,
        bool? loadICUMSData = null)
    {
        var parts = new List<string>();

        if (!string.IsNullOrWhiteSpace(type))
        {
            parts.Add($"type={Uri.EscapeDataString(type)}");
        }

        if (loadScannerData.HasValue)
        {
            parts.Add($"loadScannerData={loadScannerData.Value.ToString().ToLowerInvariant()}");
        }

        if (loadImageData.HasValue)
        {
            parts.Add($"loadImageData={loadImageData.Value.ToString().ToLowerInvariant()}");
        }

        if (loadICUMSData.HasValue)
        {
            parts.Add($"loadICUMSData={loadICUMSData.Value.ToString().ToLowerInvariant()}");
        }

        var path = $"{BasePath}/{Uri.EscapeDataString(groupIdentifier)}";
        return parts.Count == 0 ? path : $"{path}?{string.Join("&", parts)}";
    }

    public static string BuildAiSummaryPath(string groupIdentifier)
    {
        return $"{BasePath}/{Uri.EscapeDataString(groupIdentifier)}/ai-summary";
    }

    public static string BuildLookupPath(string query, int limit = 50)
    {
        return $"{BasePath}/lookup?q={Uri.EscapeDataString(query)}&limit={limit}";
    }
}
