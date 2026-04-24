using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class NotificationsController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<NotificationsController> _logger;

        public NotificationsController(
            ApplicationDbContext context,
            ILogger<NotificationsController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get notifications for the current user
        /// </summary>
        [HttpGet("user/{username}", Name = "GetUserNotifications")]
        public async Task<ActionResult<List<NotificationDto>>> GetUserNotifications(
            string username,
            [FromQuery] bool includeRead = false,
            [FromQuery] int limit = 50)
        {
            try
            {
                var userRole = User.Claims.FirstOrDefault(c => c.Type == "role")?.Value;

                // ✅ FIX: Use AND logic instead of OR - notification must match both user AND role conditions
                // Show notifications where:
                // - TargetUser is null (all users) OR TargetUser matches username OR TargetUser is "*" (all users)
                // AND
                // - TargetRole is null (all roles) OR TargetRole matches user's role
                var query = _context.Notifications
                    .Where(n => (n.TargetUser == null || n.TargetUser == username || n.TargetUser == "*") &&
                               (n.TargetRole == null || n.TargetRole == userRole))
                    .Where(n => n.ExpiresAt == null || n.ExpiresAt > DateTime.UtcNow);

                if (!includeRead)
                {
                    query = query.Where(n => !n.IsRead);
                }

                var notifications = await query
                    .OrderByDescending(n => n.CreatedAt)
                    .Take(limit)
                    .ToListAsync();

                var notificationDtos = notifications.Select(n => new NotificationDto
                {
                    Id = n.Id,
                    NotificationType = n.NotificationType,
                    Title = n.Title,
                    Message = n.Message,
                    TargetUser = n.TargetUser,
                    TargetRole = n.TargetRole,
                    CreatedAt = n.CreatedAt,
                    ExpiresAt = n.ExpiresAt,
                    IsRead = n.IsRead,
                    ReadAt = n.ReadAt,
                    AdditionalData = string.IsNullOrEmpty(n.AdditionalDataJson)
                        ? new Dictionary<string, object>()
                        : JsonSerializer.Deserialize<Dictionary<string, object>>(n.AdditionalDataJson)
                          ?? new Dictionary<string, object>()
                }).ToList();

                return Ok(notificationDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting user notifications for {Username}", username);
                return StatusCode(500, "Error retrieving notifications");
            }
        }

        /// <summary>
        /// Get unread notification count for a user
        /// </summary>
        [HttpGet("user/{username}/count")]
        [ResponseCache(Duration = 15)] // 15s server cache acceptable for badge
        public async Task<ActionResult<int>> GetUnreadCount(string username)
        {
            try
            {
                var userRole = User.Claims.FirstOrDefault(c => c.Type == "role")?.Value;

                // ✅ FIX: Use AND logic instead of OR - notification must match both user AND role conditions
                var count = await _context.Notifications
                    .Where(n => !n.IsRead)
                    .Where(n => (n.TargetUser == null || n.TargetUser == username || n.TargetUser == "*") &&
                               (n.TargetRole == null || n.TargetRole == userRole))
                    .Where(n => n.ExpiresAt == null || n.ExpiresAt > DateTime.UtcNow)
                    .CountAsync();

                return Ok(count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting unread count for {Username}", username);
                return StatusCode(500, "Error retrieving unread count");
            }
        }

        /// <summary>
        /// Mark notification as read
        /// </summary>
        [HttpPut("{id}/read")]
        [Authorize] // ✅ Require authentication for write operations
        public async Task<ActionResult> MarkAsRead(int id)
        {
            try
            {
                var notification = await _context.Notifications.AsTracking().FirstOrDefaultAsync(n => n.Id == id);
                if (notification == null)
                {
                    return NotFound($"Notification {id} not found");
                }

                notification.IsRead = true;
                notification.ReadAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Notification marked as read" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification {Id} as read", id);
                return StatusCode(500, "Error marking notification as read");
            }
        }

        /// <summary>
        /// Mark notification as unread
        /// </summary>
        [HttpPut("{id}/unread")]
        [Authorize] // ✅ Require authentication for write operations
        public async Task<ActionResult> MarkAsUnread(int id)
        {
            try
            {
                var notification = await _context.Notifications.AsTracking().FirstOrDefaultAsync(n => n.Id == id);
                if (notification == null)
                {
                    return NotFound($"Notification {id} not found");
                }

                notification.IsRead = false;
                notification.ReadAt = null;
                await _context.SaveChangesAsync();

                return Ok(new { message = "Notification marked as unread" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking notification {Id} as unread", id);
                return StatusCode(500, "Error marking notification as unread");
            }
        }

        /// <summary>
        /// Mark all notifications as read for a user
        /// </summary>
        [HttpPut("user/{username}/read-all")]
        [Authorize] // ✅ Require authentication for write operations
        public async Task<ActionResult> MarkAllAsRead(string username)
        {
            try
            {
                var userRole = User.Claims.FirstOrDefault(c => c.Type == "role")?.Value;

                // ✅ FIX: Use AND logic instead of OR - notification must match both user AND role conditions
                var notifications = await _context.Notifications
                    .AsTracking()
                    .Where(n => !n.IsRead)
                    .Where(n => (n.TargetUser == null || n.TargetUser == username || n.TargetUser == "*") &&
                               (n.TargetRole == null || n.TargetRole == userRole))
                    .Where(n => n.ExpiresAt == null || n.ExpiresAt > DateTime.UtcNow)
                    .ToListAsync();

                foreach (var notification in notifications)
                {
                    notification.IsRead = true;
                    notification.ReadAt = DateTime.UtcNow;
                }

                await _context.SaveChangesAsync();

                return Ok(new { message = $"Marked {notifications.Count} notifications as read" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error marking all notifications as read for {Username}", username);
                return StatusCode(500, "Error marking notifications as read");
            }
        }

        /// <summary>
        /// Create a new notification (Authenticated users can create their own notifications)
        /// </summary>
        [HttpPost]
        [Authorize] // ✅ Changed: Allow any authenticated user to create notifications (for error tracking)
        public async Task<ActionResult<NotificationDto>> CreateNotification([FromBody] CreateNotificationRequest request)
        {
            try
            {
                var notification = new SystemNotification
                {
                    NotificationType = request.NotificationType,
                    Title = request.Title,
                    Message = request.Message,
                    TargetUser = request.TargetUser,
                    TargetRole = request.TargetRole,
                    CreatedAt = DateTime.UtcNow,
                    ExpiresAt = request.ExpiresAt,
                    IsRead = false,
                    AdditionalDataJson = request.AdditionalData != null && request.AdditionalData.Any()
                        ? JsonSerializer.Serialize(request.AdditionalData)
                        : null
                };

                _context.Notifications.Add(notification);
                await _context.SaveChangesAsync();

                var dto = new NotificationDto
                {
                    Id = notification.Id,
                    NotificationType = notification.NotificationType,
                    Title = notification.Title,
                    Message = notification.Message,
                    TargetUser = notification.TargetUser,
                    TargetRole = notification.TargetRole,
                    CreatedAt = notification.CreatedAt,
                    ExpiresAt = notification.ExpiresAt,
                    IsRead = notification.IsRead,
                    AdditionalData = request.AdditionalData ?? new Dictionary<string, object>()
                };

                _logger.LogInformation("Created notification {Id}: {Title}", notification.Id, notification.Title);
                return CreatedAtAction(nameof(GetUserNotifications), new { username = request.TargetUser ?? "*" }, dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating notification");
                return StatusCode(500, "Error creating notification");
            }
        }

        /// <summary>
        /// Delete a notification (users can delete their own notifications)
        /// </summary>
        [HttpDelete("{id}")]
        [Authorize] // ✅ Require authentication for write operations
        public async Task<ActionResult> DeleteNotification(int id)
        {
            try
            {
                var notification = await _context.Notifications.AsTracking().FirstOrDefaultAsync(n => n.Id == id);
                if (notification == null)
                {
                    return NotFound($"Notification {id} not found");
                }

                _context.Notifications.Remove(notification);
                await _context.SaveChangesAsync();

                return Ok(new { message = "Notification deleted successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting notification {Id}", id);
                return StatusCode(500, "Error deleting notification");
            }
        }

        /// <summary>
        /// Clear all read notifications for a user
        /// </summary>
        [HttpDelete("user/{username}/clear-read")]
        [Authorize] // ✅ Require authentication for write operations
        public async Task<ActionResult> ClearReadNotifications(string username)
        {
            try
            {
                var userRole = User.Claims.FirstOrDefault(c => c.Type == "role")?.Value;

                // ✅ FIX: Use AND logic instead of OR - notification must match both user AND role conditions
                var readNotifications = await _context.Notifications
                    .Where(n => n.IsRead)
                    .Where(n => (n.TargetUser == null || n.TargetUser == username || n.TargetUser == "*") &&
                               (n.TargetRole == null || n.TargetRole == userRole))
                    .ToListAsync();

                _context.Notifications.RemoveRange(readNotifications);
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Cleared {readNotifications.Count} read notifications", count = readNotifications.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing read notifications for {Username}", username);
                return StatusCode(500, "Error clearing notifications");
            }
        }

        /// <summary>
        /// Clear ALL notifications for a user (mark as deleted)
        /// </summary>
        [HttpDelete("user/{username}/clear-all")]
        [Authorize] // ✅ Require authentication for write operations
        public async Task<ActionResult> ClearAllNotifications(string username)
        {
            try
            {
                var userRole = User.Claims.FirstOrDefault(c => c.Type == "role")?.Value;

                // ✅ FIX: Use AND logic instead of OR - notification must match both user AND role conditions
                var notifications = await _context.Notifications
                    .Where(n => (n.TargetUser == null || n.TargetUser == username || n.TargetUser == "*") &&
                               (n.TargetRole == null || n.TargetRole == userRole))
                    .Where(n => n.ExpiresAt == null || n.ExpiresAt > DateTime.UtcNow)
                    .ToListAsync();

                _context.Notifications.RemoveRange(notifications);
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Cleared {notifications.Count} notifications", count = notifications.Count });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all notifications for {Username}", username);
                return StatusCode(500, "Error clearing notifications");
            }
        }

        /// <summary>
        /// Delete expired notifications (Admin only)
        /// </summary>
        [HttpDelete("expired")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<ActionResult> DeleteExpiredNotifications()
        {
            try
            {
                var expiredNotifications = await _context.Notifications
                    .Where(n => n.ExpiresAt != null && n.ExpiresAt < DateTime.UtcNow)
                    .ToListAsync();

                _context.Notifications.RemoveRange(expiredNotifications);
                await _context.SaveChangesAsync();

                return Ok(new { message = $"Deleted {expiredNotifications.Count} expired notifications" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting expired notifications");
                return StatusCode(500, "Error deleting expired notifications");
            }
        }
    }

    // DTOs
    public class NotificationDto
    {
        public int Id { get; set; }
        public string NotificationType { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? TargetUser { get; set; }
        public string? TargetRole { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public bool IsRead { get; set; }
        public DateTime? ReadAt { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }

    public class CreateNotificationRequest
    {
        public string NotificationType { get; set; } = "System";
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? TargetUser { get; set; }
        public string? TargetRole { get; set; }
        public DateTime? ExpiresAt { get; set; }
        public Dictionary<string, object>? AdditionalData { get; set; }
    }
}

