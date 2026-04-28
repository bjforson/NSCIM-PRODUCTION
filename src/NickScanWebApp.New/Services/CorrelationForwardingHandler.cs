using Microsoft.AspNetCore.Http;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// DelegatingHandler that forwards the inbound X-Correlation-ID header onto every
    /// outbound HTTP request made by the WebApp's typed HttpClients (primarily NickScanAPI).
    /// Closes the gap surfaced by the 2026-04-27 audit where CorrelationIdMiddleware stamped
    /// inbound requests but no handler propagated the value across service boundaries.
    ///
    /// Mirrors NickScanCentralImagingPortal.Services.Http.CorrelationForwardingHandler — kept
    /// as a separate copy because WebApp.New does not reference the Services project (and
    /// adding that reference would bloat the WebApp deploy with EF Core / Npgsql).
    ///
    /// Register as Transient and chain via `.AddHttpMessageHandler&lt;CorrelationForwardingHandler&gt;()`.
    /// Requires `services.AddHttpContextAccessor()` in the host.
    /// </summary>
    public sealed class CorrelationForwardingHandler : DelegatingHandler
    {
        private const string CorrelationIdHeader = "X-Correlation-ID";
        private readonly IHttpContextAccessor _httpContextAccessor;

        public CorrelationForwardingHandler(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (!request.Headers.Contains(CorrelationIdHeader))
            {
                var correlationId = _httpContextAccessor.HttpContext?.Items["CorrelationId"]?.ToString();
                if (!string.IsNullOrEmpty(correlationId))
                {
                    request.Headers.Add(CorrelationIdHeader, correlationId);
                }
            }
            return base.SendAsync(request, cancellationToken);
        }
    }
}
