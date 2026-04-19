using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ContainerValidation
{
    /// <summary>
    /// Service for detecting and determining clearance types (CMR vs IM/EX) based on ICUMS data
    /// </summary>
    public class ClearanceTypeDetectionService : IClearanceTypeDetectionService
    {
        private readonly IcumDownloadsDbContext _icumDownloadsDbContext;
        private readonly ILogger<ClearanceTypeDetectionService> _logger;

        public ClearanceTypeDetectionService(
            IcumDownloadsDbContext icumDownloadsDbContext,
            ILogger<ClearanceTypeDetectionService> logger)
        {
            _icumDownloadsDbContext = icumDownloadsDbContext;
            _logger = logger;
        }

        /// <summary>
        /// Detects clearance type based on ICUMS data for a container
        /// </summary>
        public async Task<ClearanceType> DetectClearanceTypeAsync(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Detecting clearance type for container: {ContainerNumber}", containerNumber);

                // Get BOE document for the container
                var boeDocument = await _icumDownloadsDbContext.BOEDocuments
                    .FirstOrDefaultAsync(b => b.ContainerNumber == containerNumber);

                if (boeDocument == null)
                {
                    _logger.LogWarning("No BOE document found for container: {ContainerNumber}", containerNumber);
                    return ClearanceType.CMR; // Default to CMR if no BOE data
                }

                var clearanceType = DetectClearanceTypeFromBOE(boeDocument);
                _logger.LogInformation("Detected clearance type {ClearanceType} for container: {ContainerNumber}",
                    clearanceType, containerNumber);

                return clearanceType;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting clearance type for container: {ContainerNumber}", containerNumber);
                return ClearanceType.CMR; // Default fallback
            }
        }

        /// <summary>
        /// Detects clearance type from BOE document data
        /// </summary>
        public ClearanceType DetectClearanceTypeFromBOE(BOEDocument boeDocument)
        {
            try
            {
                // Check if ClearanceType field is present and valid
                if (!string.IsNullOrEmpty(boeDocument.ClearanceType))
                {
                    var clearanceTypeCode = boeDocument.ClearanceType.ToUpperInvariant();

                    // Based on ICUMS API spec, clearance types are:
                    // "IM" = Import (IM/EX with BOE data)
                    // "EX" = Export (IM/EX with BOE data)
                    // "CMR" = CMR (no BOE data required)

                    return clearanceTypeCode switch
                    {
                        "IM" or "EX" => ClearanceType.IMEX,
                        "CMR" => ClearanceType.CMR,
                        _ => DetermineFromDataAvailability(boeDocument)
                    };
                }

                // Fallback: determine based on data availability
                return DetermineFromDataAvailability(boeDocument);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting clearance type from BOE document for container: {ContainerNumber}",
                    boeDocument.ContainerNumber);
                return ClearanceType.CMR; // Default fallback
            }
        }

        /// <summary>
        /// Determines clearance type based on available data when ClearanceType field is missing
        /// </summary>
        private ClearanceType DetermineFromDataAvailability(BOEDocument boeDocument)
        {
            // If we have BOE-specific data (DeclarationNumber), it's likely IM/EX
            if (!string.IsNullOrEmpty(boeDocument.DeclarationNumber))
            {
                return ClearanceType.IMEX;
            }

            // If we have rotation number but no BOE data, it's likely CMR
            if (!string.IsNullOrEmpty(boeDocument.RotationNumber))
            {
                return ClearanceType.CMR;
            }

            // Default fallback
            return ClearanceType.CMR;
        }

        /// <summary>
        /// Determines if a clearance type requires BOE data
        /// </summary>
        public bool RequiresBOEData(ClearanceType clearanceType)
        {
            return clearanceType switch
            {
                ClearanceType.IMEX => true,  // IM/EX requires BOE data
                ClearanceType.CMR => false,  // CMR does not require BOE data
                _ => false
            };
        }

        /// <summary>
        /// Gets required fields for a specific clearance type
        /// </summary>
        public List<string> GetRequiredFields(ClearanceType clearanceType)
        {
            return clearanceType switch
            {
                ClearanceType.CMR => new List<string>
                {
                    "ContainerNumber",
                    "RotationNumber",
                    "BLNumber", // Either BLNumber or HouseBL
                    "HouseBL"   // Either BLNumber or HouseBL
                },
                ClearanceType.IMEX => new List<string>
                {
                    "ContainerNumber",
                    "BOENumber", // DeclarationNumber in BOE document
                    "BLNumber",  // Either BLNumber or HouseBL
                    "HouseBL"    // Either BLNumber or HouseBL
                },
                _ => new List<string> { "ContainerNumber" }
            };
        }

        /// <summary>
        /// Validates if all required fields are present for a clearance type
        /// </summary>
        public async Task<ClearanceTypeValidationResult> ValidateRequiredFieldsAsync(string containerNumber, ClearanceType clearanceType)
        {
            try
            {
                _logger.LogInformation("Validating required fields for container: {ContainerNumber}, clearance type: {ClearanceType}",
                    containerNumber, clearanceType);

                var result = new ClearanceTypeValidationResult();
                var boeDocument = await _icumDownloadsDbContext.BOEDocuments
                    .FirstOrDefaultAsync(b => b.ContainerNumber == containerNumber);

                if (boeDocument == null)
                {
                    result.IsValid = false;
                    result.ValidationMessage = "No ICUMS data found for container";
                    result.MissingFields = GetRequiredFields(clearanceType);
                    result.CompletenessScore = 0;
                    return result;
                }

                var requiredFields = GetRequiredFields(clearanceType);
                var presentFields = new List<string>();
                var missingFields = new List<string>();

                // Check each required field
                foreach (var field in requiredFields)
                {
                    var isPresent = field switch
                    {
                        "ContainerNumber" => !string.IsNullOrEmpty(boeDocument.ContainerNumber),
                        "RotationNumber" => !string.IsNullOrEmpty(boeDocument.RotationNumber),
                        "BOENumber" => !string.IsNullOrEmpty(boeDocument.DeclarationNumber),
                        "BLNumber" => !string.IsNullOrEmpty(boeDocument.BlNumber),
                        "HouseBL" => !string.IsNullOrEmpty(boeDocument.HouseBl),
                        _ => false
                    };

                    if (isPresent)
                    {
                        presentFields.Add(field);
                    }
                    else
                    {
                        missingFields.Add(field);
                    }
                }

                // Special handling for BL/HouseBL - only one is required
                if (clearanceType == ClearanceType.CMR || clearanceType == ClearanceType.IMEX)
                {
                    if (presentFields.Contains("BLNumber") || presentFields.Contains("HouseBL"))
                    {
                        // Remove both from missing fields if at least one is present
                        missingFields.Remove("BLNumber");
                        missingFields.Remove("HouseBL");

                        // Add the present one to present fields if not already there
                        if (presentFields.Contains("BLNumber") && !presentFields.Contains("HouseBL"))
                        {
                            // BLNumber is present, HouseBL is not required
                        }
                        else if (presentFields.Contains("HouseBL") && !presentFields.Contains("BLNumber"))
                        {
                            // HouseBL is present, BLNumber is not required
                        }
                    }
                }

                result.PresentFields = presentFields;
                result.MissingFields = missingFields;
                result.IsValid = missingFields.Count == 0;
                result.CompletenessScore = CalculateCompletenessScore(presentFields.Count, requiredFields.Count, clearanceType);
                result.ValidationMessage = result.IsValid
                    ? "All required fields are present"
                    : $"Missing required fields: {string.Join(", ", missingFields)}";

                _logger.LogInformation("Validation result for container {ContainerNumber}: {IsValid}, score: {Score}",
                    containerNumber, result.IsValid, result.CompletenessScore);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating required fields for container: {ContainerNumber}", containerNumber);
                return new ClearanceTypeValidationResult
                {
                    IsValid = false,
                    ValidationMessage = "Error during validation",
                    CompletenessScore = 0
                };
            }
        }

        /// <summary>
        /// Calculates completeness score based on present fields and clearance type
        /// </summary>
        private int CalculateCompletenessScore(int presentCount, int requiredCount, ClearanceType clearanceType)
        {
            if (requiredCount == 0) return 100;

            // Special handling for BL/HouseBL fields
            var adjustedRequiredCount = clearanceType switch
            {
                ClearanceType.CMR => Math.Max(1, requiredCount - 1), // BL or HouseBL counts as one
                ClearanceType.IMEX => Math.Max(1, requiredCount - 1), // BL or HouseBL counts as one
                _ => requiredCount
            };

            var score = (int)Math.Round((double)presentCount / adjustedRequiredCount * 100);
            return Math.Min(score, 100);
        }
    }
}
