using System.ComponentModel.DataAnnotations;

namespace NickScanCentralImagingPortal.Core.Entities
{
    /// <summary>
    /// Links scanner data with ICUMS BOE data for complete container information
    /// </summary>
    public class ContainerBOERelation
    {
        public int Id { get; set; }

        [Required]
        [StringLength(50)]
        public string ContainerNumber { get; set; } = string.Empty;

        public int ScannerDataId { get; set; }

        [Required]
        [StringLength(20)]
        public string ScannerType { get; set; } = string.Empty;

        public int ICUMSBOEId { get; set; }

        [Required]
        [StringLength(20)]
        public string RelationType { get; set; } = string.Empty; // 'ASE', 'FS6000', 'NUCTECH'

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [StringLength(500)]
        public string? Notes { get; set; }

        public bool IsActive { get; set; } = true;

        public DateTime? LastValidatedAt { get; set; }
    }
}
