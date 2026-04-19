using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.ShiftAttendance
{
    /// <summary>
    /// Employee-initiated shift exchange requests
    /// </summary>
    [Table("ShiftSwapRequests")]
    public class ShiftSwapRequest
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid RequestingEmployeeId { get; set; }

        [Required]
        public Guid RequestingShiftAssignmentId { get; set; }

        public Guid? RequestedEmployeeId { get; set; } // NULL = open to anyone

        public Guid? RequestedShiftAssignmentId { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, REJECTED, CANCELLED

        public DateTime RequestedAt { get; set; } = DateTime.UtcNow;

        [StringLength(100)]
        public string? ApprovedBy { get; set; }

        public DateTime? ApprovedAt { get; set; }

        [StringLength(1000)]
        public string? RejectionReason { get; set; }

        [StringLength(1000)]
        public string? Remarks { get; set; }

        // Navigation properties
        [ForeignKey("RequestingEmployeeId")]
        public virtual HR.Employee RequestingEmployee { get; set; } = null!;

        [ForeignKey("RequestingShiftAssignmentId")]
        public virtual ShiftAssignment RequestingShiftAssignment { get; set; } = null!;

        [ForeignKey("RequestedEmployeeId")]
        public virtual HR.Employee? RequestedEmployee { get; set; }

        [ForeignKey("RequestedShiftAssignmentId")]
        public virtual ShiftAssignment? RequestedShiftAssignment { get; set; }
    }
}

