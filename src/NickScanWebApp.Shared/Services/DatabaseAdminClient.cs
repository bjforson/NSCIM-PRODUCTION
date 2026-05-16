namespace NickScanWebApp.Shared.Services;

public sealed class DatabaseAdminClient
{
    public const string BasePath = "/api/DatabaseAdmin";
    public const string ConnectionsPath = BasePath + "/connections";
    public const string TablesPath = BasePath + "/tables";
    public const string QueryPath = BasePath + "/query";

    private readonly ApiService _apiService;

    public DatabaseAdminClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<TConnections?> GetConnectionsAsync<TConnections>()
    {
        return _apiService.GetAsync<TConnections>(ConnectionsPath);
    }

    public Task<TTables?> GetTablesAsync<TTables>()
    {
        return _apiService.GetAsync<TTables>(TablesPath);
    }

    public Task<TResponse?> ExecuteQueryAsync<TRequest, TResponse>(TRequest request)
    {
        return _apiService.PostAsync<TRequest, TResponse>(QueryPath, request);
    }
}
