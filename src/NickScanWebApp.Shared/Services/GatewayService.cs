using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanWebApp.Shared.Models;

namespace NickScanWebApp.Shared.Services
{
    /// <summary>
    /// Frontend service for calling the unified Gateway API
    /// Provides simplified access to aggregated backend data
    /// ✅ Unified service for both desktop and mobile applications
    /// </summary>
    public class GatewayService
    {
        public const string BasePath = "/api/Gateway";
        public const string SearchPath = BasePath + "/search";
        public const string HealthPath = BasePath + "/health";

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<GatewayService> _logger;
        private const string API_CLIENT_NAME = "NickScanAPI";

        public GatewayService(
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<GatewayService> logger)
        {
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
        }

        private HttpClient GetHttpClient()
        {
            return _httpClientFactory.CreateClient(API_CLIENT_NAME);
        }

        /// <summary>
        /// Get complete container data including image, scanner data, ICUMS, and validation
        /// </summary>
        public async Task<ContainerCompleteData?> GetContainerCompleteAsync(
            string containerNumber,
            bool includeImage = true,
            bool includeScanner = true,
            bool includeICUMS = true,
            bool includeValidation = true,
            bool includeVehicles = false,
            bool includeHistory = false)
        {
            try
            {
                var url = BuildApiUrl(BuildContainerCompletePath(
                    containerNumber,
                    includeImage,
                    includeScanner,
                    includeICUMS,
                    includeValidation,
                    includeVehicles,
                    includeHistory));

                _logger.LogInformation("Gateway: Requesting complete data for {Container}", containerNumber);

                var http = GetHttpClient();
                var response = await http.GetFromJsonAsync<ContainerCompleteData>(url);

                if (response != null)
                {
                    _logger.LogInformation(
                        "Gateway: Received data for {Container} - HasImage={HasImage}, HasScanner={HasScanner}, HasICUMS={HasICUMS}, Time={Ms}ms",
                        containerNumber,
                        response.Available?.HasImage,
                        response.Available?.HasScannerData,
                        response.Available?.HasICUMSData,
                        response.ResponseTimeMs);
                }

                return response;
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Gateway: HTTP error getting data for {Container}", containerNumber);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gateway: Error getting data for {Container}", containerNumber);
                return null;
            }
        }

        /// <summary>
        /// Get image URL for use in img tags.
        /// </summary>
        /// <remarks>
        /// DEPRECATED since 2026-04-24 Week-1 security rollout. This returns an
        /// UNSIGNED URL that will 401 from a browser &lt;img src&gt; or cross-origin
        /// fetch because the Gateway image endpoint now requires auth. No caller
        /// currently exists. If you need an image URL for an &lt;img src&gt; tag, use
        /// NickScanWebApp.New.Services.SignedImageUrlBuilder.Build(...) instead.
        /// </remarks>
        [Obsolete("Use SignedImageUrlBuilder.Build with the Gateway container image route and userId; unsigned URLs 401 after the Week-1 security rollout.", false)]
        public string GetContainerImageUrl(string containerNumber)
        {
            return BuildApiUrl(BuildContainerImagePath(containerNumber));
        }

        public async Task<TResponse?> SearchAsync<TRequest, TResponse>(TRequest request)
        {
            try
            {
                var http = GetHttpClient();
                using var response = await http.PostAsJsonAsync(SearchPath, request);
                response.EnsureSuccessStatusCode();
                return await response.Content.ReadFromJsonAsync<TResponse>();
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Gateway: HTTP error searching via unified gateway");
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Gateway: Error searching via unified gateway");
                throw;
            }
        }

        /// <summary>
        /// Check gateway health
        /// </summary>
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var http = GetHttpClient();
                var response = await http.GetAsync(HealthPath);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public static string BuildContainerCompletePath(
            string containerNumber,
            bool includeImage = true,
            bool includeScanner = true,
            bool includeICUMS = true,
            bool includeValidation = true,
            bool includeVehicles = false,
            bool includeHistory = false)
        {
            var queryParams = new List<string>();

            if (!includeImage) queryParams.Add("includeImage=false");
            if (!includeScanner) queryParams.Add("includeScanner=false");
            if (!includeICUMS) queryParams.Add("includeICUMS=false");
            if (!includeValidation) queryParams.Add("includeValidation=false");
            if (includeVehicles) queryParams.Add("includeVehicles=true");
            if (includeHistory) queryParams.Add("includeHistory=true");

            var queryString = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
            return $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}{queryString}";
        }

        public static string BuildContainerImagePath(string containerNumber)
        {
            return $"{BasePath}/container/{Uri.EscapeDataString(containerNumber)}/image";
        }

        private string BuildApiUrl(string path)
        {
            var apiBaseUrl = _configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
            return $"{apiBaseUrl.TrimEnd('/')}{path}";
        }
    }
}

