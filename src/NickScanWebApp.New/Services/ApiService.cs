using NickScanWebApp.Shared.Services;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Wrapper for backward compatibility - delegates to shared ApiService
    /// ✅ MIGRATION: All functionality now in NickScanWebApp.Shared.Services.ApiService
    /// </summary>
    public class ApiService : NickScanWebApp.Shared.Services.ApiService
    {
        public ApiService(
            System.Net.Http.IHttpClientFactory httpClientFactory,
            Microsoft.Extensions.Logging.ILogger<NickScanWebApp.Shared.Services.ApiService> logger,
            Microsoft.AspNetCore.Components.Authorization.AuthenticationStateProvider authStateProvider)
            : base(httpClientFactory, logger, authStateProvider)
        {
        }
    }
}

