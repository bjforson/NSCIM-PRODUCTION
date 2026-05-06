using System.Net.Http;
using System.Net.Security;
using Microsoft.Extensions.Configuration;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// SignalR HubConnectionBuilder builds its own internal HttpClient (for the
    /// negotiate POST) and its own ClientWebSocket (for the WebSocket upgrade),
    /// bypassing the named HttpClient that ApiService uses with the pinned-
    /// thumbprint cert callback. Under the LocalSystem service account the
    /// mkcert leaf cert chain build fails (the mkcert CA in LocalMachine\Root
    /// isn't visible to LocalSystem the same way it is to Administrator), so
    /// the negotiate handshake throws "Could not establish trust relationship"
    /// and the page surfaces the "Could not connect to readiness service" toast.
    ///
    /// This helper applies the same accept-on-OS-chain-OR-pinned-thumbprint
    /// validator (mirroring Program.cs:160) to both transports SignalR uses.
    /// Net effect: strictly stronger than an unconditional "=> true" while
    /// still accepting the leaf the API actually serves.
    /// </summary>
    public static class HubTlsConfig
    {
        public static RemoteCertificateValidationCallback CreateValidator(
            IConfiguration configuration,
            bool isProduction)
        {
            var pinnedThumbprint = System.Environment.GetEnvironmentVariable("NICKSCAN_API_CERT_THUMBPRINT")
                                   ?? configuration["Security:ApiCertThumbprint"];
            var expectedThumbprint = pinnedThumbprint?.Replace(":", "").Replace(" ", "").Trim();

            return (sender, cert, chain, errors) =>
            {
                if (cert == null) return false;
                if (errors == SslPolicyErrors.None) return true;
                if (!string.IsNullOrEmpty(expectedThumbprint)
                    && cert is System.Security.Cryptography.X509Certificates.X509Certificate2 c2
                    && string.Equals(c2.Thumbprint, expectedThumbprint, System.StringComparison.OrdinalIgnoreCase))
                    return true;
                if (!isProduction) return true;
                return false;
            };
        }

        public static HttpMessageHandler CreateHttpHandler(
            IConfiguration configuration,
            bool isProduction)
        {
            var validator = CreateValidator(configuration, isProduction);
            return new SocketsHttpHandler
            {
                SslOptions = new System.Net.Security.SslClientAuthenticationOptions
                {
                    RemoteCertificateValidationCallback = validator
                }
            };
        }

        public static System.Action<System.Net.WebSockets.ClientWebSocketOptions> CreateWebSocketConfig(
            IConfiguration configuration,
            bool isProduction)
        {
            var validator = CreateValidator(configuration, isProduction);
            return ws =>
            {
                ws.RemoteCertificateValidationCallback = validator;
            };
        }
    }
}
