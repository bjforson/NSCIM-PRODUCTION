using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    public class IcumDataTransferService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<IcumDataTransferService> _logger;
        private const string SERVICE_ID = "[ICUMS-DATA-TRANSFER]";

        public IcumDataTransferService(
            IServiceProvider serviceProvider,
            ILogger<IcumDataTransferService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("{ServiceId} ICUMS Data Transfer Service started", SERVICE_ID);

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await TransferProcessedDataAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "{ServiceId} Error in ICUMS Data Transfer Service", SERVICE_ID);
                }

                // Transfer at configured interval (read from database settings)
                if (stoppingToken.IsCancellationRequested) break;
                try
                {
                    using var scope = _serviceProvider.CreateScope();
                    var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                    var transferInterval = await settingsProvider.GetIntAsync("BackgroundServices", "IcumDataTransferService.TransferIntervalMinutes", 5);
                    _logger.LogDebug("⏰ Next ICUMS data transfer in {Interval} minutes (from settings)", transferInterval);
                    await Task.Delay(TimeSpan.FromMinutes(transferInterval), stoppingToken);
                }
                catch (ObjectDisposedException)
                {
                    // Host is shutting down; exit gracefully
                    break;
                }
            }
        }

        private async Task TransferProcessedDataAsync(CancellationToken stoppingToken)
        {
            if (stoppingToken.IsCancellationRequested) return;
            using var scope = _serviceProvider.CreateScope();
            var downloadsRepository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();
            var icumRepository = scope.ServiceProvider.GetRequiredService<IIcumRepository>();

            try
            {
                _logger.LogInformation("{ServiceId} Starting ICUMS data transfer from downloads to main database", SERVICE_ID);

                // Get completed BOE documents that haven't been transferred yet
                var completedDocuments = await downloadsRepository.GetPendingBOEDocumentsAsync();

                if (!completedDocuments.Any())
                {
                    _logger.LogDebug("No completed BOE documents to transfer");
                    return;
                }

                _logger.LogInformation("{ServiceId} Found {Count} completed BOE documents to transfer", SERVICE_ID, completedDocuments.Count);

                var transferredCount = 0;
                var transferredItemsCount = 0;

                foreach (var boeDoc in completedDocuments)
                {
                    try
                    {
                        // Use a fresh scope for each BOE document to avoid Entity Framework tracking conflicts
                        if (stoppingToken.IsCancellationRequested) break;
                        using var documentScope = _serviceProvider.CreateScope();
                        var scopedDownloadsRepository = documentScope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();
                        var scopedIcumRepository = documentScope.ServiceProvider.GetRequiredService<IIcumRepository>();

                        // Get manifest items for this specific BOE document (PERFORMANCE FIX: query by ID instead of loading all)
                        var itemsForThisDocument = await scopedDownloadsRepository.GetManifestItemsByBOEDocumentIdAsync(boeDoc.Id);

                        _logger.LogDebug("{ServiceId} BOE Document {DocId} ({Container}) has {ItemCount} manifest items ready for transfer",
                            SERVICE_ID, boeDoc.Id, boeDoc.ContainerNumber, itemsForThisDocument.Count);

                        // Convert to main database format
                        var containerData = ConvertToIcumContainerData(boeDoc);
                        var icumManifestItems = itemsForThisDocument.Select(ConvertToIcumManifestItem).ToList();

                        // Save to main ICUMS database
                        await scopedIcumRepository.SaveContainerDataWithItemsAsync(containerData, icumManifestItems);

                        // Mark as transferred
                        await scopedDownloadsRepository.UpdateBOEDocumentProcessingStatusAsync(boeDoc.Id, "Transferred");

                        foreach (var item in itemsForThisDocument)
                        {
                            await scopedDownloadsRepository.UpdateManifestItemProcessingStatusAsync(item.Id, "Transferred");
                        }

                        transferredCount++;
                        transferredItemsCount += itemsForThisDocument.Count;

                        if (itemsForThisDocument.Count > 0)
                        {
                            _logger.LogInformation("{ServiceId} ✓ Transferred {ClearanceType} container {ContainerNumber} with {ItemCount} manifest items",
                                SERVICE_ID, boeDoc.ClearanceType ?? "UNKNOWN", boeDoc.ContainerNumber, itemsForThisDocument.Count);
                        }
                        else
                        {
                            // CMR records are expected to have 0 items (pre-clearance manifests)
                            // IM/EX records with 0 items may indicate a data issue
                            if (boeDoc.ClearanceType == "CMR")
                            {
                                _logger.LogDebug("{ServiceId} ℹ Transferred CMR container {ContainerNumber} with 0 manifest items (expected - pre-clearance manifest)",
                                    SERVICE_ID, boeDoc.ContainerNumber);
                            }
                            else if (boeDoc.ClearanceType == "IM" || boeDoc.ClearanceType == "EX")
                            {
                                _logger.LogWarning("{ServiceId} ⚠ Transferred {ClearanceType} container {ContainerNumber} with 0 manifest items (possible data issue)",
                                    SERVICE_ID, boeDoc.ClearanceType, boeDoc.ContainerNumber);
                            }
                            else
                            {
                                _logger.LogWarning("{ServiceId} ⚠ Transferred container {ContainerNumber} with unknown clearance type '{ClearanceType}' and 0 manifest items",
                                    SERVICE_ID, boeDoc.ContainerNumber, boeDoc.ClearanceType ?? "NULL");
                            }
                        }
                    }
                    catch (ObjectDisposedException)
                    {
                        // Host shutting down; stop processing
                        break;
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{ServiceId} ✗ Failed to transfer BOE document {DocumentId} (Container: {ContainerNumber}): {Error}",
                            SERVICE_ID, boeDoc.Id, boeDoc.ContainerNumber, ex.Message);

                        await downloadsRepository.UpdateBOEDocumentProcessingStatusAsync(boeDoc.Id, "TransferFailed", ex.Message);
                    }
                }

                if (transferredItemsCount > 0)
                {
                    _logger.LogInformation("{ServiceId} ✓ Completed ICUMS data transfer: {Documents} documents, {Items} manifest items transferred",
                        SERVICE_ID, transferredCount, transferredItemsCount);
                }
                else if (transferredCount > 0)
                {
                    _logger.LogWarning("{ServiceId} ⚠ Completed ICUMS data transfer: {Documents} documents transferred but 0 manifest items (CHECK DATA INTEGRITY)",
                        SERVICE_ID, transferredCount);
                }
                else
                {
                    _logger.LogDebug("{ServiceId} No data transferred in this cycle", SERVICE_ID);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error transferring ICUMS data");
            }
        }

        private IcumContainerData ConvertToIcumContainerData(BOEDocument boeDoc)
        {
            return new IcumContainerData
            {
                ContainerNumber = boeDoc.ContainerNumber,
                BoeData = System.Text.Json.JsonSerializer.Serialize(boeDoc, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                }),

                // Container Details
                ContainerWeight = boeDoc.ContainerWeight,
                ContainerQuantity = boeDoc.ContainerQuantity,
                ContainerISO = boeDoc.ContainerISO ?? string.Empty,

                // Header Information
                TotalDutyPaid = boeDoc.TotalDutyPaid ?? 0m,
                CrmsLevel = boeDoc.CrmsLevel ?? string.Empty,
                ClearanceType = boeDoc.ClearanceType ?? string.Empty,
                DeclarationNumber = boeDoc.DeclarationNumber ?? string.Empty,

                // Manifest Details
                MasterBlNumber = boeDoc.BlNumber ?? string.Empty,
                HouseBl = boeDoc.HouseBl ?? string.Empty,
                RotationNumber = boeDoc.RotationNumber ?? string.Empty,
                ConsigneeName = boeDoc.ConsigneeName ?? string.Empty,
                ShipperName = boeDoc.ShipperName ?? string.Empty,
                CountryOfOrigin = boeDoc.CountryOfOrigin ?? string.Empty,

                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }

        private IcumManifestItem ConvertToIcumManifestItem(DownloadedManifestItem item)
        {
            return new IcumManifestItem
            {
                HsCode = item.HsCode ?? string.Empty,
                Description = item.Description ?? string.Empty,
                Quantity = item.Quantity ?? 0m,
                Unit = item.Unit ?? string.Empty,
                Weight = item.Weight ?? 0m,
                ItemFob = item.ItemFob ?? 0m,
                ItemDutyPaid = item.ItemDutyPaid ?? 0m,
                FobCurrency = item.FobCurrency ?? string.Empty,
                CountryOfOrigin = item.CountryOfOrigin ?? string.Empty,
                ItemNo = item.ItemNo ?? 0,
                Cpc = item.Cpc ?? string.Empty,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };
        }
    }
}
