using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Components.Authorization;

namespace NickScanWebApp.Mobile.Services
{
    /// <summary>
    /// Generic API service wrapper for HTTP communication with error handling
    /// ✅ FIXED: Now includes authentication support
    /// </summary>
    public class ApiService
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ApiService> _logger;
        private readonly AuthenticationStateProvider _authStateProvider;
        private const string API_CLIENT_NAME = "NickScanAPI";

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
        /// Get authenticated HTTP client with JWT token
        /// </summary>
        private async Task<HttpClient> GetAuthenticatedClientAsync()
        {
            var client = _httpClientFactory.CreateClient(API_CLIENT_NAME);
            
            // Get JWT token from auth state provider
            if (_authStateProvider is SimpleAuthStateProvider simpleAuth)
            {
                var token = await simpleAuth.GetTokenAsync();
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
                    _logger.LogWarning("⚠️ No authentication token available - request will be unauthenticated");
                }
            }
            else
            {
                _logger.LogWarning("⚠️ AuthStateProvider is not SimpleAuthStateProvider: {Type}", _authStateProvider?.GetType().Name);
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
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<T>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error calling GET {Endpoint}", endpoint);
                throw new ApiException($"Failed to GET {endpoint}: {ex.Message}", ex);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calling GET {Endpoint}", endpoint);
                throw new ApiException($"Unexpected error calling {endpoint}", ex);
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
                    _logger.LogError("HTTP {Status} POST {Endpoint}. Body: {Body}", status, endpoint, raw);
                    throw new ApiException($"POST {endpoint} failed: {status} {reason}. Body: {raw}");
                }

                try
                {
                    return await response.Content.ReadFromJsonAsync<TResponse>();
                }
                catch
                {
                    // If no JSON body expected
                    return default;
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error calling POST {Endpoint}", endpoint);
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
                return await response.Content.ReadFromJsonAsync<TResponse>();
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
                
                var result = await response.Content.ReadFromJsonAsync<T>();
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

