using NickScanWebApp.New.Services;

namespace NickScanWebApp.New.Middleware
{
    /// <summary>
    /// Middleware to detect mobile devices and redirect to mobile app
    /// </summary>
    public class MobileDetectionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<MobileDetectionMiddleware> _logger;
        private readonly IConfiguration _configuration;

        public MobileDetectionMiddleware(
            RequestDelegate next,
            ILogger<MobileDetectionMiddleware> logger,
            IConfiguration configuration)
        {
            _next = next;
            _logger = logger;
            _configuration = configuration;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Check if mobile detection is enabled
            var enableMobileDetection = _configuration.GetValue<bool>("MobileApp:EnableDetection", true);

            if (enableMobileDetection && ShouldRedirectToMobile(context))
            {
                var mobileAppUrl = _configuration["MobileApp:BaseUrl"] ?? "http://localhost:5299";
                var currentPath = context.Request.Path.Value ?? "/";

                // Preserve query string if present
                var queryString = context.Request.QueryString.HasValue
                    ? context.Request.QueryString.Value
                    : string.Empty;

                var redirectUrl = $"{mobileAppUrl}{currentPath}{queryString}";

                _logger.LogInformation("Redirecting mobile device to: {Url}", redirectUrl);

                context.Response.Redirect(redirectUrl, permanent: false);
                return;
            }

            await _next(context);
        }

        private bool ShouldRedirectToMobile(HttpContext context)
        {
            // Skip redirect if already on mobile app path or API calls
            var path = context.Request.Path.Value?.ToLowerInvariant() ?? string.Empty;
            if (path.Contains("/mobile") || path.StartsWith("/api") || path.StartsWith("/health"))
            {
                return false;
            }

            // Skip if user explicitly wants desktop version (cookie/session)
            if (context.Request.Cookies.ContainsKey("useDesktop") ||
                context.Request.Query.ContainsKey("desktop"))
            {
                return false;
            }

            // Check User-Agent header
            var userAgent = context.Request.Headers["User-Agent"].ToString();

            return BrowserDetectionService.ShouldUseMobileVersion(userAgent);
        }
    }

    /// <summary>
    /// Browser detection service (shared between desktop and mobile apps)
    /// </summary>
    public static class BrowserDetectionService
    {
        private static readonly string[] MobileUserAgentPatterns = new[]
        {
            "Mobile", "Android", "iPhone", "iPad", "iPod", "BlackBerry",
            "Windows Phone", "Opera Mini", "IEMobile", "Kindle", "Silk",
            "webOS", "Palm", "Fennec", "Maemo", "Tablet", "Phone"
        };

        private static readonly string[] TabletUserAgentPatterns = new[]
        {
            "iPad", "Android", "Tablet", "Kindle", "PlayBook", "TouchPad"
        };

        public static bool IsMobileDevice(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return false;

            var userAgentLower = userAgent.ToLowerInvariant();
            return MobileUserAgentPatterns.Any(pattern =>
                userAgentLower.Contains(pattern.ToLowerInvariant()));
        }

        public static bool IsTabletDevice(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return false;

            var userAgentLower = userAgent.ToLowerInvariant();
            return TabletUserAgentPatterns.Any(pattern =>
                userAgentLower.Contains(pattern.ToLowerInvariant()));
        }

        public static bool ShouldUseMobileVersion(string? userAgent)
        {
            return IsMobileDevice(userAgent) || IsTabletDevice(userAgent);
        }
    }
}

