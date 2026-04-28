using Microsoft.AspNetCore.Http;

namespace NickScanCentralImagingPortal.Services.Http
{
    /// <summary>
    /// DelegatingHandler that forwards the inbound X-Correlation-ID header onto every outbound
    /// HTTP request made through a typed/named HttpClient. Closes the gap surfaced by the
    /// 2026-04-27 audit where CorrelationIdMiddleware stamped inbound requests but no handler
    /// propagated the value across service boundaries (NickComms.Gateway, NickHR, splitter).
    ///
    /// Register as Transient and chain with `.AddHttpMessageHandler&lt;CorrelationForwardingHandler&gt;()`
    /// on every typed client. Requires `services.AddHttpContextAccessor()` in the host.
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
            // Don't clobber an explicit header set by the caller — they may be forwarding
            // an upstream system's correlation ID through unchanged.
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
