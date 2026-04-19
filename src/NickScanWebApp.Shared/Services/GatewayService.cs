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
                var apiBaseUrl = _configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
                var queryParams = new List<string>();

                if (!includeImage) queryParams.Add("includeImage=false");
                if (!includeScanner) queryParams.Add("includeScanner=false");
                if (!includeICUMS) queryParams.Add("includeICUMS=false");
                if (!includeValidation) queryParams.Add("includeValidation=false");
                if (includeVehicles) queryParams.Add("includeVehicles=true");
                if (includeHistory) queryParams.Add("includeHistory=true");

                var queryString = queryParams.Any() ? "?" + string.Join("&", queryParams) : "";
                var url = $"{apiBaseUrl}/api/Gateway/container/{containerNumber}{queryString}";

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
        /// Get image URL for use in img tags
        /// </summary>
        public string GetContainerImageUrl(string containerNumber)
        {
            var apiBaseUrl = _configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
            return $"{apiBaseUrl}/api/Gateway/container/{containerNumber}/image";
        }

        /// <summary>
        /// Check gateway health
        /// </summary>
        public async Task<bool> IsHealthyAsync()
        {
            try
            {
                var apiBaseUrl = _configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
                var url = $"{apiBaseUrl}/api/Gateway/health";

                var http = GetHttpClient();
                var response = await http.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }
}

