using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Models
{
    public class AuditLog
    {
        public int Id { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        public int? UserId { get; set; }

        [StringLength(100)]
        public string? Username { get; set; }

        [Required]
        [StringLength(50)]
        public string EventType { get; set; } = string.Empty; // Login, Logout, DataModification, PermissionChange, ConfigChange, etc.

        [Required]
        [StringLength(100)]
        public string Action { get; set; } = string.Empty; // Created, Updated, Deleted, Accessed, etc.

        public string? Description { get; set; }

        [StringLength(50)]
        public string? EntityType { get; set; } // User, Role, Container, Scan, etc.

        [StringLength(100)]
        public string? EntityId { get; set; }

        [Required]
        [StringLength(20)]
        public string Severity { get; set; } = "Info"; // Info, Warning, Error, Critical

        [StringLength(45)]
        public string? IpAddress { get; set; }

        [StringLength(500)]
        public string? UserAgent { get; set; }

        public string? OldValue { get; set; }

        public string? NewValue { get; set; }

        public bool Success { get; set; } = true;

        public string? AdditionalDataJson { get; set; }
    }
}

