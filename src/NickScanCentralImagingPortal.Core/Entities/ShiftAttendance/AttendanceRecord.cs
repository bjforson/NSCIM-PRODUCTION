using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.ShiftAttendance
{
    /// <summary>
    /// Check-in/out tracking for employees
    /// </summary>
    [Table("AttendanceRecords")]
    public class AttendanceRecord
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        public Guid? ShiftAssignmentId { get; set; }

        [Required]
        public Guid EmployeeId { get; set; }

        [Required]
        public Guid SiteId { get; set; }

        [Required]
        public DateTime Date { get; set; }

        public DateTime? CheckInTime { get; set; }

        public DateTime? CheckOutTime { get; set; }

        [Required]
        [StringLength(20)]
        public string Source { get; set; } = "MANUAL"; // MANUAL, DEVICE, MOBILE_APP, BIOMETRIC

        [StringLength(20)]
        public string Status { get; set; } = "PRESENT"; // PRESENT, LATE, ABSENT, EARLY_LEAVE, PARTIAL

        public int? LateMinutes { get; set; }

        public int? EarlyLeaveMinutes { get; set; }

        public int? OvertimeMinutes { get; set; }

        [StringLength(1000)]
        public string? Remarks { get; set; }

        [StringLength(100)]
        public string? ApprovedBy { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("ShiftAssignmentId")]
        public virtual ShiftAssignment? ShiftAssignment { get; set; }

        [ForeignKey("EmployeeId")]
        public virtual HR.Employee Employee { get; set; } = null!;

        [ForeignKey("SiteId")]
        public virtual HR.Site Site { get; set; } = null!;
    }
}

