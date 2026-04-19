using Swashbuckle.AspNetCore.Filters;

namespace NickScanCentralImagingPortal.API.Swagger
{
    /// <summary>
    /// Example for login request
    /// </summary>
    public class LoginRequestExample : IExamplesProvider<object>
    {
        public object GetExamples()
        {
            return new
            {
                username = "your_username",
                password = "your_password"
            };
        }
    }

    /// <summary>
    /// Example for login response
    /// </summary>
    public class LoginResponseExample : IExamplesProvider<object>
    {
        public object GetExamples()
        {
            return new
            {
                token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ1bmlxdWVfbmFtZSI6ImFkbWluIiwibmFtZWlkIjoiMSIsInJvbGUiOiJTdXBlciBBZG1pbmlzdHJhdG9yIiwiZW1haWwiOiJhZG1pbkBuaWNrc2Nhbi5jb20iLCJuYmYiOjE2OTc1NDMyMDAsImV4cCI6MTY5NzU3MjAwMCwiaWF0IjoxNjk3NTQzMjAwLCJpc3MiOiJOaWNrU2NhbkNlbnRyYWxJbWFnaW5nUG9ydGFsIiwiYXVkIjoiTmlja1NjYW5Qb3J0YWxVc2VycyJ9.example_signature",
                refreshToken = "a1b2c3d4e5f6g7h8i9j0k1l2m3n4o5p6",
                expiresIn = 28800,
                tokenType = "Bearer",
                user = new
                {
                    id = 1,
                    username = "admin",
                    email = "admin@nickscan.com",
                    roleId = 1,
                    roleName = "Super Administrator",
                    isActive = true
                }
            };
        }
    }

    /// <summary>
    /// Example for validation error response
    /// </summary>
    public class ValidationErrorExample : IExamplesProvider<object>
    {
        public object GetExamples()
        {
            return new
            {
                message = "Validation failed",
                errors = new
                {
                    Username = new[] { "Username is required" },
                    Password = new[] { "Password is required" }
                },
                statusCode = 400
            };
        }
    }

    /// <summary>
    /// Example for rate limit exceeded response
    /// </summary>
    public class RateLimitExceededExample : IExamplesProvider<object>
    {
        public object GetExamples()
        {
            return new
            {
                message = "Rate limit exceeded. Please try again later.",
                retryAfter = "30"
            };
        }
    }

    /// <summary>
    /// Example for unauthorized response
    /// </summary>
    public class UnauthorizedExample : IExamplesProvider<object>
    {
        public object GetExamples()
        {
            return new
            {
                statusCode = 401,
                message = "Unauthorized. Please provide a valid JWT token in the Authorization header."
            };
        }
    }

    /// <summary>
    /// Example for forbidden response
    /// </summary>
    public class ForbiddenExample : IExamplesProvider<object>
    {
        public object GetExamples()
        {
            return new
            {
                statusCode = 403,
                message = "Forbidden. You do not have permission to access this resource.",
                requiredRole = "Administrator"
            };
        }
    }
}

