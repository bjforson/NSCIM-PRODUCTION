using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageSplitter;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Service for validating multi-container scans and detecting cross-record scenarios.
    /// 2.15.4 — detection now also submits the scan to the Python image splitter
    /// automatically, so analysts land in the viewer with both candidate splits
    /// pre-computed. Prior to 2.15.4 this was a manual out-of-band step run via
    /// <c>services/image-splitter/submit_backlog.py</c>, which left most
    /// cross-records unsplit until someone noticed.
    /// </summary>
    public class MultiContainerValidationService
    {
        private readonly ILogger<MultiContainerValidationService> _logger;
        private readonly IImageSplitterService _imageSplitter;
        private readonly IImageProcessingService _imageProcessing;
        private const string SERVICE_ID = "[MULTI-CONTAINER-VALIDATION]";

        public MultiContainerValidationService(
            ILogger<MultiContainerValidationService> logger,
            IImageSplitterService imageSplitter,
            IImageProcessingService imageProcessing)
        {
            _logger = logger;
            _imageSplitter = imageSplitter;
            _imageProcessing = imageProcessing;
        }

        /// <summary>
        /// Validates if a multi-container scan contains containers from different records
        /// </summary>
        public async Task<MultiContainerValidationResult> ValidateMultiContainerScanAsync(
            string multiContainerString,
            ApplicationDbContext dbContext,
            IcumDownloadsDbContext icumsContext)
        {
            var containers = multiContainerString
                .Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            var result = new MultiContainerValidationResult
            {
                Container1 = containers.ElementAtOrDefault(0) ?? string.Empty,
                Container2 = containers.ElementAtOrDefault(1) ?? string.Empty
            };

            if (containers.Count != 2)
            {
                _logger.LogDebug("{ServiceId} Scan {Scan} does not have exactly 2 containers, skipping validation",
                    SERVICE_ID, multiContainerString);
                result.IsSameRecord = true; // Treat as normal
                return result;
            }

            // Get BOE data for both containers
            var boe1 = await icumsContext.BOEDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.ContainerNumber == containers[0]);

            var boe2 = await icumsContext.BOEDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.ContainerNumber == containers[1]);

            if (boe1 == null || boe2 == null)
            {
                // One or both containers don't have BOE data yet
                result.PendingBOEData = true;
                _logger.LogDebug("{ServiceId} BOE data pending for {Scan} - BOE1: {Has1}, BOE2: {Has2}",
                    SERVICE_ID, multiContainerString, boe1 != null, boe2 != null);
                return result;
            }

            // ✅ PERFORM COMPARISONS
            result.SameDeclaration = string.Equals(
                boe1.DeclarationNumber,
                boe2.DeclarationNumber,
                StringComparison.OrdinalIgnoreCase);

            result.SameConsignee = string.Equals(
                boe1.ConsigneeName,
                boe2.ConsigneeName,
                StringComparison.OrdinalIgnoreCase);

            result.SameMasterBL = string.Equals(
                boe1.BlNumber,
                boe2.BlNumber,
                StringComparison.OrdinalIgnoreCase);

            result.SameRotation = string.Equals(
                boe1.RotationNumber,
                boe2.RotationNumber,
                StringComparison.OrdinalIgnoreCase);

            result.SameCRMS = string.Equals(
                boe1.CrmsLevel,
                boe2.CrmsLevel,
                StringComparison.OrdinalIgnoreCase);

            result.SameClearanceType = string.Equals(
                boe1.ClearanceType,
                boe2.ClearanceType,
                StringComparison.OrdinalIgnoreCase);

            // ✅ DETERMINE IF SAME RECORD
            // Containers belong to same record if they share:
            // - Same Declaration Number OR
            // - Same Master BL Number (for consolidated cargo)
            result.IsSameRecord = result.SameDeclaration || result.SameMasterBL;

            if (!result.IsSameRecord)
            {
                // 🚨 CROSS-RECORD DETECTED
                result.RequiresSpecialTracking = true;
                result.CrossRecordType = DetermineCrossRecordType(boe1, boe2, result);

                // Build warnings
                if (!result.SameConsignee)
                {
                    result.AddCritical($"DIFFERENT IMPORTERS: '{boe1.ConsigneeName}' vs '{boe2.ConsigneeName}'");
                }

                if (!result.SameCRMS)
                {
                    result.AddWarning($"MIXED CRMS LEVELS: '{boe1.CrmsLevel}' vs '{boe2.CrmsLevel}'");
                }

                if (!result.SameClearanceType)
                {
                    result.AddCritical($"MIXED CLEARANCE TYPES: '{boe1.ClearanceType}' vs '{boe2.ClearanceType}'");
                }

                if (!result.SameDeclaration)
                {
                    result.AddWarning($"DIFFERENT BOEs: '{boe1.DeclarationNumber}' vs '{boe2.DeclarationNumber}'");
                }

                _logger.LogWarning(
                    "{ServiceId} 🚨 CROSS-RECORD SCAN DETECTED: {Scan}\n" +
                    "  Container 1: {C1} → BOE: {BOE1}, Consignee: {Cons1}, CRMS: {CRMS1}\n" +
                    "  Container 2: {C2} → BOE: {BOE2}, Consignee: {Cons2}, CRMS: {CRMS2}\n" +
                    "  Type: {Type}",
                    SERVICE_ID, multiContainerString,
                    containers[0], boe1.DeclarationNumber, boe1.ConsigneeName, boe1.CrmsLevel,
                    containers[1], boe2.DeclarationNumber, boe2.ConsigneeName, boe2.CrmsLevel,
                    result.CrossRecordType);
            }
            else
            {
                // ✅ Same record - normal processing
                _logger.LogInformation(
                    "{ServiceId} ✅ Same-record scan: {Scan} - Declaration: {Declaration}",
                    SERVICE_ID, multiContainerString,
                    boe1.DeclarationNumber ?? boe1.BlNumber);
            }

            return result;
        }

        /// <summary>
        /// Determines the type and severity of cross-record issue
        /// </summary>
        private CrossRecordType DetermineCrossRecordType(
            BOEDocument boe1,
            BOEDocument boe2,
            MultiContainerValidationResult result)
        {
            // Priority order (most severe first):
            // 1. Different Importers (privacy/compliance risk)
            // 2. Different Clearance Types (process violation)
            // 3. Different Risk Levels (operational complexity)
            // 4. Just Different BOEs (same importer, different shipments)

            if (!result.SameConsignee)
                return CrossRecordType.DifferentImporters;

            if (!result.SameClearanceType)
                return CrossRecordType.DifferentClearanceTypes;

            if (!result.SameCRMS)
                return CrossRecordType.DifferentRiskLevels;

            return CrossRecordType.DifferentBOEs;
        }

        /// <summary>
        /// Creates a CrossRecordScan tracking record
        /// </summary>
        public async Task<CrossRecordScan> CreateCrossRecordTrackingAsync(
            string multiContainerString,
            Guid scannerRecordId,
            string scannerType,
            DateTime scanDateTime,
            MultiContainerValidationResult validation,
            ApplicationDbContext dbContext,
            IcumDownloadsDbContext icumsContext)
        {
            // Get BOE details for both containers
            var boe1 = await icumsContext.BOEDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.ContainerNumber == validation.Container1);

            var boe2 = await icumsContext.BOEDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.ContainerNumber == validation.Container2);

            var crossRecord = new CrossRecordScan
            {
                OriginalScanRecord = multiContainerString,
                ScannerRecordId = scannerRecordId,
                ScannerType = scannerType,
                ScanDateTime = scanDateTime,

                // Container 1
                Container1 = validation.Container1,
                Container1_BOE = boe1?.DeclarationNumber,
                Container1_Consignee = boe1?.ConsigneeName,
                Container1_CRMS = boe1?.CrmsLevel,
                Container1_ClearanceType = boe1?.ClearanceType,
                Container1_MasterBL = boe1?.BlNumber,
                Container1_Rotation = boe1?.RotationNumber,

                // Container 2
                Container2 = validation.Container2,
                Container2_BOE = boe2?.DeclarationNumber,
                Container2_Consignee = boe2?.ConsigneeName,
                Container2_CRMS = boe2?.CrmsLevel,
                Container2_ClearanceType = boe2?.ClearanceType,
                Container2_MasterBL = boe2?.BlNumber,
                Container2_Rotation = boe2?.RotationNumber,

                // Classification
                CrossRecordType = validation.CrossRecordType.ToString(),
                Severity = DetermineSeverity(validation.CrossRecordType),
                RequiresReview = validation.CrossRecordType == CrossRecordType.DifferentImporters,

                // Comparisons
                SameDeclaration = validation.SameDeclaration,
                SameConsignee = validation.SameConsignee,
                SameMasterBL = validation.SameMasterBL,
                SameRotation = validation.SameRotation,
                SameCRMS = validation.SameCRMS,
                SameClearanceType = validation.SameClearanceType,

                ReviewStatus = "Pending",
                CreatedAt = DateTime.UtcNow
            };

            dbContext.CrossRecordScans.Add(crossRecord);
            await dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "{ServiceId} ✅ Created cross-record tracking for {Scan} - Type: {Type}, Severity: {Severity}",
                SERVICE_ID, multiContainerString, crossRecord.CrossRecordType, crossRecord.Severity);

            // 2.15.4 — auto-submit to the image splitter so analysts see two
            // candidate splits the instant they open the viewer, instead of
            // the unsplit composite. Failures are non-fatal: detection still
            // succeeds, and a future retry sweep (or the manual
            // services/image-splitter/submit_backlog.py script) can backfill.
            try
            {
                var jobRef = await SubmitSplitJobForCrossRecordAsync(crossRecord);
                if (jobRef != null)
                {
                    crossRecord.SplitJobId = jobRef.JobId;
                    dbContext.Entry(crossRecord).State = EntityState.Modified;
                    await dbContext.SaveChangesAsync();
                    _logger.LogInformation(
                        "{ServiceId} ✅ Split job {JobId} submitted for cross-record {CrId} ({C1} + {C2}).",
                        SERVICE_ID, jobRef.JobId, crossRecord.Id, crossRecord.Container1, crossRecord.Container2);
                }
                else
                {
                    _logger.LogWarning(
                        "{ServiceId} ⚠️ Split submission returned no job id for cross-record {CrId} ({C1} + {C2}); will require retry.",
                        SERVICE_ID, crossRecord.Id, crossRecord.Container1, crossRecord.Container2);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "{ServiceId} ⚠️ Split submission failed for cross-record {CrId} ({C1} + {C2}); row persisted without SplitJobId, will retry on next sweep or via submit_backlog.py.",
                    SERVICE_ID, crossRecord.Id, crossRecord.Container1, crossRecord.Container2);
            }

            return crossRecord;
        }

        /// <summary>
        /// Fetches the scan's composite image bytes and POSTs them to the Python
        /// image splitter (<c>POST /api/split/upload</c>) via
        /// <see cref="IImageSplitterService"/>. Returns the new job reference or
        /// <c>null</c> if the image couldn't be fetched or the splitter rejected
        /// the submission.
        ///
        /// Uses the scan's *primary* container number as the lookup key for
        /// <see cref="IImageProcessingService.GetCompleteContainerDataAsync(string, string?)"/>
        /// — for multi-container scans both container numbers resolve to the
        /// same physical image file, so either works.
        /// </summary>
        private async Task<SplitJobReference?> SubmitSplitJobForCrossRecordAsync(CrossRecordScan crossRecord)
        {
            if (string.IsNullOrWhiteSpace(crossRecord.Container1) || string.IsNullOrWhiteSpace(crossRecord.Container2))
            {
                _logger.LogDebug("{ServiceId} Skip split submission — container numbers missing on cross-record {CrId}.",
                    SERVICE_ID, crossRecord.Id);
                return null;
            }

            var completeData = await _imageProcessing.GetCompleteContainerDataAsync(
                crossRecord.Container1, crossRecord.ScannerType);
            if (completeData?.ImageBytes == null || completeData.ImageBytes.Length == 0)
            {
                _logger.LogWarning(
                    "{ServiceId} ⚠️ No image bytes available for cross-record {CrId} container {Container} (scanner={Scanner}); cannot submit split job.",
                    SERVICE_ID, crossRecord.Id, crossRecord.Container1, crossRecord.ScannerType);
                return null;
            }

            var containerCsv = $"{crossRecord.Container1},{crossRecord.Container2}";
            return await _imageSplitter.SubmitSplitJobAsync(
                containerCsv,
                completeData.ImageBytes,
                sourceImageId: crossRecord.ScannerRecordId,
                scannerType: crossRecord.ScannerType);
        }

        private string DetermineSeverity(CrossRecordType type) => type switch
        {
            CrossRecordType.DifferentImporters => "High",
            CrossRecordType.DifferentClearanceTypes => "High",
            CrossRecordType.DifferentRiskLevels => "Medium",
            CrossRecordType.DifferentBOEs => "Low",
            _ => "Low"
        };
    }
}

