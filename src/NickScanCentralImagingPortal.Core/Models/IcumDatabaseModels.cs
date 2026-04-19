using System.Text.Json.Serialization;

namespace NickScanCentralImagingPortal.Core.Models
{
    // ICUMS Database Models
    public class IcumContainerData
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string? BoeData { get; set; }

        // Structured fields for better querying and analysis
        public string? MasterBlNumber { get; set; }
        public string? HouseBl { get; set; }
        public string? RotationNumber { get; set; }
        public string? ConsigneeName { get; set; }
        public string? ShipperName { get; set; }
        public string? CountryOfOrigin { get; set; }
        public decimal? TotalDutyPaid { get; set; }
        public string? CrmsLevel { get; set; }
        public string? ClearanceType { get; set; }
        public string? DeclarationNumber { get; set; }
        public decimal? ContainerWeight { get; set; }
        public int? ContainerQuantity { get; set; }
        public string? ContainerISO { get; set; }

        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string? Status { get; set; }

        // Navigation property for manifest items
        public virtual ICollection<IcumManifestItem> ManifestItems { get; set; } = new List<IcumManifestItem>();
    }

    public class IcumDocument
    {
        public int Id { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string DocumentType { get; set; } = string.Empty;
        public string DocumentData { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class IcumBatchLog
    {
        public int Id { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime EndDate { get; set; }
        public int RecordsProcessed { get; set; }
        public string Status { get; set; } = string.Empty;
        public string? ErrorMessage { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }

    public class IcumManifestItem
    {
        public int Id { get; set; }
        public int IcumContainerDataId { get; set; }
        // Consolidated cargo: link manifest items to specific House BL within the container
        public string? HouseBl { get; set; }
        public string HsCode { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public string Unit { get; set; } = string.Empty;
        public decimal Weight { get; set; }
        public decimal ItemFob { get; set; }
        public decimal ItemDutyPaid { get; set; }
        public string FobCurrency { get; set; } = string.Empty;
        public string CountryOfOrigin { get; set; } = string.Empty;
        public int ItemNo { get; set; }
        public string Cpc { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }

        // Navigation property
        public virtual IcumContainerData IcumContainerData { get; set; } = null!;
    }

    public class IcumApiStatusModel
    {
        public string Status { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; }
    }
}
