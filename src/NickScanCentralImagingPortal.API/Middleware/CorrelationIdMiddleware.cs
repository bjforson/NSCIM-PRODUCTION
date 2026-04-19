namespace NickScanCentralImagingPortal.API.Middleware
{
    /// <summary>
    /// Middleware to add correlation ID to each request for distributed tracing
    /// </summary>
    public class CorrelationIdMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CorrelationIdMiddleware> _logger;
        private const string CorrelationIdHeader = "X-Correlation-ID";

        public CorrelationIdMiddleware(RequestDelegate next, ILogger<CorrelationIdMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check if correlation ID already exists (from client or load balancer)
            if (!context.Request.Headers.TryGetValue(CorrelationIdHeader, out var correlationId)
                || string.IsNullOrWhiteSpace(correlationId))
            {
                // Generate new correlation ID
                correlationId = Guid.NewGuid().ToString();
            }

            // Store correlation ID in HttpContext for access throughout the request
            context.Items["CorrelationId"] = correlationId.ToString();

            // Add correlation ID to response headers
            context.Response.Headers.TryAdd(CorrelationIdHeader, correlationId.ToString());

            // Add correlation ID to logging context
            var correlationIdString = correlationId.ToString() ?? string.Empty;
            using (_logger.BeginScope(new Dictionary<string, object>
            {
                ["CorrelationId"] = correlationIdString
            }))
            {
                _logger.LogDebug("Request started with CorrelationId: {CorrelationId}", correlationIdString);

                try
                {
                    await _next(context);
                }
                finally
                {
                    _logger.LogDebug("Request completed with CorrelationId: {CorrelationId}, StatusCode: {StatusCode}",
                        correlationId, context.Response.StatusCode);
                }
            }
        }
    }

    /// <summary>
    /// Extension method to register correlation ID middleware
    /// </summary>
    public static class CorrelationIdMiddlewareExtensions
    {
        public static IApplicationBuilder UseCorrelationId(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<CorrelationIdMiddleware>();
        }

        /// <summary>
        /// Get correlation ID from HttpContext
        /// </summary>
        public static string? GetCorrelationId(this HttpContext context)
        {
            return context.Items["CorrelationId"]?.ToString();
        }
    }
}

