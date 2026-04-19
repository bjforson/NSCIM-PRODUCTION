using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Represents a granular permission in the system
    /// </summary>
    public class Permission
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty; // e.g., "containers.approve"

        [Required]
        [StringLength(200)]
        public string DisplayName { get; set; } = string.Empty; // e.g., "Approve Containers"

        [StringLength(500)]
        public string? Description { get; set; }

        [StringLength(50)]
        public string? Category { get; set; } // e.g., "Containers", "System", "Users"

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // Navigation properties
        public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
        public virtual ICollection<UserPermission> UserPermissions { get; set; } = new List<UserPermission>();
    }
}

