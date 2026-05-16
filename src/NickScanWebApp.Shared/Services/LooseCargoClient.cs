namespace NickScanWebApp.Shared.Services;

public sealed class LooseCargoClient
{
    public const string BasePath = "/api/LooseCargo";
    public const string StatsPath = BasePath + "/stats";
    public const string RecentPath = BasePath + "/recent";

    private readonly ApiService _apiService;

    public LooseCargoClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TStats?> GetStatsAsync<TStats>()
    {
        return _apiService.GetAsync<TStats>(StatsPath);
    }

    public Task<TResponse?> SearchAsync<TResponse>(
        string? clearanceType,
        string? crmsLevel,
        string? search,
        string? countryOfOrigin,
        string? regimeCode,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        string sortBy = "CreatedAt",
        bool sortDescending = true)
    {
        return _apiService.GetAsync<TResponse>(
            BuildSearchPath(
                clearanceType,
                crmsLevel,
                search,
                countryOfOrigin,
                regimeCode,
                fromDate,
                toDate,
                pageNumber,
                pageSize,
                sortBy,
                sortDescending));
    }

    public Task<TDetail?> GetDetailByIdAsync<TDetail>(int looseCargoId)
    {
        return _apiService.GetAsync<TDetail>(BuildDetailByIdPath(looseCargoId));
    }

    public Task<TDetail?> GetDetailByDeclarationAsync<TDetail>(string declarationNumber)
    {
        return _apiService.GetAsync<TDetail>(BuildDetailByDeclarationPath(declarationNumber));
    }

    public Task<TRecent?> GetRecentAsync<TRecent>(int days = 7)
    {
        return _apiService.GetAsync<TRecent>(BuildRecentPath(days));
    }

    public static string BuildSearchPath(
        string? clearanceType,
        string? crmsLevel,
        string? search,
        string? countryOfOrigin,
        string? regimeCode,
        DateTime? fromDate,
        DateTime? toDate,
        int pageNumber,
        int pageSize,
        string sortBy = "CreatedAt",
        bool sortDescending = true)
    {
        var parts = new List<string>();

        if (!IsAllOrBlank(clearanceType))
        {
            parts.Add($"clearanceType={Uri.EscapeDataString(clearanceType!)}");
        }

        if (!IsAllOrBlank(crmsLevel))
        {
            parts.Add($"crmsLevel={Uri.EscapeDataString(crmsLevel!)}");
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            parts.Add($"search={Uri.EscapeDataString(search)}");
        }

        if (!string.IsNullOrWhiteSpace(countryOfOrigin))
        {
            parts.Add($"countryOfOrigin={Uri.EscapeDataString(countryOfOrigin)}");
        }

        if (!string.IsNullOrWhiteSpace(regimeCode))
        {
            parts.Add($"regimeCode={Uri.EscapeDataString(regimeCode)}");
        }

        if (fromDate.HasValue)
        {
            parts.Add($"fromDate={fromDate.Value:yyyy-MM-dd}");
        }

        if (toDate.HasValue)
        {
            parts.Add($"toDate={toDate.Value:yyyy-MM-dd}");
        }

        parts.Add($"pageNumber={pageNumber}");
        parts.Add($"pageSize={pageSize}");

        if (!string.IsNullOrWhiteSpace(sortBy))
        {
            parts.Add($"sortBy={Uri.EscapeDataString(sortBy)}");
        }

        parts.Add($"sortDescending={sortDescending.ToString().ToLowerInvariant()}");

        return $"{BasePath}?{string.Join("&", parts)}";
    }

    public static string BuildDetailByIdPath(int looseCargoId)
    {
        return $"{BasePath}/{looseCargoId}";
    }

    public static string BuildDetailByDeclarationPath(string declarationNumber)
    {
        return $"{BasePath}/declaration/{Uri.EscapeDataString(declarationNumber)}";
    }

    public static string BuildRecentPath(int days = 7)
    {
        return $"{RecentPath}?days={days}";
    }

    private static bool IsAllOrBlank(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            || string.Equals(value, "All", StringComparison.OrdinalIgnoreCase);
    }
}
