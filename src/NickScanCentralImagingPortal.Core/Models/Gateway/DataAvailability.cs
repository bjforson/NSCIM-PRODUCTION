namespace NickScanCentralImagingPortal.Core.Models.Gateway
{
    /// <summary>
    /// Indicates what data is available for a container
    /// </summary>
    public class DataAvailability
    {
        public bool HasScannerData { get; set; }
        public bool HasImage { get; set; }
        public bool HasICUMSData { get; set; }
        public bool HasValidationData { get; set; }
        public bool HasVehicleData { get; set; }
        public bool HasHistoryData { get; set; }

        /// <summary>
        /// Returns true if any data is available
        /// </summary>
        public bool HasAnyData =>
            HasScannerData || HasImage || HasICUMSData ||
            HasValidationData || HasVehicleData || HasHistoryData;

        /// <summary>
        /// Returns true if all requested data is available
        /// </summary>
        public bool IsComplete =>
            HasScannerData && HasImage && HasICUMSData && HasValidationData;
    }
}

