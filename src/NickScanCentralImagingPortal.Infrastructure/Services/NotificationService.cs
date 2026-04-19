using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Infrastructure.Services
{
    public interface INotificationService
    {
        Task CreateNotificationAsync(string type, string title, string message, string? targetUser = null, string? targetRole = null, DateTime? expiresAt = null);
        Task CreateSystemNotificationAsync(string title, string message, string? targetUser = null);
        Task CreateAlertNotificationAsync(string title, string message, string? targetUser = null);
        Task CreateUserNotificationAsync(string title, string message, string targetUser);
        Task CreateRoleNotificationAsync(string title, string message, string targetRole);
        Task NotifyContainerScanAsync(string containerNumber, string scannerType, string? targetUser = null);
        Task NotifyICUMSUpdateAsync(string containerNumber, string status, string? targetUser = null);
        Task NotifyProcessingCompleteAsync(string containerNumber, string? targetUser = null);
        Task NotifyValidationStatusAsync(string containerNumber, string status, string? targetUser = null);
        Task NotifyMaintenanceAsync(string title, string message);
        Task DeleteExpiredNotificationsAsync();
    }

    public class NotificationService : INotificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<NotificationService> _logger;

        public NotificationService(
            ApplicationDbContext context,
            ILogger<NotificationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task CreateNotificationAsync(
            string type,
            string title,
            string message,
            string? targetUser = null,
            string? targetRole = null,
            DateTime? expiresAt = null)
        {
            try
            {
                var notification = new SystemNotification
                {
                    NotificationType = type,
                    Title = title,
                    Message = message,
                    TargetUser = targetUser,
                    TargetRole = targetRole,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = expiresAt,
                    IsRead = false
                };

                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                _logger.LogInformation("Created {Type} notification: {Title} for {Target}",
                    type, title, targetUser ?? targetRole ?? "all users");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification: {Title}", title);
            }
        }

        public async Task CreateSystemNotificationAsync(string title, string message, string? targetUser = null)
        {
            await CreateNotificationAsync("System", title, message, targetUser);
        }

        public async Task CreateAlertNotificationAsync(string title, string message, string? targetUser = null)
        {
            await CreateNotificationAsync("Alert", title, message, targetUser);
        }

        public async Task CreateUserNotificationAsync(string title, string message, string targetUser)
        {
            await CreateNotificationAsync("User", title, message, targetUser);
        }

        public async Task CreateRoleNotificationAsync(string title, string message, string targetRole)
        {
            await CreateNotificationAsync("System", title, message, null, targetRole);
        }

        public async Task NotifyContainerScanAsync(string containerNumber, string scannerType, string? targetUser = null)
        {
            await CreateNotificationAsync(
                "System",
                "New Container Scan",
                $"Container {containerNumber} has been scanned using {scannerType} scanner",
                targetUser,
                null,
                DateTime.UtcNow.AddDays(7) // Expire after 7 days
            );
        }

        public async Task NotifyICUMSUpdateAsync(string containerNumber, string status, string? targetUser = null)
        {
            await CreateNotificationAsync(
                "System",
                "ICUMS Update",
                $"ICUMS data for container {containerNumber}: {status}",
                targetUser,
                null,
                DateTime.UtcNow.AddDays(7)
            );
        }

        public async Task NotifyProcessingCompleteAsync(string containerNumber, string? targetUser = null)
        {
            await CreateNotificationAsync(
                "System",
                "Processing Complete",
                $"Container {containerNumber} processing has been completed successfully",
                targetUser,
                null,
                DateTime.UtcNow.AddDays(7)
            );
        }

        public async Task NotifyValidationStatusAsync(string containerNumber, string status, string? targetUser = null)
        {
            var type = status.ToLower().Contains("error") || status.ToLower().Contains("fail") ? "Alert" : "System";

            await CreateNotificationAsync(
                type,
                "Validation Status",
                $"Container {containerNumber} validation status: {status}",
                targetUser,
                null,
                DateTime.UtcNow.AddDays(7)
            );
        }

        public async Task NotifyMaintenanceAsync(string title, string message)
        {
            await CreateNotificationAsync(
                "Maintenance",
                title,
                message,
                null,
                null,
                DateTime.UtcNow.AddDays(1) // Maintenance notifications expire after 1 day
            );
        }

        public async Task DeleteExpiredNotificationsAsync()
        {
            try
            {
                var expiredNotifications = await _context.Notifications
                    .Where(n => n.ExpiresAt != null && n.ExpiresAt < DateTime.UtcNow)
                    .ToListAsync();

                if (expiredNotifications.Any())
                {
                    _context.Notifications.RemoveRange(expiredNotifications);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("Deleted {Count} expired notifications", expiredNotifications.Count);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting expired notifications");
            }
        }
    }
}

