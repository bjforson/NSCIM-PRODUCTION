using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class CargoGroupClient
{
    public const string BasePath = "/api/cargogroup";

    private readonly ApiService _apiService;

    public CargoGroupClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TIdentifier?> GetGroupIdentifierByContainerAsync<TIdentifier>(string containerNumber)
    {
        return _apiService.GetAsync<TIdentifier>(BuildByContainerPath(containerNumber));
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

    public Task<TData?> GetCargoGroupDataAsync<TData>(
        string groupIdentifier,
        string type,
        bool? loadScannerData = null,
        bool? loadImageData = null,
        bool? loadICUMSData = null)
    {
        return _apiService.GetAsync<TData>(
            BuildCargoGroupDataPath(groupIdentifier, type, loadScannerData, loadImageData, loadICUMSData));
    }

    public Task<TData?> GetCargoGroupICUMSAsync<TData>(string groupIdentifier, string type)
    {
        return _apiService.GetAsync<TData>(BuildCargoGroupICUMSPath(groupIdentifier, type));
    }

    public Task<TData?> GetCargoGroupScannerAsync<TData>(string groupIdentifier, string type)
    {
        return _apiService.GetAsync<TData>(BuildCargoGroupScannerPath(groupIdentifier, type));
    }

    public Task<TData?> GetCargoGroupImagesAsync<TData>(string groupIdentifier, string type)
    {
        return _apiService.GetAsync<TData>(BuildCargoGroupImagesPath(groupIdentifier, type));
    }

    public Task<TGroups?> GetCargoGroupsAsync<TGroups>(
        string? type = null,
        string? clearanceType = null,
        int page = 1,
        int pageSize = 50)
    {
        return _apiService.GetAsync<TGroups>(BuildCargoGroupsPath(type, clearanceType, page, pageSize));
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

    public static string BuildByContainerPath(string containerNumber)
    {
        return $"{BasePath}/by-container/{Uri.EscapeDataString(containerNumber)}";
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

    public static string BuildCargoGroupDataPath(
        string groupIdentifier,
        string type,
        bool? loadScannerData = null,
        bool? loadImageData = null,
        bool? loadICUMSData = null)
    {
        var parts = new List<string>();
        Add(parts, "type", type);
        Add(parts, "loadScannerData", loadScannerData);
        Add(parts, "loadImageData", loadImageData);
        Add(parts, "loadICUMSData", loadICUMSData);

        return $"{BasePath}/{Uri.EscapeDataString(groupIdentifier)}/data{BuildQueryString(parts)}";
    }

    public static string BuildCargoGroupICUMSPath(string groupIdentifier, string type)
    {
        return $"{BasePath}/{Uri.EscapeDataString(groupIdentifier)}/icums{BuildRequiredTypeQuery(type)}";
    }

    public static string BuildCargoGroupScannerPath(string groupIdentifier, string type)
    {
        return $"{BasePath}/{Uri.EscapeDataString(groupIdentifier)}/scanner{BuildRequiredTypeQuery(type)}";
    }

    public static string BuildCargoGroupImagesPath(string groupIdentifier, string type)
    {
        return $"{BasePath}/{Uri.EscapeDataString(groupIdentifier)}/images{BuildRequiredTypeQuery(type)}";
    }

    public static string BuildCargoGroupsPath(
        string? type = null,
        string? clearanceType = null,
        int page = 1,
        int pageSize = 50)
    {
        var parts = new List<string>();
        Add(parts, "type", type);
        Add(parts, "clearanceType", clearanceType);
        Add(parts, "page", page);
        Add(parts, "pageSize", pageSize);

        return $"{BasePath}{BuildQueryString(parts)}";
    }

    public static string BuildAiSummaryPath(string groupIdentifier)
    {
        return $"{BasePath}/{Uri.EscapeDataString(groupIdentifier)}/ai-summary";
    }

    public static string BuildLookupPath(string query, int limit = 50)
    {
        return $"{BasePath}/lookup?q={Uri.EscapeDataString(query)}&limit={limit}";
    }

    private static string BuildRequiredTypeQuery(string type)
    {
        var parts = new List<string>();
        Add(parts, "type", type);
        return BuildQueryString(parts);
    }

    private static void Add(List<string> parts, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            parts.Add($"{Uri.EscapeDataString(name)}={Uri.EscapeDataString(value)}");
        }
    }

    private static void Add(List<string> parts, string name, bool? value)
    {
        if (value.HasValue)
        {
            parts.Add($"{Uri.EscapeDataString(name)}={value.Value.ToString().ToLowerInvariant()}");
        }
    }

    private static void Add(List<string> parts, string name, int value)
    {
        parts.Add($"{Uri.EscapeDataString(name)}={value.ToString(CultureInfo.InvariantCulture)}");
    }

    private static string BuildQueryString(List<string> parts)
    {
        return parts.Count == 0 ? string.Empty : $"?{string.Join("&", parts)}";
    }
}
