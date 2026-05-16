using System.Globalization;

namespace NickScanWebApp.Shared.Services;

public sealed class ConsolidatedCargoClient
{
    private const string BasePath = "/api/consolidatedcargo";
    private readonly ApiService _apiService;

    public ConsolidatedCargoClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<List<TGroup>?> GetNonConsolidatedAsync<TGroup>(int pageSize)
    {
        return _apiService.GetAsync<List<TGroup>>(
            $"{BasePath}/non-consolidated?pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}");
    }

    public Task<List<TGroup>?> GetConsolidatedAsync<TGroup>(int pageSize)
    {
        return _apiService.GetAsync<List<TGroup>>(
            $"{BasePath}/consolidated?pageSize={pageSize.ToString(CultureInfo.InvariantCulture)}");
    }

    public Task<List<TContainer>?> GetContainersAsync<TContainer>(string? containerNumber = null)
    {
        return _apiService.GetAsync<List<TContainer>>(BuildContainersPath(containerNumber));
    }

    public Task<List<THouseBl>?> GetHouseBlsByContainerAsync<THouseBl>(string containerNumber)
    {
        return _apiService.GetAsync<List<THouseBl>>(
            $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}/housebls");
    }

    public Task<List<string>?> GetContainersByDeclarationAsync(string declarationNumber)
    {
        return _apiService.GetAsync<List<string>>(
            $"{BasePath}/declaration/{Uri.EscapeDataString(declarationNumber)}/containers");
    }

    public static string BuildContainersPath(string? containerNumber = null)
    {
        if (string.IsNullOrWhiteSpace(containerNumber))
        {
            return $"{BasePath}/containers";
        }

        return $"{BasePath}/containers?containerNumber={Uri.EscapeDataString(containerNumber)}";
    }
}
