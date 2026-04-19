using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Startup
{
    /// <summary>
    /// Ensures that the SuperAdmin account is always available with a known password,
    /// so operators can recover access without manual database work.
    /// </summary>
    public static class SuperAdminGuard
    {
        public static async Task EnsureAsync(IServiceProvider services, IConfiguration configuration)
        {
            var options = configuration.GetSection("SuperAdminGuard").Get<SuperAdminGuardOptions>();
            if (options is null || !options.Enabled)
            {
                return;
            }

            using var scope = services.CreateScope();
            var loggerFactory = scope.ServiceProvider.GetRequiredService<ILoggerFactory>();
            var logger = loggerFactory.CreateLogger("SuperAdminGuard");

            string username = string.IsNullOrWhiteSpace(options.Username) ? "superadmin" : options.Username.Trim();
            string? password = ResolvePassword(options);

            if (string.IsNullOrWhiteSpace(password))
            {
                logger.LogWarning("SuperAdminGuard enabled but no password provided. Set either SuperAdminGuard:Password or SuperAdminGuard:PasswordEnvironmentVariable. Skipping guard execution.");
                return;
            }

            var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // ✅ FIX: Add retry logic with exponential backoff for connection pool exhaustion
            const int maxRetries = 5;
            const int baseDelayMs = 1000; // Start with 1 second

            for (int attempt = 0; attempt < maxRetries; attempt++)
            {
                try
                {
                    var user = await context.Users.FirstOrDefaultAsync(u =>
                        u.Username.ToLower() == username.ToLower());

                    if (user == null)
                    {
                        if (!options.CreateIfMissing)
                        {
                            logger.LogWarning("SuperAdminGuard could not find user '{Username}' and CreateIfMissing=false. No action taken.", username);
                            return;
                        }

                        await CreateSuperAdminAsync(context, logger, username, password, options);
                        return;
                    }

                    await RefreshSuperAdminAsync(context, logger, user, password, options);
                    return; // Success - exit retry loop
                }
                catch (System.InvalidOperationException ex) when (ex.Message.Contains("Timeout expired") || ex.Message.Contains("connection from the pool"))
                {
                    // Connection pool exhausted - retry with exponential backoff
                    if (attempt < maxRetries - 1)
                    {
                        var delayMs = baseDelayMs * (int)Math.Pow(2, attempt); // Exponential backoff: 1s, 2s, 4s, 8s, 16s
                        logger.LogWarning("SuperAdminGuard: Connection pool exhausted (attempt {Attempt}/{MaxRetries}). Retrying in {DelayMs}ms...",
                            attempt + 1, maxRetries, delayMs);
                        await Task.Delay(delayMs);
                    }
                    else
                    {
                        logger.LogWarning(ex, "SuperAdminGuard: Failed after {MaxRetries} attempts due to connection pool exhaustion. Application will continue but SuperAdmin account may not be configured.", maxRetries);
                        return; // Don't crash the application
                    }
                }
                catch (Microsoft.Data.SqlClient.SqlException sqlEx)
                {
                    // SQL Server connection errors - log and continue (don't crash the app)
                    logger.LogWarning(sqlEx, "SuperAdminGuard: Cannot connect to SQL Server. Application will continue but SuperAdmin account may not be configured. Error: {Message}", sqlEx.Message);
                    return; // Don't crash the application
                }
                catch (Exception ex)
                {
                    // Other errors - log as warning and continue (don't crash the app)
                    logger.LogWarning(ex, "SuperAdminGuard: Unexpected error during execution. Application will continue but SuperAdmin account may not be configured.");
                    return; // Don't crash the application
                }
            }
        }

        private static async Task CreateSuperAdminAsync(ApplicationDbContext context, ILogger logger, string username, string password, SuperAdminGuardOptions options)
        {
            var superAdminRole = await context.Roles.FirstOrDefaultAsync(r =>
                r.Name.ToLower() == "superadmin");

            if (superAdminRole == null)
            {
                logger.LogWarning("SuperAdminGuard could not create user '{Username}' because the 'SuperAdmin' role does not exist yet.", username);
                return;
            }

            var now = DateTime.UtcNow;

            var user = new User
            {
                Username = username,
                Email = options.Email ?? $"{username}@nickscan.local",
                FirstName = options.FirstName ?? "Super",
                LastName = options.LastName ?? "Administrator",
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12),
                IsActive = true,
                CreatedAt = now,
                CreatedBy = "SuperAdminGuard",
                RoleId = superAdminRole.Id,
                LegacyRole = UserRole.SuperAdmin
            };

            context.Users.Add(user);
            await context.SaveChangesAsync();

            logger.LogInformation("✅ SuperAdminGuard created SuperAdmin account '{Username}' and reset password.", username);
        }

        private static async Task RefreshSuperAdminAsync(ApplicationDbContext context, ILogger logger, User user, string password, SuperAdminGuardOptions options)
        {
            bool updated = false;

            if (options.ReactivateAccount && !user.IsActive)
            {
                user.IsActive = true;
                updated = true;
            }

            if (options.AssignRoleIfMissing && (user.RoleId == null || user.LegacyRole != UserRole.SuperAdmin))
            {
                var superAdminRole = await context.Roles.FirstOrDefaultAsync(r =>
                    r.Name.ToLower() == "superadmin");

                if (superAdminRole != null)
                {
                    user.RoleId = superAdminRole.Id;
                    user.LegacyRole = UserRole.SuperAdmin;
                    updated = true;
                }
                else
                {
                    logger.LogWarning("SuperAdminGuard could not assign SuperAdmin role because it does not exist.");
                }
            }

            if (options.ResetPasswordOnStartup)
            {
                bool passwordMatches = BCrypt.Net.BCrypt.Verify(password, user.PasswordHash);
                if (!passwordMatches)
                {
                    user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, workFactor: 12);
                    updated = true;
                }
            }

            if (updated)
            {
                user.UpdatedAt = DateTime.UtcNow;
                user.UpdatedBy = "SuperAdminGuard";
                await context.SaveChangesAsync();

                logger.LogInformation("✅ SuperAdminGuard refreshed account '{Username}' (reactivated: {Reactivated}, password reset: {PasswordReset}).",
                    user.Username,
                    options.ReactivateAccount,
                    options.ResetPasswordOnStartup);
            }
            else
            {
                logger.LogInformation("ℹ️ SuperAdminGuard checked account '{Username}' – no changes required.", user.Username);
            }
        }

        private static string? ResolvePassword(SuperAdminGuardOptions options)
        {
            if (!string.IsNullOrWhiteSpace(options.PasswordEnvironmentVariable))
            {
                var envPassword = Environment.GetEnvironmentVariable(options.PasswordEnvironmentVariable);
                if (!string.IsNullOrWhiteSpace(envPassword))
                {
                    return envPassword;
                }
            }

            return options.Password;
        }
    }
}

