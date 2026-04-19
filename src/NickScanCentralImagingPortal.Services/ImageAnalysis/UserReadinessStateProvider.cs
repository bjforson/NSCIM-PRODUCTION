using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    /// <summary>
    /// Provides access to user readiness state from SignalR hub
    /// This is a bridge to avoid Services project referencing API project
    /// The API project sets this provider, Services project reads from it
    /// </summary>
    public static class UserReadinessStateProvider
    {
        /// <summary>
        /// In-memory store for user readiness state (populated by SignalR hub)
        /// Key format: "username:role", Value: UserReadinessState
        /// </summary>
        private static readonly ConcurrentDictionary<string, UserReadinessState> _readyUsers = new();

        /// <summary>
        /// Set readiness state (called by SignalR hub in API project)
        /// </summary>
        public static void SetReadiness(string username, string role, bool isReady, DateTime lastHeartbeat, string? connectionId = null, string? sessionId = null)
        {
            var key = $"{username}:{role}";
            _readyUsers.AddOrUpdate(key,
                new UserReadinessState
                {
                    Username = username,
                    Role = role,
                    IsReady = isReady,
                    LastHeartbeat = lastHeartbeat,
                    ConnectionId = connectionId ?? "",
                    SessionId = sessionId
                },
                (key, existing) =>
                {
                    existing.IsReady = isReady;
                    existing.LastHeartbeat = lastHeartbeat;
                    if (!string.IsNullOrEmpty(connectionId))
                        existing.ConnectionId = connectionId;
                    if (!string.IsNullOrEmpty(sessionId))
                        existing.SessionId = sessionId;
                    return existing;
                });
        }

        /// <summary>
        /// Get ready users for a specific role
        /// </summary>
        public static List<string> GetReadyUsers(string role, TimeSpan maxIdleTime)
        {
            var cutoff = DateTime.UtcNow - maxIdleTime;
            return _readyUsers.Values
                .Where(u => u.Role.Equals(role, StringComparison.OrdinalIgnoreCase)
                    && u.IsReady
                    && u.LastHeartbeat >= cutoff)
                .Select(u => u.Username)
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// Get all readiness states (for sync service)
        /// </summary>
        public static Dictionary<string, UserReadinessState> GetAllReadyUsers()
        {
            return new Dictionary<string, UserReadinessState>(_readyUsers);
        }

        /// <summary>
        /// Remove user from tracking
        /// </summary>
        public static void RemoveUser(string username, string? role = null)
        {
            if (string.IsNullOrEmpty(role))
            {
                var keysToRemove = _readyUsers.Keys
                    .Where(k => k.StartsWith($"{username}:", StringComparison.OrdinalIgnoreCase))
                    .ToList();

                foreach (var key in keysToRemove)
                {
                    _readyUsers.TryRemove(key, out _);
                }
            }
            else
            {
                var key = $"{username}:{role}";
                _readyUsers.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// Update heartbeat for a user
        /// </summary>
        public static void UpdateHeartbeat(string username, string role)
        {
            var key = $"{username}:{role}";
            if (_readyUsers.TryGetValue(key, out var state))
            {
                state.LastHeartbeat = DateTime.UtcNow;
            }
        }

        /// <summary>
        /// Get last heartbeat time for a user
        /// </summary>
        public static DateTime GetLastHeartbeat(string username, string role)
        {
            var key = $"{username}:{role}";
            if (_readyUsers.TryGetValue(key, out var state))
            {
                return state.LastHeartbeat;
            }
            return DateTime.UtcNow.AddHours(-1); // Default to 1 hour ago if not found
        }

        /// <summary>
        /// ✅ FIX: Clear all readiness for a user (called on logout)
        /// Marks user as not ready for all roles and removes from tracking
        /// </summary>
        public static void ClearUserReadiness(string username)
        {
            var keysToUpdate = _readyUsers.Keys
                .Where(k => k.StartsWith($"{username}:", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var key in keysToUpdate)
            {
                if (_readyUsers.TryGetValue(key, out var state))
                {
                    state.IsReady = false;
                    state.LastHeartbeat = DateTime.UtcNow.AddHours(-1); // Mark as expired
                }
            }
        }
    }

    /// <summary>
    /// User readiness state stored in memory
    /// </summary>
    public class UserReadinessState
    {
        public string Username { get; set; } = "";
        public string Role { get; set; } = "";
        public bool IsReady { get; set; }
        public DateTime LastHeartbeat { get; set; }
        public string ConnectionId { get; set; } = "";
        public string? SessionId { get; set; }
    }
}

