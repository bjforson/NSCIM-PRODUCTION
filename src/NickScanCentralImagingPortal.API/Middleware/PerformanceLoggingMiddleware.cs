using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Services.Monitoring;

namespace NickScanCentralImagingPortal.API.Middleware
{
    /// <summary>
    /// Middleware to log request performance metrics and track endpoint usage
    /// </summary>
    public class PerformanceLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<PerformanceLoggingMiddleware> _logger;
        private readonly int _slowRequestThresholdMs;

        public PerformanceLoggingMiddleware(
            RequestDelegate next,
            ILogger<PerformanceLoggingMiddleware> logger,
            IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            _slowRequestThresholdMs = configuration.GetValue<int>("Performance:SlowRequestThresholdMs", 1000);
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var correlationId = context.GetCorrelationId();
            var correlationIdGuid = Guid.TryParse(correlationId, out var guid) ? guid : (Guid?)null;
            var method = context.Request.Method;
            var path = context.Request.Path.Value ?? "";
            var ipAddress = context.Connection.RemoteIpAddress?.ToString() ?? "Unknown";
            var userAgent = context.Request.Headers["User-Agent"].ToString();
            var stopwatch = Stopwatch.StartNew();

            // Determine if endpoint is deprecated or Phase 3 route
            var isDeprecated = EndpointRouteUsageCatalog.IsDeprecatedEndpoint(path);
            var isPhase3Route = EndpointRouteUsageCatalog.IsPhase3Route(path);

            try
            {
                await _next(context);
            }
            finally
            {
                stopwatch.Stop();
                var elapsedMs = stopwatch.ElapsedMilliseconds;
                var statusCode = context.Response.StatusCode;

                // Log performance metrics
                if (elapsedMs > _slowRequestThresholdMs)
                {
                    // Slow request warning
                    _logger.LogWarning(
                        "⚠️ SLOW REQUEST: {Method} {Path} completed in {ElapsedMs}ms | Status: {StatusCode} | CorrelationId: {CorrelationId}",
                        method, path, elapsedMs, statusCode, correlationId);
                }
                else
                {
                    // Normal request info
                    _logger.LogInformation(
                        "✅ {Method} {Path} completed in {ElapsedMs}ms | Status: {StatusCode} | CorrelationId: {CorrelationId}",
                        method, path, elapsedMs, statusCode, correlationId);
                }

                // Record endpoint usage - use buffer service (batched) when available, else fire-and-forget per-request
                var record = new EndpointUsageRecord
                {
                    Endpoint = EndpointUsagePathNormalizer.Normalize(path),
                    Method = method,
                    StatusCode = statusCode,
                    ResponseTimeMs = elapsedMs,
                    IpAddress = ipAddress,
                    UserAgent = userAgent,
                    Timestamp = DateTime.UtcNow,
                    IsDeprecated = isDeprecated,
                    IsPhase3Route = isPhase3Route,
                    CorrelationId = correlationIdGuid
                };

                var bufferService = context.RequestServices.GetService<IEndpointUsageBufferService>();
                if (bufferService != null)
                {
                    bufferService.Enqueue(record);
                }
                else
                {
                    var scopeFactory = context.RequestServices.GetService<IServiceScopeFactory>();
                    if (scopeFactory != null)
                    {
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var scope = scopeFactory.CreateScope();
                                var svc = scope.ServiceProvider.GetRequiredService<IEndpointUsageService>();
                                await svc.RecordEndpointUsageAsync(record);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogWarning(ex, "Failed to record endpoint usage for {Path}", path);
                            }
                        });
                    }
                }

                // Add performance headers to response (only if response hasn't started)
                if (!context.Response.HasStarted)
                {
                    context.Response.Headers.TryAdd("X-Response-Time-Ms", elapsedMs.ToString());
                    context.Response.Headers.TryAdd("X-Correlation-ID", correlationId ?? "N/A");
                }
            }
        }

    }

    /// <summary>
    /// Extension method to register performance logging middleware
    /// </summary>
    public static class PerformanceLoggingMiddlewareExtensions
    {
        public static IApplicationBuilder UsePerformanceLogging(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<PerformanceLoggingMiddleware>();
        }
    }
}

