using System.Diagnostics;
using NickScanCentralImagingPortal.API.Services;

namespace NickScanCentralImagingPortal.API.Middleware
{
    /// <summary>
    /// Middleware to collect performance metrics for all requests
    /// </summary>
    public class PerformanceMetricsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly IPerformanceMetricsService _metricsService;

        public PerformanceMetricsMiddleware(
            RequestDelegate next,
            IPerformanceMetricsService metricsService)
        {
            _next = next;
            _metricsService = metricsService;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var stopwatch = Stopwatch.StartNew();

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();

                // Record metrics
                var endpoint = context.Request.Path.ToString();
                var method = context.Request.Method;
                var durationMs = stopwatch.ElapsedMilliseconds;
                var statusCode = context.Response.StatusCode;

                _metricsService.RecordRequest(endpoint, method, durationMs, statusCode);
            }
        }
    }

    /// <summary>
    /// Extension method to register performance metrics middleware
    /// </summary>
    public static class PerformanceMetricsMiddlewareExtensions
    {
        public static IApplicationBuilder UsePerformanceMetrics(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PerformanceMetricsMiddleware>();
        }
    }
}

