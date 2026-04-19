using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using MudBlazor;
using NickScanWebApp.New.Services;

namespace NickScanWebApp.New.Services
{
    /// <summary>
    /// Service that creates both Snackbar (pop-up) and database notifications for important messages
    /// </summary>
    public class DualNotificationService
    {
        private readonly ISnackbar _snackbar;
        private readonly ApiService _apiService;
        private readonly AuthenticationStateProvider _authStateProvider;
        private readonly ILogger<DualNotificationService>? _logger;

        public DualNotificationService(
            ISnackbar snackbar,
            ApiService apiService,
            AuthenticationStateProvider authStateProvider,
            ILogger<DualNotificationService>? logger = null)
        {
            _snackbar = snackbar;
            _apiService = apiService;
            _authStateProvider = authStateProvider;
            _logger = logger;
        }

        /// <summary>
        /// Show Snackbar notification only (for minor feedback)
        /// </summary>
        public void ShowSnackbar(string message, Severity severity = Severity.Normal)
        {
            _snackbar.Add(message, severity);
        }

        /// <summary>
        /// Get current username from authentication state
        /// </summary>
        private async Task<string?> GetCurrentUsernameAsync()
        {
            try
            {
                var authState = await _authStateProvider.GetAuthenticationStateAsync();
                return authState.User.Identity?.Name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Show Snackbar AND create database notification (for important messages)
        /// </summary>
        public async Task ShowAndSaveAsync(
            string message,
            Severity severity = Severity.Error,
            string? username = null,
            string? title = null)
        {
            // Show Snackbar immediately
            _snackbar.Add(message, severity);

            // Only create database notification for errors and warnings (important messages)
            if (severity != Severity.Error && severity != Severity.Warning)
            {
                return; // Don't save success/info messages
            }

            // Create database notification
            try
            {
                // Get username if not provided
                if (string.IsNullOrEmpty(username))
                {
                    username = await GetCurrentUsernameAsync();
                }

                var notificationType = severity switch
                {
                    Severity.Error => "Alert",
                    Severity.Warning => "Alert",
                    _ => "System"
                };

                var notificationTitle = title ?? (severity switch
                {
                    Severity.Error => "Error",
                    Severity.Warning => "Warning",
                    _ => "Notification"
                });

                // Create notification via API
                var notificationRequest = new
                {
                    NotificationType = notificationType,
                    Title = notificationTitle,
                    Message = message,
                    TargetUser = username,
                    TargetRole = (string?)null,
                    ExpiresAt = (DateTime?)DateTime.UtcNow.AddDays(7), // Expire after 7 days
                    AdditionalData = (Dictionary<string, object>?)null
                };

                await _apiService.PostAsync<object, object>("/api/Notifications", notificationRequest);
                _logger?.LogInformation("Created database notification for {Severity}: {Message}", severity, message);
            }
            catch (Exception ex)
            {
                // Log error but don't fail - Snackbar already shown
                _logger?.LogError(ex, "Failed to create database notification for message: {Message}", message);
            }
        }

        /// <summary>
        /// Show error notification (both Snackbar and database)
        /// </summary>
        public async Task ShowErrorAsync(string message, string? username = null, string? title = "Error")
        {
            await ShowAndSaveAsync(message, Severity.Error, username, title);
        }

        /// <summary>
        /// Show warning notification (both Snackbar and database)
        /// </summary>
        public async Task ShowWarningAsync(string message, string? username = null, string? title = "Warning")
        {
            await ShowAndSaveAsync(message, Severity.Warning, username, title);
        }

        /// <summary>
        /// Show success notification (Snackbar only - not saved to database)
        /// </summary>
        public void ShowSuccess(string message)
        {
            ShowSnackbar(message, Severity.Success);
        }

        /// <summary>
        /// Show info notification (Snackbar only - not saved to database)
        /// </summary>
        public void ShowInfo(string message)
        {
            ShowSnackbar(message, Severity.Info);
        }
    }
}

