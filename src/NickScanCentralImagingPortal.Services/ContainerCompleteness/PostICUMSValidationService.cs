using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Background service that validates multi-container scans AFTER ICUMS downloads complete.
    /// Detects cross-record scenarios and creates tracking records.
    /// </summary>
    public class PostICUMSValidationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<PostICUMSValidationService> _logger;
        private const string SERVICE_ID = "[POST-ICUMS-VALIDATION]";

        public PostICUMSValidationService(
            IServiceProvider serviceProvider,
            ILogger<PostICUMSValidationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Random startup delay to prevent all services starting simultaneously
            var startupDelay = Random.Shared.Next(2000, 8000);
            await Task.Delay(startupDelay, stoppingToken);

            _logger.LogInformation("{ServiceId} Post-ICUMS Validation Service started", SERVICE_ID);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ValidatePendingMultiContainerScansAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} Error during validation cycle", SERVICE_ID);
                }

                // Check every 5 minutes (reduced from 10 for faster processing)
                await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
            }

            _logger.LogInformation("{ServiceId} Post-ICUMS Validation Service stopped", SERVICE_ID);
        }

        private async Task ValidatePendingMultiContainerScansAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icumsContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
            var validationService = scope.ServiceProvider.GetRequiredService<MultiContainerValidationService>();

            _logger.LogInformation("{ServiceId} Starting validation cycle", SERVICE_ID);

            try
            {
                // Find multi-container completeness records that:
                // 1. Have comma-separated container numbers
                // 2. Have NOT been validated yet (Status != "Complete-CrossRecord" and != "Complete")
                // 3. Are not already in CrossRecordScans table
                // 4. Include records with various statuses that might need validation

                var multiContainerRecords = await dbContext.ContainerCompletenessStatuses
                    .Where(c => c.ContainerNumber.Contains(",") &&
                                c.Status != "Complete-CrossRecord" &&
                                c.Status != "Complete" &&
                                // Also check records that haven't been validated recently
                                (c.UpdatedAt == null || c.UpdatedAt < DateTime.UtcNow.AddMinutes(-30)))
                    .OrderByDescending(c => c.ScanDate) // Process newest first
                    .Take(100) // Process 100 per cycle (increased from 50)
                    .ToListAsync(stoppingToken);

                if (!multiContainerRecords.Any())
                {
                    _logger.LogDebug("{ServiceId} No multi-container scans pending validation", SERVICE_ID);
                    return;
                }

                _logger.LogInformation("{ServiceId} Found {Count} multi-container scans to validate",
                    SERVICE_ID, multiContainerRecords.Count);

                var validatedCount = 0;
                var crossRecordCount = 0;
                var sameRecordCount = 0;
                var pendingBOECount = 0;

                foreach (var record in multiContainerRecords)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    try
                    {
                        // Check if already validated (exists in CrossRecordScans)
                        var alreadyTracked = await dbContext.CrossRecordScans
                            .AnyAsync(cr => cr.OriginalScanRecord == record.ContainerNumber, stoppingToken);

                        if (alreadyTracked)
                        {
                            // Already validated and tracked - update status if needed
                            if (record.Status != "Complete-CrossRecord")
                            {
                                record.Status = "Complete-CrossRecord";
                                record.UpdatedAt = DateTime.UtcNow;
                            }
                            continue;
                        }

                        // Validate the multi-container scan
                        var validation = await validationService.ValidateMultiContainerScanAsync(
                            record.ContainerNumber,
                            dbContext,
                            icumsContext);

                        if (validation.PendingBOEData)
                        {
                            // Still waiting for BOE data
                            record.Status = "Pending-Validation";
                            record.UpdatedAt = DateTime.UtcNow;
                            pendingBOECount++;
                            continue;
                        }

                        if (!validation.IsSameRecord && validation.RequiresSpecialTracking)
                        {
                            // 🚨 CROSS-RECORD DETECTED - Create tracking record
                            var scannerRecordId = await GetScannerRecordId(
                                record.ContainerNumber,
                                record.ScannerType,
                                dbContext);

                            if (scannerRecordId != Guid.Empty)
                            {
                                await validationService.CreateCrossRecordTrackingAsync(
                                    record.ContainerNumber,
                                    scannerRecordId,
                                    record.ScannerType,
                                    record.ScanDate,
                                    validation,
                                    dbContext,
                                    icumsContext);

                                // Update parent record status
                                record.Status = "Complete-CrossRecord";
                                record.HasICUMSData = true;
                                record.UpdatedAt = DateTime.UtcNow;

                                crossRecordCount++;
                                _logger.LogWarning(
                                    "{ServiceId} 🚨 Cross-record scan flagged: {Scan}",
                                    SERVICE_ID, record.ContainerNumber);
                            }
                        }
                        else
                        {
                            // ✅ Same record - normal processing
                            record.Status = "Complete";
                            record.HasICUMSData = true;
                            record.UpdatedAt = DateTime.UtcNow;
                            sameRecordCount++;
                        }

                        validatedCount++;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex,
                            "{ServiceId} Error validating multi-container scan: {Scan}",
                            SERVICE_ID, record.ContainerNumber);
                    }
                }

                // Save all changes
                await dbContext.SaveChangesAsync(stoppingToken);

                _logger.LogInformation(
                    "{ServiceId} Validation cycle complete:\n" +
                    "  ✅ Validated: {Validated}\n" +
                    "  🚨 Cross-Record: {CrossRecord}\n" +
                    "  ✅ Same-Record: {SameRecord}\n" +
                    "  ⏳ Pending BOE: {Pending}",
                    SERVICE_ID, validatedCount, crossRecordCount, sameRecordCount, pendingBOECount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error in validation cycle", SERVICE_ID);
                throw;
            }
        }

        /// <summary>
        /// Gets the scanner record GUID for linking
        /// </summary>
        private async Task<Guid> GetScannerRecordId(
            string multiContainerString,
            string scannerType,
            ApplicationDbContext dbContext)
        {
            try
            {
                switch (scannerType.ToUpper())
                {
                    case "FS6000":
                        var fs6000Scan = await dbContext.FS6000Scans
                            .FirstOrDefaultAsync(s => s.ContainerNumber == multiContainerString);
                        return fs6000Scan?.Id ?? Guid.Empty;

                    case "ASE":
                        var aseScan = await dbContext.AseScans
                            .FirstOrDefaultAsync(s => s.ContainerNumber == multiContainerString);
                        return aseScan?.Id ?? Guid.Empty;

                    default:
                        _logger.LogWarning("{ServiceId} Unknown scanner type: {Type}", SERVICE_ID, scannerType);
                        return Guid.Empty;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "{ServiceId} Error getting scanner record ID for {Scan}",
                    SERVICE_ID, multiContainerString);
                return Guid.Empty;
            }
        }
    }
}

