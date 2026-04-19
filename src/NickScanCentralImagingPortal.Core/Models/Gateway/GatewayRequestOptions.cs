namespace NickScanCentralImagingPortal.Core.Models.Gateway
{
    /// <summary>
    /// Options for controlling what data to include in gateway responses
    /// </summary>
    public class GatewayRequestOptions
    {
        /// <summary>
        /// Include image data (Base64 and bytes)
        /// </summary>
        public bool IncludeImage { get; set; } = true;

        /// <summary>
        /// Include scanner data (FS6000/ASE records)
        /// </summary>
        public bool IncludeScannerData { get; set; } = true;

        /// <summary>
        /// Include ICUMS/BOE data
        /// </summary>
        public bool IncludeICUMS { get; set; } = true;

        /// <summary>
        /// Include validation status and completeness data
        /// </summary>
        public bool IncludeValidation { get; set; } = true;

        /// <summary>
        /// Include vehicle data (VIN, make, model)
        /// </summary>
        public bool IncludeVehicles { get; set; } = false;

        /// <summary>
        /// Include processing history/audit trail
        /// </summary>
        public bool IncludeHistory { get; set; } = false;

        /// <summary>
        /// Generate a cache key based on options
        /// </summary>
        public string GetCacheKeySuffix()
        {
            return $"{IncludeImage}_{IncludeScannerData}_{IncludeICUMS}_{IncludeValidation}_{IncludeVehicles}_{IncludeHistory}";
        }
    }
}

