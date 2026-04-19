using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    /// <summary>
    /// Tracks whether users are ready and available for assignment
    /// Hybrid approach: SignalR provides real-time updates, database provides persistence
    /// </summary>
    [Table("UserReadiness")]
    public class UserReadiness
    {
        [Key]
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string Username { get; set; } = "";

        [Required]
        [StringLength(20)]
        public string Role { get; set; } = ""; // "Analyst" or "Audit"

        /// <summary>
        /// Whether the user is currently ready to receive new assignments
        /// </summary>
        public bool IsReady { get; set; }

        /// <summary>
        /// Last heartbeat/ping from the user (indicates they're still active)
        /// </summary>
        public DateTime LastHeartbeat { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// When the readiness state last changed (ready ↔ not ready)
        /// </summary>
        public DateTime LastChangedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Username of who changed the readiness state (usually the user themselves)
        /// </summary>
        [StringLength(50)]
        public string? ChangedBy { get; set; }

        /// <summary>
        /// Optional: Browser session ID for tracking
        /// </summary>
        [StringLength(100)]
        public string? SessionId { get; set; }
    }
}

