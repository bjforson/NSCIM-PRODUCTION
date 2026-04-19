using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Audit trail for system settings changes
    /// Tracks who changed what and when
    /// </summary>
    [Table("SettingsHistory")]
    public class SettingsHistory
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Reference to the system setting that was changed
        /// </summary>
        [Required]
        public int SystemSettingId { get; set; }

        /// <summary>
        /// Category of the setting
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Key of the setting that was changed
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string SettingKey { get; set; } = string.Empty;

        /// <summary>
        /// Previous value before the change
        /// </summary>
        public string? OldValue { get; set; }

        /// <summary>
        /// New value after the change
        /// </summary>
        [Required]
        public string NewValue { get; set; } = string.Empty;

        /// <summary>
        /// Username of the person who made the change
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string ChangedBy { get; set; } = string.Empty;

        /// <summary>
        /// Reason for the change (optional)
        /// </summary>
        [MaxLength(500)]
        public string? Reason { get; set; }

        /// <summary>
        /// IP address of the user who made the change
        /// </summary>
        [MaxLength(50)]
        public string? IpAddress { get; set; }

        /// <summary>
        /// When the change was made
        /// </summary>
        public DateTime ChangedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        [ForeignKey("SystemSettingId")]
        public virtual SystemSetting? SystemSetting { get; set; }
    }
}

