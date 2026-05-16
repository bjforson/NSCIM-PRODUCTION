namespace NickScanWebApp.Shared.Services;

public sealed class BusinessRulesClient
{
    private const string BasePath = "/api/BusinessRules";
    private readonly ApiService _apiService;

    public BusinessRulesClient(ApiService apiService)
    {
        _apiService = apiService;
    }

    public Task<List<TRule>?> GetRulesAsync<TRule>()
    {
        return _apiService.GetAsync<List<TRule>>(BasePath);
    }

    public Task<TResponse?> SeedAsync<TResponse>()
    {
        return _apiService.PostAsync<object, TResponse>($"{BasePath}/seed", new { });
    }

    public async Task UpdateStatusAsync(int id, bool isActive)
    {
        await _apiService.PutAsync<object, object>($"{BasePath}/{id}/status", new { IsActive = isActive });
    }

    public Task<TRule?> CreateAsync<TRule>(object request)
    {
        return _apiService.PostAsync<object, TRule>(BasePath, request);
    }

    public Task<TRule?> UpdateAsync<TRule>(int id, object request)
    {
        return _apiService.PutAsync<object, TRule>($"{BasePath}/{id}", request);
    }

    public Task DeleteAsync(int id)
    {
        return _apiService.DeleteAsync($"{BasePath}/{id}");
    }
}
