using System.ComponentModel.DataAnnotations;
using NickScanCentralImagingPortal.Core.Entities;

namespace NickScanCentralImagingPortal.Core.Models
{
    public class User
    {
        public int Id { get; set; }

        /// <summary>
        /// Unique user number for anonymized reporting (e.g., USR-00001)
        /// Generated automatically on user creation
        /// </summary>
        [StringLength(20)]
        public string? UserNumber { get; set; }

        [Required]
        [StringLength(50)]
        public string Username { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string Email { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string PasswordHash { get; set; } = string.Empty;

        [StringLength(50)]
        public string FirstName { get; set; } = string.Empty;

        [StringLength(50)]
        public string LastName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? Department { get; set; }

        [StringLength(20)]
        public string? PhoneNumber { get; set; }

        /// <summary>
        /// Primary role assignment (new flexible system)
        /// </summary>
        public int? RoleId { get; set; }

        /// <summary>
        /// Legacy role (kept for backward compatibility)
        /// </summary>
        public UserRole LegacyRole { get; set; } = UserRole.Viewer;

        /// <summary>
        /// For backward compatibility - returns LegacyRole or role from RoleId
        /// </summary>
        public UserRole Role
        {
            get => RoleId.HasValue && AssignedRole != null ? (AssignedRole.BaseRole ?? LegacyRole) : LegacyRole;
            set => LegacyRole = value;
        }

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? LastLoginAt { get; set; }

        public string? CreatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        public string? UpdatedBy { get; set; }

        /// <summary>
        /// Single-session enforcement (added 2026-04-25). Rotated on every
        /// successful login, logout, password change, and force-deactivation.
        /// Embedded as the <c>sid</c> claim in the JWT; JwtBearerEvents.OnTokenValidated
        /// rejects tokens whose <c>sid</c> doesn't match this column. Effect: when
        /// the same user logs in on Device 2, Device 1's token immediately becomes
        /// invalid the next time it's used.
        /// </summary>
        public Guid CurrentSessionId { get; set; } = Guid.NewGuid();

        // Navigation properties
        public virtual Role? AssignedRole { get; set; }
        public virtual ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
    }

    public enum UserRole
    {
        // Level 0: Read-Only Access
        Viewer = 0,           // Basic read-only access

        // Level 1: Operational Access
        Operator = 1,         // Can perform basic operations
        ScannerOperator = 2,  // Scanner-specific operations

        // Level 2: Management Access
        Supervisor = 3,       // Team management capabilities
        Manager = 4,          // Department-level access

        // Level 3: Administrative Access
        Admin = 5,            // System administration
        SuperAdmin = 6        // Full system access
    }
}
