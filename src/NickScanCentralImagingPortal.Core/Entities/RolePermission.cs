using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Links roles to their permissions (Many-to-Many)
    /// </summary>
    public class RolePermission
    {
        public int Id { get; set; }

        public int RoleId { get; set; }

        public int PermissionId { get; set; }

        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string? GrantedBy { get; set; }

        // Navigation properties
        public virtual Role Role { get; set; } = null!;
        public virtual Permission Permission { get; set; } = null!;
    }
}

