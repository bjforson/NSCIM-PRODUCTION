using System.Text.Json.Serialization;

namespace NickScanCentralImagingPortal.Core.Models
{
    public class IcumApiResponse<T>
    {
        [JsonPropertyName("status")]
        public string Status { get; set; } = string.Empty;

        [JsonPropertyName("statusMsg")]
        public string? StatusMsg { get; set; }

        [JsonPropertyName("error")]
        public IcumError? Error { get; set; }

        public T? Data { get; set; }
    }

    public class IcumError
    {
        [JsonPropertyName("errorCode")]
        public int ErrorCode { get; set; }

        [JsonPropertyName("errorMsg")]
        public string ErrorMsg { get; set; } = string.Empty;
    }

    public class BoeSelectivityResponse
    {
        [JsonPropertyName("BOEScanDocument")]
        public List<BoeScanDocument> BoeScanDocuments { get; set; } = new List<BoeScanDocument>();

        [JsonPropertyName("Status")]
        public string Status { get; set; } = string.Empty;
    }

    public class BoeScanDocument
    {
        [JsonPropertyName("ContainerDetails")]
        public IcumApiContainerDetails ContainerDetails { get; set; } = new IcumApiContainerDetails();

        [JsonPropertyName("Header")]
        public BoeHeader Header { get; set; } = new BoeHeader();

        [JsonPropertyName("ManifestDetails")]
        public ManifestDetails ManifestDetails { get; set; } = new ManifestDetails();

        [JsonPropertyName("ManifestItems")]
        public List<ManifestItem> ManifestItems { get; set; } = new List<ManifestItem>();
    }


    public class BoeHeader
    {
        [JsonPropertyName("CCVRIntelRemarks")]
        public string? CcvrIntelRemarks { get; set; }

        [JsonPropertyName("CRMSLevel")]
        public string CrmsLevel { get; set; } = string.Empty;

        [JsonPropertyName("CompOffRemarks")]
        public string CompOffRemarks { get; set; } = string.Empty;

        [JsonPropertyName("DeclarantAddress")]
        public string DeclarantAddress { get; set; } = string.Empty;

        [JsonPropertyName("DeclarantName")]
        public string DeclarantName { get; set; } = string.Empty;

        [JsonPropertyName("DeclarationDate")]
        public string DeclarationDate { get; set; } = string.Empty;

        [JsonPropertyName("DeclarationNumber")]
        public string DeclarationNumber { get; set; } = string.Empty;

        [JsonPropertyName("DeclarationVersion")]
        public int DeclarationVersion { get; set; }

        [JsonPropertyName("ClearanceType")]
        public string ClearanceType { get; set; } = string.Empty;

        [JsonPropertyName("ImpExpAddress")]
        public string ImpExpAddress { get; set; } = string.Empty;

        [JsonPropertyName("ImpExpName")]
        public string ImpExpName { get; set; } = string.Empty;

        [JsonPropertyName("ImpAddress")]
        public string ImpAddress { get; set; } = string.Empty;

        [JsonPropertyName("ImpName")]
        public string ImpName { get; set; } = string.Empty;

        [JsonPropertyName("ExpAddress")]
        public string ExpAddress { get; set; } = string.Empty;

        [JsonPropertyName("ExpName")]
        public string ExpName { get; set; } = string.Empty;

        [JsonPropertyName("NoofContainers")]
        public int NoofContainers { get; set; }

        [JsonPropertyName("RegimeCode")]
        public string RegimeCode { get; set; } = string.Empty;

        [JsonPropertyName("TotalDutyPaid")]
        public decimal TotalDutyPaid { get; set; }
    }

    public class ManifestDetails
    {
        [JsonPropertyName("BLNumber")]
        public string MasterBlNumber { get; set; } = string.Empty;

        [JsonPropertyName("HouseBL")]
        public string HouseBl { get; set; } = string.Empty;

        [JsonPropertyName("ConsigneeAddress")]
        public string ConsigneeAddress { get; set; } = string.Empty;

        [JsonPropertyName("ConsigneeName")]
        public string ConsigneeName { get; set; } = string.Empty;

        [JsonPropertyName("CountryofOrigin")]
        public string CountryofOrigin { get; set; } = string.Empty;

        [JsonPropertyName("GoodsDescription")]
        public string GoodsDescription { get; set; } = string.Empty;

        [JsonPropertyName("MarksNumbers")]
        public string MarksNumbers { get; set; } = string.Empty;

        [JsonPropertyName("RotationNumber")]
        public string RotationNumber { get; set; } = string.Empty;

        [JsonPropertyName("DeliveryPlace")]
        public string DeliveryPlace { get; set; } = string.Empty;

        [JsonPropertyName("ShipperAddress")]
        public string ShipperAddress { get; set; } = string.Empty;

        [JsonPropertyName("ShipperName")]
        public string ShipperName { get; set; } = string.Empty;
    }

    public class ManifestItem
    {
        [JsonPropertyName("CPC")]
        public string Cpc { get; set; } = string.Empty;

        [JsonPropertyName("COUNTRYOFORIGIN")]
        public string CountryofOrigin { get; set; } = string.Empty;

        [JsonPropertyName("DESCRIPTION")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("FOBCURRENCY")]
        public string FobCurrency { get; set; } = string.Empty;

        [JsonPropertyName("HSCODE")]
        public string HsCode { get; set; } = string.Empty;

        [JsonPropertyName("ITEMDUTYPAID")]
        public decimal ItemDutyPaid { get; set; }

        [JsonPropertyName("ITEMFOB")]
        public decimal ItemFob { get; set; }

        [JsonPropertyName("ITEMNO")]
        public int ItemNo { get; set; }

        [JsonPropertyName("QUANTITY")]
        public decimal Quantity { get; set; }

        [JsonPropertyName("UNIT")]
        public string Unit { get; set; } = string.Empty;

        [JsonPropertyName("WEIGHT")]
        public decimal Weight { get; set; }
    }
}
