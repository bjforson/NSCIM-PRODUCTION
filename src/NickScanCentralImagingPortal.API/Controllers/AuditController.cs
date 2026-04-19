using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [Authorize(Policy = "AdminOnly")]
    [ApiController]
    [Route("api/[controller]")]
    public class AuditController : ControllerBase
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<AuditController> _logger;

        public AuditController(
            ApplicationDbContext context,
            ILogger<AuditController> logger)
        {
            _context = context;
            _logger = logger;
        }

        /// <summary>
        /// Get audit logs with optional filtering
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<AuditLogDto>>> GetAuditLogs(
            [FromQuery] string? eventType = null,
            [FromQuery] string? severity = null,
            [FromQuery] string? username = null,
            [FromQuery] int limit = 100,
            [FromQuery] int skip = 0)
        {
            try
            {
                var query = _context.AuditLogs.AsQueryable();

                // Apply filters
                if (!string.IsNullOrEmpty(eventType) && eventType != "All")
                {
                    query = query.Where(a => a.EventType == eventType);
                }

                if (!string.IsNullOrEmpty(severity) && severity != "All")
                {
                    query = query.Where(a => a.Severity == severity);
                }

                if (!string.IsNullOrEmpty(username))
                {
                    query = query.Where(a => a.Username != null && a.Username.Contains(username));
                }

                var auditLogs = await query
                    .OrderByDescending(a => a.Timestamp)
                    .Skip(skip)
                    .Take(limit)
                    .ToListAsync();

                var auditLogDtos = auditLogs.Select(a => new AuditLogDto
                {
                    Id = a.Id,
                    Timestamp = a.Timestamp,
                    User = a.Username ?? "System",
                    EventType = a.EventType,
                    Action = a.Action,
                    Description = a.Description ?? "",
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Severity = a.Severity,
                    IpAddress = a.IpAddress ?? "N/A",
                    Success = a.Success
                }).ToList();

                _logger.LogDebug("Retrieved {Count} audit logs", auditLogDtos.Count);
                return Ok(auditLogDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving audit logs");
                return StatusCode(500, "Error retrieving audit logs");
            }
        }

        /// <summary>
        /// Get audit logs for a specific user
        /// </summary>
        [HttpGet("user/{username}")]
        public async Task<ActionResult<List<AuditLogDto>>> GetUserAuditLogs(
            string username,
            [FromQuery] int limit = 50)
        {
            try
            {
                var auditLogs = await _context.AuditLogs
                    .Where(a => a.Username == username)
                    .OrderByDescending(a => a.Timestamp)
                    .Take(limit)
                    .ToListAsync();

                var auditLogDtos = auditLogs.Select(a => new AuditLogDto
                {
                    Id = a.Id,
                    Timestamp = a.Timestamp,
                    User = a.Username ?? "System",
                    EventType = a.EventType,
                    Action = a.Action,
                    Description = a.Description ?? "",
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Severity = a.Severity,
                    IpAddress = a.IpAddress ?? "N/A",
                    Success = a.Success
                }).ToList();

                return Ok(auditLogDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving user audit logs for {Username}", username);
                return StatusCode(500, "Error retrieving user audit logs");
            }
        }

        /// <summary>
        /// Get audit logs for a specific entity
        /// </summary>
        [HttpGet("entity/{entityType}/{entityId}")]
        public async Task<ActionResult<List<AuditLogDto>>> GetEntityAuditLogs(
            string entityType,
            string entityId,
            [FromQuery] int limit = 50)
        {
            try
            {
                var auditLogs = await _context.AuditLogs
                    .Where(a => a.EntityType == entityType && a.EntityId == entityId)
                    .OrderByDescending(a => a.Timestamp)
                    .Take(limit)
                    .ToListAsync();

                var auditLogDtos = auditLogs.Select(a => new AuditLogDto
                {
                    Id = a.Id,
                    Timestamp = a.Timestamp,
                    User = a.Username ?? "System",
                    EventType = a.EventType,
                    Action = a.Action,
                    Description = a.Description ?? "",
                    EntityType = a.EntityType,
                    EntityId = a.EntityId,
                    Severity = a.Severity,
                    IpAddress = a.IpAddress ?? "N/A",
                    Success = a.Success
                }).ToList();

                return Ok(auditLogDtos);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving entity audit logs for {EntityType}/{EntityId}", entityType, entityId);
                return StatusCode(500, "Error retrieving entity audit logs");
            }
        }

        /// <summary>
        /// Get audit statistics
        /// </summary>
        [HttpGet("statistics")]
        public async Task<ActionResult<AuditStatisticsDto>> GetStatistics([FromQuery] int days = 7)
        {
            try
            {
                var since = DateTime.UtcNow.AddDays(-days);

                var totalEvents = await _context.AuditLogs.CountAsync(a => a.Timestamp >= since);
                var failedEvents = await _context.AuditLogs.CountAsync(a => a.Timestamp >= since && !a.Success);
                var criticalEvents = await _context.AuditLogs.CountAsync(a => a.Timestamp >= since && a.Severity == "Critical");

                var topUsers = await _context.AuditLogs
                    .Where(a => a.Timestamp >= since && a.Username != null)
                    .GroupBy(a => a.Username)
                    .Select(g => new { Username = g.Key, Count = g.Count() })
                    .OrderByDescending(x => x.Count)
                    .Take(5)
                    .ToListAsync();

                var eventsByType = await _context.AuditLogs
                    .Where(a => a.Timestamp >= since)
                    .GroupBy(a => a.EventType)
                    .Select(g => new { EventType = g.Key, Count = g.Count() })
                    .ToListAsync();

                var statistics = new AuditStatisticsDto
                {
                    TotalEvents = totalEvents,
                    FailedEvents = failedEvents,
                    CriticalEvents = criticalEvents,
                    TopUsers = topUsers.ToDictionary(x => x.Username!, x => x.Count),
                    EventsByType = eventsByType.ToDictionary(x => x.EventType, x => x.Count),
                    Days = days
                };

                return Ok(statistics);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving audit statistics");
                return StatusCode(500, "Error retrieving audit statistics");
            }
        }
    }

    // DTOs
    public class AuditLogDto
    {
        public int Id { get; set; }
        public DateTime Timestamp { get; set; }
        public string User { get; set; } = string.Empty;
        public string EventType { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string? EntityType { get; set; }
        public string? EntityId { get; set; }
        public string Severity { get; set; } = "Info";
        public string IpAddress { get; set; } = string.Empty;
        public bool Success { get; set; }
    }

    public class AuditStatisticsDto
    {
        public int TotalEvents { get; set; }
        public int FailedEvents { get; set; }
        public int CriticalEvents { get; set; }
        public Dictionary<string, int> TopUsers { get; set; } = new();
        public Dictionary<string, int> EventsByType { get; set; } = new();
        public int Days { get; set; }
    }
}

