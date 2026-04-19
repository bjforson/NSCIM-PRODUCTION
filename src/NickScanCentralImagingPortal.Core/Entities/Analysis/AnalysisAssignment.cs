using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities.Analysis
{
    [Table("AnalysisAssignments")]
    public class AnalysisAssignment
    {
        [Key]
        public int Id { get; set; }

        [Required]
        public Guid GroupId { get; set; }

        [Required]
        [StringLength(100)]
        public string AssignedTo { get; set; } = string.Empty;

        [Required]
        [StringLength(20)]
        public string Role { get; set; } = "Analyst"; // Analyst | Audit

        public DateTime? LeaseUntilUtc { get; set; }

        [Required]
        [StringLength(20)]
        public string State { get; set; } = "Active"; // Active | Released | Expired

        public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
        public DateTime? UpdatedAtUtc { get; set; }

        /// <summary>
        /// Last time this assignment was accessed (used to prevent expiration during active work sessions)
        /// Updated whenever user calls GetMyAssignments API
        /// </summary>
        public DateTime? LastAccessedAtUtc { get; set; }
    }
}


