using System.Text.Json.Serialization;

namespace NickScanCentralImagingPortal.API.Models
{
    /// <summary>
    /// Standardized API error response format
    /// </summary>
    public class ApiErrorResponse
    {
        /// <summary>
        /// Unique correlation ID for request tracking
        /// </summary>
        [JsonPropertyName("correlationId")]
        public string CorrelationId { get; set; } = string.Empty;

        /// <summary>
        /// Timestamp when error occurred (ISO 8601)
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// HTTP status code
        /// </summary>
        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        /// <summary>
        /// Error code for programmatic handling
        /// </summary>
        [JsonPropertyName("error")]
        public string Error { get; set; } = string.Empty;

        /// <summary>
        /// User-friendly error message
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;

        /// <summary>
        /// Detailed error information (only in development)
        /// </summary>
        [JsonPropertyName("details")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public object? Details { get; set; }

        /// <summary>
        /// Request path that caused the error
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; } = string.Empty;

        /// <summary>
        /// Validation errors (for 400 Bad Request)
        /// </summary>
        [JsonPropertyName("validationErrors")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public Dictionary<string, string[]>? ValidationErrors { get; set; }

        /// <summary>
        /// Stack trace (only in development)
        /// </summary>
        [JsonPropertyName("stackTrace")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? StackTrace { get; set; }
    }

    /// <summary>
    /// Error codes for programmatic error handling
    /// </summary>
    public static class ErrorCodes
    {
        // General errors (1xxx)
        public const string INTERNAL_SERVER_ERROR = "ERR_1000";
        public const string VALIDATION_ERROR = "ERR_1001";
        public const string NOT_FOUND = "ERR_1002";
        public const string CONFLICT = "ERR_1003";
        public const string BAD_REQUEST = "ERR_1004";

        // Authentication errors (2xxx)
        public const string UNAUTHORIZED = "ERR_2000";
        public const string INVALID_CREDENTIALS = "ERR_2001";
        public const string TOKEN_EXPIRED = "ERR_2002";
        public const string TOKEN_INVALID = "ERR_2003";
        public const string ACCOUNT_INACTIVE = "ERR_2004";
        public const string ACCOUNT_LOCKED = "ERR_2005";

        // Authorization errors (3xxx)
        public const string FORBIDDEN = "ERR_3000";
        public const string INSUFFICIENT_PERMISSIONS = "ERR_3001";
        public const string ROLE_REQUIRED = "ERR_3002";

        // Rate limiting errors (4xxx)
        public const string RATE_LIMIT_EXCEEDED = "ERR_4000";
        public const string QUOTA_EXCEEDED = "ERR_4001";

        // Database errors (5xxx)
        public const string DATABASE_ERROR = "ERR_5000";
        public const string DATABASE_CONNECTION_FAILED = "ERR_5001";
        public const string DATABASE_TIMEOUT = "ERR_5002";

        // External API errors (6xxx)
        public const string EXTERNAL_API_ERROR = "ERR_6000";
        public const string ICUMS_API_ERROR = "ERR_6001";
        public const string ICUMS_API_TIMEOUT = "ERR_6002";

        // File/Image errors (7xxx)
        public const string FILE_NOT_FOUND = "ERR_7000";
        public const string FILE_TOO_LARGE = "ERR_7001";
        public const string INVALID_FILE_FORMAT = "ERR_7002";
        public const string IMAGE_PROCESSING_ERROR = "ERR_7003";

        // Business logic errors (8xxx)
        public const string CONTAINER_NOT_FOUND = "ERR_8000";
        public const string DUPLICATE_CONTAINER = "ERR_8001";
        public const string INVALID_CONTAINER_STATE = "ERR_8002";

        /// <summary>
        /// Get user-friendly message for error code
        /// </summary>
        public static string GetMessage(string errorCode)
        {
            return errorCode switch
            {
                INTERNAL_SERVER_ERROR => "An unexpected error occurred. Please try again later.",
                VALIDATION_ERROR => "One or more validation errors occurred.",
                NOT_FOUND => "The requested resource was not found.",
                CONFLICT => "A conflict occurred with an existing resource.",
                BAD_REQUEST => "The request was invalid or malformed.",

                UNAUTHORIZED => "Authentication is required to access this resource.",
                INVALID_CREDENTIALS => "The provided credentials are invalid.",
                TOKEN_EXPIRED => "Your authentication token has expired. Please log in again.",
                TOKEN_INVALID => "The provided authentication token is invalid.",
                ACCOUNT_INACTIVE => "This account is inactive. Please contact support.",
                ACCOUNT_LOCKED => "This account has been locked. Please contact support.",

                FORBIDDEN => "You do not have permission to access this resource.",
                INSUFFICIENT_PERMISSIONS => "Your account does not have the required permissions.",
                ROLE_REQUIRED => "A specific role is required to access this resource.",

                RATE_LIMIT_EXCEEDED => "Too many requests. Please slow down and try again later.",
                QUOTA_EXCEEDED => "Your request quota has been exceeded.",

                DATABASE_ERROR => "A database error occurred. Please try again later.",
                DATABASE_CONNECTION_FAILED => "Unable to connect to the database.",
                DATABASE_TIMEOUT => "Database query timed out. Please try again.",

                EXTERNAL_API_ERROR => "An external service error occurred.",
                ICUMS_API_ERROR => "ICUMS API error. Please try again later.",
                ICUMS_API_TIMEOUT => "ICUMS API request timed out.",

                FILE_NOT_FOUND => "The requested file was not found.",
                FILE_TOO_LARGE => "The file size exceeds the maximum allowed limit.",
                INVALID_FILE_FORMAT => "The file format is not supported.",
                IMAGE_PROCESSING_ERROR => "An error occurred while processing the image.",

                CONTAINER_NOT_FOUND => "Container not found in the system.",
                DUPLICATE_CONTAINER => "A container with this number already exists.",
                INVALID_CONTAINER_STATE => "The container is in an invalid state for this operation.",

                _ => "An error occurred."
            };
        }
    }
}

