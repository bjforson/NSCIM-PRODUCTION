using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// Represents a vehicle import record identified by VIN number
    /// This model stores vehicle-specific data that was previously incorrectly stored as container data
    /// </summary>
    [Table("VehicleImports")]
    public class VehicleImport
    {
        [Key]
        public int Id { get; set; }

        /// <summary>
        /// The VIN number that was used as ContainerNumber in the original BOE data
        /// </summary>
        [Required]
        [StringLength(17)]
        public string VIN { get; set; } = string.Empty;

        /// <summary>
        /// Reference to the original BOE document for traceability
        /// </summary>
        [ForeignKey(nameof(BOEDocument))]
        public int BOEDocumentId { get; set; }
        public BOEDocument BOEDocument { get; set; } = null!;

        /// <summary>
        /// Declaration number from the BOE document
        /// </summary>
        [StringLength(50)]
        public string? DeclarationNumber { get; set; }

        /// <summary>
        /// Chassis number (often same as VIN but extracted from description)
        /// </summary>
        [StringLength(50)]
        public string? ChassisNumber { get; set; }

        /// <summary>
        /// Vehicle type (e.g., "ACURA RDX", "RENAULT PREMIUM 270 DCI FUEL TANKER TRUCK")
        /// </summary>
        [StringLength(200)]
        public string? VehicleType { get; set; }

        /// <summary>
        /// Vehicle make (extracted from vehicle type)
        /// </summary>
        [StringLength(100)]
        public string? Make { get; set; }

        /// <summary>
        /// Vehicle model (extracted from vehicle type)
        /// </summary>
        [StringLength(100)]
        public string? Model { get; set; }

        /// <summary>
        /// Vehicle year/age (e.g., "2025", "2010")
        /// </summary>
        [StringLength(10)]
        public string? VehicleYear { get; set; }

        /// <summary>
        /// Engine capacity in CC (e.g., "2000", "7200")
        /// </summary>
        [StringLength(20)]
        public string? EngineCapacity { get; set; }

        /// <summary>
        /// Vehicle weight in kg
        /// </summary>
        public decimal? Weight { get; set; }

        /// <summary>
        /// Quantity of vehicles (usually 1)
        /// </summary>
        public int Quantity { get; set; } = 1;

        /// <summary>
        /// HS Code for the vehicle
        /// </summary>
        [StringLength(20)]
        public string? HSCode { get; set; }

        /// <summary>
        /// Country of origin
        /// </summary>
        [StringLength(10)]
        public string? CountryOfOrigin { get; set; }

        /// <summary>
        /// FOB value in original currency
        /// </summary>
        public decimal? FOBValue { get; set; }

        /// <summary>
        /// FOB currency (e.g., "USD", "EUR")
        /// </summary>
        [StringLength(10)]
        public string? FOBCurrency { get; set; }

        /// <summary>
        /// Duty paid amount
        /// </summary>
        public decimal? DutyPaid { get; set; }

        /// <summary>
        /// Importer name
        /// </summary>
        [StringLength(500)]
        public string? ImporterName { get; set; }

        /// <summary>
        /// Shipper name
        /// </summary>
        [StringLength(500)]
        public string? ShipperName { get; set; }

        /// <summary>
        /// Consignee name
        /// </summary>
        [StringLength(500)]
        public string? ConsigneeName { get; set; }

        /// <summary>
        /// Bill of Lading number
        /// </summary>
        [StringLength(100)]
        public string? BLNumber { get; set; }

        /// <summary>
        /// House Bill of Lading number
        /// </summary>
        [StringLength(100)]
        public string? HouseBL { get; set; }

        /// <summary>
        /// Rotation number
        /// </summary>
        [StringLength(50)]
        public string? RotationNumber { get; set; }

        /// <summary>
        /// Clearance type (IM, EX, etc.)
        /// </summary>
        [StringLength(10)]
        public string? ClearanceType { get; set; }

        /// <summary>
        /// CRMS level (Red, Yellow, Green)
        /// </summary>
        [StringLength(20)]
        public string? CrmsLevel { get; set; }

        /// <summary>
        /// Processing status
        /// </summary>
        [StringLength(50)]
        public string ProcessingStatus { get; set; } = "Pending";

        /// <summary>
        /// Error message if processing failed
        /// </summary>
        [StringLength(1000)]
        public string? ErrorMessage { get; set; }

        /// <summary>
        /// Additional remarks or notes
        /// </summary>
        [StringLength(2000)]
        public string? Remarks { get; set; }

        /// <summary>
        /// Record creation timestamp
        /// </summary>
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Record last update timestamp
        /// </summary>
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Processing completion timestamp
        /// </summary>
        public DateTime? ProcessedAt { get; set; }

        /// <summary>
        /// Whether this is a direct VIN record (Type 1) or VIN within container (Type 2)
        /// </summary>
        public VehicleImportType ImportType { get; set; } = VehicleImportType.DirectVIN;

        /// <summary>
        /// Container number if this is a Type 2 record (VIN within container)
        /// </summary>
        [StringLength(20)]
        public string? ContainerNumber { get; set; }
    }

    /// <summary>
    /// Enum for vehicle import types
    /// </summary>
    public enum VehicleImportType
    {
        /// <summary>
        /// VIN used directly as container number (Type 1)
        /// </summary>
        DirectVIN = 1,

        /// <summary>
        /// VIN found within a real container record (Type 2)
        /// </summary>
        VINInContainer = 2
    }
}
