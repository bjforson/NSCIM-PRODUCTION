using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ContainerValidation
{
    /// <summary>
    /// Service for validating scanner data completeness and quality
    /// </summary>
    public class ScannerDataValidationService : IScannerDataValidationService
    {
        private readonly ApplicationDbContext _applicationDbContext;
        private readonly ILogger<ScannerDataValidationService> _logger;

        public ScannerDataValidationService(
            ApplicationDbContext applicationDbContext,
            ILogger<ScannerDataValidationService> logger)
        {
            _applicationDbContext = applicationDbContext;
            _logger = logger;
        }

        /// <summary>
        /// Validates scanner data for a specific container
        /// </summary>
        public async Task<ScannerDataCompleteness> ValidateScannerDataAsync(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Validating scanner data for container: {ContainerNumber}", containerNumber);

                var completeness = new ScannerDataCompleteness();

                // Check FS6000 scanner data first
                var fs6000Scan = await _applicationDbContext.FS6000Scans
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (fs6000Scan != null)
                {
                    return await ValidateFS6000DataAsync(fs6000Scan);
                }

                // Check ASE scanner data
                var aseScan = await _applicationDbContext.AseScans
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (aseScan != null)
                {
                    return await ValidateASEDataAsync(aseScan);
                }

                // No scanner data found
                completeness.HasScannerData = false;
                completeness.CompletenessScore = 0;
                completeness.ValidationErrors.Add("No scanner data found for container");

                _logger.LogWarning("No scanner data found for container: {ContainerNumber}", containerNumber);
                return completeness;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating scanner data for container: {ContainerNumber}", containerNumber);
                return new ScannerDataCompleteness
                {
                    HasScannerData = false,
                    CompletenessScore = 0,
                    ValidationErrors = new List<string> { "Error validating scanner data" }
                };
            }
        }

        /// <summary>
        /// Gets all containers with scanner data
        /// </summary>
        public async Task<List<ScannerContainer>> GetContainersWithScannerDataAsync()
        {
            try
            {
                _logger.LogInformation("Getting all containers with scanner data");

                var containers = new List<ScannerContainer>();

                // Get FS6000 scanner containers
                var fs6000Containers = await _applicationDbContext.FS6000Scans
                    .Where(s => !string.IsNullOrEmpty(s.ContainerNumber))
                    .Select(s => new ScannerContainer
                    {
                        Id = s.Id,
                        ContainerNumber = s.ContainerNumber,
                        ScannerType = "FS6000",
                        ScanDateTime = s.ScanTime
                    })
                    .ToListAsync();

                // Get ASE scanner containers
                var aseContainers = await _applicationDbContext.AseScans
                    .Where(s => !string.IsNullOrEmpty(s.ContainerNumber))
                    .Select(s => new ScannerContainer
                    {
                        Id = s.Id,
                        ContainerNumber = s.ContainerNumber,
                        ScannerType = "ASE",
                        ScanDateTime = s.ScanTime
                    })
                    .ToListAsync();

                containers.AddRange(fs6000Containers);
                containers.AddRange(aseContainers);

                _logger.LogInformation("Found {Count} containers with scanner data", containers.Count);
                return containers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting containers with scanner data");
                throw;
            }
        }

        /// <summary>
        /// Gets scanner data for a specific container
        /// </summary>
        public async Task<ScannerContainer?> GetScannerDataAsync(string containerNumber)
        {
            try
            {
                _logger.LogInformation("Getting scanner data for container: {ContainerNumber}", containerNumber);

                // Check FS6000 scanner data first
                var fs6000Scan = await _applicationDbContext.FS6000Scans
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (fs6000Scan != null)
                {
                    return new ScannerContainer
                    {
                        Id = fs6000Scan.Id,
                        ContainerNumber = fs6000Scan.ContainerNumber,
                        ScannerType = "FS6000",
                        ScanDateTime = fs6000Scan.ScanTime
                    };
                }

                // Check ASE scanner data
                var aseScan = await _applicationDbContext.AseScans
                    .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                if (aseScan != null)
                {
                    return new ScannerContainer
                    {
                        Id = aseScan.Id,
                        ContainerNumber = aseScan.ContainerNumber,
                        ScannerType = "ASE",
                        ScanDateTime = aseScan.ScanTime
                    };
                }

                _logger.LogWarning("No scanner data found for container: {ContainerNumber}", containerNumber);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scanner data for container: {ContainerNumber}", containerNumber);
                throw;
            }
        }

        /// <summary>
        /// Validates scanner data quality
        /// </summary>
        public async Task<ScannerDataQualityResult> ValidateScannerDataQualityAsync(ScannerContainer scannerData)
        {
            try
            {
                _logger.LogInformation("Validating scanner data quality for container: {ContainerNumber}", scannerData.ContainerNumber);

                var result = new ScannerDataQualityResult
                {
                    ScannerType = scannerData.ScannerType,
                    ScanDateTime = scannerData.ScanDateTime
                };

                var qualityScore = 0;
                var maxScore = 100;

                // Basic data validation (40 points)
                if (!string.IsNullOrEmpty(scannerData.ContainerNumber))
                {
                    qualityScore += 20;
                    result.QualityStrengths.Add("Container number is present");
                }
                else
                {
                    result.QualityIssues.Add("Container number is missing");
                }

                if (scannerData.ScanDateTime != default)
                {
                    qualityScore += 20;
                    result.QualityStrengths.Add("Scan date/time is present");
                }
                else
                {
                    result.QualityIssues.Add("Scan date/time is missing");
                }

                // Scanner-specific validation (60 points)
                if (scannerData.ScannerType == "FS6000")
                {
                    var fs6000Scan = await _applicationDbContext.FS6000Scans
                        .FirstOrDefaultAsync(s => s.Id == scannerData.Id);

                    if (fs6000Scan != null)
                    {
                        qualityScore += await ValidateFS6000QualityAsync(fs6000Scan, result);
                    }
                }
                else if (scannerData.ScannerType == "ASE")
                {
                    var aseScan = await _applicationDbContext.AseScans
                        .FirstOrDefaultAsync(s => s.Id == scannerData.Id);

                    if (aseScan != null)
                    {
                        qualityScore += await ValidateASEQualityAsync(aseScan, result);
                    }
                }

                result.QualityScore = Math.Min(qualityScore, maxScore);
                result.IsValid = result.QualityScore >= 80 && result.QualityIssues.Count == 0;

                _logger.LogInformation("Scanner data quality validation completed for container {ContainerNumber}: Score {Score}, Valid {IsValid}",
                    scannerData.ContainerNumber, result.QualityScore, result.IsValid);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating scanner data quality for container: {ContainerNumber}", scannerData.ContainerNumber);
                throw;
            }
        }

        /// <summary>
        /// Gets scanner statistics
        /// </summary>
        public async Task<ScannerValidationStatistics> GetScannerStatisticsAsync()
        {
            try
            {
                _logger.LogInformation("Getting scanner statistics");

                var stats = new ScannerValidationStatistics();

                // Count FS6000 scans
                stats.FS6000Scans = await _applicationDbContext.FS6000Scans.CountAsync();
                stats.TotalScans += stats.FS6000Scans;

                // Count ASE scans
                stats.ASEScans = await _applicationDbContext.AseScans.CountAsync();
                stats.TotalScans += stats.ASEScans;

                // Get last scan date
                var lastFS6000Scan = await _applicationDbContext.FS6000Scans
                    .OrderByDescending(s => s.ScanTime)
                    .FirstOrDefaultAsync();

                var lastASEScan = await _applicationDbContext.AseScans
                    .OrderByDescending(s => s.ScanTime)
                    .FirstOrDefaultAsync();

                if (lastFS6000Scan != null && lastASEScan != null)
                {
                    stats.LastScanDate = lastFS6000Scan.ScanTime > lastASEScan.ScanTime
                        ? lastFS6000Scan.ScanTime
                        : lastASEScan.ScanTime;
                }
                else if (lastFS6000Scan != null)
                {
                    stats.LastScanDate = lastFS6000Scan.ScanTime;
                }
                else if (lastASEScan != null)
                {
                    stats.LastScanDate = lastASEScan.ScanTime;
                }

                // Calculate valid/invalid scans (simplified - could be enhanced)
                stats.ValidScans = stats.TotalScans; // Assume all are valid for now
                stats.InvalidScans = 0;

                // Build statistics by scanner type
                stats.ScansByScannerType["FS6000"] = stats.FS6000Scans;
                stats.ScansByScannerType["ASE"] = stats.ASEScans;

                _logger.LogInformation("Scanner statistics: {TotalScans} total scans, {FS6000Scans} FS6000, {ASEScans} ASE",
                    stats.TotalScans, stats.FS6000Scans, stats.ASEScans);

                return stats;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scanner statistics");
                throw;
            }
        }

        #region Private Helper Methods

        /// <summary>
        /// Validates FS6000 scanner data
        /// </summary>
        private Task<ScannerDataCompleteness> ValidateFS6000DataAsync(FS6000Scan fs6000Scan)
        {
            var completeness = new ScannerDataCompleteness
            {
                HasScannerData = true,
                ScannerType = "FS6000",
                ScanDateTime = fs6000Scan.ScanTime
            };

            var score = 0;
            var maxScore = 100;

            // Container number validation (25 points)
            if (!string.IsNullOrEmpty(fs6000Scan.ContainerNumber))
            {
                score += 25;
            }
            else
            {
                completeness.ValidationErrors.Add("Container number is missing");
            }

            // Scan time validation (25 points)
            if (fs6000Scan.ScanTime != default)
            {
                score += 25;
            }
            else
            {
                completeness.ValidationErrors.Add("Scan time is missing");
            }

            // File path validation (25 points)
            if (!string.IsNullOrEmpty(fs6000Scan.FilePath))
            {
                score += 25;
            }
            else
            {
                completeness.ValidationErrors.Add("File path is missing");
            }

            // Additional data validation (25 points)
            if (!string.IsNullOrEmpty(fs6000Scan.OperatorId))
            {
                score += 15;
            }
            else
            {
                completeness.MissingFields.Add("Operator ID");
            }

            if (!string.IsNullOrEmpty(fs6000Scan.VesselName))
            {
                score += 10;
            }
            else
            {
                completeness.MissingFields.Add("Vessel Name");
            }

            completeness.CompletenessScore = Math.Min(score, maxScore);
            completeness.IsDataComplete = completeness.CompletenessScore >= 80;

            return Task.FromResult(completeness);
        }

        /// <summary>
        /// Validates ASE scanner data
        /// </summary>
        private Task<ScannerDataCompleteness> ValidateASEDataAsync(AseScan aseScan)
        {
            var completeness = new ScannerDataCompleteness
            {
                HasScannerData = true,
                ScannerType = "ASE",
                ScanDateTime = aseScan.ScanTime
            };

            var score = 0;
            var maxScore = 100;

            // Container number validation (30 points)
            if (!string.IsNullOrEmpty(aseScan.ContainerNumber))
            {
                score += 30;
            }
            else
            {
                completeness.ValidationErrors.Add("Container number is missing");
            }

            // Scan time validation (30 points)
            if (aseScan.ScanTime != default)
            {
                score += 30;
            }
            else
            {
                completeness.ValidationErrors.Add("Scan time is missing");
            }

            // Inspection ID validation (20 points)
            if (aseScan.InspectionId > 0)
            {
                score += 20;
            }
            else
            {
                completeness.MissingFields.Add("Inspection ID");
            }

            // Additional data validation (20 points)
            if (aseScan.InspectionId > 0)
            {
                score += 10;
            }
            else
            {
                completeness.MissingFields.Add("Inspection ID");
            }

            if (!string.IsNullOrEmpty(aseScan.ImageDisplayName))
            {
                score += 10;
            }
            else
            {
                completeness.MissingFields.Add("Image Display Name");
            }

            completeness.CompletenessScore = Math.Min(score, maxScore);
            completeness.IsDataComplete = completeness.CompletenessScore >= 80;

            return Task.FromResult(completeness);
        }

        /// <summary>
        /// Validates FS6000 data quality
        /// </summary>
        private Task<int> ValidateFS6000QualityAsync(FS6000Scan fs6000Scan, ScannerDataQualityResult result)
        {
            var qualityScore = 0;

            // File path validation (20 points)
            if (!string.IsNullOrEmpty(fs6000Scan.FilePath))
            {
                qualityScore += 20;
                result.QualityStrengths.Add("File path is present");
            }
            else
            {
                result.QualityIssues.Add("File path is missing");
            }

            // Operator ID validation (20 points)
            if (!string.IsNullOrEmpty(fs6000Scan.OperatorId))
            {
                qualityScore += 20;
                result.QualityStrengths.Add("Operator ID is present");
            }
            else
            {
                result.QualityIssues.Add("Operator ID is missing");
            }

            // Vessel name validation (10 points)
            if (!string.IsNullOrEmpty(fs6000Scan.VesselName))
            {
                qualityScore += 10;
                result.QualityStrengths.Add("Vessel name is present");
            }
            else
            {
                result.QualityIssues.Add("Vessel name is missing");
            }

            // Sync status validation (10 points)
            if (!string.IsNullOrEmpty(fs6000Scan.SyncStatus))
            {
                qualityScore += 10;
                result.QualityStrengths.Add("Sync status is present");
            }
            else
            {
                result.QualityIssues.Add("Sync status is missing");
            }

            return Task.FromResult(qualityScore);
        }

        /// <summary>
        /// Validates ASE data quality
        /// </summary>
        private Task<int> ValidateASEQualityAsync(AseScan aseScan, ScannerDataQualityResult result)
        {
            var qualityScore = 0;

            // Inspection ID validation (30 points)
            if (aseScan.InspectionId > 0)
            {
                qualityScore += 30;
                result.QualityStrengths.Add("Inspection ID is present");
            }
            else
            {
                result.QualityIssues.Add("Inspection ID is missing");
            }

            // Image Display Name validation (20 points)
            if (!string.IsNullOrEmpty(aseScan.ImageDisplayName))
            {
                qualityScore += 20;
                result.QualityStrengths.Add("Image Display Name is present");
            }
            else
            {
                result.QualityIssues.Add("Image Display Name is missing");
            }

            // Truck Plate validation (10 points)
            if (!string.IsNullOrEmpty(aseScan.TruckPlate))
            {
                qualityScore += 10;
                result.QualityStrengths.Add("Truck Plate is present");
            }
            else
            {
                result.QualityIssues.Add("Truck Plate is missing");
            }

            return Task.FromResult(qualityScore);
        }

        #endregion
    }
}
