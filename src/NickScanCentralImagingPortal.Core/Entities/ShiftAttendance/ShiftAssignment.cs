using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.ShiftAttendance
{
    /// <summary>
    /// Actual scheduled shifts for employees
    /// </summary>
    [Table("ShiftAssignments")]
    public class ShiftAssignment
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid EmployeeId { get; set; }

        public Guid? PositionId { get; set; }

        [Required]
        public Guid SiteId { get; set; }

        public Guid? LaneId { get; set; }

        [Required]
        public Guid ShiftTemplateId { get; set; }

        [Required]
        public DateTime Date { get; set; }

        public DateTime? ActualStartTime { get; set; }

        public DateTime? ActualEndTime { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "SCHEDULED"; // SCHEDULED, CONFIRMED, IN_PROGRESS, COMPLETED, CANCELLED, NO_SHOW

        [StringLength(20)]
        public string ShiftType { get; set; } = "REGULAR"; // REGULAR, OVERTIME, ON_CALL, STANDBY

        [StringLength(1000)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [StringLength(100)]
        public string? UpdatedBy { get; set; }

        // Navigation properties
        [ForeignKey("EmployeeId")]
        public virtual HR.Employee Employee { get; set; } = null!;

        [ForeignKey("PositionId")]
        public virtual HR.Position? Position { get; set; }

        [ForeignKey("SiteId")]
        public virtual HR.Site Site { get; set; } = null!;

        [ForeignKey("LaneId")]
        public virtual HR.Lane? Lane { get; set; }

        [ForeignKey("ShiftTemplateId")]
        public virtual ShiftTemplate ShiftTemplate { get; set; } = null!;

        public virtual ICollection<AttendanceRecord> AttendanceRecords { get; set; } = new List<AttendanceRecord>();
    }
}

