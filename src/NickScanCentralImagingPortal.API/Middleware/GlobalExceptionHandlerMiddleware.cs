using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.API.Models;

namespace NickScanCentralImagingPortal.API.Middleware
{
    /// <summary>
    /// Global exception handler middleware for consistent error responses
    /// </summary>
    public class GlobalExceptionHandlerMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;
        private readonly IWebHostEnvironment _environment;

        public GlobalExceptionHandlerMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionHandlerMiddleware> logger,
            IWebHostEnvironment environment)
        {
            _next = next;
            _logger = logger;
            _environment = environment;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private async Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            var correlationId = context.GetCorrelationId() ?? Guid.NewGuid().ToString();
            var path = context.Request.Path.ToString();

            // Log the exception with correlation ID
            _logger.LogError(exception,
                "❌ Unhandled exception occurred. CorrelationId: {CorrelationId}, Path: {Path}, Message: {Message}",
                correlationId, path, exception.Message);

            // Determine error response based on exception type
            var errorResponse = CreateErrorResponse(exception, correlationId, path);

            // Set response status code and content type
            context.Response.StatusCode = errorResponse.StatusCode;
            context.Response.ContentType = "application/json";

            // Serialize and write response
            var json = JsonSerializer.Serialize(errorResponse, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = _environment.IsDevelopment()
            });

            await context.Response.WriteAsync(json);
        }

        private ApiErrorResponse CreateErrorResponse(Exception exception, string correlationId, string path)
        {
            var response = new ApiErrorResponse
            {
                CorrelationId = correlationId,
                Timestamp = DateTime.UtcNow,
                Path = path
            };

            // Map exception type to appropriate error response
            switch (exception)
            {
                case UnauthorizedAccessException:
                    response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    response.Error = ErrorCodes.UNAUTHORIZED;
                    response.Message = "You are not authorized to access this resource.";
                    break;

                case InvalidOperationException invalidOp when invalidOp.Message.Contains("password"):
                    response.StatusCode = (int)HttpStatusCode.Unauthorized;
                    response.Error = ErrorCodes.INVALID_CREDENTIALS;
                    response.Message = "Invalid username or password.";
                    break;

                case KeyNotFoundException:
                case FileNotFoundException:
                    response.StatusCode = (int)HttpStatusCode.NotFound;
                    response.Error = ErrorCodes.NOT_FOUND;
                    response.Message = "The requested resource was not found.";
                    break;

                case ArgumentException argEx:
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.Error = ErrorCodes.BAD_REQUEST;
                    response.Message = argEx.Message;
                    break;

                case InvalidDataException:
                    response.StatusCode = (int)HttpStatusCode.BadRequest;
                    response.Error = ErrorCodes.VALIDATION_ERROR;
                    response.Message = "Invalid data provided.";
                    break;

                case DbUpdateException dbEx:
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    response.Error = ErrorCodes.DATABASE_ERROR;
                    response.Message = "A database error occurred. Please try again.";

                    // Check for specific SQL errors
                    if (dbEx.InnerException?.Message.Contains("duplicate", StringComparison.OrdinalIgnoreCase) == true)
                    {
                        response.StatusCode = (int)HttpStatusCode.Conflict;
                        response.Error = ErrorCodes.CONFLICT;
                        response.Message = "A record with this information already exists.";
                    }
                    break;

                case TimeoutException:
                    response.StatusCode = (int)HttpStatusCode.RequestTimeout;
                    response.Error = ErrorCodes.DATABASE_TIMEOUT;
                    response.Message = "The request timed out. Please try again.";
                    break;

                case HttpRequestException httpEx:
                    response.StatusCode = (int)HttpStatusCode.BadGateway;
                    response.Error = ErrorCodes.EXTERNAL_API_ERROR;
                    response.Message = "An external service error occurred.";

                    // Check if it's ICUMS API
                    if (httpEx.Message.Contains("ICUMS", StringComparison.OrdinalIgnoreCase))
                    {
                        response.Error = ErrorCodes.ICUMS_API_ERROR;
                        response.Message = "ICUMS API is temporarily unavailable. Please try again later.";
                    }
                    break;

                case OperationCanceledException:
                    response.StatusCode = (int)HttpStatusCode.RequestTimeout;
                    response.Error = ErrorCodes.DATABASE_TIMEOUT;
                    response.Message = "The operation was canceled or timed out.";
                    break;

                default:
                    response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    response.Error = ErrorCodes.INTERNAL_SERVER_ERROR;
                    response.Message = "An unexpected error occurred. Please try again later.";
                    break;
            }

            // H1: Even in development, do NOT serialize the full stack trace into the response body.
            // Devs have access to the server logs; including the trace in the HTTP response is a leak
            // surface (e.g., dev mode accidentally enabled in a non-dev deployment, dev tunnel exposed
            // to the internet, response captured in logs/screenshots/issue trackers). Keep type and
            // message only — same diagnostic value 95% of the time.
            if (_environment.IsDevelopment())
            {
                response.Details = new
                {
                    ExceptionType = exception.GetType().Name,
                    ExceptionMessage = exception.Message,
                    InnerException = exception.InnerException?.Message
                };
                // Stack trace intentionally omitted — server logs have full detail with CorrelationId.
            }

            return response;
        }
    }

    /// <summary>
    /// Extension method to register global exception handler
    /// </summary>
    public static class GlobalExceptionHandlerMiddlewareExtensions
    {
        public static IApplicationBuilder UseGlobalExceptionHandler(this IApplicationBuilder builder)
        {
            return builder.UseMiddleware<GlobalExceptionHandlerMiddleware>();
        }
    }
}

