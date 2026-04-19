using System.Text.RegularExpressions;

namespace NickScanCentralImagingPortal.Core.Utilities
{
    /// <summary>
    /// Utility class for validating container numbers
    /// ISO 6346 standard: 4 letters (owner code) + 6 digits (serial) + 1 check digit = 11 characters
    /// </summary>
    public static class ContainerNumberValidator
    {
        /// <summary>
        /// Validates if a string is a valid ISO container number
        /// Standard format: 4 letters + 7 digits (11 characters total)
        /// </summary>
        public static bool IsValidContainerNumber(string? containerNumber)
        {
            if (string.IsNullOrWhiteSpace(containerNumber))
                return false;

            var containerNum = containerNumber.Trim().ToUpper();

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
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Checks if a string is a valid loose cargo identifier
        /// Accepts only known patterns: "BULK", "LOOSE", or known prefixes (NSP, ZRR, CDM)
        /// </summary>
        public static bool IsLooseCargoIdentifier(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;

            var id = identifier.Trim().ToUpper();

            // Accept literal identifiers for bulk commodities
            if (id == "BULK" || id == "LOOSE")
                return true;

            // Accept known loose cargo prefixes (NSP, ZRR, CDM) followed by numbers/hyphens
            if (id.StartsWith("NSP") || id.StartsWith("ZRR") || id.StartsWith("CDM"))
            {
                // Must be followed by at least 3 characters (numbers/hyphens)
                var suffix = id.Substring(3);
                if (suffix.Length >= 3 && suffix.All(c => char.IsDigit(c) || c == '-'))
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a string is a vehicle chassis number
        /// Pattern: 2-5 letters followed by digits/hyphens, total 12-14 characters with hyphen
        /// Examples: CCFFW-104767, ACA38-5147102
        /// </summary>
        public static bool IsVehicleChassisNumber(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;

            var id = identifier.Trim().ToUpper();

            // Pattern: 12-14 characters with hyphen
            if (id.Length >= 12 && id.Length <= 14 && id.Contains('-'))
            {
                // Pattern: [A-Z]{2,5}[\d-]+
                var match = Regex.Match(id, @"^[A-Z]{2,5}[\d-]+$");
                if (match.Success)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a string is a vehicle registration number
        /// Pattern: 2-3 letters followed by 5-7 digits (7-8 characters total)
        /// Examples: AAA59204, D073102
        /// </summary>
        public static bool IsVehicleRegistrationNumber(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;

            var id = identifier.Trim().ToUpper();

            // Pattern: 7-8 characters: 2-3 letters + 5-7 digits
            if (id.Length >= 7 && id.Length <= 8)
            {
                var match = Regex.Match(id, @"^[A-Z]{2,3}\d{5,7}$");
                if (match.Success)
                    return true;
            }

            return false;
        }

        /// <summary>
        /// Checks if a string is a VIN number (17 characters, alphanumeric, no I/O/Q)
        /// </summary>
        public static bool IsVINNumber(string? identifier)
        {
            if (string.IsNullOrWhiteSpace(identifier))
                return false;

            var id = identifier.Trim().ToUpper();

            // VIN: exactly 17 characters, alphanumeric, no I, O, Q
            if (id.Length == 17 && id.All(c => char.IsLetterOrDigit(c) && c != 'I' && c != 'O' && c != 'Q'))
                return true;

            return false;
        }

        /// <summary>
        /// Validates if a string is a valid ISO container number
        /// Alias for IsValidContainerNumber for consistency
        /// </summary>
        public static bool IsValidISOContainerNumber(string? containerNumber)
        {
            return IsValidContainerNumber(containerNumber);
        }

        /// <summary>
        /// Checks if a string is a valid container number OR loose cargo identifier
        /// Excludes vehicle identifiers (VIN, chassis, registration)
        /// </summary>
        public static bool IsValidContainerOrLooseCargo(string? containerNumber)
        {
            if (string.IsNullOrWhiteSpace(containerNumber))
                return false;

            var type = GetContainerNumberType(containerNumber);
            return type == ContainerNumberType.ValidISO || type == ContainerNumberType.LooseCargo;
        }

        /// <summary>
        /// Validates and classifies a container number
        /// Returns the classification type
        /// </summary>
        public static ContainerNumberType ClassifyContainerNumber(string? containerNumber)
        {
            return GetContainerNumberType(containerNumber);
        }

        /// <summary>
        /// Gets the type of container number with enhanced validation
        /// Checks in order: ISO Container, VIN, Vehicle Chassis, Vehicle Registration, Loose Cargo, Invalid
        /// </summary>
        public static ContainerNumberType GetContainerNumberType(string? containerNumber)
        {
            if (string.IsNullOrWhiteSpace(containerNumber))
                return ContainerNumberType.Invalid;

            var containerNum = containerNumber.Trim().ToUpper();

            // 1. Check ISO container (highest priority)
            if (IsValidISOContainerNumber(containerNum))
                return ContainerNumberType.ValidISO;

            // 2. Check 17-char VIN
            if (IsVINNumber(containerNum))
                return ContainerNumberType.VIN;

            // 3. Check vehicle chassis number (12-14 chars with hyphen)
            if (IsVehicleChassisNumber(containerNum))
                return ContainerNumberType.VehicleChassis;

            // 4. Check vehicle registration number (7-8 chars)
            if (IsVehicleRegistrationNumber(containerNum))
                return ContainerNumberType.VehicleRegistration;

            // 5. Check loose cargo (strict validation)
            if (IsLooseCargoIdentifier(containerNum))
                return ContainerNumberType.LooseCargo;

            // 6. Invalid
            return ContainerNumberType.Invalid;
        }

        /// <summary>
        /// Filters a list of container numbers, returning only valid ones
        /// </summary>
        public static List<string> FilterValidContainerNumbers(IEnumerable<string> containerNumbers)
        {
            return containerNumbers
                .Where(cn => IsValidContainerOrLooseCargo(cn))
                .ToList();
        }
    }

    /// <summary>
    /// Classification of container number types
    /// </summary>
    public enum ContainerNumberType
    {
        Invalid,              // Not a valid container number
        ValidISO,             // Valid ISO container number (4 letters + 7 digits)
        LooseCargo,           // Valid loose cargo identifier (BULK, LOOSE, or known prefixes)
        VIN,                  // Vehicle Identification Number (17 characters)
        VehicleChassis,        // Vehicle chassis number (12-14 chars with hyphen)
        VehicleRegistration   // Vehicle registration number (7-8 chars: 2-3 letters + 5-7 digits)
    }
}

