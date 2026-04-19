namespace NickScanCentralImagingPortal.Core.Interfaces
{
    /// <summary>
    /// Interface for accessing System Settings throughout the application
    /// Provides type-safe access to configuration values with caching
    /// </summary>
    public interface ISettingsProvider
    {
        /// <summary>
        /// Get setting value as string with fallback to default
        /// </summary>
        Task<string> GetStringAsync(string category, string key, string defaultValue = "");

        /// <summary>
        /// Get setting value as integer with fallback to default
        /// </summary>
        Task<int> GetIntAsync(string category, string key, int defaultValue = 0);

        /// <summary>
        /// Get setting value as boolean with fallback to default
        /// </summary>
        Task<bool> GetBoolAsync(string category, string key, bool defaultValue = false);

        /// <summary>
        /// Get setting value as decimal with fallback to default
        /// </summary>
        Task<decimal> GetDecimalAsync(string category, string key, decimal defaultValue = 0m);

        /// <summary>
        /// Get all settings for a category as a dictionary
        /// </summary>
        Task<Dictionary<string, string>> GetCategorySettingsAsync(string category);

        /// <summary>
        /// Invalidate cache for a specific setting or entire category
        /// </summary>
        void InvalidateCache(string category, string? key = null);

        /// <summary>
        /// Clear all settings cache
        /// </summary>
        void ClearCache();

        /// <summary>
        /// Check if a setting exists and is active
        /// </summary>
        Task<bool> ExistsAsync(string category, string key);
    }
}

