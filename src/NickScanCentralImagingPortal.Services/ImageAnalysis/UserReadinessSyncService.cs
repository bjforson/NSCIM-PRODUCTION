using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    /// <summary>
    /// Background service that syncs SignalR user readiness state to database
    /// Hybrid approach: SignalR provides real-time updates, database provides persistence
    /// Runs every 30 seconds to keep database in sync with SignalR state
    /// </summary>
    public class UserReadinessSyncService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<UserReadinessSyncService> _logger;
        private static readonly TimeSpan SyncInterval = TimeSpan.FromSeconds(30);

        public UserReadinessSyncService(
            IServiceScopeFactory scopeFactory,
            ILogger<UserReadinessSyncService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("🚀 User Readiness Sync Service starting...");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await SyncSignalRStateToDatabaseAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "❌ Error syncing user readiness state to database");
                }

                await Task.Delay(SyncInterval, stoppingToken);
            }

            _logger.LogInformation("🛑 User Readiness Sync Service stopping...");
        }

        private async Task SyncSignalRStateToDatabaseAsync(CancellationToken cancellationToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                // Get all users from SignalR state provider (real-time state)
                var signalRUsers = UserReadinessStateProvider.GetAllReadyUsers();

                if (!signalRUsers.Any())
                {
                    // No users in SignalR, but don't clear database (they might reconnect)
                    return;
                }

                var syncedCount = 0;
                var now = DateTime.UtcNow;

                // Sync each user from SignalR to database
                foreach (var kvp in signalRUsers)
                {
                    var key = kvp.Key; // Format: "username:role"
                    var state = kvp.Value;

                    // Parse username and role from key
                    var parts = key.Split(':', 2);
                    if (parts.Length != 2) continue;

                    var username = parts[0];
                    var role = parts[1];

                    try
                    {
                        // Find or create database record (AsTracking required since DbContext defaults to NoTracking)
                        var dbReadiness = await db.UserReadiness
                            .AsTracking()
                            .FirstOrDefaultAsync(r => r.Username == username && r.Role == role, cancellationToken);

                        if (dbReadiness == null)
                        {
                            // Create new record
                            dbReadiness = new UserReadiness
                            {
                                Username = username,
                                Role = role,
                                IsReady = state.IsReady,
                                LastHeartbeat = state.LastHeartbeat,
                                LastChangedAt = now,
                                ChangedBy = username,
                                SessionId = state.SessionId
                            };
                            db.UserReadiness.Add(dbReadiness);
                            syncedCount++;
                        }
                        else
                        {
                            // Update existing record
                            var wasChanged = dbReadiness.IsReady != state.IsReady;
                            dbReadiness.IsReady = state.IsReady;
                            dbReadiness.LastHeartbeat = state.LastHeartbeat;
                            if (!string.IsNullOrEmpty(state.SessionId))
                                dbReadiness.SessionId = state.SessionId;

                            if (wasChanged)
                            {
                                dbReadiness.LastChangedAt = now;
                                dbReadiness.ChangedBy = username;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Error syncing user readiness for {Username}:{Role}", username, role);
                    }
                }

                if (syncedCount > 0 || signalRUsers.Any())
                {
                    await db.SaveChangesAsync(cancellationToken);
                    _logger.LogDebug("✅ Synced {Count} user readiness records to database (Total SignalR users: {SignalRCount})",
                        syncedCount, signalRUsers.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in SyncSignalRStateToDatabaseAsync");
                throw;
            }
        }
    }
}

