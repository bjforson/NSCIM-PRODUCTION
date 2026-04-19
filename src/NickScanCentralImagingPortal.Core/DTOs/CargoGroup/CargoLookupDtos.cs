namespace NickScanCentralImagingPortal.Core.DTOs.CargoGroup
{
    public sealed class CargoLookupRowDto
    {
        public int BoeDocumentId { get; set; }
        public string ContainerNumber { get; set; } = string.Empty;
        public string? BlNumber { get; set; }
        public string? MasterBlNumber { get; set; }
        public string? HouseBl { get; set; }
        public string? DeclarationNumber { get; set; }
        public string? RotationNumber { get; set; }
        public string? ClearanceType { get; set; }
        public bool IsConsolidated { get; set; }
        public string? ImpExpName { get; set; }
        public string? DeclarantName { get; set; }
        public DateTime UpdatedAt { get; set; }
        public string MatchedField { get; set; } = string.Empty;
        public string? MatchedValue { get; set; }
    }
}
