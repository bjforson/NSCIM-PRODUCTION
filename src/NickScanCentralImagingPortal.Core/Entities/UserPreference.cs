using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// User-specific preferences and customizations
    /// Allows each user to personalize their experience
    /// </summary>
    [Table("UserPreferences")]
    public class UserPreference
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// Reference to the user this preference belongs to
        /// </summary>
        [Required]
        public int UserId { get; set; }

        /// <summary>
        /// Unique key for the preference (e.g., "UI.DarkMode", "Dashboard.DefaultView")
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string PreferenceKey { get; set; } = string.Empty;

        /// <summary>
        /// Value of the preference (can be JSON for complex types)
        /// </summary>
        [Required]
        public string PreferenceValue { get; set; } = string.Empty;

        /// <summary>
        /// Data type of the preference value
        /// </summary>
        [Required]
        [MaxLength(20)]
        public string DataType { get; set; } = "string"; // string, int, bool, json

        /// <summary>
        /// Description of what this preference controls
        /// </summary>
        [MaxLength(500)]
        public string? Description { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        // Navigation property to User (if User entity exists)
        // [ForeignKey("UserId")]
        // public virtual User? User { get; set; }
    }
}

