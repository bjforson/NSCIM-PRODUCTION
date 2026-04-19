using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.Validation
{
    /// <summary>
    /// Service for validating CMR (Export) records to ensure they have required composite key fields
    /// </summary>
    public class CMRValidationService : ICMRValidationService
    {
        private readonly IcumDownloadsDbContext _context;
        private readonly ILogger<CMRValidationService> _logger;

        public CMRValidationService(
            IcumDownloadsDbContext context,
            ILogger<CMRValidationService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public Task<CMRValidationResult> ValidateCMRRecordAsync(BOEDocument boeDocument)
        {
            var result = new CMRValidationResult
            {
                ContainerNumber = boeDocument.ContainerNumber ?? string.Empty,
                ClearanceType = boeDocument.ClearanceType ?? string.Empty
            };

            try
            {
                // CMR records must have BL Number, Rotation Number, and Container Number
                var missingFields = new List<string>();

                if (string.IsNullOrWhiteSpace(boeDocument.BlNumber))
                {
                    missingFields.Add("BlNumber");
                }

                if (string.IsNullOrWhiteSpace(boeDocument.RotationNumber))
                {
                    missingFields.Add("RotationNumber");
                }

                if (string.IsNullOrWhiteSpace(boeDocument.ContainerNumber))
                {
                    missingFields.Add("ContainerNumber");
                }

                result.MissingFields = missingFields;
                result.IsValid = missingFields.Count == 0;

                if (result.IsValid)
                {
                    result.ValidationMessage = "CMR record is valid - all required fields present";
                    _logger.LogDebug("✅ CMR validation passed for container {Container}", boeDocument.ContainerNumber);
                }
                else
                {
                    result.ValidationMessage = $"CMR record is invalid - missing fields: {string.Join(", ", missingFields)}";
                    result.Warnings.Add($"Container {boeDocument.ContainerNumber} is missing critical fields for CMR processing");

                    _logger.LogWarning("❌ CMR validation failed for container {Container} - Missing: {MissingFields}",
                        boeDocument.ContainerNumber, string.Join(", ", missingFields));
                }

                return Task.FromResult(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error validating CMR record for container {Container}", boeDocument.ContainerNumber);
                result.IsValid = false;
                result.ValidationMessage = $"Validation error: {ex.Message}";
                result.Warnings.Add($"Validation failed due to error: {ex.Message}");
                return Task.FromResult(result);
            }
        }

        public async Task<CMRBatchValidationResult> ValidateCMRBatchAsync(List<BOEDocument> boeDocuments)
        {
            var result = new CMRBatchValidationResult
            {
                TotalRecords = boeDocuments.Count
            };

            try
            {
                _logger.LogInformation("Starting batch CMR validation for {Count} records", boeDocuments.Count);

                var validationResults = new List<CMRValidationResult>();
                var summaryWarnings = new List<string>();

                foreach (var boeDocument in boeDocuments)
                {
                    var validationResult = await ValidateCMRRecordAsync(boeDocument);
                    validationResults.Add(validationResult);

                    if (!validationResult.IsValid)
                    {
                        result.InvalidRecords++;
                        summaryWarnings.Add($"Container {boeDocument.ContainerNumber}: Missing {string.Join(", ", validationResult.MissingFields)}");
                    }
                    else
                    {
                        result.ValidRecords++;
                    }
                }

                result.ValidationResults = validationResults;
                result.SummaryWarnings = summaryWarnings;

                _logger.LogInformation("Batch CMR validation completed - Valid: {Valid}, Invalid: {Invalid}",
                    result.ValidRecords, result.InvalidRecords);

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during batch CMR validation");
                result.SummaryWarnings.Add($"Batch validation failed: {ex.Message}");
                return result;
            }
        }

        public async Task<List<ProblematicCMRRecord>> GetProblematicCMRRecordsAsync()
        {
            try
            {
                _logger.LogInformation("Fetching problematic CMR records");

                var problematicRecords = await _context.BOEDocuments
                    .Where(b => b.ClearanceType == "CMR" &&
                               (string.IsNullOrEmpty(b.BlNumber) ||
                                string.IsNullOrEmpty(b.RotationNumber) ||
                                string.IsNullOrEmpty(b.ContainerNumber)))
                    .Select(b => new ProblematicCMRRecord
                    {
                        ContainerNumber = b.ContainerNumber ?? string.Empty,
                        DeclarationNumber = b.DeclarationNumber ?? string.Empty,
                        ClearanceType = b.ClearanceType ?? string.Empty,
                        BlNumber = b.BlNumber,
                        RotationNumber = b.RotationNumber,
                        CreatedAt = b.CreatedAt,
                        LastUpdatedAt = b.UpdatedAt,
                        NeedsRedownload = true
                    })
                    .ToListAsync();

                // Determine missing fields for each record
                foreach (var record in problematicRecords)
                {
                    var missingFields = new List<string>();

                    if (string.IsNullOrWhiteSpace(record.BlNumber))
                        missingFields.Add("BlNumber");

                    if (string.IsNullOrWhiteSpace(record.RotationNumber))
                        missingFields.Add("RotationNumber");

                    if (string.IsNullOrWhiteSpace(record.ContainerNumber))
                        missingFields.Add("ContainerNumber");

                    record.MissingFields = missingFields;
                }

                _logger.LogInformation("Found {Count} problematic CMR records", problematicRecords.Count);
                return problematicRecords;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching problematic CMR records");
                return new List<ProblematicCMRRecord>();
            }
        }

        /// <summary>
        /// Get CMR validation statistics using efficient SQL aggregates (no ToListAsync).
        /// Single query returns all counts - avoids loading full table into memory.
        /// </summary>
        public async Task<CMRValidationStatistics> GetCMRValidationStatisticsAsync()
        {
            try
            {
                _logger.LogInformation("Calculating CMR validation statistics");

                // Single SQL query with conditional aggregates - no table load
                var sql = @"
SELECT 
    COUNT(*)::int AS ""TotalCMRRecords"",
    COUNT(CASE WHEN TRIM(COALESCE(blnumber,'')) <> '' 
          AND TRIM(COALESCE(rotationnumber,'')) <> '' 
          AND TRIM(COALESCE(containernumber,'')) <> '' THEN 1 END)::int AS ""ValidRecords"",
    COUNT(CASE WHEN blnumber IS NULL OR TRIM(COALESCE(blnumber,'')) = '' THEN 1 END)::int AS ""MissingBlNumber"",
    COUNT(CASE WHEN rotationnumber IS NULL OR TRIM(COALESCE(rotationnumber,'')) = '' THEN 1 END)::int AS ""MissingRotationNumber"",
    COUNT(CASE WHEN (blnumber IS NULL OR TRIM(COALESCE(blnumber,'')) = '')
          AND (rotationnumber IS NULL OR TRIM(COALESCE(rotationnumber,'')) = '') THEN 1 END)::int AS ""MissingBothFields""
FROM boedocuments
WHERE clearancetype = 'CMR'";

                var result = await _context.Database.SqlQueryRaw<CMRStatsRow>(sql).ToListAsync();
                var row = result.FirstOrDefault();

                var totalCMRRecords = row?.TotalCMRRecords ?? 0;
                var validRecords = row?.ValidRecords ?? 0;
                var invalidRecords = totalCMRRecords - validRecords;
                var missingBlNumber = row?.MissingBlNumber ?? 0;
                var missingRotationNumber = row?.MissingRotationNumber ?? 0;
                var missingBothFields = row?.MissingBothFields ?? 0;
                var successRate = totalCMRRecords > 0 ? (double)validRecords / totalCMRRecords * 100 : 0;

                var statistics = new CMRValidationStatistics
                {
                    TotalCMRRecords = totalCMRRecords,
                    ValidCMRRecords = validRecords,
                    InvalidCMRRecords = invalidRecords,
                    MissingBlNumber = missingBlNumber,
                    MissingRotationNumber = missingRotationNumber,
                    MissingBothFields = missingBothFields,
                    ValidationSuccessRate = successRate,
                    LastValidationRun = DateTime.UtcNow
                };

                _logger.LogInformation("CMR validation statistics - Total: {Total}, Valid: {Valid}, Invalid: {Invalid}, Success Rate: {Rate:F2}%",
                    totalCMRRecords, validRecords, invalidRecords, successRate);

                return statistics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating CMR validation statistics");
                return new CMRValidationStatistics();
            }
        }

        private class CMRStatsRow
        {
            public int TotalCMRRecords { get; set; }
            public int ValidRecords { get; set; }
            public int MissingBlNumber { get; set; }
            public int MissingRotationNumber { get; set; }
            public int MissingBothFields { get; set; }
        }
    }
}
