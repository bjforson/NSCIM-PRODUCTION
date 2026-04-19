namespace NickScanWebApp.Mobile.Services
{
    /// <summary>
    /// Service to detect mobile devices from user agent strings
    /// </summary>
    public static class BrowserDetectionService
    {
        // Common mobile device user agent patterns
        private static readonly string[] MobileUserAgentPatterns = new[]
        {
            "Mobile", "Android", "iPhone", "iPad", "iPod", "BlackBerry", 
            "Windows Phone", "Opera Mini", "IEMobile", "Kindle", "Silk",
            "webOS", "Palm", "Fennec", "Maemo", "Tablet", "Phone"
        };

        // Tablet-specific patterns (should be treated as mobile)
        private static readonly string[] TabletUserAgentPatterns = new[]
        {
            "iPad", "Android", "Tablet", "Kindle", "PlayBook", "TouchPad"
        };

        /// <summary>
        /// Detects if the request is from a mobile device based on User-Agent header
        /// </summary>
        public static bool IsMobileDevice(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return false;

            var userAgentLower = userAgent.ToLowerInvariant();

            // Check for mobile patterns
            return MobileUserAgentPatterns.Any(pattern => 
                userAgentLower.Contains(pattern.ToLowerInvariant()));
        }

        /// <summary>
        /// Detects if the request is from a tablet device
        /// </summary>
        public static bool IsTabletDevice(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return false;

            var userAgentLower = userAgent.ToLowerInvariant();

            // Check for tablet patterns
            return TabletUserAgentPatterns.Any(pattern => 
                userAgentLower.Contains(pattern.ToLowerInvariant()));
        }

        /// <summary>
        /// Detects if the request should use mobile version (mobile or tablet)
        /// </summary>
        public static bool ShouldUseMobileVersion(string? userAgent)
        {
            return IsMobileDevice(userAgent) || IsTabletDevice(userAgent);
        }

        /// <summary>
        /// Gets device type from user agent
        /// </summary>
        public static DeviceType GetDeviceType(string? userAgent)
        {
            if (string.IsNullOrWhiteSpace(userAgent))
                return DeviceType.Desktop;

            if (IsTabletDevice(userAgent))
                return DeviceType.Tablet;

            if (IsMobileDevice(userAgent))
                return DeviceType.Mobile;

            return DeviceType.Desktop;
        }

        public enum DeviceType
        {
            Desktop,
            Tablet,
            Mobile
        }
    }
}

