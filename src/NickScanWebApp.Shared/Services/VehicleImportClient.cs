using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class VehicleImportClient
{
    public const string BasePath = "/api/vehicleimport";
    public const string SearchPath = BasePath + "/search";

    private readonly ApiService _apiService;

    public VehicleImportClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TResponse?> SearchAsync<TResponse>(
        int page = 1,
        int pageSize = 50,
        string? searchTerm = null,
        string? importType = null,
        string? processingStatus = null)
    {
        return _apiService.GetAsync<TResponse>(
            BuildSearchPath(page, pageSize, searchTerm, importType, processingStatus));
    }

    public static string BuildSearchPath(
        int page = 1,
        int pageSize = 50,
        string? searchTerm = null,
        string? importType = null,
        string? processingStatus = null)
    {
        var parts = new List<string>
        {
            $"page={page.ToString(CultureInfo.InvariantCulture)}",
            $"pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}"
        };

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            parts.Add($"searchTerm={Uri.EscapeDataString(searchTerm)}");
        }

        if (!string.IsNullOrWhiteSpace(importType))
        {
            parts.Add($"importType={Uri.EscapeDataString(importType)}");
        }

        if (!string.IsNullOrWhiteSpace(processingStatus))
        {
            parts.Add($"processingStatus={Uri.EscapeDataString(processingStatus)}");
        }

        return $"{SearchPath}?{string.Join("&", parts)}";
    }
}
