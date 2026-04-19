using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    /// <summary>
    /// Retry policy configuration for ICUMS operations
    /// </summary>
    public class RetryPolicyOptions
    {
        public int MaxRetries { get; set; } = 3;
        public TimeSpan InitialDelay { get; set; } = TimeSpan.FromSeconds(1);
        public TimeSpan MaxDelay { get; set; } = TimeSpan.FromSeconds(30);
        public double BackoffMultiplier { get; set; } = 2.0;
        public bool UseJitter { get; set; } = true;
        public double JitterPercentage { get; set; } = 0.1; // 10% jitter
    }

    /// <summary>
    /// Enhanced retry logic with exponential backoff and jitter
    /// Phase 2.1: Reliability improvements for transient errors
    /// </summary>
    public static class RetryPolicy
    {
        private static readonly Random _random = new();

        /// <summary>
        /// Determines if an exception represents a transient error that should be retried
        /// </summary>
        public static bool IsTransientError(Exception exception)
        {
            return exception switch
            {
                // Network-related transient errors
                HttpRequestException httpEx when httpEx.InnerException is SocketException => true,
                TaskCanceledException when exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => true,
                TimeoutException => true,
                SocketException => true,

                // HTTP status codes that indicate transient errors
                HttpRequestException httpEx when httpEx.Data.Contains("StatusCode") =>
                    IsTransientHttpStatusCode((HttpStatusCode?)httpEx.Data["StatusCode"]),

                // Database connection errors
                Microsoft.Data.SqlClient.SqlException sqlEx when sqlEx.Number == -2 || // Timeout
                                                                 sqlEx.Number == 2 ||  // Connection timeout
                                                                 sqlEx.Number == 53 => true, // Network error

                // Generic transient indicators
                _ when exception.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) => true,
                _ when exception.Message.Contains("connection", StringComparison.OrdinalIgnoreCase) => true,
                _ when exception.Message.Contains("network", StringComparison.OrdinalIgnoreCase) => true,
                _ => false
            };
        }

        private static bool IsTransientHttpStatusCode(HttpStatusCode? statusCode)
        {
            return statusCode switch
            {
                HttpStatusCode.RequestTimeout => true,
                HttpStatusCode.BadGateway => true,
                HttpStatusCode.ServiceUnavailable => true,
                HttpStatusCode.GatewayTimeout => true,
                HttpStatusCode.TooManyRequests => true,
                _ => false
            };
        }

        /// <summary>
        /// Executes an operation with retry logic and exponential backoff
        /// </summary>
        public static async Task<T> ExecuteWithRetryAsync<T>(
            Func<Task<T>> operation,
            RetryPolicyOptions? options = null,
            ILogger? logger = null,
            string? operationName = null,
            CancellationToken cancellationToken = default)
        {
            options ??= new RetryPolicyOptions();
            var attempt = 0;
            Exception? lastException = null;

            while (attempt < options.MaxRetries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                try
                {
                    attempt++;
                    var result = await operation();

                    if (attempt > 1)
                    {
                        logger?.LogInformation(
                            "Operation {OperationName} succeeded on attempt {Attempt}/{MaxRetries}",
                            operationName ?? "Unknown", attempt, options.MaxRetries);
                    }

                    return result;
                }
                catch (Exception ex)
                {
                    lastException = ex;

                    // Check if this is a transient error
                    if (!IsTransientError(ex))
                    {
                        logger?.LogWarning(
                            "Operation {OperationName} failed with non-transient error on attempt {Attempt}: {Error}",
                            operationName ?? "Unknown", attempt, ex.Message);
                        throw; // Don't retry non-transient errors
                    }

                    if (attempt >= options.MaxRetries)
                    {
                        logger?.LogError(ex,
                            "Operation {OperationName} failed after {MaxRetries} attempts: {Error}",
                            operationName ?? "Unknown", options.MaxRetries, ex.Message);
                        throw;
                    }

                    // Calculate delay with exponential backoff
                    var delay = CalculateDelay(attempt, options);

                    logger?.LogWarning(
                        "Operation {OperationName} failed on attempt {Attempt}/{MaxRetries} (transient error: {Error}). Retrying in {Delay}ms...",
                        operationName ?? "Unknown", attempt, options.MaxRetries, ex.Message, delay.TotalMilliseconds);

                    await Task.Delay(delay, cancellationToken);
                }
            }

            // Should never reach here, but just in case
            throw lastException ?? new InvalidOperationException($"Operation {operationName ?? "Unknown"} failed after {options.MaxRetries} attempts");
        }

        /// <summary>
        /// Executes an operation with retry logic (void return)
        /// </summary>
        public static async Task ExecuteWithRetryAsync(
            Func<Task> operation,
            RetryPolicyOptions? options = null,
            ILogger? logger = null,
            string? operationName = null,
            CancellationToken cancellationToken = default)
        {
            await ExecuteWithRetryAsync(async () =>
            {
                await operation();
                return true;
            }, options, logger, operationName, cancellationToken);
        }

        /// <summary>
        /// Calculates delay with exponential backoff and optional jitter
        /// </summary>
        public static TimeSpan CalculateDelay(int attempt, RetryPolicyOptions options)
        {
            // Exponential backoff: initialDelay * (backoffMultiplier ^ (attempt - 1))
            var exponentialDelay = options.InitialDelay.TotalMilliseconds * Math.Pow(options.BackoffMultiplier, attempt - 1);

            // Cap at max delay
            var delay = Math.Min(exponentialDelay, options.MaxDelay.TotalMilliseconds);

            // Add jitter to prevent thundering herd problem
            if (options.UseJitter)
            {
                var jitterRange = delay * options.JitterPercentage;
                var jitter = (_random.NextDouble() * 2 - 1) * jitterRange; // Random between -jitterRange and +jitterRange
                delay += jitter;
                delay = Math.Max(delay, 0); // Ensure non-negative
            }

            return TimeSpan.FromMilliseconds(delay);
        }

        /// <summary>
        /// Creates a retry policy optimized for ICUMS API calls
        /// </summary>
        public static RetryPolicyOptions CreateIcumApiRetryPolicy()
        {
            return new RetryPolicyOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromSeconds(2),
                MaxDelay = TimeSpan.FromSeconds(30),
                BackoffMultiplier = 2.0,
                UseJitter = true,
                JitterPercentage = 0.15 // 15% jitter for API calls
            };
        }

        /// <summary>
        /// Creates a retry policy optimized for database operations
        /// </summary>
        public static RetryPolicyOptions CreateDatabaseRetryPolicy()
        {
            return new RetryPolicyOptions
            {
                MaxRetries = 3,
                InitialDelay = TimeSpan.FromSeconds(1),
                MaxDelay = TimeSpan.FromSeconds(10),
                BackoffMultiplier = 2.0,
                UseJitter = true,
                JitterPercentage = 0.1 // 10% jitter for database calls
            };
        }

        /// <summary>
        /// Creates a retry policy optimized for file operations
        /// </summary>
        public static RetryPolicyOptions CreateFileOperationRetryPolicy()
        {
            return new RetryPolicyOptions
            {
                MaxRetries = 5,
                InitialDelay = TimeSpan.FromMilliseconds(500),
                MaxDelay = TimeSpan.FromSeconds(5),
                BackoffMultiplier = 2.0,
                UseJitter = true,
                JitterPercentage = 0.2 // 20% jitter for file operations (file locks can be brief)
            };
        }
    }
}

