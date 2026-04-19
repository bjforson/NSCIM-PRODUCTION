using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.ShiftAttendance
{
    /// <summary>
    /// Leave requests that affect shift planning
    /// </summary>
    [Table("LeaveRequests")]
    public class LeaveRequest
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        public Guid EmployeeId { get; set; }

        [Required]
        [StringLength(20)]
        public string LeaveType { get; set; } = string.Empty; // ANNUAL, SICK, EMERGENCY, MATERNITY, PATERNITY, UNPAID

        [Required]
        public DateTime StartDate { get; set; }

        [Required]
        public DateTime EndDate { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "PENDING"; // PENDING, APPROVED, REJECTED, CANCELLED

        [Required]
        [StringLength(100)]
        public string RequestedBy { get; set; } = string.Empty;

        [StringLength(100)]
        public string? ApprovedBy { get; set; }

        public DateTime? ApprovedAt { get; set; }

        [StringLength(1000)]
        public string? RejectionReason { get; set; }

        [StringLength(1000)]
        public string? Remarks { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        // Navigation properties
        [ForeignKey("EmployeeId")]
        public virtual HR.Employee Employee { get; set; } = null!;
    }
}

