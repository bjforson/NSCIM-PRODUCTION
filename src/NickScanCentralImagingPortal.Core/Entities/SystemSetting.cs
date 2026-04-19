using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// System-wide configuration settings stored in database
    /// Supports key-value pairs with encryption for sensitive data
    /// </summary>
    [Table("SystemSettings")]
    public class SystemSetting
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Category of the setting (e.g., "ICUMS", "Email", "Security")
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Category { get; set; } = string.Empty;

        /// <summary>
        /// Unique key for the setting (e.g., "ICUMS.BaseUrl", "Email.SmtpServer")
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string SettingKey { get; set; } = string.Empty;

        /// <summary>
        /// Value of the setting (can be JSON for complex types)
        /// </summary>
        [Required]
        public string SettingValue { get; set; } = string.Empty;

        /// <summary>
        /// Data type of the setting value
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string DataType { get; set; } = "string"; // string, int, bool, decimal, json, array

        /// <summary>
        /// Human-readable description of what this setting does
        /// </summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        /// <summary>
        /// Default value for the setting
        /// </summary>
        [MaxLength(1000)]
        public string? DefaultValue { get; set; }

        /// <summary>
        /// Is this value encrypted in the database?
        /// </summary>
        public bool IsEncrypted { get; set; } = false;

        /// <summary>
        /// Does changing this setting require application restart?
        /// </summary>
        public bool RequiresRestart { get; set; } = false;

        /// <summary>
        /// Comma-separated list of roles that can edit this setting
        /// Null or empty = all admins can edit
        /// </summary>
        [MaxLength(200)]
        public string? AllowedRoles { get; set; }

        /// <summary>
        /// Is this setting currently active/enabled?
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>
        /// Display order within the category
        /// </summary>
        public int DisplayOrder { get; set; } = 0;

        /// <summary>
        /// Validation rules (JSON format)
        /// e.g., {"min": 1, "max": 100, "pattern": "^[0-9]+$"}
        /// </summary>
        public string? ValidationRules { get; set; }

        /// <summary>
        /// Username of the person who last modified this setting
        /// </summary>
        [MaxLength(100)]
        public string? LastModifiedBy { get; set; }

        /// <summary>
        /// When was this setting last modified
        /// </summary>
        public DateTime? LastModifiedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property
        public virtual ICollection<SettingsHistory> History { get; set; } = new List<SettingsHistory>();
    }
}

