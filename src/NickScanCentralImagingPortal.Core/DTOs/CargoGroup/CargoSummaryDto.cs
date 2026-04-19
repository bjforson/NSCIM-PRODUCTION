namespace NickScanCentralImagingPortal.Core.DTOs.CargoGroup
{
    /// <summary>
    /// Human-readable cargo content summary generated from ICUMS data
    /// </summary>
    public class CargoSummaryDto
    {
        /// <summary>
        /// Main summary text (formatted for display)
        /// </summary>
        public string SummaryText { get; set; } = string.Empty;

        /// <summary>
        /// Consignee name(s) - may be multiple for consolidated cargo
        /// </summary>
        public List<string> Consignees { get; set; } = new();

        /// <summary>
        /// Primary goods description (aggregated from all items)
        /// </summary>
        public string? GoodsDescription { get; set; }

        /// <summary>
        /// Individual line item descriptions (for clean display as a list)
        /// </summary>
        public List<LineItemSummary> ItemDescriptions { get; set; } = new();

        /// <summary>
        /// HS Codes found in cargo (unique list)
        /// </summary>
        public List<string> HSCodes { get; set; } = new();

        /// <summary>
        /// Total quantity across all items (with unit if available)
        /// </summary>
        public string? TotalQuantity { get; set; }

        /// <summary>
        /// Total weight (if available)
        /// </summary>
        public string? TotalWeight { get; set; }

        /// <summary>
        /// Total FOB value (if available)
        /// </summary>
        public string? TotalFOBValue { get; set; }

        /// <summary>
        /// Total duty paid (if available)
        /// </summary>
        public string? TotalDutyPaid { get; set; }

        /// <summary>
        /// Country/Countries of origin
        /// </summary>
        public List<string> CountriesOfOrigin { get; set; } = new();

        /// <summary>
        /// Number of line items in the cargo
        /// </summary>
        public int LineItemCount { get; set; }

        /// <summary>
        /// Whether this is consolidated cargo (multiple consignees)
        /// </summary>
        public bool IsConsolidated { get; set; }

        /// <summary>
        /// AI-generated consolidated summary of all line item descriptions
        /// </summary>
        public string? AiSummaryText { get; set; }

        /// <summary>
        /// Additional details that may be relevant
        /// </summary>
        public Dictionary<string, string> AdditionalDetails { get; set; } = new();
    }

    /// <summary>
    /// Individual line item from manifest data
    /// </summary>
    public class LineItemSummary
    {
        public int ItemNo { get; set; }
        public string Description { get; set; } = string.Empty;
        public string? HsCode { get; set; }
        public string? Quantity { get; set; }
        public string? Weight { get; set; }
        public string? CountryOfOrigin { get; set; }
    }
}

