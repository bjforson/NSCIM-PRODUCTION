using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Services.ImageAnalysis;

namespace NickScanCentralImagingPortal.API.Hubs
{
    /// <summary>
    /// SignalR Hub for tracking user readiness for assignments in real-time
    /// Hybrid approach: Real-time updates via SignalR, synced to database for persistence
    /// </summary>
    public class UserReadinessHub : Hub
    {
        private readonly ILogger<UserReadinessHub> _logger;

        public UserReadinessHub(ILogger<UserReadinessHub> logger)
        {
            _logger = logger;
        }

        public override Task OnConnectedAsync()
        {
            var username = GetAuthenticatedUsername();
            if (!string.IsNullOrEmpty(username))
            {
                _logger.LogDebug("UserReadinessHub: Client connected: {Username} (ConnectionId: {ConnectionId})",
                    username, Context.ConnectionId);
            }
            return base.OnConnectedAsync();
        }

        public override Task OnDisconnectedAsync(Exception? exception)
        {
            var username = GetAuthenticatedUsername();
            if (!string.IsNullOrEmpty(username))
            {
                // ✅ FIX: Remove user from tracking when they disconnect (indicates logout or session expired)
                // This ensures no assignments are created for users who have disconnected
                UserReadinessStateProvider.RemoveUser(username);
                _logger.LogInformation("UserReadinessHub: User {Username} disconnected and removed from readiness tracking (ConnectionId: {ConnectionId})",
                    username, Context.ConnectionId);
            }
            return base.OnDisconnectedAsync(exception);
        }

        /// <summary>
        /// Set user's readiness status for a specific role
        /// </summary>
        public async Task SetReadyForAssignment(string role, bool isReady, string? sessionId = null)
        {
            var username = GetAuthenticatedUsername();
            if (string.IsNullOrEmpty(username))
            {
                _logger.LogWarning("UserReadinessHub: SetReadyForAssignment called but user is not authenticated");
                return;
            }

            // Update state provider (shared with Services project)
            UserReadinessStateProvider.SetReadiness(username, role, isReady, DateTime.UtcNow, Context.ConnectionId, sessionId);

            _logger.LogInformation("UserReadinessHub: User {Username} set readiness to {IsReady} for role {Role}",
                username, isReady, role);

            // Notify all clients (optional - for dashboard updates)
            await Clients.All.SendAsync("UserReadinessChanged", username, role, isReady);
        }

        /// <summary>
        /// Send heartbeat to indicate user is still active
        /// </summary>
        public async Task SendHeartbeat(string role)
        {
            var username = GetAuthenticatedUsername();
            if (string.IsNullOrEmpty(username))
                return;

            // Update heartbeat in state provider
            UserReadinessStateProvider.UpdateHeartbeat(username, role);
            _logger.LogDebug("UserReadinessHub: Received heartbeat from {Username} (Role: {Role})", username, role);

            await Task.CompletedTask;
        }

        private string GetAuthenticatedUsername()
        {
            return Context.User?.Identity?.Name
                ?? Context.User?.FindFirst(ClaimTypes.Name)?.Value
                ?? Context.User?.FindFirst("username")?.Value
                ?? Context.User?.FindFirst("name")?.Value
                ?? Context.User?.FindFirst("preferred_username")?.Value
                ?? string.Empty;
        }

    }
}

