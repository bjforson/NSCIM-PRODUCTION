using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class ContainersClient
{
    private const string BasePath = "/api/containers";
    private readonly ApiService _apiService;

    public ContainersClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TResponse?> GetEnrichedAsync<TResponse>(
        int page,
        int pageSize,
        string? search = null,
        string? stage = null,
        string? cargoType = null,
        string? clearanceType = null)
    {
        return _apiService.GetAsync<TResponse>(
            BuildEnrichedPath(page, pageSize, search, stage, cargoType, clearanceType));
    }

    public static string BuildEnrichedPath(
        int page,
        int pageSize,
        string? search = null,
        string? stage = null,
        string? cargoType = null,
        string? clearanceType = null)
    {
        var query = BuildQuery(
            ("page", page.ToString(CultureInfo.InvariantCulture)),
            ("pageSize", pageSize.ToString(CultureInfo.InvariantCulture)),
            ("search", search),
            ("stage", stage),
            ("cargoType", cargoType),
            ("clearanceType", clearanceType));

        return $"{BasePath}/enriched{query}";
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
