using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ContainerValidation;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// ✅ FIX RE-DOWNLOAD ISSUE: Background service that periodically reconciles 
    /// ContainerCompletenessStatuses with actual BOE data in ICUMS_Downloads database.
    /// This catches any containers that were missed during real-time updates.
    /// </summary>
    public class ContainerStatusReconciliationService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ContainerStatusReconciliationService> _logger;
        private readonly bool _cmrCompositeProgressionEnabled;
        private const string SERVICE_ID = "[STATUS-RECONCILIATION]";

        public ContainerStatusReconciliationService(
            IServiceProvider serviceProvider,
            ILogger<ContainerStatusReconciliationService> logger,
            IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _cmrCompositeProgressionEnabled = configuration.GetValue<bool>("CmrCompositeProgression:Enabled", false);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Wait 10 minutes after startup before first run
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);

            _logger.LogInformation("{ServiceId} Container Status Reconciliation Service started", SERVICE_ID);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ReconcileContainerStatusesAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} Error during status reconciliation", SERVICE_ID);
                }

                // Run every 6 hours
                await Task.Delay(TimeSpan.FromHours(6), stoppingToken);
            }
        }

        private async Task ReconcileContainerStatusesAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icumDownloadsRepository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();

            try
            {
                _logger.LogInformation("{ServiceId} 🔄 Starting container status reconciliation...", SERVICE_ID);

                // Get all containers marked as "Missing" but with HasICUMSData = false
                var missingContainers = await dbContext.ContainerCompletenessStatuses
                    .Where(c => c.Status == "Missing" && !c.HasICUMSData)
                    .ToListAsync(stoppingToken);

                if (missingContainers.Count == 0)
                {
                    _logger.LogInformation("{ServiceId} ✅ All statuses are in sync - no reconciliation needed", SERVICE_ID);
                    return;
                }

                _logger.LogInformation("{ServiceId} Found {Count} containers marked as 'Missing' - checking against actual BOE data",
                    SERVICE_ID, missingContainers.Count);

                var updatedCount = 0;
                var batchSize = 100;

                for (int i = 0; i < missingContainers.Count; i += batchSize)
                {
                    if (stoppingToken.IsCancellationRequested) break;

                    var batch = missingContainers.Skip(i).Take(batchSize).ToList();

                    foreach (var container in batch)
                    {
                        if (stoppingToken.IsCancellationRequested) break;

                        try
                        {
                            // Check if BOE data actually exists for this container and
                            // carry the canonical BOE identity back into CCS. Marking a
                            // row Complete without BOEDocumentId/GroupIdentifier creates
                            // dashboard integrity noise and breaks downstream grouping.
                            var boeRecords = await icumDownloadsRepository.GetBOEDocumentsByContainerNumberAsync(container.ContainerNumber);
                            var primaryBOE = boeRecords.CanonicalBoeQuery().FirstOrDefault();

                            if (primaryBOE != null && !ScannerLocationMap.IsLocationMatch(container.ScannerType ?? "", primaryBOE.DeliveryPlace))
                            {
                                var expectedPort = ScannerLocationMap.GetExpectedPortCode(container.ScannerType ?? "");
                                var actualPort = ScannerLocationMap.ExtractPortCode(primaryBOE.DeliveryPlace) ?? "UNKNOWN";
                                _logger.LogWarning(
                                    "{ServiceId} Skipping reconciliation for {ContainerNumber}: scanner={ScannerType} expected port {ExpectedPort}, BOE delivery place '{DeliveryPlace}' has port {ActualPort}",
                                    SERVICE_ID, container.ContainerNumber, container.ScannerType, expectedPort, primaryBOE.DeliveryPlace, actualPort);
                                continue;
                            }

                            if (primaryBOE != null && (!container.HasICUMSData || container.Status == "Missing"))
                            {
                                var groupIdentifier = primaryBOE.IsConsolidated
                                    ? container.ContainerNumber
                                    : primaryBOE.DeclarationNumber;

                                if (_cmrCompositeProgressionEnabled
                                    && string.Equals(primaryBOE.ClearanceType, "CMR", StringComparison.OrdinalIgnoreCase)
                                    && CmrCompositeKeyHelper.TryCreate(
                                        primaryBOE.RotationNumber,
                                        container.ContainerNumber,
                                        primaryBOE.BlNumber,
                                        out var cmrCompositeKey))
                                {
                                    groupIdentifier = cmrCompositeKey.OperationalKey;
                                }

                                var decision = ContainerCompletenessPolicy.Evaluate(
                                    hasScannerData: true,
                                    hasICUMSData: true,
                                    hasImageData: container.HasImageData,
                                    clearanceType: primaryBOE.ClearanceType,
                                    groupIdentifier: groupIdentifier,
                                    cmrCompositeProgressionEnabled: _cmrCompositeProgressionEnabled,
                                    cmrRotationNumber: primaryBOE.RotationNumber,
                                    cmrContainerNumber: container.ContainerNumber,
                                    cmrBlNumber: primaryBOE.BlNumber);

                                container.HasICUMSData = true;
                                container.ICUMSDataDate = DateTime.UtcNow;
                                container.BOEDocumentId = primaryBOE.Id;
                                container.ClearanceType = primaryBOE.ClearanceType;
                                container.IsConsolidated = primaryBOE.IsConsolidated;
                                container.GroupIdentifier = groupIdentifier;
                                container.Status = decision.Status;
                                container.WorkflowStage = decision.WorkflowStage;
                                container.ICUMSDataCompleteness = 100;
                                container.OverallCompleteness = (container.ScannerDataCompleteness + 100 + container.ImageDataCompleteness) / 3;
                                container.ErrorMessage = null;
                                container.UpdatedAt = DateTime.UtcNow;
                                updatedCount++;

                                _logger.LogDebug("{ServiceId} ✓ Reconciled {ContainerNumber}: Missing → {Status} (BOE={BOEId}, Group={GroupIdentifier})",
                                    SERVICE_ID, container.ContainerNumber, container.Status, primaryBOE.Id, groupIdentifier ?? "NULL");
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "{ServiceId} Error checking container {ContainerNumber}",
                                SERVICE_ID, container.ContainerNumber);
                        }
                    }

                    // Save batch changes
                    if (updatedCount > 0)
                    {
                        await dbContext.SaveChangesAsync(stoppingToken);
                        _logger.LogInformation("{ServiceId} 💾 Saved batch: {UpdatedInBatch} records updated",
                            SERVICE_ID, updatedCount);
                    }
                }

                if (updatedCount > 0)
                {
                    _logger.LogInformation("{ServiceId} ✅ Reconciliation complete: Updated {Count} container statuses from 'Missing' to 'Complete'",
                        SERVICE_ID, updatedCount);
                }
                else
                {
                    _logger.LogInformation("{ServiceId} ℹ️ No status updates needed - all records are accurate", SERVICE_ID);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Fatal error during reconciliation", SERVICE_ID);
            }
        }
    }
}

