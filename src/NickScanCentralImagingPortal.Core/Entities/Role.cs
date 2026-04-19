using System.ComponentModel.DataAnnotations;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Represents a role that can be assigned to users
    /// </summary>
    public class Role
    {
        public int Id { get; set; }

        [Required]
        [StringLength(100)]
        public string Name { get; set; } = string.Empty; // e.g., "Customs Officer"

        [Required]
        [StringLength(200)]
        public string DisplayName { get; set; } = string.Empty;

        [StringLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// Maps to the legacy UserRole enum for backward compatibility
        /// </summary>
        public UserRole? BaseRole { get; set; }

        /// <summary>
        /// System roles cannot be deleted (e.g., SuperAdmin, Admin)
        /// </summary>
        public bool IsSystemRole { get; set; } = false;

        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string? CreatedBy { get; set; }

        public DateTime? UpdatedAt { get; set; }

        [StringLength(50)]
        public string? UpdatedBy { get; set; }

        // Navigation properties
        public virtual ICollection<RolePermission> RolePermissions { get; set; } = new List<RolePermission>();
        public virtual ICollection<User> Users { get; set; } = new List<User>();
    }
}

