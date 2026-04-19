using System.ComponentModel.DataAnnotations;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Audit log for all permission-related actions
    /// </summary>
    public class PermissionAuditLog
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Action { get; set; } = string.Empty; // GRANT, REVOKE, CHECK, ROLE_ASSIGN, etc.

        [Required]
        [StringLength(50)]
        public string EntityType { get; set; } = string.Empty; // User, Role

        public int EntityId { get; set; }

        public int? PermissionId { get; set; }

        public int? RoleId { get; set; }

        public int? UserId { get; set; } // Who performed the action

        public bool? Result { get; set; } // TRUE = allowed, FALSE = denied (for CHECK actions)

        [StringLength(50)]
        public string? IPAddress { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [StringLength(4000)]
        public string? Details { get; set; } // JSON or text details

        // Navigation properties
        public virtual Permission? Permission { get; set; }
        public virtual Role? Role { get; set; }
        public virtual User? User { get; set; }
    }
}

