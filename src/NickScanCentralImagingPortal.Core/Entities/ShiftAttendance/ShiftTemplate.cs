using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.ShiftAttendance
{
    /// <summary>
    /// Reusable shift patterns (e.g., "Day Shift 06:00-14:00")
    /// </summary>
    [Table("ShiftTemplates")]
    public class ShiftTemplate
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string Code { get; set; } = string.Empty;

        [Required]
        [StringLength(200)]
        public string Name { get; set; } = string.Empty;

        [Required]
        public TimeSpan StartTime { get; set; }

        [Required]
        public TimeSpan EndTime { get; set; }

        public decimal DurationHours { get; set; }

        public bool IsNightShift { get; set; } = false;

        [StringLength(2000)]
        public string? BreakRules { get; set; } // JSON: { "breaks": [...], "totalBreakMinutes": ... }

        public Guid? SiteId { get; set; } // NULL = global template

        [StringLength(20)]
        public string Status { get; set; } = "ACTIVE";

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [StringLength(100)]
        public string? UpdatedBy { get; set; }

        // Navigation properties
        [ForeignKey("SiteId")]
        public virtual HR.Site? Site { get; set; }

        public virtual ICollection<ShiftAssignment> ShiftAssignments { get; set; } = new List<ShiftAssignment>();
    }
}

