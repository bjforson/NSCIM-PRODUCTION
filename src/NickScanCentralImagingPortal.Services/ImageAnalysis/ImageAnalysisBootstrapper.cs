using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ImageAnalysis
{
    /// <summary>
    /// Ensures baseline roles and settings exist for Image Analysis.
    /// </summary>
    public class ImageAnalysisBootstrapper : IHostedService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<ImageAnalysisBootstrapper> _logger;

        public ImageAnalysisBootstrapper(IServiceScopeFactory scopeFactory, ILogger<ImageAnalysisBootstrapper> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        [Obsolete]
        public async Task StartAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                // Check database connectivity first
                if (!await db.Database.CanConnectAsync(cancellationToken))
                {
                    _logger.LogWarning("Database not available during ImageAnalysisBootstrapper startup - will retry on next service cycle");
                    return; // Don't throw - allow application to start
                }

                // Ensure AnalysisSettings row
                var settings = await db.AnalysisSettings.AsTracking().FirstOrDefaultAsync(cancellationToken);
                if (settings == null)
                {
                    settings = new AnalysisSettings
                    {
                        Enabled = true,
                        AssignmentMode = "Manual",
                        AutoAssignStrategy = "RoundRobin",
                        AutoAssign = false, // Backward compatibility
                        LeaseMinutes = 15,
                        MaxConcurrentPerUser = 5, // ✅ FIX ISSUE #6: Changed default from 1 to 5 to allow more concurrent assignments
                        CreatedAtUtc = DateTime.UtcNow
                    };
                    db.AnalysisSettings.Add(settings);
                }
                else
                {
                    // Migrate existing settings if needed
                    if (string.IsNullOrEmpty(settings.AssignmentMode))
                    {
                        settings.AssignmentMode = settings.AutoAssign ? "Auto" : "Manual";
                        settings.AutoAssignStrategy = "RoundRobin";
                        settings.UpdatedAtUtc = DateTime.UtcNow;
                        await db.SaveChangesAsync(cancellationToken);
                    }
                }

                // Ensure roles: Analyst, Audit, Lead
                // ✅ FIX: Use raw SQL to avoid CTE generation that requires semicolons in SQL Server 2014
                var requiredRoles = new[] { "Analyst", "Audit", "Lead" };
                var parameters = requiredRoles.Cast<object>().ToArray();
                var placeholders = string.Join(",", requiredRoles.Select((_, i) => $"{{{i}}}"));
                var existingRoles = await db.Roles
                    .FromSqlRaw($"SELECT * FROM Roles WHERE Name IN ({placeholders})", parameters)
                    .Select(r => r.Name)
                    .ToListAsync(cancellationToken);
                foreach (var roleName in requiredRoles.Except(existingRoles))
                {
                    db.Roles.Add(new Role
                    {
                        Name = roleName,
                        DisplayName = roleName,
                        Description = $"Role for {roleName} operations",
                        IsSystemRole = true,
                        IsActive = true,
                        CreatedAt = DateTime.UtcNow
                    });
                }

                await db.SaveChangesAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // Service cancelled during startup - exit gracefully
                _logger.LogInformation("ImageAnalysisBootstrapper cancelled during startup");
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx) when (
                sqlEx.Number == 2 || sqlEx.Number == 40 || sqlEx.Number == 53 ||
                sqlEx.Number == 121 || sqlEx.Number == 10053 || sqlEx.Number == 10054 ||
                sqlEx.Number == 10060 || sqlEx.Number == 1225 ||
                sqlEx.Message.Contains("network-related") ||
                sqlEx.Message.Contains("instance-specific error") ||
                sqlEx.Message.Contains("cannot find the file specified") ||
                sqlEx.Message.Contains("refused the network connection"))
            {
                // Database connectivity issue - log at Debug level and continue
                _logger.LogDebug("Database not available during ImageAnalysisBootstrapper startup: {Message}", sqlEx.Message);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ImageAnalysisBootstrapper failed");
                // Don't throw - allow application to start even if bootstrapper fails
            }
        }

        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}


