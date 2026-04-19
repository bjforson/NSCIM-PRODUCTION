using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Entity for tracking API endpoint usage for monitoring Phase 3 and Phase 4 migrations
    /// </summary>
    [Table("EndpointUsageLog")]
    public class EndpointUsageLog
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public long Id { get; set; }

        [Required]
        [MaxLength(500)]
        public string Endpoint { get; set; } = string.Empty;

        [Required]
        [MaxLength(10)]
        public string Method { get; set; } = string.Empty;

        [Required]
        public int StatusCode { get; set; }

        [Required]
        public double ResponseTimeMs { get; set; }

        [MaxLength(50)]
        public string? IpAddress { get; set; }

        [MaxLength(500)]
        public string? UserAgent { get; set; }

        [Required]
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        [Required]
        public bool IsDeprecated { get; set; } = false;

        [Required]
        public bool IsPhase3Route { get; set; } = false;

        public Guid? CorrelationId { get; set; }
    }
}

