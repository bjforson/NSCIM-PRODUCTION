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
            AuthenticationStateProvider authStateProvider)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _authStateProvider = authStateProvider;
        }

        /// <summary>
        /// Get JWT token from auth state provider using reflection
        /// Works with SimpleAuthStateProvider from both desktop and mobile apps
        /// </summary>
        private async Task<string?> GetTokenFromProviderAsync()
        {
            try
            {
                // Use reflection to call GetTokenAsync() method if it exists
                var method = _authStateProvider.GetType().GetMethod("GetTokenAsync", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (method != null)
                {
                    var task = method.Invoke(_authStateProvider, null) as Task<string?>;
                    if (task != null)
                    {
                        var token = await task;
                        if (!string.IsNullOrEmpty(token))
                        {
                            _logger.LogDebug("✅ Token retrieved successfully from auth provider: {ProviderType}", _authStateProvider.GetType().Name);
                            return token;
                        }
                        else
                        {
                            // ✅ FIX: Downgrade to Debug level - this is expected when user is not logged in
                            _logger.LogDebug("⚠️ Auth provider returned null/empty token: {ProviderType} (user not authenticated)", _authStateProvider.GetType().Name);
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("⚠️ GetTokenAsync method not found on auth provider: {ProviderType}", _authStateProvider.GetType().Name);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "❌ Error retrieving token from auth provider: {ProviderType}", _authStateProvider.GetType().Name);
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
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.GetAsync(endpoint);

                // Handle 401/403 errors more gracefully - don't log as error if it's expected
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    if (status == 401 || status == 403)
                    {
                        // 401/403 might be expected for unauthenticated requests or insufficient permissions
                        _logger.LogDebug("HTTP {Status} GET {Endpoint} - authentication/authorization required", status, endpoint);
                    }
                    else
                    {
                        var content = await response.Content.ReadAsStringAsync();
                        _logger.LogError("HTTP {Status} GET {Endpoint}. Response: {Content}", status, endpoint, content);
                    }
                    response.EnsureSuccessStatusCode();
                }

                return await response.Content.ReadFromJsonAsync<T>(JsonOptions);
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || ex.Message.Contains("canceled", StringComparison.OrdinalIgnoreCase))
            {
                // Request timed out - log as warning instead of error
                _logger.LogWarning("Request timeout calling GET {Endpoint} (timeout after 60 seconds). The API may be slow or unavailable.", endpoint);
                throw new ApiException($"Request to {endpoint} timed out. The API may be slow or unavailable.", ex);
            }
            catch (HttpRequestException ex)
            {
                // Check if it's a 401/403 - log at debug level instead of error
                if (ex.Message.Contains("401") || ex.Message.Contains("403") || ex.Message.Contains("Unauthorized") || ex.Message.Contains("Forbidden"))
                {
                    _logger.LogDebug(ex, "HTTP 401/403 calling GET {Endpoint} - authentication/authorization required", endpoint);
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

        /// <summary>
        /// POST request with authentication
        /// </summary>
        public async Task<TResponse?> PostAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.PostAsJsonAsync(endpoint, data);

                var raw = await response.Content.ReadAsStringAsync();
                if (!response.IsSuccessStatusCode)
                {
                    var status = (int)response.StatusCode;
                    var reason = response.ReasonPhrase ?? "";

                    // Log validation errors (400, 409) as warnings, not errors
                    if (status == 400 || status == 409)
                    {
                        _logger.LogWarning("HTTP {Status} POST {Endpoint}. Body: {Body}", status, endpoint, raw);
                    }
                    // Log 401/403 as debug - authentication/authorization required
                    else if (status == 401 || status == 403)
                    {
                        _logger.LogDebug("HTTP {Status} POST {Endpoint} - authentication/authorization required. Body: {Body}", status, endpoint, raw);
                    }
                    else
                    {
                        _logger.LogError("HTTP {Status} POST {Endpoint}. Body: {Body}", status, endpoint, raw);
                    }

                    throw new ApiException($"POST {endpoint} failed: {status} {reason}. Body: {raw}");
                }

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
            catch (HttpRequestException ex)
            {
                // Check if it's a 401/403 - log at debug level instead of error
                if (ex.Message.Contains("401") || ex.Message.Contains("403") || ex.Message.Contains("Unauthorized") || ex.Message.Contains("Forbidden"))
                {
                    _logger.LogDebug(ex, "HTTP 401/403 calling POST {Endpoint} - authentication/authorization required", endpoint);
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
        /// PUT request with authentication
        /// </summary>
        public async Task<TResponse?> PutAsync<TRequest, TResponse>(string endpoint, TRequest data)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.PutAsJsonAsync(endpoint, data);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TResponse>(JsonOptions);
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
        /// DELETE request with authentication
        /// </summary>
        public async Task DeleteAsync(string endpoint)
        {
            try
            {
                var client = await GetAuthenticatedClientAsync();
                var response = await client.DeleteAsync(endpoint);
                response.EnsureSuccessStatusCode();
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
                response.EnsureSuccessStatusCode();

                var result = await response.Content.ReadFromJsonAsync<T>(JsonOptions);
                return result;
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
        public ApiException(string message) : base(message) { }
        public ApiException(string message, Exception innerException) : base(message, innerException) { }
    }
}
