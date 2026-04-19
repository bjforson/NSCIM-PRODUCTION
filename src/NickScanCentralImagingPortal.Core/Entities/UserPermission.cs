using System.ComponentModel.DataAnnotations;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// User-specific permission overrides (grants or revokes)
    /// </summary>
    public class UserPermission
    {
        public int Id { get; set; }

        public int UserId { get; set; }

        public int PermissionId { get; set; }

        /// <summary>
        /// TRUE = permission granted to user
        /// FALSE = permission revoked from user (even if role has it)
        /// </summary>
        public bool IsGranted { get; set; } = true;

        public DateTime GrantedAt { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string? GrantedBy { get; set; }

        /// <summary>
        /// Optional: Permission expires at this time
        /// </summary>
        public DateTime? ExpiresAt { get; set; }

        [StringLength(500)]
        public string? Reason { get; set; }

        // Navigation properties
        public virtual User User { get; set; } = null!;
        public virtual Permission Permission { get; set; } = null!;
    }
}

