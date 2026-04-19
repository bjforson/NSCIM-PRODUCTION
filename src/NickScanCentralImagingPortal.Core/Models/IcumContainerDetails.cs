using System.Text.Json.Serialization;

namespace NickScanCentralImagingPortal.Core.Models
{
    /// <summary>
    /// ICUMS API-specific ContainerDetails class for API responses
    /// This class handles the distinction between container numbers and VIN numbers
    /// </summary>
    public class IcumApiContainerDetails
    {
        [JsonPropertyName("ContainerNumber")]
        public string ContainerNumber { get; set; } = string.Empty;

        [JsonPropertyName("VINNumber")]
        public string? VinNumber { get; set; }

        [JsonPropertyName("ContainerType")]
        public string? ContainerType { get; set; }

        [JsonPropertyName("ContainerSize")]
        public string? ContainerSize { get; set; }

        [JsonPropertyName("ContainerWeight")]
        public decimal? ContainerWeight { get; set; }

        [JsonPropertyName("ContainerISO")]
        public string? ContainerISO { get; set; }

        [JsonPropertyName("SealNumber")]
        public string? SealNumber { get; set; }

        [JsonPropertyName("TruckPlateNumber")]
        public string? TruckPlateNumber { get; set; }

        [JsonPropertyName("DriverName")]
        public string? DriverName { get; set; }

        [JsonPropertyName("DriverLicense")]
        public string? DriverLicense { get; set; }

        [JsonPropertyName("Status")]
        public string? Status { get; set; }

        [JsonPropertyName("Remarks")]
        public string? Remarks { get; set; }

        /// <summary>
        /// Determines if the ContainerNumber field contains a valid container number or a VIN
        /// </summary>
        public bool IsValidContainerNumber()
        {
            if (string.IsNullOrWhiteSpace(ContainerNumber))
                return false;

            var containerNum = ContainerNumber.Trim().ToUpper();

            // Check if it's a VIN number (17 characters, alphanumeric, no I, O, Q)
            if (containerNum.Length == 17 &&
                containerNum.All(c => char.IsLetterOrDigit(c) && c != 'I' && c != 'O' && c != 'Q'))
            {
                // This is a VIN number, not a container number
                VinNumber = containerNum;
                ContainerNumber = string.Empty;
                return false;
            }

            // Check if it's a valid ISO container number (4 letters + 7 digits)
            if (containerNum.Length == 11 &&
                containerNum.Substring(0, 4).All(char.IsLetter) &&
                containerNum.Substring(4, 7).All(char.IsDigit))
            {
                return true;
            }

            // Check if it's a valid container number with size indicator (e.g., "MSNU9807858(20FT)")
            if (containerNum.Length > 11 && containerNum.Contains("(") && containerNum.Contains(")"))
            {
                var baseContainer = containerNum.Split('(')[0];
                if (baseContainer.Length == 11 &&
                    baseContainer.Substring(0, 4).All(char.IsLetter) &&
                    baseContainer.Substring(4, 7).All(char.IsDigit))
                {
                    ContainerNumber = baseContainer;
                    return true;
                }
            }

            // Use the centralized validator for loose cargo
            if (Utilities.ContainerNumberValidator.IsLooseCargoIdentifier(containerNum))
            {
                ContainerNumber = containerNum;
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets the actual container number, filtering out VIN numbers
        /// </summary>
        public string GetActualContainerNumber()
        {
            if (IsValidContainerNumber())
            {
                return ContainerNumber;
            }

            // If it's a VIN, return empty string
            return string.Empty;
        }

        /// <summary>
        /// Gets the VIN number if present
        /// </summary>
        public string GetVinNumber()
        {
            if (!string.IsNullOrWhiteSpace(VinNumber))
            {
                return VinNumber;
            }

            // Check if ContainerNumber is actually a VIN
            if (!string.IsNullOrWhiteSpace(ContainerNumber) &&
                ContainerNumber.Length == 17 &&
                ContainerNumber.All(c => char.IsLetterOrDigit(c) && c != 'I' && c != 'O' && c != 'Q'))
            {
                return ContainerNumber;
            }

            return string.Empty;
        }

        /// <summary>
        /// Extracts vehicle data from various fields for VIN records
        /// </summary>
        public VehicleData ExtractVehicleData()
        {
            var vehicleData = new VehicleData();

            var vin = GetVinNumber();
            if (string.IsNullOrEmpty(vin))
                return vehicleData;

            vehicleData.VIN = vin;
            vehicleData.ChassisNumber = vin; // VIN is often the chassis number
            vehicleData.Weight = ContainerWeight;
            vehicleData.Quantity = 1; // Default for vehicles

            // Extract vehicle information from remarks or other fields
            if (!string.IsNullOrWhiteSpace(Remarks))
            {
                ExtractVehicleInfoFromText(Remarks, vehicleData);
            }

            return vehicleData;
        }

        /// <summary>
        /// Extracts vehicle data from manifest details and items
        /// </summary>
        public VehicleData ExtractVehicleDataFromManifest(ManifestDetails? manifestDetails, List<ManifestItem>? manifestItems)
        {
            var vehicleData = new VehicleData();

            var vin = GetVinNumber();
            if (string.IsNullOrEmpty(vin))
                return vehicleData;

            vehicleData.VIN = vin;
            vehicleData.ChassisNumber = vin;

            // Extract from manifest details
            if (manifestDetails != null)
            {
                vehicleData.ShipperName = manifestDetails.ShipperName;
                vehicleData.ConsigneeName = manifestDetails.ConsigneeName;
                vehicleData.BLNumber = manifestDetails.MasterBlNumber;
                vehicleData.HouseBL = manifestDetails.HouseBl;
                vehicleData.RotationNumber = manifestDetails.RotationNumber;
                vehicleData.CountryOfOrigin = manifestDetails.CountryofOrigin;

                // Extract VIN from marks and numbers
                if (!string.IsNullOrWhiteSpace(manifestDetails.MarksNumbers))
                {
                    ExtractVehicleInfoFromText(manifestDetails.MarksNumbers, vehicleData);
                }

                // Extract VIN from goods description
                if (!string.IsNullOrWhiteSpace(manifestDetails.GoodsDescription))
                {
                    ExtractVehicleInfoFromText(manifestDetails.GoodsDescription, vehicleData);
                }
            }

            // Extract from manifest items
            if (manifestItems != null && manifestItems.Any())
            {
                var firstItem = manifestItems.First();
                vehicleData.HSCode = firstItem.HsCode;
                vehicleData.FOBValue = firstItem.ItemFob;
                vehicleData.FOBCurrency = firstItem.FobCurrency;
                vehicleData.DutyPaid = firstItem.ItemDutyPaid;
                vehicleData.Weight = firstItem.Weight;
                vehicleData.Quantity = (int)firstItem.Quantity;

                // Extract vehicle info from item description
                if (!string.IsNullOrWhiteSpace(firstItem.Description))
                {
                    ExtractVehicleInfoFromText(firstItem.Description, vehicleData);
                }
            }

            return vehicleData;
        }

        /// <summary>
        /// Extracts vehicle information from text descriptions
        /// </summary>
        private void ExtractVehicleInfoFromText(string text, VehicleData vehicleData)
        {
            if (string.IsNullOrWhiteSpace(text))
                return;

            var textUpper = text.ToUpper();

            // Extract vehicle type (look for common patterns)
            var vehicleTypePatterns = new[]
            {
                @"(ACURA|AUDI|BMW|CHEVROLET|FORD|HONDA|HYUNDAI|KIA|LEXUS|MAZDA|MERCEDES|NISSAN|RENAULT|TOYOTA|VOLKSWAGEN)\s+[A-Z0-9\s]+",
                @"(TRUCK|CAR|SUV|VAN|PICKUP|SEDAN|HATCHBACK|COUPE|CONVERTIBLE|WAGON|CROSSOVER)",
                @"(FUEL\s+TANKER|CEMENT\s+MIXER|DUMP\s+TRUCK|FLATBED|REFRIGERATED)"
            };

            foreach (var pattern in vehicleTypePatterns)
            {
                var match = System.Text.RegularExpressions.Regex.Match(textUpper, pattern, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (match.Success)
                {
                    vehicleData.VehicleType = match.Value.Trim();
                    break;
                }
            }

            // Extract make and model
            if (!string.IsNullOrWhiteSpace(vehicleData.VehicleType))
            {
                var parts = vehicleData.VehicleType.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                {
                    vehicleData.Make = parts[0];
                    if (parts.Length > 1)
                    {
                        vehicleData.Model = string.Join(" ", parts.Skip(1));
                    }
                }
            }

            // Extract year (look for 4-digit years)
            var yearMatch = System.Text.RegularExpressions.Regex.Match(text, @"\b(19|20)\d{2}\b");
            if (yearMatch.Success)
            {
                vehicleData.VehicleYear = yearMatch.Value;
            }

            // Extract engine capacity (look for CC patterns)
            var ccMatch = System.Text.RegularExpressions.Regex.Match(text, @"CC\s*:\s*(\d+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (ccMatch.Success)
            {
                vehicleData.EngineCapacity = ccMatch.Groups[1].Value;
            }

            // Extract chassis number (look for CHASSIS patterns)
            var chassisMatch = System.Text.RegularExpressions.Regex.Match(text, @"CHASSIS\s*(?:NO|NUMBER)?\s*:\s*([A-Z0-9]+)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (chassisMatch.Success)
            {
                vehicleData.ChassisNumber = chassisMatch.Groups[1].Value;
            }
        }
    }

    /// <summary>
    /// Data class for extracted vehicle information
    /// </summary>
    public class VehicleData
    {
        public string VIN { get; set; } = string.Empty;
        public string? ChassisNumber { get; set; }
        public string? VehicleType { get; set; }
        public string? Make { get; set; }
        public string? Model { get; set; }
        public string? VehicleYear { get; set; }
        public string? EngineCapacity { get; set; }
        public decimal? Weight { get; set; }
        public int Quantity { get; set; } = 1;
        public string? HSCode { get; set; }
        public decimal? FOBValue { get; set; }
        public string? FOBCurrency { get; set; }
        public decimal? DutyPaid { get; set; }
        public string? ShipperName { get; set; }
        public string? ConsigneeName { get; set; }
        public string? BLNumber { get; set; }
        public string? HouseBL { get; set; }
        public string? RotationNumber { get; set; }
        public string? CountryOfOrigin { get; set; }
    }
}
