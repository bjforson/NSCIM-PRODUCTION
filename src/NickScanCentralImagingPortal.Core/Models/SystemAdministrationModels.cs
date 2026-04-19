using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Models
{
    public class UserSearchCriteria
    {
        public string? SearchTerm { get; set; }
        public string? Role { get; set; }
        public bool? IsActive { get; set; }
        public int Page { get; set; } = 1;
        public int PageSize { get; set; } = 20;
        public string SortBy { get; set; } = "CreatedAt";
        public string SortOrder { get; set; } = "desc";
    }

    public class UserSearchResult
    {
        public IEnumerable<UserDetails> Users { get; set; } = new List<UserDetails>();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasNextPage => Page < TotalPages;
        public bool HasPreviousPage => Page > 1;
    }

    public class UserDetails
    {
        public int Id { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
        public string? Department { get; set; }
        public string? PhoneNumber { get; set; }
        public Dictionary<string, object> AdditionalData { get; set; } = new();
    }

    public class CreateUserRequest
    {
        [Required]
        public string Username { get; set; } = string.Empty;

        [Required]
        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        public string LastName { get; set; } = string.Empty;

        [Required]
        public string Role { get; set; } = string.Empty;

        public int? RoleId { get; set; }

        public string? Department { get; set; }

        public string? PhoneNumber { get; set; }

        [Required]
        [MinLength(6, ErrorMessage = "Password must be at least 6 characters")]
        public string Password { get; set; } = string.Empty;

        public bool IsActive { get; set; } = true;

        public string? CreatedBy { get; set; }
    }

    public class UpdateUserRequest
    {
        public string? Username { get; set; }
        public string? Email { get; set; }
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Role { get; set; }
        public int? RoleId { get; set; }
        public string? Department { get; set; }
        public string? PhoneNumber { get; set; }
        public bool? IsActive { get; set; }
        public string? UpdatedBy { get; set; }
    }

    public class SystemStatistics
    {
        public int TotalUsers { get; set; }
        public int ActiveUsers { get; set; }
        public int InactiveUsers { get; set; }
        public Dictionary<string, int> RoleBreakdown { get; set; } = new();
        public Dictionary<string, int> DepartmentBreakdown { get; set; } = new();
        public int UsersToday { get; set; }
        public int UsersThisWeek { get; set; }
        public int UsersThisMonth { get; set; }
        public DateTime StatisticsDate { get; set; } = DateTime.UtcNow;
    }

    public class SystemConfiguration
    {
        public string SystemName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string Environment { get; set; } = string.Empty;
        public bool MaintenanceMode { get; set; }
        public int SessionTimeout { get; set; } = 30; // minutes
        public int MaxLoginAttempts { get; set; } = 5;
        public bool RequirePasswordChange { get; set; } = true;
        public int PasswordExpiryDays { get; set; } = 90;
        public bool EnableAuditLogging { get; set; } = true;
        public int AuditLogRetentionDays { get; set; } = 365;
        public Dictionary<string, object> AdditionalSettings { get; set; } = new();
    }

    public class SystemConfigurationUpdate
    {
        public string? SystemName { get; set; }
        public bool? MaintenanceMode { get; set; }
        public int? SessionTimeout { get; set; }
        public int? MaxLoginAttempts { get; set; }
        public bool? RequirePasswordChange { get; set; }
        public int? PasswordExpiryDays { get; set; }
        public bool? EnableAuditLogging { get; set; }
        public int? AuditLogRetentionDays { get; set; }
        public Dictionary<string, object> AdditionalSettings { get; set; } = new();
        public string? UpdatedBy { get; set; }
    }

    public class AuditLogEntry
    {
        public int Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string Username { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string Resource { get; set; } = string.Empty;
        public string? ResourceId { get; set; }
        public string? Details { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public string UserAgent { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Status { get; set; } = string.Empty; // Success, Failed, Warning
    }

    public class SystemHealthStatus
    {
        public string OverallStatus { get; set; } = string.Empty; // Healthy, Warning, Critical
        public DateTime CheckedAt { get; set; } = DateTime.UtcNow;
        public List<HealthCheck> HealthChecks { get; set; } = new();
        public Dictionary<string, object> SystemMetrics { get; set; } = new();
    }

    public class HealthCheck
    {
        public string Name { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // Healthy, Warning, Critical
        public string Description { get; set; } = string.Empty;
        public double ResponseTime { get; set; } // in milliseconds
        public string? ErrorMessage { get; set; }
        public DateTime LastChecked { get; set; }
    }

    public class SystemPerformanceMetrics
    {
        public double CpuUsage { get; set; } // percentage
        public double MemoryUsage { get; set; } // percentage
        public double DiskUsage { get; set; } // percentage
        public double NetworkLatency { get; set; } // milliseconds
        public int ActiveConnections { get; set; }
        public int TotalRequests { get; set; }
        public double AverageResponseTime { get; set; } // milliseconds
        public int ErrorRate { get; set; } // percentage
        public DateTime MetricsDate { get; set; } = DateTime.UtcNow;
    }

    public class PasswordResetRequest
    {
        public string? NewPassword { get; set; }
        public bool GenerateRandomPassword { get; set; } = true;
        public bool RequirePasswordChangeOnLogin { get; set; } = true;
        public string? ResetBy { get; set; }
    }

    public class PasswordResetResult
    {
        public bool Success { get; set; }
        public string? NewPassword { get; set; }
        public string? Message { get; set; }
        public DateTime ResetAt { get; set; } = DateTime.UtcNow;
    }

    public class UserActivityLog
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string Action { get; set; } = string.Empty;
        public string Resource { get; set; } = string.Empty;
        public string? Details { get; set; }
        public string IpAddress { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
        public string Status { get; set; } = string.Empty;
    }

    public class SystemDataExportRequest
    {
        public string DataType { get; set; } = string.Empty; // Users, AuditLogs, SystemLogs
        public DateTime? FromDate { get; set; }
        public DateTime? ToDate { get; set; }
        public string? UserId { get; set; }
        public string? Role { get; set; }
        public List<string> Fields { get; set; } = new();
    }

    public class SystemExportData
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? LastLoginAt { get; set; }
    }

    public class SystemBackupRequest
    {
        public string BackupType { get; set; } = string.Empty; // Full, Incremental, Differential
        public bool IncludeUserData { get; set; } = true;
        public bool IncludeSystemLogs { get; set; } = true;
        public bool IncludeConfiguration { get; set; } = true;
        public string? BackupLocation { get; set; }
        public string? CreatedBy { get; set; }
    }

    public class SystemBackupResult
    {
        public bool Success { get; set; }
        public string BackupId { get; set; } = string.Empty;
        public string BackupLocation { get; set; } = string.Empty;
        public long BackupSize { get; set; } // in bytes
        public DateTime BackupDate { get; set; } = DateTime.UtcNow;
        public string? ErrorMessage { get; set; }
    }

    public class SystemMaintenanceRequest
    {
        public string MaintenanceType { get; set; } = string.Empty; // Scheduled, Emergency, Update
        public DateTime StartTime { get; set; }
        public DateTime EndTime { get; set; }
        public string Description { get; set; } = string.Empty;
        public bool NotifyUsers { get; set; } = true;
        public string? RequestedBy { get; set; }
    }

    public class SystemMaintenanceResult
    {
        public bool Success { get; set; }
        public string MaintenanceId { get; set; } = string.Empty;
        public string Status { get; set; } = string.Empty; // Scheduled, InProgress, Completed, Cancelled
        public string? Message { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    public class SystemAlert
    {
        public int Id { get; set; }
        public string AlertType { get; set; } = string.Empty; // Info, Warning, Error, Critical
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? Resource { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime? AcknowledgedAt { get; set; }
        public string? AcknowledgedBy { get; set; }
        public bool IsActive { get; set; } = true;
    }

    public class SystemNotification
    {
        public int Id { get; set; }
        public string NotificationType { get; set; } = string.Empty; // System, User, Maintenance, Alert
        public string Title { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public string? TargetUser { get; set; }
        public string? TargetRole { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime? ExpiresAt { get; set; }
        public bool IsRead { get; set; } = false;
        public DateTime? ReadAt { get; set; }
        public string? AdditionalDataJson { get; set; } // JSON serialized additional data
    }
}
