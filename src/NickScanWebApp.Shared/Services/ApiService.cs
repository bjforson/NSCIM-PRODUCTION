using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.Logging;

namespace NickScanWebApp.Shared.Services
{
    /// <summary>
    /// Generic API service wrapper for HTTP communication with error handling
    /// ✅ Unified service for both desktop and mobile applications
    /// </summary>
    public class ApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ApiService> _logger;
        private readonly AuthenticationStateProvider _authStateProvider;
        // 6.07 (Sprint 4): direct typed interface replacement for the previous
        // reflection-based GetTokenAsync probe. Falls back to the old reflection
        // path when not registered (e.g. an out-of-tree consumer DI'ing only the
        // base AuthenticationStateProvider) so existing behaviour is preserved.
        private readonly IAuthTokenSource? _tokenSource;
        private const string API_CLIENT_NAME = "NickScanAPI";

        // Shared JSON options for enum string deserialization
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        public ApiService(
            IHttpClientFactory httpClientFactory,
            ILogger<ApiService> logger,
            AuthenticationStateProvider authStateProvider,
            IAuthTokenSource? tokenSource = null)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _authStateProvider = authStateProvider;
            // 6.07: prefer the typed token source. When the AuthenticationStateProvider
            // also implements IAuthTokenSource (the common case for SimpleAuthStateProvider),
            // resolve to that — keeps the single shared source of truth.
            _tokenSource = tokenSource ?? authStateProvider as IAuthTokenSource;
        }

        private static bool IsTransientGetFailure(Exception ex)
        {
            var message = ex.ToString();
            return message.Contains("No connection could be made", StringComparison.OrdinalIgnoreCase)
                || message.Contains("actively refused", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection refused", StringComparison.OrdinalIgnoreCase)
                || message.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection reset", StringComparison.OrdinalIgnoreCase)
                || message.Contains("connection aborted", StringComparison.OrdinalIgnoreCase);
        }

        private static TimeSpan GetTransientRetryDelay(int attempt)
        {
            return attempt switch
            {
                1 => TimeSpan.FromMilliseconds(350),
                2 => TimeSpan.FromSeconds(1),
                _ => TimeSpan.FromSeconds(2)
            };
        }

        private async Task EnsureSuccessOrThrowAsync(HttpResponseMessage response, string method, string endpoint)
        {
            if (response.IsSuccessStatusCode)
            {
                return;
            }

            var status = (int)response.StatusCode;
            var reason = response.ReasonPhrase ?? string.Empty;
            var raw = await response.Content.ReadAsStringAsync();

            if (status == 400 || status == 409)
            {
                _logger.LogWarning("HTTP {Status} {Method} {Endpoint}. Body: {Body}", status, method, endpoint, raw);
            }
            else if (status == 401 || status == 403)
            {
                _logger.LogWarning("HTTP {Status} {Method} {Endpoint} - authentication/authorization required. Body: {Body}", status, method, endpoint, raw);
            }
            else
            {
                _logger.LogError("HTTP {Status} {Method} {Endpoint}. Body: {Body}", status, method, endpoint, raw);
            }

            var message = string.IsNullOrWhiteSpace(raw)
                ? $"{method} {endpoint} failed: {status} {reason}".TrimEnd()
                : $"{method} {endpoint} failed: {status} {reason}. Body: {raw}".TrimEnd();

            throw new ApiException(message, status, raw, method, endpoint);
        }

        /// <summary>
        /// Get JWT token from the typed IAuthTokenSource interface (6.07).
        /// Falls back to reflection-based lookup on AuthenticationStateProvider
        /// for legacy DI configurations that haven't registered IAuthTokenSource.
        /// </summary>
        private async Task<string?> GetTokenFromProviderAsync()
        {
            // 6.07: typed interface path
            if (_tokenSource != null)
            {
                try
                {
                    var token = await _tokenSource.GetTokenAsync();
                    if (!string.IsNullOrEmpty(token))
                    {
                        _logger.LogDebug("✅ Token retrieved from IAuthTokenSource: {ProviderType}", _tokenSource.GetType().Name);
                        return token;
                    }
                    else
                    {
                        _logger.LogDebug("⚠️ IAuthTokenSource returned null/empty token: {ProviderType} (user not authenticated)", _tokenSource.GetType().Name);
                        return null;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "❌ Error retrieving token from IAuthTokenSource: {ProviderType}", _tokenSource.GetType().Name);
                    return null;
                }
            }

            // Legacy fallback: reflection on AuthenticationStateProvider for DI configs
            // that haven't migrated to IAuthTokenSource. New code paths should not rely
            // on this branch — register IAuthTokenSource explicitly.
            try
            {
                var method = _authStateProvider.GetType().GetMethod("GetTokenAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    var task = method.Invoke(_authStateProvider, null) as Task<string?>;
                    if (task != null)
                    {
                        var token = await task;
                        if (!string.IsNullOrEmpty(token))
                        {
                            _logger.LogDebug("✅ Token retrieved via legacy reflection fallback: {ProviderType}", _authStateProvider.GetType().Name);
                            return token;
                        }
                        else
                        {
                            _logger.LogDebug("⚠️ Reflection fallback returned null/empty token: {ProviderType} (user not authenticated)", _authStateProvider.GetType().Name);
                        }
                    }
                }
                else
                {
                    // 6.07: bumped to Warning so operators see DI misconfig rather than the silent unauthenticated request
                    _logger.LogWarning("⚠️ Neither IAuthTokenSource nor a GetTokenAsync method is available on auth provider: {ProviderType} — request will be unauthenticated", _authStateProvider.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "❌ Error retrieving token from auth provider via reflection: {ProviderType}", _authStateProvider.GetType().Name);
            }
            return null;
        }

        /// <summary>
        /// Get authenticated HTTP client with JWT token
        /// </summary>
        private async Task<HttpClient> GetAuthenticatedClientAsync()
        {
            var client = _httpClientFactory.CreateClient(API_CLIENT_NAME);

            // Get JWT token from auth state provider
            var token = await GetTokenFromProviderAsync();
            if (!string.IsNullOrEmpty(token))
            {
                // Remove any existing Authorization header to avoid duplicates
                client.DefaultRequestHeaders.Remove("Authorization");
                client.DefaultRequestHeaders.Authorization =
                    new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                _logger.LogDebug("✅ Added Bearer token to request");
            }
            else
            {
                // Only log as debug - unauthenticated requests may be expected for public endpoints
                _logger.LogDebug("⚠️ No authentication token available - request will be unauthenticated. AuthProvider: {ProviderType}",
                    _authStateProvider.GetType().Name);
            }

            return client;
        }

        /// <summary>
        /// GET request with authentication
        /// </summary>
        public async Task<T?> GetAsync<T>(string endpoint)
        {
            const int maxTransientAttempts = 3;

            for (var attempt = 1; attempt <= maxTransientAttempts; attempt++)
            {
                try
                {
                    var client = await GetAuthenticatedClientAsync();
                    var response = await client.GetAsync(endpoint);

                    await EnsureSuccessOrThrowAsync(response, "GET", endpoint);

                    return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
                }
                catch (HttpRequestException ex) when (IsTransientGetFailure(ex) && attempt < maxTransientAttempts)
                {
                    var delay = GetTransientRetryDelay(attempt);
                    _logger.LogWarning(ex, "Transient HTTP failure calling GET {Endpoint} (attempt {Attempt}/{MaxAttempts}); retrying in {DelayMs}ms", endpoint, attempt, maxTransientAttempts, (int)delay.TotalMilliseconds);
                    await Task.Delay(delay);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase))
                {
                    // Request timed out - log as warning instead of error
                    _logger.LogWarning("Request timeout calling GET {Endpoint} (timeout after 60 seconds). The API may be slow or unavailable.", endpoint);
                    throw new ApiException($"Request to {endpoint} timed out. The API may be slow or unavailable.", ex);
                }
                catch (ApiException)
                {
                    throw;
                }
                catch (HttpRequestException ex)
                {
                    // 6.07: bumped 401/403 path from Debug to Warning — operators must see session-expiry signals.
                    if (ex.Message.Contains("401") || ex.Message.Contains("403") || ex.Message.Contains("Unauthorized") || ex.Message.Contains("Forbidden"))
                    {
                        _logger.LogWarning(ex, "HTTP 401/403 calling GET {Endpoint} - authentication/authorization required", endpoint);
                    }
                    else
                    {
                        _logger.LogError(ex, "HTTP error calling GET {Endpoint}", endpoint);
                    }
                    throw new ApiException($"Failed to GET {endpoint}: {ex.Message}", ex);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error calling GET {Endpoint}", endpoint);
                    throw new ApiException($"Unexpected error calling {endpoint}", ex);
                }
            }

            throw new ApiException($"Failed to GET {endpoint}: API unavailable after retry attempts");
        }

        /// <summary>
        /// Best-effort GET used for optional cache lookups. Returns null for misses
        /// and unavailable optional endpoints so callers can fall back quietly.
        /// </summary>
        public async Task<T?> TryGetAsync<T>(string endpoint)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.GetAsync(endpoint);
                var status = (int)response.StatusCode;

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                    response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    _logger.LogDebug("Optional GET {Endpoint} returned {Status}", endpoint, status);
                    return default;
                }

                if (!response.IsSuccessStatusCode)
                {
                    if (status == 401 || status == 403)
                    {
                        _logger.LogWarning("Optional GET {Endpoint} returned HTTP {Status} - authentication/authorization required", endpoint, status);
                    }
                    else
                    {
                        _logger.LogDebug("Optional GET {Endpoint} returned HTTP {Status}", endpoint, status);
                    }

                    return default;
                }

                return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogDebug("Optional GET {Endpoint} timed out", endpoint);
                return default;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Optional GET {Endpoint} failed", endpoint);
                return default;
            }
        }

        /// <summary>
        /// GET raw bytes with authentication (for images, files, etc.).
        ///
        /// BUG FIX: previously this method created a raw HttpClient and relied on
        /// the AuthenticatedHttpMessageHandler in the named-client pipeline to
        /// attach the JWT. That handler reads the token via JavaScript interop on
        /// ProtectedSessionStorage, which can silently fail when called from
        /// inside the HttpClient pipeline (outside the Blazor render context),
        /// leaving the request unauthenticated → 401 → null bytes returned.
        ///
        /// Now uses the same GetAuthenticatedClientAsync() helper as GetAsync&lt;T&gt;,
        /// which fetches the token in the calling Blazor component context (where
        /// JS interop works) and attaches it to DefaultRequestHeaders before the
        /// request is dispatched.
        /// </summary>
        public async Task<byte[]?> GetBytesAsync(string endpoint)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.GetAsync(endpoint);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }
                _logger.LogWarning("GetBytesAsync {Endpoint} returned {Status}", endpoint, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "GetBytesAsync failed for {Endpoint}", endpoint);
                return null;
            }
        }

        public async Task<byte[]?> PostForBytesAsync<TRequest>(string endpoint, TRequest data)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.PostAsJsonAsync(endpoint, data);
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }

                _logger.LogWarning("PostForBytesAsync {Endpoint} returned {Status}", endpoint, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PostForBytesAsync failed for {Endpoint}", endpoint);
                return null;
            }
        }

        public async Task<byte[]?> PostEmptyForBytesAsync(string endpoint)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.PostAsync(endpoint, new StringContent(string.Empty));
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsByteArrayAsync();
                }

                _logger.LogWarning("PostEmptyForBytesAsync {Endpoint} returned {Status}", endpoint, response.StatusCode);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "PostEmptyForBytesAsync failed for {Endpoint}", endpoint);
                return null;
            }
        }

        /// <summary>
        /// POST request with authentication
        /// </summary>
        public async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.PostAsJsonAsync(endpoint, data);

                await EnsureSuccessOrThrowAsync(response, "POST", endpoint);

                try
                {
                    return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
                }
                catch
                {
                    // If no JSON body expected
                    return default;
                }
            }
            catch (ApiException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                // 6.07: bumped 401/403 from Debug to Warning so operators see auth signals
                if (ex.Message.Contains("401") || ex.Message.Contains("403") || ex.Message.Contains("Unauthorized") || ex.Message.Contains("Forbidden"))
                {
                    _logger.LogWarning(ex, "HTTP 401/403 calling POST {Endpoint} - authentication/authorization required", endpoint);
                }
                else
                {
                    _logger.LogError(ex, "HTTP error calling POST {Endpoint}", endpoint);
                }
                throw new ApiException($"Failed to POST {endpoint}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling POST {Endpoint}", endpoint);
                throw new ApiException($"Unexpected error calling {endpoint}", ex);
            }
        }

        /// <summary>
        /// Best-effort POST used for optional forward-contract calls. Returns
        /// false when the route is absent or rejects the request so callers can
        /// fall back to compatibility endpoints without surfacing transient UI errors.
        /// </summary>
        public async Task<bool> TryPostAsync<TRequest>(string endpoint, TRequest data)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.PostAsJsonAsync(endpoint, data);
                var status = (int)response.StatusCode;

                if (response.IsSuccessStatusCode)
                {
                    return true;
                }

                if (response.StatusCode == System.Net.HttpStatusCode.NotFound ||
                    response.StatusCode == System.Net.HttpStatusCode.MethodNotAllowed ||
                    response.StatusCode == System.Net.HttpStatusCode.NoContent)
                {
                    _logger.LogDebug("Optional POST {Endpoint} returned {Status}", endpoint, status);
                    return false;
                }

                _logger.LogDebug("Optional POST {Endpoint} returned HTTP {Status}", endpoint, status);
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Optional POST {Endpoint} failed", endpoint);
                return false;
            }
        }

        /// <summary>
        /// PUT request with authentication
        /// </summary>
        public async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.PutAsJsonAsync(endpoint, data);
                await EnsureSuccessOrThrowAsync(response, "PUT", endpoint);
                return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
            }
            catch (ApiException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error calling PUT {Endpoint}", endpoint);
                throw new ApiException($"Failed to PUT {endpoint}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling PUT {Endpoint}", endpoint);
                throw new ApiException($"Unexpected error calling {endpoint}", ex);
            }
        }

        /// <summary>
        /// PATCH request with authentication
        /// </summary>
        public async Task<TResponse?> PatchAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.PatchAsJsonAsync(endpoint, data);
                await EnsureSuccessOrThrowAsync(response, "PATCH", endpoint);
                return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
            }
            catch (ApiException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error calling PATCH {Endpoint}", endpoint);
                throw new ApiException($"Failed to PATCH {endpoint}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling PATCH {Endpoint}", endpoint);
                throw new ApiException($"Unexpected error calling {endpoint}", ex);
            }
        }

        /// <summary>
        /// DELETE request with authentication
        /// </summary>
        public async Task DeleteAsync(string endpoint)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.DeleteAsync(endpoint);
                await EnsureSuccessOrThrowAsync(response, "DELETE", endpoint);
            }
            catch (ApiException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error calling DELETE {Endpoint}", endpoint);
                throw new ApiException($"Failed to DELETE {endpoint}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling DELETE {Endpoint}", endpoint);
                throw new ApiException($"Unexpected error calling {endpoint}", ex);
            }
        }

        public async Task<T?> DeleteAsync<T>(string endpoint)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.DeleteAsync(endpoint);
                await EnsureSuccessOrThrowAsync(response, "DELETE", endpoint);

                var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
                return result;
            }
            catch (ApiException)
            {
                throw;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error calling DELETE {Endpoint}", endpoint);
                throw new ApiException($"Failed to DELETE {endpoint}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling DELETE {Endpoint}", endpoint);
                throw new ApiException($"Unexpected error calling {endpoint}", ex);
            }
        }
    }

    /// <summary>
    /// Custom exception for API errors
    /// </summary>
    public class ApiException : Exception
    {
        public int? StatusCode { get; }
        public string? ResponseBody { get; }
        public string? Method { get; }
        public string? Endpoint { get; }

        public ApiException(string message) : base(message) { }
        public ApiException(string message, Exception innerException) : base(message, innerException) { }
        public ApiException(string message, int? statusCode, string? responseBody, string? method, string? endpoint) : base(message)
        {
            StatusCode = statusCode;
            ResponseBody = responseBody;
            Method = method;
            Endpoint = endpoint;
        }
    }
}
