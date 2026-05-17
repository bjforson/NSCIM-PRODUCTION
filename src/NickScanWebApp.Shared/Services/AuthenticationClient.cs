using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

namespace NickScanWebApp.Shared.Services;

public sealed class AuthenticationClient
{
    private const string ApiClientName = "NickScanAPI";
    private readonly IHttpClientFactory _httpClientFactory;

    public AuthenticationClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public Task<TStats?> GetPublicSystemStatsAsync<TStats>(TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var httpClient = CreateClient(timeout);
        return httpClient.GetFromJsonAsync<TStats>(AuthenticationRoutes.PublicSystemStatsPath, cancellationToken);
    }

    public async Task<AuthenticationLoginResult<TResponse>> LoginAsync<TRequest, TResponse>(
        TRequest request,
        TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        var httpClient = CreateClient(timeout);
        using var response = await httpClient.PostAsJsonAsync(
            AuthenticationRoutes.LoginPath,
            request,
            cancellationToken);

        var payload = response.IsSuccessStatusCode
            ? await response.Content.ReadFromJsonAsync<TResponse>(cancellationToken)
            : default;

        return new AuthenticationLoginResult<TResponse>(response.StatusCode, payload);
    }

    public async Task<List<string>?> GetMyPermissionsAsync(string token, CancellationToken cancellationToken = default)
    {
        var httpClient = CreateClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, AuthenticationRoutes.MyPermissionsPath);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }

        return await response.Content.ReadFromJsonAsync<List<string>>(cancellationToken);
    }

    private HttpClient CreateClient(TimeSpan? timeout = null)
    {
        var httpClient = _httpClientFactory.CreateClient(ApiClientName);
        if (timeout.HasValue)
        {
            httpClient.Timeout = timeout.Value;
        }

        return httpClient;
    }
}

public sealed record AuthenticationLoginResult<TResponse>(HttpStatusCode StatusCode, TResponse? Payload)
{
    public bool IsSuccessStatusCode => (int)StatusCode >= 200 && (int)StatusCode <= 299;
}
