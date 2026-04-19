using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.HR
{
    /// <summary>
    /// Represents an employee in the system
    /// </summary>
    [Table("Employees")]
    public class Employee
    {
        [Key]
        public Guid Id { get; set; } = Guid.NewGuid();

        [Required]
        [StringLength(50)]
        public string EmployeeNumber { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required]
        [StringLength(100)]
        public string LastName { get; set; } = string.Empty;

        [StringLength(100)]
        public string? OtherNames { get; set; }

        public DateTime? DateOfBirth { get; set; }

        [StringLength(20)]
        public string? Gender { get; set; }

        [StringLength(50)]
        public string? NationalId { get; set; }

        [StringLength(100)]
        public string? Email { get; set; }

        [StringLength(20)]
        public string? Phone { get; set; }

        [Required]
        public Guid OrganizationId { get; set; }

        public Guid? PrimarySiteId { get; set; }

        [StringLength(20)]
        public string Status { get; set; } = "ACTIVE"; // ACTIVE, ON_LEAVE, SUSPENDED, INACTIVE

        public DateTime HireDate { get; set; }

        public DateTime? TerminationDate { get; set; }

        [StringLength(20)]
        public string EmploymentType { get; set; } = "PERMANENT"; // PERMANENT, CONTRACT, CONSULTANT, SECONDMENT

        [StringLength(1000)]
        public string? Notes { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        public DateTime? UpdatedAt { get; set; }

        [StringLength(100)]
        public string? CreatedBy { get; set; }

        [StringLength(100)]
        public string? UpdatedBy { get; set; }

        // Navigation properties
        [ForeignKey("OrganizationId")]
        public virtual Organization Organization { get; set; } = null!;

        [ForeignKey("PrimarySiteId")]
        public virtual Site? PrimarySite { get; set; }

        public virtual ICollection<EmployeePosition> EmployeePositions { get; set; } = new List<EmployeePosition>();
    }
}

