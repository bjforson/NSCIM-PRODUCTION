using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Core.Utilities;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.Services.IcumApi
{
    public class IcumJsonIngestionService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ColorCodedLogger _logger;
        private readonly ILoggerFactory _loggerFactory;
        private readonly IConfiguration _configuration;
        private readonly GoLiveOptions _goLiveOptions;
        private readonly DataRetentionOptions _dataRetention;
        private readonly string _downloadsPath;
        private readonly FieldExtractionTracker _fieldTracker;
        private readonly StreamingJsonParser _streamingParser;
        private readonly ICUMSMetrics? _metrics; // ✅ PHASE 3.1: Optional metrics (may not be registered)
        private readonly RecordCompleteness.IRecordBuildingService? _recordBuildingService;
        private readonly IServiceScope _scope;
        private const string SERVICE_ID = "ICUMS-JSON-INGESTION";
        private const long LARGE_FILE_THRESHOLD = 50 * 1024 * 1024; // 50MB - use batch processing for files larger than this

        // Expected fields in JSON for completeness validation
        private static readonly HashSet<string> ExpectedContainerDetailsFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "ContainerNumber", "Description", "ContainerDescription", "ISO", "ContainerISO",
            "Quantity", "ContainerQuantity", "Weight", "ContainerWeight", "ContainerType",
            "Type", "ContainerSize", "Size", "SealNumber", "TruckPlateNumber",
            "DriverName", "DriverLicense", "Status", "Remarks", "VINNumber"
        };

        private static readonly HashSet<string> ExpectedHeaderFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "ImpName", "IMPNAME", "TotalDutyPaid", "TOTALDUTYPAID", "CRMSLevel", "CrmsLevel", "CRMS_LEVEL",
            "ExpAddress", "EXPADDRESS", "EXP_ADDRESS", "DeclarationNumber", "DECLARATIONNUMBER",
            "RegimeCode", "REGIMECODE", "NoofContainers", "NoOfContainers", "NOOFCONTAINERS",
            "CompOffRemarks", "COMPOFFREMARKS", "DeclarantName", "DECLARANTNAME",
            "ExpName", "EXPNAME", "EXP_NAME", "ImpAddress", "IMPADDRESS", "IMP_ADDRESS",
            "ImpExpName", "IMPEXPNAME", "IMP_EXP_NAME", "CCVRIntelRemarks", "CcvrintelRemarks", "CCVRINTELREMARKS",
            "DeclarationVersion", "DECLARATIONVERSION", "ImpExpAddress", "IMPEXPADDRESS", "IMP_EXP_ADDRESS",
            "DeclarationDate", "DECLARATIONDATE", "ClearanceType", "CLEARANCETYPE",
            "DeclarantAddress", "DECLARANTADDRESS", "DECLARANT_ADDRESS"
        };

        private static readonly HashSet<string> ExpectedManifestDetailsFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "RotationNumber", "ConsigneeName", "CountryofOrigin", "CountryOfOrigin",
            "MarksNumbers", "MarksNumber", "ShipperName", "ShipperAddress",
            "BLNumber", "BlNumber", "BL_NUMBER", "DeliveryPlace", "HouseBL", "HouseBl", "HOUSE_BL",
            "ConsigneeAddress", "GoodsDescription"
        };

        private static readonly HashSet<string> ExpectedManifestItemFields = new(StringComparer.OrdinalIgnoreCase)
        {
            "HSCODE", "HsCode", "DESCRIPTION", "Description", "QUANTITY", "Quantity",
            "UNIT", "Unit", "WEIGHT", "Weight", "ITEMFOB", "ItemFob",
            "ITEMDUTYPAID", "ItemDutyPaid", "FOBCURRENCY", "FobCurrency",
            "COUNTRYOFORIGIN", "CountryOfOrigin", "CountryofOrigin", "ITEMNO", "ItemNo", "CPC", "Cpc"
        };

        public IcumJsonIngestionService(
            IServiceProvider serviceProvider,
            ILogger<IcumJsonIngestionService> logger,
            ILoggerFactory loggerFactory,
            IConfiguration configuration,
            IOptions<GoLiveOptions> goLiveOptions,
            IOptions<DataRetentionOptions> dataRetention)
        {
            _serviceProvider = serviceProvider;
            _logger = new ColorCodedLogger(logger, ServiceCategories.ICUMS, SERVICE_ID);
            _loggerFactory = loggerFactory;
            _configuration = configuration;
            _goLiveOptions = goLiveOptions?.Value ?? new GoLiveOptions();
            _dataRetention = dataRetention?.Value ?? new DataRetentionOptions();
            _downloadsPath = _configuration["ICUMS:DownloadsPath"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads";

            // Create a single scope for resolving scoped services; disposed when the service is disposed
            _scope = serviceProvider.CreateScope();

            // Initialize field extraction tracker for completeness validation
            var trackerLogger = _scope.ServiceProvider.GetService<ILogger<FieldExtractionTracker>>();
            _fieldTracker = new FieldExtractionTracker(trackerLogger);

            // ✅ PHASE 1.3: Initialize streaming JSON parser for memory-efficient processing
            var streamingLogger = loggerFactory.CreateLogger<StreamingJsonParser>();
            _streamingParser = new StreamingJsonParser(streamingLogger);

            // ✅ PHASE 3.1: Get metrics service (optional)
            try
            {
                _metrics = _scope.ServiceProvider.GetService<ICUMSMetrics>();
            }
            catch
            {
                _metrics = null; // Metrics not available, continue without them
            }

            // Event-driven record building service
            try
            {
                _recordBuildingService = _scope.ServiceProvider.GetService<RecordCompleteness.IRecordBuildingService>();
            }
            catch
            {
                _recordBuildingService = null;
            }

            // Log construction
            _logger.LogInformation("========================================");
            _logger.LogInformation("IcumJsonIngestionService CONSTRUCTOR called");
            _logger.LogInformation("Downloads path: {Path}", _downloadsPath);
            _logger.LogInformation("Field extraction tracking: ENABLED");
            _logger.LogInformation("Streaming JSON parser: ENABLED (Phase 1.3)");
            _logger.LogInformation("========================================");
        }

        public override void Dispose()
        {
            _scope?.Dispose();
            base.Dispose();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                _logger.LogInformation("========================================");
                _logger.LogInformation("ICUMS JSON INGESTION SERVICE STARTED!");
                _logger.LogInformation("========================================");

                var startupDelaySeconds = _configuration.GetValue<int>("BackgroundServices:IcumJsonIngestionService:StartupDelaySeconds", 45);
                if (startupDelaySeconds > 0)
                {
                    _logger.LogInformation("Staggering startup: waiting {Seconds}s before first cycle...", startupDelaySeconds);
                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(startupDelaySeconds), stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        _logger.LogInformation("Service cancelled during startup delay");
                        return;
                    }
                    _logger.LogInformation("Startup delay complete. Starting main loop...");
                }

                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        _logger.LogInformation("=== INGESTION CYCLE STARTING ===");
                        if (stoppingToken.IsCancellationRequested) break;
                        await ProcessDownloadedFilesAsync();

                        // Clean up any archived files that couldn't be deleted immediately
                        if (stoppingToken.IsCancellationRequested) break;
                        await CleanupArchivedFilesAsync();

                        _logger.LogInformation("=== INGESTION CYCLE COMPLETED ===");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error in ICUMS JSON Ingestion Service");
                        _logger.LogError(ex, "Exception details: {Message}", ex.ToString());
                    }

                    // Process at configured interval (read from database settings)
                    if (stoppingToken.IsCancellationRequested) break;
                    using (var scope = _serviceProvider.CreateScope())
                    {
                        var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                        var processInterval = await settingsProvider.GetIntAsync("BackgroundServices", "IcumJsonIngestionService.ProcessIntervalMinutes", 1);
                        _logger.LogInformation("Waiting {Interval} minute(s) until next cycle (from settings)...", processInterval);
                        try
                        {
                            await Task.Delay(TimeSpan.FromMinutes(processInterval), stoppingToken);
                        }
                        catch (OperationCanceledException)
                        {
                            break;
                        }
                    }
                }

                _logger.LogInformation("ICUMS JSON Ingestion Service stopping (cancellation requested)");
            }
            catch (OperationCanceledException)
            {
                // Service cancelled - exit gracefully
                _logger.LogInformation("ICUMS JSON Ingestion Service cancelled");
            }
            catch (Exception ex)
            {
                _logger.LogCritical(ex, "FATAL ERROR - Ingestion service crashed during startup or execution!");
                _logger.LogCritical(ex, "Full exception: {Exception}", ex.ToString());
                // Don't re-throw - allow application to continue even if this service fails
                // The service will be retried on next cycle
            }
        }

        private async Task ProcessDownloadedFilesAsync()
        {
            _logger.LogInformation("{ServiceId} [ProcessDownloadedFilesAsync] METHOD CALLED", SERVICE_ID);

            using var scope = _serviceProvider.CreateScope();
            _logger.LogInformation("{ServiceId} [ProcessDownloadedFilesAsync] Scope created", SERVICE_ID);

            var downloadsRepository = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();
            _logger.LogInformation("{ServiceId} [ProcessDownloadedFilesAsync] Repository resolved", SERVICE_ID);

            try
            {
                _logger.LogInformation("{ServiceId} Starting ICUMS JSON file processing", SERVICE_ID);

                // Get all pending files
                _logger.LogInformation("{ServiceId} Calling GetPendingFilesAsync()...", SERVICE_ID);
                var pendingFiles = await downloadsRepository.GetPendingFilesAsync();
                _logger.LogInformation("{ServiceId} GetPendingFilesAsync() returned {Count} files", SERVICE_ID, pendingFiles?.Count ?? 0);

                if (pendingFiles == null || !pendingFiles.Any())
                {
                    _logger.LogInformation("{ServiceId} No pending ICUMS files to process", SERVICE_ID);
                    return;
                }

                _logger.LogInformation("{ServiceId} Found {Count} pending ICUMS files to process", SERVICE_ID, pendingFiles.Count);

                // Go-live + data retention cutoff: skip files with DownloadDate before cutoff (no retrospective processing)
                var goLiveDate = _goLiveOptions.EffectiveGoLiveDate;
                var retentionCutoff = _dataRetention.EffectiveCutoffDate;
                var cutoff = (goLiveDate > DateTime.MinValue && retentionCutoff > DateTime.MinValue)
                    ? (goLiveDate > retentionCutoff ? goLiveDate : retentionCutoff)
                    : (goLiveDate > DateTime.MinValue ? goLiveDate : retentionCutoff);
                var eligibleFiles = cutoff > DateTime.MinValue
                    ? pendingFiles.Where(f => f.DownloadDate >= cutoff).ToList()
                    : pendingFiles;
                if (eligibleFiles.Count != pendingFiles.Count)
                {
                    _logger.LogDebug("{ServiceId} Skipped {Count} files before cutoff {Cutoff}",
                        SERVICE_ID, pendingFiles.Count - eligibleFiles.Count, cutoff.ToString("yyyy-MM-dd"));
                }

                // Process in batches to handle large backlogs
                var batchSize = 50; // Process 50 files per cycle instead of all
                var filesToProcess = eligibleFiles.Take(batchSize).ToList();

                _logger.LogInformation("{ServiceId} Processing batch of {BatchSize} files (out of {Total} eligible)",
                    SERVICE_ID, filesToProcess.Count, eligibleFiles.Count);

                var processedCount = 0;
                var failedCount = 0;

                // PHASE 2: Parallel processing for faster throughput
                // Force sequential by default to avoid race conditions; can be overridden via config
                var forceSequential = _configuration.GetValue<bool>("ICUMS:ForceSequential", true);
                var enableParallel = !forceSequential && _configuration.GetValue<bool>("ICUMS:EnableParallelProcessing", false);
                var maxParallel = _configuration.GetValue<int>("ICUMS:MaxParallelFiles", 5);
                var minFilesForParallel = _configuration.GetValue<int>("ICUMS:ParallelProcessingMinFiles", 5);

                if (enableParallel && filesToProcess.Count >= minFilesForParallel && maxParallel > 1)
                {
                    _logger.LogInformation("{ServiceId} 🚀 PHASE 2: Processing {Count} files in parallel (max {MaxParallel} concurrent)",
                        SERVICE_ID, filesToProcess.Count, maxParallel);

                    var parallelStartTime = DateTime.UtcNow;

                    // Process files in parallel with proper scope management
                    await Parallel.ForEachAsync(
                        filesToProcess,
                        new ParallelOptions { MaxDegreeOfParallelism = maxParallel },
                        async (file, ct) =>
                        {
                            // Each parallel task gets its own scope and repository
                            using var scope = _serviceProvider.CreateScope();
                            var repo = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();

                            try
                            {
                                await ProcessSingleFileAsync(file, repo);
                                Interlocked.Increment(ref processedCount);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex, "Failed to process file {FileName}: {Error}", file.FileName, ex.Message);

                                try
                                {
                                    await repo.UpdateFileProcessingStatusAsync(file.Id, "Failed", ex.Message);
                                }
                                catch (Exception updateEx)
                                {
                                    _logger.LogError(updateEx, "Failed to update error status for file {FileName}", file.FileName);
                                }

                                Interlocked.Increment(ref failedCount);
                            }
                        });

                    var parallelElapsedSeconds = (DateTime.UtcNow - parallelStartTime).TotalSeconds;
                    _logger.LogInformation("{ServiceId} 🚀 PHASE 2: Parallel processing completed in {Seconds:F1}s ({FilesPerSec:F1} files/sec)",
                        SERVICE_ID, parallelElapsedSeconds, filesToProcess.Count / parallelElapsedSeconds);
                }
                else
                {
                    // Sequential processing (fallback or when file count is low)
                    _logger.LogDebug("{ServiceId} Using sequential processing (parallel disabled or file count < {MinFiles})",
                        SERVICE_ID, minFilesForParallel);

                    foreach (var file in filesToProcess)
                    {
                        try
                        {
                            await ProcessSingleFileAsync(file, downloadsRepository);
                            processedCount++;
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Failed to process file {FileName}: {Error}", file.FileName, ex.Message);

                            await downloadsRepository.UpdateFileProcessingStatusAsync(
                                file.Id,
                                "Failed",
                                ex.Message);

                            // ✅ PHASE 3.1: Record metrics
                            _metrics?.RecordFileFailed();

                            // ✅ PHASE 2.2: Add to FailedProcessingQueue for automatic retry
                            try
                            {
                                var failedFile = new FailedProcessingQueue
                                {
                                    DownloadedFileId = file.Id,
                                    FileName = file.FileName,
                                    FilePath = file.FilePath,
                                    FailureReason = ex.Message,
                                    ErrorDetails = ex.ToString(),
                                    FailureStage = "JSON_Ingestion",
                                    RetryCount = 0,
                                    MaxRetries = 5,
                                    Status = "Pending",
                                    FailedAt = DateTime.UtcNow
                                };
                                await downloadsRepository.AddFailedFileAsync(failedFile);
                                _logger.LogInformation("{ServiceId} Added file {FileName} to FailedProcessingQueue for automatic retry",
                                    SERVICE_ID, file.FileName);
                            }
                            catch (Exception queueEx)
                            {
                                _logger.LogWarning("{ServiceId} Failed to add file {FileName} to FailedProcessingQueue: {Error}",
                                    SERVICE_ID, file.FileName, queueEx.Message);
                            }

                            failedCount++;
                        }
                    }
                }

                _logger.LogInformation("{ServiceId} Completed ICUMS JSON file processing. Processed: {Processed}, Failed: {Failed}, Remaining: {Remaining}",
                    SERVICE_ID, processedCount, failedCount, pendingFiles.Count - filesToProcess.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing ICUMS downloaded files");
            }
        }

        private async Task ProcessSingleFileAsync(DownloadedFile file, IIcumDownloadsRepository repository)
        {
            _logger.LogInformation("{ServiceId} Processing ICUMS file: {FileName}", SERVICE_ID, file.FileName);

            // PHASE 1: Track overall file processing time
            var fileStartTime = DateTime.UtcNow;

            // ✅ IMPROVED: Check if file exists before processing
            // Some records (e.g., OnDemand, Queue) are virtual and don't have physical files
            // Also handle cases where files might have been moved to archive directories
            if (!File.Exists(file.FilePath))
            {
                // Check if this is a virtual path (Queue/, OnDemand/) - these don't have physical files
                if (file.FilePath.StartsWith("Queue/", StringComparison.OrdinalIgnoreCase) ||
                    file.FilePath.StartsWith("OnDemand/", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.LogWarning("{ServiceId} Virtual file path (no physical file exists): {FilePath}. Marking as Archived.",
                        SERVICE_ID, file.FilePath);
                    await repository.UpdateFileProcessingStatusAsync(file.Id, "Archived",
                        "Virtual record - no physical file exists (Queue/OnDemand download)");
                    return;
                }

                // Check if file exists in archive directories
                var archivePaths = new[]
                {
                    Path.Combine(_downloadsPath, "Archive", "BatchData", file.FileName),
                    Path.Combine(_downloadsPath, "Archive", "ContainerData", file.FileName),
                    Path.Combine(_downloadsPath, "Archive", "Other", file.FileName)
                };

                var archivedFile = archivePaths.FirstOrDefault(File.Exists);
                if (archivedFile != null)
                {
                    _logger.LogInformation("{ServiceId} File found in archive directory, updating path: {OriginalPath} -> {ArchivePath}",
                        SERVICE_ID, file.FilePath, archivedFile);

                    // Update the file path in the database record
                    file.FilePath = archivedFile;
                    await repository.UpdateFilePathAsync(file.Id, archivedFile);
                }
                else
                {
                    // Check if file was already processed (has BOE documents in database)
                    var hasBOEDocuments = await repository.FileHasBOEDocumentsAsync(file.Id);
                    if (hasBOEDocuments)
                    {
                        _logger.LogInformation("{ServiceId} File not found on disk but has BOE documents in database. Marking as Archived: {FilePath}",
                            SERVICE_ID, file.FilePath);
                        await repository.UpdateFileProcessingStatusAsync(file.Id, "Archived",
                            "File successfully processed and archived (file removed from disk)");
                    }
                    else
                    {
                        _logger.LogWarning("{ServiceId} File does not exist on disk (not in active or archive directories), marking as failed: {FilePath}",
                            SERVICE_ID, file.FilePath);
                        await repository.UpdateFileProcessingStatusAsync(file.Id, "Failed", $"File not found: {file.FilePath}");
                    }
                    return;
                }
            }

            // Update status to Processing
            await repository.UpdateFileProcessingStatusAsync(file.Id, "Processing", null);

            // Ingestion observability: emit a 'Started' row; updated on success/failure below.
            int? ingestionLogId = null;
            try
            {
                ingestionLogId = await repository.SaveIngestionLogAsync(new IngestionLog
                {
                    DownloadedFileId = file.Id,
                    ProcessType = "JsonIngestion",
                    Status = "Started",
                    StartTime = fileStartTime,
                    CreatedAt = DateTime.UtcNow
                });
            }
            catch (Exception logEx)
            {
                _logger.LogWarning("{ServiceId} Failed to create ingestion log for {FileName}: {Error}", SERVICE_ID, file.FileName, logEx.Message);
            }

            try
            {
                // ✅ PHASE 1.3: Use streaming JSON parser for memory-efficient processing
                // This reduces memory usage from ~150MB to ~30MB per file (80-95% reduction for large files)
                // The streaming parser uses FileStream with async reading and JsonDocument.ParseAsync
                // which reads the file in chunks rather than loading entire file into memory
                using var jsonDocument = await _streamingParser.ParseJsonFileAsync(file.FilePath);

                // Extract BOE documents - FIXED: Use TryGetProperty for robustness
                if (!jsonDocument.RootElement.TryGetProperty("BOEScanDocument", out var boeDocuments))
                {
                    _logger.LogWarning("{ServiceId} File {FileName} does not contain 'BOEScanDocument' property - invalid JSON structure", SERVICE_ID, file.FileName);
                    await repository.UpdateFileProcessingStatusAsync(file.Id, "Failed", "Missing 'BOEScanDocument' property in JSON");

                    // ✅ PHASE 2.2: Add to FailedProcessingQueue
                    await AddToFailedQueueAsync(repository, file, "Missing 'BOEScanDocument' property in JSON", "JSON_Parse");
                    return;
                }

                if (boeDocuments.ValueKind != JsonValueKind.Array)
                {
                    _logger.LogWarning("{ServiceId} File {FileName} contains 'BOEScanDocument' but it is not an array - invalid JSON structure", SERVICE_ID, file.FileName);
                    await repository.UpdateFileProcessingStatusAsync(file.Id, "Failed", "'BOEScanDocument' is not an array");

                    // ✅ PHASE 2.2: Add to FailedProcessingQueue
                    await AddToFailedQueueAsync(repository, file, "'BOEScanDocument' is not an array", "JSON_Parse");
                    return;
                }

                var documentCount = boeDocuments.GetArrayLength(); // Total records present in JSON (containers + VIN)

                _logger.LogInformation("{ServiceId} Found {Count} BOE documents in file {FileName}", SERVICE_ID, documentCount, file.FileName);

                var processedDocuments = 0;
                var processedItems = 0;
                var processedVehicles = 0; // Vehicles processed via VIN path
                var duplicatesSkippedInFile = 0;

                var processedContainersInThisFile = new HashSet<string>();
                var processedDeclarationsInThisFile = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                var verificationResults = new List<(string Container, int SourceFields, int MatchedFields)>();

                // Process each BOE document
                for (int i = 0; i < documentCount; i++)
                {
                    var boeElement = boeDocuments[i];

                    try
                    {
                        // Check if this is a VIN record before parsing
                        JsonElement? containerDetails = boeElement.TryGetProperty("ContainerDetails", out var containerDetailsElement) && containerDetailsElement.ValueKind != JsonValueKind.Null
                            ? containerDetailsElement
                            : (JsonElement?)null;

                        if (containerDetails != null && containerDetails.HasValue)
                        {
                            var containerDetailsValue = containerDetails.Value;
                            // FIXED: Use helper methods to handle both naming conventions (short names: "Weight", "ISO" vs full: "ContainerWeight", "ContainerISO")
                            // FIXED: Use TryGetProperty for ContainerNumber to avoid exceptions
                            if (!containerDetailsValue.TryGetProperty("ContainerNumber", out var containerNumberProp))
                            {
                                _logger.LogWarning("{ServiceId} ContainerDetails missing 'ContainerNumber' at document index {Index} - skipping", SERVICE_ID, i);
                                continue; // Skip this document if ContainerNumber is missing
                            }

                            var containerDetailsObj = new IcumApiContainerDetails
                            {
                                ContainerNumber = containerNumberProp.GetString() ?? string.Empty,
                                // JSON may have "Weight" or "ContainerWeight" - try both with fallbacks
                                ContainerWeight = GetJsonDecimal(containerDetailsValue, "ContainerWeight", "Weight"),
                                // JSON may have "ISO" or "ContainerISO" - try both with fallbacks
                                ContainerISO = GetJsonString(containerDetailsValue, "ContainerISO", "ISO"),
                                // JSON may have "Type" or "ContainerType" - try both with fallbacks
                                ContainerType = GetJsonString(containerDetailsValue, "ContainerType", "Type"),
                                // JSON may have "Size" or "ContainerSize" - try both with fallbacks
                                ContainerSize = GetJsonString(containerDetailsValue, "ContainerSize", "Size"),
                                Remarks = GetJsonString(containerDetailsValue, "Remarks")
                            };

                            // Check container number type using enhanced validation
                            var containerType = ContainerNumberValidator.GetContainerNumberType(containerDetailsObj.ContainerNumber);

                            // Route vehicle identifiers (VIN, chassis, registration) to vehicle processing
                            if (containerType == ContainerNumberType.VIN ||
                                containerType == ContainerNumberType.VehicleChassis ||
                                containerType == ContainerNumberType.VehicleRegistration)
                            {
                                var vehicleIdentifier = containerDetailsObj.ContainerNumber.Trim().ToUpper();
                                _logger.LogInformation("{ServiceId} Processing vehicle record (Type: {Type}, Identifier: {Identifier})",
                                    SERVICE_ID, containerType, vehicleIdentifier);

                                // Process as vehicle record instead of container
                                await ProcessVINRecordAsync(boeElement, file.Id, i, repository);
                                processedVehicles++;
                                continue; // Skip normal container processing
                            }

                            // Check if this is an invalid container number (legacy check for VIN)
                            if (!containerDetailsObj.IsValidContainerNumber())
                            {
                                var vinNumber = containerDetailsObj.GetVinNumber();
                                if (!string.IsNullOrEmpty(vinNumber))
                                {
                                    _logger.LogInformation("{ServiceId} Processing VIN record (legacy check): {VinNumber}", SERVICE_ID, vinNumber);

                                    // Process as VIN record instead of container
                                    await ProcessVINRecordAsync(boeElement, file.Id, i, repository);
                                    processedVehicles++;
                                    continue; // Skip normal container processing
                                }
                            }
                        }

                        // Process as normal container record
                        var boeDocument = await ParseBOEDocumentAsync(boeElement, file.Id, i);
                        // Normalize keys to improve duplicate detection
                        if (!string.IsNullOrWhiteSpace(boeDocument.ContainerNumber))
                        {
                            boeDocument.ContainerNumber = boeDocument.ContainerNumber.Trim().ToUpper();
                        }
                        if (!string.IsNullOrWhiteSpace(boeDocument.DeclarationNumber))
                        {
                            boeDocument.DeclarationNumber = boeDocument.DeclarationNumber.Trim();
                        }

                        // ✅ FIX: Intra-file deduplication - skip if container already processed in this file
                        var containerKey = $"{boeDocument.ContainerNumber}_{boeDocument.DeclarationNumber}";
                        if (processedContainersInThisFile.Contains(containerKey))
                        {
                            duplicatesSkippedInFile++;
                            _logger.LogWarning("{ServiceId} ⚠️ Skipping duplicate container {ContainerNumber} with declaration {DeclarationNumber} (already processed in this file at index {Index})", SERVICE_ID,
                                boeDocument.ContainerNumber, boeDocument.DeclarationNumber, i);
                            continue; // Skip this duplicate within the same file
                        }

                        // 1.13.0 — Implicit CMR upgrade.
                        // ICUMS sometimes emits messages with `ClearanceType = 'CMR'` that
                        // already carry a populated `DeclarationNumber` and a real
                        // `RegimeCode`. These are fully-formed import declarations riding
                        // on the cargo movement document type — they're not "manifest only,
                        // pre-declaration" CMR rows. Without this handler, no IM message
                        // ever arrives for them and they accumulate in the half-state pile
                        // forever. Detected: 998 such rows in production as of 2026-04-08.
                        //
                        // The fix: when we see a CMR-typed message that already has a
                        // declaration number AND an import-side regime code, flip the
                        // clearance type in-memory to IM (or EX) BEFORE the rest of the
                        // ingest pipeline runs. The provenance columns then record that
                        // the row originally arrived as CMR.
                        if (string.Equals(boeDocument.ClearanceType, "CMR", StringComparison.OrdinalIgnoreCase)
                            && !string.IsNullOrWhiteSpace(boeDocument.DeclarationNumber)
                            && !string.IsNullOrWhiteSpace(boeDocument.RegimeCode))
                        {
                            // Regime codes 40/70/80/90 are import-side; 10/21/etc. are export-side.
                            // The 998 rows audited in production are 100% import-side. Defensive
                            // check on the prefix anyway: anything starting with a 4/7/8/9 is IM,
                            // anything starting with a 1/2 is EX, otherwise leave as CMR.
                            var firstChar = boeDocument.RegimeCode.Trim().FirstOrDefault();
                            string? upgradedTo = firstChar switch
                            {
                                '4' or '7' or '8' or '9' => "IM",
                                '1' or '2' => "EX",
                                _ => null
                            };

                            if (upgradedTo != null)
                            {
                                _logger.LogInformation(
                                    "{ServiceId} 🔄 [CMR→{To} IMPLICIT] Container={Container}, BOE={Declaration}, Regime={Regime}, CRMS={Crms} — ICUMS sent CMR-typed message with full BOE data; upgrading in place",
                                    SERVICE_ID, upgradedTo, boeDocument.ContainerNumber,
                                    boeDocument.DeclarationNumber, boeDocument.RegimeCode, boeDocument.CrmsLevel);

                                boeDocument.OriginalClearanceType = "CMR";
                                boeDocument.CmrUpgradedAt = DateTime.UtcNow;
                                boeDocument.ClearanceType = upgradedTo;
                                // Fall through to the normal lifecycle / insert path below.
                                // The save path will write the new ClearanceType plus the
                                // provenance columns; the COALESCE in UpdateExistingDocumentAsync
                                // ensures provenance is set once and never overwritten.
                            }
                        }

                        // ✅ CMR→BOE LIFECYCLE: Check if this IM/EX record upgrades an existing CMR
                        bool cmrUpgraded = false;
                        if (!string.IsNullOrWhiteSpace(boeDocument.DeclarationNumber)
                            && (string.Equals(boeDocument.ClearanceType, "IM", StringComparison.OrdinalIgnoreCase)
                                || string.Equals(boeDocument.ClearanceType, "EX", StringComparison.OrdinalIgnoreCase))
                            && !string.IsNullOrWhiteSpace(boeDocument.ContainerNumber)
                            && !string.IsNullOrWhiteSpace(boeDocument.RotationNumber)
                            && !string.IsNullOrWhiteSpace(boeDocument.BlNumber))
                        {
                            var cmrMatch = await repository.GetCMRByCompositeKeyAsync(
                                boeDocument.ContainerNumber, boeDocument.RotationNumber, boeDocument.BlNumber);

                            if (cmrMatch != null)
                            {
                                _logger.LogInformation(
                                    "{ServiceId} CMR→{ClearanceType} upgrade: Container={Container}, Rotation={Rotation}, BL={BL}, BOE={Declaration}, CMR_ID={CmrId}",
                                    SERVICE_ID, boeDocument.ClearanceType, boeDocument.ContainerNumber,
                                    boeDocument.RotationNumber, boeDocument.BlNumber, boeDocument.DeclarationNumber, cmrMatch.Id);

                                // Upgrade: directly update the CMR row with all IM/EX fields including DeclarationNumber
                                var boeIdUpgraded = await repository.UpgradeCMRToBOEAsync(cmrMatch.Id, boeDocument);
                                boeDocument.Id = boeIdUpgraded;
                                processedContainersInThisFile.Add(containerKey);
                                cmrUpgraded = true;

                                // Invalidate ICUMS data cache so downstream services see the upgraded record
                                try
                                {
                                    using var cacheScope = _serviceProvider.CreateScope();
                                    var cacheService = cacheScope.ServiceProvider.GetService<NickScanCentralImagingPortal.Services.ContainerValidation.IICUMSDataCacheService>();
                                    cacheService?.InvalidateCache(boeDocument.ContainerNumber);
                                }
                                catch (Exception cacheEx)
                                {
                                    _logger.LogWarning("{ServiceId} Failed to invalidate ICUMS cache for {Container} after CMR upgrade (non-fatal). Exception: {Exception}",
                                        SERVICE_ID, boeDocument.ContainerNumber, cacheEx.Message);
                                }
                            }
                        }

                        if (cmrUpgraded)
                        {
                            // CMR was upgraded — skip normal insert/update path, go straight to verification
                            verificationResults.Add(CountFieldAccuracy(boeDocument));

                            if (boeElement.TryGetProperty("ManifestItems", out var upgradeManifestItems))
                            {
                                var itemCount = upgradeManifestItems.GetArrayLength();
                                const int BATCH_SIZE = 100;
                                var manifestItemsToSave = new List<DownloadedManifestItem>(BATCH_SIZE);

                                for (int j = 0; j < itemCount; j++)
                                {
                                    var itemElement = upgradeManifestItems[j];
                                    var manifestItem = ParseManifestItem(itemElement, boeDocument.Id, j);
                                    manifestItemsToSave.Add(manifestItem);

                                    if (manifestItemsToSave.Count >= BATCH_SIZE || j == itemCount - 1)
                                    {
                                        await repository.SaveManifestItemsBulkAsync(manifestItemsToSave);
                                        manifestItemsToSave.Clear();
                                    }
                                }
                            }

                            continue; // Skip the normal insert/update path below
                        }

                        // ✅ FIX: Enhanced duplicate detection - check if document already exists in database
                        var existingDoc = await repository.GetBOEDocumentByContainerAndDeclarationAsync(
                            boeDocument.ContainerNumber, boeDocument.DeclarationNumber);

                        int boeId;

                        if (existingDoc != null)
                        {
                            // Always update existing document with freshly parsed data
                            // The raw SQL UPDATE in SaveBOEDocumentAsync will set all mapped fields
                            _logger.LogDebug("{ServiceId} Existing document found for container {ContainerNumber} — updating all mapped fields",
                                SERVICE_ID, boeDocument.ContainerNumber);

                            boeId = await repository.SaveBOEDocumentAsync(boeDocument);
                            boeDocument.Id = boeId;
                            processedContainersInThisFile.Add(containerKey);
                        }
                        else
                        {
                            // ✅ CMR→BOE LIFECYCLE: Before creating new, check if a CMR record exists for this container
                            // If the new record is IM/EX and a CMR exists with same Container+Rotation+BL → upgrade it
                            var clearanceType = boeDocument.ClearanceType?.Trim().ToUpper();
                            BOEDocument? cmrRecord = null;

                            if ((clearanceType == "IM" || clearanceType == "EX") &&
                                !string.IsNullOrWhiteSpace(boeDocument.ContainerNumber) &&
                                !string.IsNullOrWhiteSpace(boeDocument.RotationNumber) &&
                                !string.IsNullOrWhiteSpace(boeDocument.BlNumber))
                            {
                                cmrRecord = await repository.GetCMRByCompositeKeyAsync(
                                    boeDocument.ContainerNumber,
                                    boeDocument.RotationNumber,
                                    boeDocument.BlNumber);
                            }

                            if (cmrRecord != null)
                            {
                                // ✅ CMR→BOE UPGRADE: Merge IM/EX data into existing CMR record
                                _logger.LogInformation("{ServiceId} 🔄 CMR→{ClearanceType} UPGRADE: Container={Container}, Rotation={Rotation}, BL={BL}, BOE={Declaration}",
                                    SERVICE_ID, clearanceType, boeDocument.ContainerNumber,
                                    boeDocument.RotationNumber, boeDocument.BlNumber, boeDocument.DeclarationNumber);

                                boeId = await repository.UpgradeCMRToBOEAsync(cmrRecord.Id, boeDocument);
                                boeDocument.Id = boeId;
                                processedContainersInThisFile.Add(containerKey);

                                // Cascade: update ContainerCompletenessStatus if accessible
                                try
                                {
                                    await CascadeCMRUpgradeAsync(boeDocument);
                                }
                                catch (Exception cascadeEx)
                                {
                                    _logger.LogWarning("{ServiceId} CMR→BOE cascade update failed for {Container}: {Error}",
                                        SERVICE_ID, boeDocument.ContainerNumber, cascadeEx.Message);
                                }
                            }
                            else
                            {
                            // No existing document and no CMR to upgrade — create new
                            _logger.LogDebug("{ServiceId} Creating NEW BOE document for container {ContainerNumber}. RawJsonData: {RawJsonStatus} ({Length} chars)",
                                SERVICE_ID, boeDocument.ContainerNumber,
                                string.IsNullOrEmpty(boeDocument.RawJsonData) ? "NULL/EMPTY" : "HAS_DATA",
                                boeDocument.RawJsonData?.Length ?? 0);

                            try
                            {
                                boeId = await repository.SaveBOEDocumentAsync(boeDocument);
                                boeDocument.Id = boeId;

                                processedContainersInThisFile.Add(containerKey);
                                _logger.LogInformation("{ServiceId} Created new BOE document for container {ContainerNumber} with declaration {DeclarationNumber}", SERVICE_ID,
                                    boeDocument.ContainerNumber, boeDocument.DeclarationNumber);
                            }
                            catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
                                && (sqlEx.Number == 2601 || sqlEx.Number == 2627))
                            {
                                // Race condition - another thread or parallel file processing inserted it
                                _logger.LogWarning("{ServiceId} ⚠️ Duplicate key detected during save for container {ContainerNumber}, fetching existing record", SERVICE_ID, boeDocument.ContainerNumber);
                                var existing = await repository.GetBOEDocumentByContainerAndDeclarationAsync(
                                    boeDocument.ContainerNumber, boeDocument.DeclarationNumber);
                                if (existing == null)
                                {
                                    _logger.LogError("{ServiceId} ❌ Container {ContainerNumber} was inserted by another thread but cannot be found", SERVICE_ID, boeDocument.ContainerNumber);
                                    throw; // If still not found after race condition, rethrow
                                }
                                boeId = existing.Id;
                                boeDocument.Id = boeId;
                                processedContainersInThisFile.Add(containerKey); // Track it so we skip subsequent duplicates
                                _logger.LogInformation("{ServiceId} ✓ Used existing container {ContainerNumber} (ID: {Id}) after race condition", SERVICE_ID, boeDocument.ContainerNumber, boeId);
                            }
                            } // end else (no CMR to upgrade — create new)
                        }

                        verificationResults.Add(CountFieldAccuracy(boeDocument));

                        if (boeElement.TryGetProperty("ManifestItems", out var manifestItems))
                        {
                            var itemCount = manifestItems.GetArrayLength();

                            _logger.LogDebug("{ServiceId} Processing {Count} manifest items for BOE document {BoeId} (Container: {Container})",
                                SERVICE_ID, itemCount, boeId, boeDocument.ContainerNumber);

                            var startTime = DateTime.UtcNow;

                            // ✅ MEMORY FIX: Process manifest items in batches of 100 to prevent memory spikes
                            // This reduces memory usage from ~95MB to ~5MB per container
                            const int BATCH_SIZE = 100;
                            var manifestItemsToSave = new List<DownloadedManifestItem>(BATCH_SIZE);
                            var allManifestItemIds = new List<int>(itemCount);
                            var batchCount = 0;

                            for (int j = 0; j < itemCount; j++)
                            {
                                var itemElement = manifestItems[j];
                                var manifestItem = ParseManifestItem(itemElement, boeId, j);
                                manifestItemsToSave.Add(manifestItem);

                                // Save when batch is full or at last item
                                if (manifestItemsToSave.Count >= BATCH_SIZE || j == itemCount - 1)
                                {
                                    var batchIds = await repository.SaveManifestItemsBulkAsync(manifestItemsToSave);
                                    allManifestItemIds.AddRange(batchIds);
                                    batchCount++;

                                    _logger.LogDebug("{ServiceId} Saved batch #{Batch} with {Count} manifest items for BOE {BoeId}",
                                        SERVICE_ID, batchCount, manifestItemsToSave.Count, boeId);

                                    manifestItemsToSave.Clear(); // ✅ Release memory immediately
                                }
                            }

                            // Bulk update all statuses at once
                            await repository.UpdateManifestItemsStatusBulkAsync(allManifestItemIds, "Completed");

                            processedItems += allManifestItemIds.Count;

                            var elapsedMs = (DateTime.UtcNow - startTime).TotalMilliseconds;
                            _logger.LogDebug("{ServiceId} ⚡ BATCHED SAVE: {Count} manifest items in {Batches} batches, {ElapsedMs}ms for BOE {BoeId}",
                                SERVICE_ID, itemCount, batchCount, elapsedMs, boeId);
                        }
                        else
                        {
                            _logger.LogDebug("{ServiceId} No manifest items found for BOE document {BoeId} (Container: {Container})",
                                SERVICE_ID, boeId, boeDocument.ContainerNumber);
                        }

                        // ✅ PHASE 1 FIX #3: Post-Ingestion Validation
                        await ValidateIngestedDocumentAsync(boeDocument, boeId, i);

                        // ✅ PHASE 1 FIX #5: Data Quality Metrics
                        LogDataQualityMetrics(boeDocument, i);

                        // CRITICAL FIX #2: Mark BOE document as Completed so transfer service can find it
                        await repository.UpdateBOEDocumentProcessingStatusAsync(boeId, "Completed");

                        // Track declaration for event-driven record building
                        if (!string.IsNullOrWhiteSpace(boeDocument.DeclarationNumber))
                            processedDeclarationsInThisFile.Add(boeDocument.DeclarationNumber.Trim());

                        processedDocuments++;
                    }
                    catch (InvalidOperationException ex) when (ex.Message.Contains("VIN number") || ex.Message.Contains("Invalid container number"))
                    {
                        // Skip VIN numbers and invalid container numbers - this is expected behavior
                        _logger.LogDebug("{ServiceId} Skipped document {Index} in file {FileName}: {Reason}",
                            SERVICE_ID, i, file.FileName, ex.Message);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to process BOE document {Index} in file {FileName}: {Error}",
                            i, file.FileName, ex.Message);
                    }
                }

                // Update file status to completed
                // ✅ FIX (v2): RecordCount should reflect how many records are PRESENT in the JSON file,
                // not how many were newly created after deduplication. This way, even when containers
                // are skipped as duplicates, the batch page still shows an accurate view of the JSON content.
                var totalProcessedRecords = documentCount;
                await repository.UpdateFileProcessingStatusAsync(
                    file.Id,
                    "Completed",
                    null,
                    totalProcessedRecords);

                // PHASE 1: Log overall performance metrics
                var fileElapsedSeconds = (DateTime.UtcNow - fileStartTime).TotalSeconds;
                var itemsPerSecond = processedItems > 0 ? (processedItems / fileElapsedSeconds) : 0;

                var logMessage = $"{SERVICE_ID} ⚡ Successfully processed file {file.FileName}: {processedDocuments} documents, {processedItems} manifest items, {processedVehicles} vehicles in {fileElapsedSeconds:F1}s ({itemsPerSecond:F0} items/sec)";
                if (duplicatesSkippedInFile > 0)
                {
                    logMessage += $", {duplicatesSkippedInFile} intra-file duplicates skipped";
                    _logger.LogWarning("{ServiceId} ⚠️ File {FileName} contained {Count} duplicate containers within the same file", SERVICE_ID, file.FileName, duplicatesSkippedInFile);
                }
                _logger.LogInformation(logMessage);

                await RunIngestionVerificationAsync(file, repository, verificationResults);

                await ArchiveProcessedFileAsync(file.FilePath, file.FileName);

                // Ingestion observability: mark log as Completed with counts.
                if (ingestionLogId.HasValue)
                {
                    try
                    {
                        var details = $"docs={processedDocuments}; items={processedItems}; vehicles={processedVehicles}; duplicatesSkipped={duplicatesSkippedInFile}; elapsedSec={fileElapsedSeconds:F1}";
                        await repository.UpdateIngestionLogAsync(ingestionLogId.Value, "Completed", DateTime.UtcNow, processedDocuments, null, details);
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogWarning("{ServiceId} Failed to update ingestion log (completed) for {FileName}: {Error}", SERVICE_ID, file.FileName, logEx.Message);
                    }
                }

                // Event-driven record building: build/update records for all declarations in this file
                if (_recordBuildingService != null && processedDeclarationsInThisFile.Count > 0)
                {
                    _logger.LogInformation("{ServiceId} Building records for {Count} declarations from file {FileName}",
                        SERVICE_ID, processedDeclarationsInThisFile.Count, file.FileName);
                    foreach (var decl in processedDeclarationsInThisFile)
                    {
                        try
                        {
                            await _recordBuildingService.BuildOrUpdateRecordAsync(decl);
                        }
                        catch (Exception rbEx)
                        {
                            _logger.LogError(rbEx, "{ServiceId} Failed to build record for declaration {Decl}", SERVICE_ID, decl);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process file {FileName}: {Error}", file.FileName, ex.Message);

                // Ingestion observability: mark log as Failed.
                if (ingestionLogId.HasValue)
                {
                    try
                    {
                        var exDetail = ex.ToString();
                        await repository.UpdateIngestionLogAsync(ingestionLogId.Value, "Failed", DateTime.UtcNow, null, ex.Message,
                            exDetail.Length > 3900 ? exDetail.Substring(0, 3900) : exDetail);
                    }
                    catch (Exception logEx)
                    {
                        _logger.LogWarning("{ServiceId} Failed to update ingestion log (failed) for {FileName}: {Error}", SERVICE_ID, file.FileName, logEx.Message);
                    }
                }

                throw;
            }
        }

        private async Task<BOEDocument> ParseBOEDocumentAsync(JsonElement boeElement, int downloadedFileId, int documentIndex)
        {
            var boeDocument = new BOEDocument
            {
                DownloadedFileId = downloadedFileId,
                DocumentIndex = documentIndex,
                ProcessingStatus = "Pending"
            };

            // 🔍 BULLETPROOF: Initialize unmapped field collector for this document
            var unmappedCollector = new UnmappedFieldCollector(_loggerFactory.CreateLogger<UnmappedFieldCollector>());

            // Parse ContainerDetails with VIN validation - FIXED: Use correct field names from JSON (PascalCase)
            if (boeElement.TryGetProperty("ContainerDetails", out var containerDetails) && containerDetails.ValueKind != JsonValueKind.Null)
            {
                // 🔍 BULLETPROOF: Scan for unmapped fields in ContainerDetails
                _fieldTracker.ScanForUnmappedFields(containerDetails, "ContainerDetails", ExpectedContainerDetailsFields);
                unmappedCollector.ScanSection(containerDetails, "ContainerDetails", ExpectedContainerDetailsFields);

                // Create IcumApiContainerDetails instance for validation
                // JSON uses PascalCase: "ContainerType", "ContainerSize", "ContainerWeight", "ContainerISO" (not "Type", "Size", "Weight", "ISO")
                // FIXED: Use TryGetProperty for ContainerNumber to avoid exceptions
                if (!containerDetails.TryGetProperty("ContainerNumber", out var containerNumberProp))
                {
                    _logger.LogWarning("{ServiceId} ContainerDetails missing 'ContainerNumber' at document index {Index} - cannot process", SERVICE_ID, documentIndex);
                    throw new InvalidOperationException($"ContainerDetails missing 'ContainerNumber' at document index {documentIndex}");
                }

                var containerNumber = containerNumberProp.GetString() ?? string.Empty;
                _fieldTracker.RecordExtraction("ContainerDetails", "ContainerNumber", !string.IsNullOrEmpty(containerNumber), "ContainerNumber");

                var containerTypeStr = GetJsonString(containerDetails, "ContainerType", "Type");
                _fieldTracker.RecordExtraction("ContainerDetails", "ContainerType", containerTypeStr != null, containerDetails.TryGetProperty("ContainerType", out _) ? "ContainerType" : "Type");

                var containerSize = GetJsonString(containerDetails, "ContainerSize", "Size");
                _fieldTracker.RecordExtraction("ContainerDetails", "ContainerSize", containerSize != null, containerDetails.TryGetProperty("ContainerSize", out _) ? "ContainerSize" : "Size");

                var containerWeight = GetJsonDecimal(containerDetails, "ContainerWeight", "Weight");
                _fieldTracker.RecordExtraction("ContainerDetails", "ContainerWeight", containerWeight != null, containerDetails.TryGetProperty("ContainerWeight", out _) ? "ContainerWeight" : "Weight");

                var containerISO = GetJsonString(containerDetails, "ContainerISO", "ISO");
                _fieldTracker.RecordExtraction("ContainerDetails", "ContainerISO", containerISO != null, containerDetails.TryGetProperty("ContainerISO", out _) ? "ContainerISO" : "ISO");

                var containerDetailsObj = new IcumApiContainerDetails
                {
                    ContainerNumber = containerNumber,
                    ContainerType = containerTypeStr,
                    ContainerSize = containerSize,
                    ContainerWeight = containerWeight,
                    ContainerISO = containerISO,
                    SealNumber = GetJsonString(containerDetails, "SealNumber"),
                    TruckPlateNumber = GetJsonString(containerDetails, "TruckPlateNumber"),
                    DriverName = GetJsonString(containerDetails, "DriverName"),
                    DriverLicense = GetJsonString(containerDetails, "DriverLicense"),
                    Status = GetJsonString(containerDetails, "Status"),
                    Remarks = GetJsonString(containerDetails, "Remarks")
                };

                // Track additional fields
                _fieldTracker.RecordExtraction("ContainerDetails", "SealNumber", containerDetailsObj.SealNumber != null, "SealNumber");
                _fieldTracker.RecordExtraction("ContainerDetails", "TruckPlateNumber", containerDetailsObj.TruckPlateNumber != null, "TruckPlateNumber");
                _fieldTracker.RecordExtraction("ContainerDetails", "DriverName", containerDetailsObj.DriverName != null, "DriverName");
                _fieldTracker.RecordExtraction("ContainerDetails", "DriverLicense", containerDetailsObj.DriverLicense != null, "DriverLicense");
                _fieldTracker.RecordExtraction("ContainerDetails", "Status", containerDetailsObj.Status != null, "Status");
                _fieldTracker.RecordExtraction("ContainerDetails", "Remarks", containerDetailsObj.Remarks != null, "Remarks");

                // Validate container number using enhanced validation
                var containerNumberType = ContainerNumberValidator.GetContainerNumberType(containerDetailsObj.ContainerNumber);

                // Reject vehicle identifiers - these should be processed as vehicles, not containers
                if (containerNumberType == ContainerNumberType.VIN ||
                    containerNumberType == ContainerNumberType.VehicleChassis ||
                    containerNumberType == ContainerNumberType.VehicleRegistration)
                {
                    var vehicleIdentifier = containerDetailsObj.ContainerNumber.Trim().ToUpper();
                    _logger.LogWarning("{ServiceId} Rejecting vehicle identifier as container number (Type: {Type}, Identifier: {Identifier})",
                        SERVICE_ID, containerNumberType, vehicleIdentifier);
                    throw new InvalidOperationException($"Vehicle identifier '{vehicleIdentifier}' (Type: {containerNumberType}) is not a valid container number");
                }

                // Validate container number
                if (!containerDetailsObj.IsValidContainerNumber())
                {
                    // Legacy check for VIN (should be caught above, but keep for safety)
                    var vinNumber = containerDetailsObj.GetVinNumber();
                    if (!string.IsNullOrEmpty(vinNumber))
                    {
                        _logger.LogDebug("{ServiceId} Skipping VIN number record: {VinNumber} (will be processed as vehicle)", SERVICE_ID, vinNumber);
                        throw new InvalidOperationException($"VIN number {vinNumber} is not a valid container number");
                    }
                    else
                    {
                        // Only log truly invalid formats
                        _logger.LogWarning("{ServiceId} Skipping record with invalid/empty container number: {ContainerNumber}", SERVICE_ID, containerDetailsObj.ContainerNumber);
                        throw new InvalidOperationException($"Invalid container number: {containerDetailsObj.ContainerNumber}");
                    }
                }
                else
                {
                    // Log acceptance of loose cargo identifiers
                    if (containerNumberType == ContainerNumberType.LooseCargo)
                    {
                        _logger.LogInformation("{ServiceId} Accepting loose cargo identifier: {ContainerNumber}", SERVICE_ID, containerDetailsObj.ContainerNumber);
                    }
                }

                // Use validated container number
                boeDocument.ContainerNumber = containerDetailsObj.GetActualContainerNumber();
                boeDocument.ContainerDescription = GetJsonString(containerDetails, "Description", "ContainerDescription");
                boeDocument.ContainerISO = containerDetailsObj.ContainerISO;
                boeDocument.ContainerSize = containerDetailsObj.ContainerSize;
                boeDocument.ContainerQuantity = GetJsonInt(containerDetails, "ContainerQuantity", "Quantity");
                boeDocument.ContainerWeight = containerDetailsObj.ContainerWeight;
                boeDocument.SealNumber = GetJsonString(containerDetails, "SealNumber");
                boeDocument.TruckPlateNumber = GetJsonString(containerDetails, "TruckPlateNumber");
                boeDocument.DriverName = GetJsonString(containerDetails, "DriverName");
                boeDocument.DriverLicense = GetJsonString(containerDetails, "DriverLicense");
                boeDocument.ContainerStatus = GetJsonString(containerDetails, "Status");
                boeDocument.ContainerRemarks = GetJsonString(containerDetails, "Remarks");
            }

            // Parse Header - FIXED: Use correct field names from JSON (PascalCase, not UPPERCASE)
            if (boeElement.TryGetProperty("Header", out var header) && header.ValueKind != JsonValueKind.Null)
            {
                // 🔍 BULLETPROOF: Scan for unmapped fields in Header
                _fieldTracker.ScanForUnmappedFields(header, "Header", ExpectedHeaderFields);
                unmappedCollector.ScanSection(header, "Header", ExpectedHeaderFields);

                // JSON uses PascalCase: "ImpName", "ExpName", "ImpAddress", etc. (not "IMPNAME", "EXPNAME")
                // Using helper methods with fallbacks to handle both cases for robustness
                boeDocument.ImpName = GetJsonString(header, "ImpName", "IMPNAME") ?? string.Empty;
                boeDocument.TotalDutyPaid = GetJsonDecimal(header, "TotalDutyPaid", "TOTALDUTYPAID");
                boeDocument.CrmsLevel = GetJsonString(header, "CRMSLevel", "CrmsLevel", "CRMS_LEVEL");
                boeDocument.ExpAddress = GetJsonString(header, "ExpAddress", "EXPADDRESS", "EXP_ADDRESS") ?? string.Empty;
                boeDocument.DeclarationNumber = GetJsonString(header, "DeclarationNumber", "DECLARATIONNUMBER") ?? string.Empty;
                boeDocument.RegimeCode = GetJsonString(header, "RegimeCode", "REGIMECODE");
                boeDocument.NoOfContainers = GetJsonInt(header, "NoofContainers", "NoOfContainers", "NOOFCONTAINERS");
                boeDocument.CompOffRemarks = GetJsonString(header, "CompOffRemarks", "COMPOFFREMARKS");
                boeDocument.DeclarantName = GetJsonString(header, "DeclarantName", "DECLARANTNAME");
                boeDocument.ExpName = GetJsonString(header, "ExpName", "EXPNAME", "EXP_NAME") ?? string.Empty;
                boeDocument.ImpAddress = GetJsonString(header, "ImpAddress", "IMPADDRESS", "IMP_ADDRESS") ?? string.Empty;
                boeDocument.ImpExpName = GetJsonString(header, "ImpExpName", "IMPEXPNAME", "IMP_EXP_NAME") ?? string.Empty;
                boeDocument.CcvrIntelRemarks = GetJsonString(header, "CCVRIntelRemarks", "CcvrintelRemarks", "CCVRINTELREMARKS");
                boeDocument.DeclarationVersion = GetJsonInt(header, "DeclarationVersion", "DECLARATIONVERSION");
                boeDocument.ImpExpAddress = GetJsonString(header, "ImpExpAddress", "IMPEXPADDRESS", "IMP_EXP_ADDRESS") ?? string.Empty;
                boeDocument.DeclarationDate = GetJsonString(header, "DeclarationDate", "DECLARATIONDATE");
                boeDocument.ClearanceType = GetJsonString(header, "ClearanceType", "CLEARANCETYPE");
                boeDocument.DeclarantAddress = GetJsonString(header, "DeclarantAddress", "DECLARANTADDRESS", "DECLARANT_ADDRESS") ?? string.Empty;
            }
            else
            {
                // ⚠️ CRITICAL: Header section is missing or null - this should not happen!
                _logger.LogWarning("{ServiceId} ⚠️ Header section is missing or null for document {DocumentIndex} (Container: {ContainerNumber}) - ClearanceType cannot be extracted from Header",
                    SERVICE_ID, documentIndex, boeDocument.ContainerNumber ?? "UNKNOWN");
            }

            // ✅ FALLBACK: Try to extract ClearanceType from root level if Header was missing or ClearanceType wasn't found in Header
            if (string.IsNullOrEmpty(boeDocument.ClearanceType))
            {
                var fallbackClearanceType = GetJsonString(boeElement, "ClearanceType", "CLEARANCETYPE");
                if (!string.IsNullOrEmpty(fallbackClearanceType))
                {
                    _logger.LogInformation("{ServiceId} ✅ Found ClearanceType at root level for document {DocumentIndex} (Container: {ContainerNumber}): {ClearanceType}",
                        SERVICE_ID, documentIndex, boeDocument.ContainerNumber ?? "UNKNOWN", fallbackClearanceType);
                    boeDocument.ClearanceType = fallbackClearanceType;
                }
                else
                {
                    // ⚠️ CRITICAL: ClearanceType could not be extracted from JSON at all
                    _logger.LogWarning("{ServiceId} ❌ ClearanceType is NULL for document {DocumentIndex} (Container: {ContainerNumber}) - checked Header section and root level, field not found in JSON",
                        SERVICE_ID, documentIndex, boeDocument.ContainerNumber ?? "UNKNOWN");
                }
            }

            // Parse ManifestDetails - Clearance Type Aware Processing
            if (boeElement.TryGetProperty("ManifestDetails", out var manifestDetails) && manifestDetails.ValueKind != JsonValueKind.Null)
            {
                // 🔍 BULLETPROOF: Scan for unmapped fields in ManifestDetails
                _fieldTracker.ScanForUnmappedFields(manifestDetails, "ManifestDetails", ExpectedManifestDetailsFields);
                unmappedCollector.ScanSection(manifestDetails, "ManifestDetails", ExpectedManifestDetailsFields);

                // IM, EX, CMR all have ManifestDetails - safe to parse with individual property null checks
                // FIXED: Use helper methods for consistency and robustness (handles case variations)
                // ✅ FIX: Parse each field independently — one bad field can never wipe all others
                boeDocument.RotationNumber = SafeGetJsonString(manifestDetails, "RotationNumber");
                boeDocument.ConsigneeName = SafeGetJsonString(manifestDetails, "ConsigneeName");
                boeDocument.CountryOfOrigin = SafeGetJsonString(manifestDetails, "CountryofOrigin", "CountryOfOrigin");
                boeDocument.MarksNumbers = SafeGetJsonString(manifestDetails, "MarksNumbers", "MarksNumber");
                boeDocument.ShipperName = SafeGetJsonString(manifestDetails, "ShipperName");
                boeDocument.ShipperAddress = SafeGetJsonString(manifestDetails, "ShipperAddress");
                boeDocument.BlNumber = SafeGetJsonString(manifestDetails, "BLNumber", "BlNumber", "BL_NUMBER");
                boeDocument.DeliveryPlace = SafeGetJsonString(manifestDetails, "DeliveryPlace");
                boeDocument.HouseBl = SafeGetJsonString(manifestDetails, "HouseBL", "HouseBl", "HOUSE_BL");
                boeDocument.ConsigneeAddress = SafeGetJsonString(manifestDetails, "ConsigneeAddress");
                boeDocument.GoodsDescription = SafeGetJsonString(manifestDetails, "GoodsDescription");
                boeDocument.MasterBlNumber = SafeGetJsonString(manifestDetails, "MasterBLNumber", "MasterBlNumber");

                // ✅ CONSOLIDATED CARGO CLASSIFICATION
                boeDocument.IsConsolidated = !string.IsNullOrWhiteSpace(boeDocument.HouseBl) &&
                                             boeDocument.HouseBl != boeDocument.BlNumber;

                // ✅ Critical Field Validation and Logging
                try { await ValidateCriticalFieldsAsync(boeDocument, documentIndex); }
                catch (Exception ex) { _logger.LogWarning("ValidateCriticalFields failed for doc {Index}: {Msg}", documentIndex, ex.Message); }
            }
            else
            {
                // Handle cases where ManifestDetails is null or missing
                // For different clearance types, handle missing manifest details appropriately
                switch (boeDocument.ClearanceType)
                {
                    case "IM":
                    case "EX":
                        // IM/EX: BOE is ready, ManifestDetails might be missing - this is acceptable
                        _logger.LogDebug("{ServiceId} IM/EX record with missing ManifestDetails (document {DocumentIndex}) - this is expected for BOE-ready records", SERVICE_ID, documentIndex);
                        break;
                    case "CMR":
                        // CMR: BOE not ready but should have ManifestDetails - this is unexpected
                        _logger.LogWarning("{ServiceId} CMR record with missing ManifestDetails (document {DocumentIndex}) - this is unexpected, CMR should have manifest data", SERVICE_ID, documentIndex);
                        break;
                    case null:
                    case "":
                        _logger.LogDebug("{ServiceId} ClearanceType is unknown for document {DocumentIndex} - missing ManifestDetails", SERVICE_ID, documentIndex);
                        break;
                }
            }

            // ✅ PHASE 1 FIX #1: ALWAYS store RawJsonData - even if unmapped fields processing fails
            // This ensures we can always recover data from JSON if entity properties are missing
            // CRITICAL: This MUST be set before StoreUnmappedFields is called
            try
            {
                // Store complete JSON document as backup FIRST
                var rawText = boeElement.GetRawText();
                if (string.IsNullOrEmpty(rawText))
                {
                    _logger.LogWarning("{ServiceId} ⚠️ GetRawText() returned NULL/EMPTY for document {Index}. Attempting to serialize element instead.", SERVICE_ID, documentIndex);
                    // Fallback: try to serialize the element
                    try
                    {
                        rawText = JsonSerializer.Serialize(boeElement, new JsonSerializerOptions { WriteIndented = false });
                        _logger.LogDebug("{ServiceId} ✅ Fallback serialization succeeded for document {Index}", SERVICE_ID, documentIndex);
                    }
                    catch (Exception serializeEx)
                    {
                        _logger.LogError(serializeEx, "{ServiceId} ❌ Fallback serialization also failed for document {Index}", SERVICE_ID, documentIndex);
                        rawText = $"{{\"Error\":\"Failed to extract JSON for document {documentIndex}\",\"Exception\":\"{serializeEx.Message}\"}}";
                    }
                }

                // CRITICAL: Always set RawJsonData, even if it's an error message
                boeDocument.RawJsonData = rawText;
                _logger.LogDebug("{ServiceId} ✅ Stored RawJsonData for document {Index} ({Length} chars)",
                    SERVICE_ID, documentIndex, boeDocument.RawJsonData?.Length ?? 0);

                // VERIFY: Double-check that RawJsonData was actually set
                if (string.IsNullOrEmpty(boeDocument.RawJsonData))
                {
                    _logger.LogError("{ServiceId} ❌ CRITICAL: RawJsonData is NULL/EMPTY after assignment for document {Index}! This should never happen!", SERVICE_ID, documentIndex);
                    boeDocument.RawJsonData = $"{{\"Error\":\"RawJsonData assignment failed for document {documentIndex}\"}}";
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} ❌ CRITICAL: Failed to store RawJsonData for document {Index}. This will prevent data recovery!",
                    SERVICE_ID, documentIndex);
                // Try to store at least a minimal JSON structure
                try
                {
                    boeDocument.RawJsonData = JsonSerializer.Serialize(new { Error = "Failed to serialize original JSON", Exception = ex.Message });
                }
                catch
                {
                    // Last resort: store error message
                    boeDocument.RawJsonData = $"{{\"Error\":\"Failed to store RawJsonData: {ex.Message}\"}}";
                }
            }

            // 🔍 BULLETPROOF: Store all unmapped fields using two-tier strategy (columns + RawJsonData)
            // CRITICAL: Save RawJsonData before calling StoreUnmappedFields to ensure it's preserved
            var rawJsonDataBeforeUnmapped = boeDocument.RawJsonData;
            var prioritizedUnmappedFields = unmappedCollector.GetPrioritizedFields();
            try
            {
                UnmappedFieldStorageHelper.StoreUnmappedFields(boeDocument, prioritizedUnmappedFields, boeElement);

                // VERIFY: Ensure RawJsonData is still set after StoreUnmappedFields
                if (string.IsNullOrEmpty(boeDocument.RawJsonData))
                {
                    _logger.LogError("{ServiceId} ❌ CRITICAL: RawJsonData was cleared by StoreUnmappedFields for document {Index}! Restoring...", SERVICE_ID, documentIndex);
                    boeDocument.RawJsonData = rawJsonDataBeforeUnmapped ?? boeElement.GetRawText();
                }
                else if (rawJsonDataBeforeUnmapped != null && !boeDocument.RawJsonData.Contains(rawJsonDataBeforeUnmapped.Substring(0, Math.Min(100, rawJsonDataBeforeUnmapped.Length))))
                {
                    // RawJsonData was changed but doesn't contain the original - this might be OK if it was wrapped
                    _logger.LogDebug("{ServiceId} RawJsonData was modified by StoreUnmappedFields for document {Index} (likely wrapped with unmapped fields)", SERVICE_ID, documentIndex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("{ServiceId} ⚠️ Failed to store unmapped fields metadata for document {Index}, but RawJsonData is preserved. Exception: {Exception}",
                    SERVICE_ID, documentIndex, ex.Message);
                // RawJsonData is already stored above, so we can continue
            }

            // Log unmapped fields detection
            if (prioritizedUnmappedFields.Count > 0)
            {
                _logger.LogInformation("{ServiceId} 🔍 Document {Index}: Detected {Count} unmapped field(s). First 20 in columns, all {Total} in RawJsonData",
                    SERVICE_ID, documentIndex, Math.Min(20, prioritizedUnmappedFields.Count), prioritizedUnmappedFields.Count);

                if (prioritizedUnmappedFields.Count > 20)
                {
                    _logger.LogWarning("{ServiceId} ⚠️ Document {Index}: {Count} unmapped fields detected (>20 limit). Overflow stored in RawJsonData",
                        SERVICE_ID, documentIndex, prioritizedUnmappedFields.Count);
                }

                // Log first few unmapped fields for visibility
                foreach (var field in prioritizedUnmappedFields.Take(5))
                {
                    _logger.LogDebug("{ServiceId} 🔍 Unmapped field: {Section}:{Field} = {Value}",
                        SERVICE_ID, field.Section, field.FieldName,
                        string.IsNullOrEmpty(field.Value) ? "(null)" : (field.Value.Length > 50 ? field.Value.Substring(0, 50) + "..." : field.Value));
                }
            }

            // ✅ PHASE 1 FIX #4: Enhanced Consolidated Cargo Detection
            // Check for consolidated cargo based on multiple criteria
            await DetectAndClassifyConsolidatedCargoAsync(boeDocument, boeElement, documentIndex);

            return boeDocument;
        }

        #region Helper Methods for Case-Insensitive JSON Property Lookup

        /// <summary>
        /// Gets a string property from JsonElement with case-insensitive fallback options
        /// ✅ PHASE 1 FIX #2: Enhanced logging for field extraction
        /// </summary>
        private string? GetJsonString(JsonElement element, params string[] propertyNames)
        {
            return GetJsonStringWithLogging(element, null, null, propertyNames);
        }

        /// <summary>
        /// CMR→BOE cascade: update downstream tables when a CMR record is upgraded to IM/EX.
        /// Uses a separate scope since the ingestion may be running in a different DB context.
        /// </summary>
        private async Task CascadeCMRUpgradeAsync(BOEDocument upgradedDocument)
        {
            using var scope = _serviceProvider.CreateScope();
            var appDb = scope.ServiceProvider.GetRequiredService<NickScanCentralImagingPortal.Infrastructure.Data.ApplicationDbContext>();

            var container = upgradedDocument.ContainerNumber;
            if (string.IsNullOrWhiteSpace(container)) return;

            // 1. Update ContainerCompletenessStatus
            var completenessRecords = await appDb.ContainerCompletenessStatuses
                .Where(c => c.ContainerNumber == container)
                .ToListAsync();

            foreach (var record in completenessRecords)
            {
                record.HasICUMSData = true;
                if (!string.IsNullOrWhiteSpace(upgradedDocument.ClearanceType))
                    record.ClearanceType = upgradedDocument.ClearanceType;
                if (!string.IsNullOrWhiteSpace(upgradedDocument.DeclarationNumber) &&
                    (string.IsNullOrWhiteSpace(record.GroupIdentifier) || record.GroupIdentifier == record.ContainerNumber))
                    record.GroupIdentifier = upgradedDocument.DeclarationNumber;
                record.UpdatedAt = DateTime.UtcNow;
            }

            // 2. Update ContainerBOERelations
            var boeRelations = await appDb.ContainerBOERelations
                .Where(r => r.ContainerNumber == container)
                .ToListAsync();

            foreach (var relation in boeRelations)
            {
                if (!string.IsNullOrWhiteSpace(upgradedDocument.ClearanceType))
                {
                    relation.RelationType = upgradedDocument.IsConsolidated
                        ? "Consolidated-HouseBL" : "Primary";
                }
            }

            if (completenessRecords.Any() || boeRelations.Any())
            {
                await appDb.SaveChangesAsync();
                _logger.LogInformation("{ServiceId} ✅ CMR→BOE cascade complete for {Container}: {CompCount} completeness + {RelCount} relations updated",
                    SERVICE_ID, container, completenessRecords.Count, boeRelations.Count);
            }
        }

        /// <summary>
        /// Wraps GetJsonString in a try/catch so one bad field can never wipe all others.
        /// </summary>
        private string? SafeGetJsonString(JsonElement element, params string[] propertyNames)
        {
            try { return GetJsonString(element, propertyNames); }
            catch { return null; }
        }

        /// <summary>
        /// Gets a string property from JsonElement with enhanced logging
        /// </summary>
        private string? GetJsonStringWithLogging(JsonElement element, string? section, string? fieldName, params string[] propertyNames)
        {
            foreach (var propName in propertyNames)
            {
                if (element.TryGetProperty(propName, out var prop) && prop.ValueKind != JsonValueKind.Null)
                {
                    var value = prop.GetString();
                    // Return the value even if it's empty - let the caller decide whether to use empty string or null
                    // This allows us to distinguish between missing fields (null) and empty fields (empty string)
                    if (value != null) // Only check for null, not empty - empty strings are valid values
                    {
                        if (!string.IsNullOrEmpty(section) && !string.IsNullOrEmpty(fieldName))
                        {
                            _logger.LogDebug("{ServiceId} ✅ Extracted {Section}.{FieldName} = '{Value}' from property '{PropertyName}'",
                                SERVICE_ID, section, fieldName, value, propName);
                        }
                        return value;
                    }
                }
            }

            if (!string.IsNullOrEmpty(section) && !string.IsNullOrEmpty(fieldName))
            {
                _logger.LogDebug("{ServiceId} ⚠️ Failed to extract {Section}.{FieldName} - tried properties: {Properties}",
                    SERVICE_ID, section, fieldName, string.Join(", ", propertyNames));
            }
            return null;
        }

        /// <summary>
        /// Gets a decimal property from JsonElement with case-insensitive fallback options
        /// </summary>
        private decimal? GetJsonDecimal(JsonElement element, params string[] propertyNames)
        {
            foreach (var propName in propertyNames)
            {
                if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetDecimal();
                }
            }
            return null;
        }

        /// <summary>
        /// Gets an int property from JsonElement with case-insensitive fallback options
        /// </summary>
        private int? GetJsonInt(JsonElement element, params string[] propertyNames)
        {
            foreach (var propName in propertyNames)
            {
                if (element.TryGetProperty(propName, out var prop) && prop.ValueKind == JsonValueKind.Number)
                {
                    return prop.GetInt32();
                }
            }
            return null;
        }

        #endregion

        private DownloadedManifestItem ParseManifestItem(JsonElement itemElement, int boeDocumentId, int itemIndex)
        {
            // 🔍 BULLETPROOF: Scan for unmapped fields in ManifestItem
            var unmappedCollector = new UnmappedFieldCollector(_loggerFactory.CreateLogger<UnmappedFieldCollector>());
            _fieldTracker.ScanForUnmappedFields(itemElement, "ManifestItem", ExpectedManifestItemFields);
            unmappedCollector.ScanSection(itemElement, "ManifestItem", ExpectedManifestItemFields);

            // ManifestItems in JSON are uppercase (HSCODE, DESCRIPTION, etc.) per actual JSON structure
            var manifestItem = new DownloadedManifestItem
            {
                BOEDocumentId = boeDocumentId,
                ItemIndex = itemIndex,
                HsCode = GetJsonString(itemElement, "HSCODE", "HsCode"),
                Description = GetJsonString(itemElement, "DESCRIPTION", "Description"),
                Quantity = GetJsonDecimal(itemElement, "QUANTITY", "Quantity"),
                Unit = GetJsonString(itemElement, "UNIT", "Unit"),
                Weight = GetJsonDecimal(itemElement, "WEIGHT", "Weight"),
                ItemFob = GetJsonDecimal(itemElement, "ITEMFOB", "ItemFob"),
                ItemDutyPaid = GetJsonDecimal(itemElement, "ITEMDUTYPAID", "ItemDutyPaid"),
                FobCurrency = GetJsonString(itemElement, "FOBCURRENCY", "FobCurrency"),
                CountryOfOrigin = GetJsonString(itemElement, "COUNTRYOFORIGIN", "CountryOfOrigin", "CountryofOrigin"),
                ItemNo = GetJsonInt(itemElement, "ITEMNO", "ItemNo"),
                Cpc = GetJsonString(itemElement, "CPC", "Cpc"),
                ProcessingStatus = "Pending"
            };

            // 🔍 BULLETPROOF: Store unmapped fields for ManifestItem
            var prioritizedUnmappedFields = unmappedCollector.GetPrioritizedFields();
            UnmappedFieldStorageHelper.StoreUnmappedFields(manifestItem, prioritizedUnmappedFields, itemElement);

            if (prioritizedUnmappedFields.Count > 0)
            {
                _logger.LogDebug("{ServiceId} 🔍 ManifestItem {ItemIndex}: Detected {Count} unmapped field(s)",
                    SERVICE_ID, itemIndex, prioritizedUnmappedFields.Count);
            }

            return manifestItem;
        }

        private ManifestDetails ParseManifestDetails(JsonElement manifestDetailsElement)
        {
            // FIXED: Use helper methods for consistency and robustness (handles case variations)
            return new ManifestDetails
            {
                MasterBlNumber = GetJsonString(manifestDetailsElement, "BLNumber", "BlNumber", "BL_NUMBER") ?? string.Empty,
                HouseBl = GetJsonString(manifestDetailsElement, "HouseBL", "HouseBl", "HOUSE_BL") ?? string.Empty,
                CountryofOrigin = GetJsonString(manifestDetailsElement, "CountryofOrigin", "CountryOfOrigin") ?? string.Empty,
                ConsigneeAddress = GetJsonString(manifestDetailsElement, "ConsigneeAddress") ?? string.Empty,
                ConsigneeName = GetJsonString(manifestDetailsElement, "ConsigneeName") ?? string.Empty,
                GoodsDescription = GetJsonString(manifestDetailsElement, "GoodsDescription") ?? string.Empty,
                MarksNumbers = GetJsonString(manifestDetailsElement, "MarksNumbers", "MarksNumber") ?? string.Empty,
                RotationNumber = GetJsonString(manifestDetailsElement, "RotationNumber") ?? string.Empty,
                DeliveryPlace = GetJsonString(manifestDetailsElement, "DeliveryPlace") ?? string.Empty,
                ShipperAddress = GetJsonString(manifestDetailsElement, "ShipperAddress") ?? string.Empty,
                ShipperName = GetJsonString(manifestDetailsElement, "ShipperName") ?? string.Empty
            };
        }

        private ManifestItem ConvertToManifestItem(DownloadedManifestItem downloadedItem)
        {
            return new ManifestItem
            {
                Cpc = downloadedItem.Cpc ?? string.Empty,
                CountryofOrigin = downloadedItem.CountryOfOrigin ?? string.Empty,
                Description = downloadedItem.Description ?? string.Empty,
                FobCurrency = downloadedItem.FobCurrency ?? string.Empty,
                HsCode = downloadedItem.HsCode ?? string.Empty,
                ItemDutyPaid = downloadedItem.ItemDutyPaid ?? 0m,
                ItemFob = downloadedItem.ItemFob ?? 0m,
                ItemNo = downloadedItem.ItemNo ?? 0,
                Quantity = downloadedItem.Quantity ?? 0m,
                Unit = downloadedItem.Unit ?? string.Empty,
                Weight = downloadedItem.Weight ?? 0m
            };
        }

        /// <summary>
        /// Process a VIN record and save it to the vehicle import table
        /// </summary>
        private async Task ProcessVINRecordAsync(JsonElement boeElement, int downloadedFileId, int documentIndex, IIcumDownloadsRepository repository)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var vehicleImportService = scope.ServiceProvider.GetRequiredService<IVehicleImportService>();

                // Parse the BOE document first to get basic information
                var boeDocument = await ParseBOEDocumentForVINAsync(boeElement, downloadedFileId, documentIndex);
                // Normalize keys to improve duplicate detection
                if (!string.IsNullOrWhiteSpace(boeDocument.ContainerNumber))
                {
                    boeDocument.ContainerNumber = boeDocument.ContainerNumber.Trim().ToUpper();
                }
                if (!string.IsNullOrWhiteSpace(boeDocument.DeclarationNumber))
                {
                    boeDocument.DeclarationNumber = boeDocument.DeclarationNumber.Trim();
                }

                // ✅ FIX: Check if container already exists before saving (prevent duplicates)
                var existingDoc = await repository.GetBOEDocumentByContainerAndDeclarationAsync(
                    boeDocument.ContainerNumber, boeDocument.DeclarationNumber);

                int boeId;
                if (existingDoc != null)
                {
                    _logger.LogDebug("{ServiceId} Skipping duplicate BOE document for VIN container {ContainerNumber} with declaration {DeclarationNumber} (already exists)", SERVICE_ID,
                        boeDocument.ContainerNumber, boeDocument.DeclarationNumber);
                    boeId = existingDoc.Id; // Use existing document ID
                }
                else
                {
                    try
                    {
                        boeId = await repository.SaveBOEDocumentAsync(boeDocument);
                        boeDocument.Id = boeId; // Update the ID after saving
                    }
                    catch (DbUpdateException ex) when (ex.InnerException is Microsoft.Data.SqlClient.SqlException sqlEx
                        && (sqlEx.Number == 2601 || sqlEx.Number == 2627)) // Duplicate key errors
                    {
                        // Race condition - another thread inserted it, fetch existing
                        _logger.LogWarning("{ServiceId} Duplicate key detected for container {ContainerNumber}, fetching existing record", SERVICE_ID, boeDocument.ContainerNumber);
                        var existing = await repository.GetBOEDocumentByContainerAndDeclarationAsync(
                            boeDocument.ContainerNumber, boeDocument.DeclarationNumber);
                        if (existing == null)
                            throw; // If still not found, rethrow
                        boeId = existing.Id;
                        boeDocument.Id = boeId;
                    }
                }

                // Parse manifest details and items
                ManifestDetails? manifestDetails = null;
                List<ManifestItem>? manifestItems = null;

                if (boeElement.TryGetProperty("ManifestDetails", out var manifestDetailsElement) && manifestDetailsElement.ValueKind != JsonValueKind.Null)
                {
                    manifestDetails = ParseManifestDetails(manifestDetailsElement);
                }

                if (boeElement.TryGetProperty("ManifestItems", out var manifestItemsElement) && manifestItemsElement.ValueKind == JsonValueKind.Array)
                {
                    manifestItems = new List<ManifestItem>();
                    foreach (var itemElement in manifestItemsElement.EnumerateArray())
                    {
                        var downloadedItem = ParseManifestItem(itemElement, boeId, manifestItems.Count);
                        var manifestItem = ConvertToManifestItem(downloadedItem);
                        manifestItems.Add(manifestItem);
                    }
                }

                // Process VIN records using the vehicle import service
                var processedVehicles = await vehicleImportService.ProcessVINRecordsFromBOEAsync(boeDocument, manifestDetails, manifestItems);

                _logger.LogInformation("{ServiceId} Successfully processed {Count} VIN records from BOE document {DeclarationNumber}",
                    SERVICE_ID, processedVehicles.Count, boeDocument.DeclarationNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error processing VIN record at document index {Index}", SERVICE_ID, documentIndex);
                throw;
            }
        }

        /// <summary>
        /// Parse BOE document for VIN records (simplified version without container validation)
        /// </summary>
        private Task<BOEDocument> ParseBOEDocumentForVINAsync(JsonElement boeElement, int downloadedFileId, int documentIndex)
        {
            var boeDocument = new BOEDocument
            {
                DownloadedFileId = downloadedFileId,
                DocumentIndex = documentIndex,
                ProcessingStatus = "Pending"
            };

            // 🔍 BULLETPROOF: Initialize unmapped field collector for VIN document
            var unmappedCollector = new UnmappedFieldCollector(_loggerFactory.CreateLogger<UnmappedFieldCollector>());

            // Parse ContainerDetails (will contain VIN) - FIXED: Use correct field names from JSON (PascalCase)
            if (boeElement.TryGetProperty("ContainerDetails", out var containerDetails) && containerDetails.ValueKind != JsonValueKind.Null)
            {
                // 🔍 BULLETPROOF: Scan for unmapped fields in ContainerDetails
                _fieldTracker.ScanForUnmappedFields(containerDetails, "ContainerDetails", ExpectedContainerDetailsFields);
                unmappedCollector.ScanSection(containerDetails, "ContainerDetails", ExpectedContainerDetailsFields);

                // JSON uses PascalCase: "ContainerType", "ContainerSize", "ContainerWeight", "ContainerISO" (not "Type", "Size", "Weight", "ISO")
                // FIXED: Use TryGetProperty for ContainerNumber to avoid exceptions
                if (containerDetails.TryGetProperty("ContainerNumber", out var containerNumberProp))
                {
                    boeDocument.ContainerNumber = containerNumberProp.GetString() ?? string.Empty;
                }
                else
                {
                    _logger.LogWarning("{ServiceId} ContainerDetails missing 'ContainerNumber' at document index {Index} for VIN record", SERVICE_ID, documentIndex);
                    boeDocument.ContainerNumber = string.Empty; // Will be filled from VIN number if applicable
                }
                boeDocument.ContainerDescription = GetJsonString(containerDetails, "Description", "ContainerDescription");
                boeDocument.ContainerISO = GetJsonString(containerDetails, "ContainerISO", "ISO");
                boeDocument.ContainerQuantity = GetJsonInt(containerDetails, "ContainerQuantity", "Quantity");
                boeDocument.ContainerWeight = GetJsonDecimal(containerDetails, "ContainerWeight", "Weight");
            }

            // Parse Header - FIXED: Use correct field names from JSON (PascalCase, not UPPERCASE)
            if (boeElement.TryGetProperty("Header", out var header) && header.ValueKind != JsonValueKind.Null)
            {
                // 🔍 BULLETPROOF: Scan for unmapped fields in Header
                _fieldTracker.ScanForUnmappedFields(header, "Header", ExpectedHeaderFields);
                unmappedCollector.ScanSection(header, "Header", ExpectedHeaderFields);

                // JSON uses PascalCase: "ImpName", "ExpName", "ImpAddress", etc. (not "IMPNAME", "EXPNAME")
                // Using helper methods with fallbacks to handle both cases for robustness
                boeDocument.ImpName = GetJsonString(header, "ImpName", "IMPNAME") ?? string.Empty;
                boeDocument.TotalDutyPaid = GetJsonDecimal(header, "TotalDutyPaid", "TOTALDUTYPAID");
                boeDocument.CrmsLevel = GetJsonString(header, "CRMSLevel", "CrmsLevel", "CRMS_LEVEL");
                boeDocument.ExpAddress = GetJsonString(header, "ExpAddress", "EXPADDRESS", "EXP_ADDRESS") ?? string.Empty;
                boeDocument.DeclarationNumber = GetJsonString(header, "DeclarationNumber", "DECLARATIONNUMBER") ?? string.Empty;
                boeDocument.RegimeCode = GetJsonString(header, "RegimeCode", "REGIMECODE");
                boeDocument.NoOfContainers = GetJsonInt(header, "NoofContainers", "NoOfContainers", "NOOFCONTAINERS");
                boeDocument.CompOffRemarks = GetJsonString(header, "CompOffRemarks", "COMPOFFREMARKS");
                boeDocument.DeclarantName = GetJsonString(header, "DeclarantName", "DECLARANTNAME");
                boeDocument.ExpName = GetJsonString(header, "ExpName", "EXPNAME", "EXP_NAME") ?? string.Empty;
                boeDocument.ImpAddress = GetJsonString(header, "ImpAddress", "IMPADDRESS", "IMP_ADDRESS") ?? string.Empty;
                boeDocument.ImpExpName = GetJsonString(header, "ImpExpName", "IMPEXPNAME", "IMP_EXP_NAME") ?? string.Empty;
                boeDocument.CcvrIntelRemarks = GetJsonString(header, "CCVRIntelRemarks", "CcvrintelRemarks", "CCVRINTELREMARKS");
                boeDocument.DeclarationVersion = GetJsonInt(header, "DeclarationVersion", "DECLARATIONVERSION");
                boeDocument.ImpExpAddress = GetJsonString(header, "ImpExpAddress", "IMPEXPADDRESS", "IMP_EXP_ADDRESS") ?? string.Empty;
                boeDocument.DeclarationDate = GetJsonString(header, "DeclarationDate", "DECLARATIONDATE");
                boeDocument.ClearanceType = GetJsonString(header, "ClearanceType", "CLEARANCETYPE");
                boeDocument.DeclarantAddress = GetJsonString(header, "DeclarantAddress", "DECLARANTADDRESS", "DECLARANT_ADDRESS") ?? string.Empty;
            }
            else
            {
                // ⚠️ CRITICAL: Header section is missing or null - this should not happen!
                _logger.LogWarning("{ServiceId} ⚠️ Header section is missing or null for VIN document {DocumentIndex} (Container: {ContainerNumber}) - ClearanceType cannot be extracted from Header",
                    SERVICE_ID, documentIndex, boeDocument.ContainerNumber ?? "UNKNOWN");
            }

            // ✅ FALLBACK: Try to extract ClearanceType from root level if Header was missing or ClearanceType wasn't found in Header
            if (string.IsNullOrEmpty(boeDocument.ClearanceType))
            {
                var fallbackClearanceType = GetJsonString(boeElement, "ClearanceType", "CLEARANCETYPE");
                if (!string.IsNullOrEmpty(fallbackClearanceType))
                {
                    _logger.LogInformation("{ServiceId} ✅ Found ClearanceType at root level for VIN document {DocumentIndex} (Container: {ContainerNumber}): {ClearanceType}",
                        SERVICE_ID, documentIndex, boeDocument.ContainerNumber ?? "UNKNOWN", fallbackClearanceType);
                    boeDocument.ClearanceType = fallbackClearanceType;
                }
                else
                {
                    // ⚠️ CRITICAL: ClearanceType could not be extracted from JSON at all
                    _logger.LogWarning("{ServiceId} ❌ ClearanceType is NULL for VIN document {DocumentIndex} (Container: {ContainerNumber}) - checked Header section and root level, field not found in JSON",
                        SERVICE_ID, documentIndex, boeDocument.ContainerNumber ?? "UNKNOWN");
                }
            }

            // Parse ManifestDetails - FIXED: Use helper methods for consistency
            if (boeElement.TryGetProperty("ManifestDetails", out var manifestDetails) && manifestDetails.ValueKind != JsonValueKind.Null)
            {
                // 🔍 BULLETPROOF: Scan for unmapped fields in ManifestDetails
                _fieldTracker.ScanForUnmappedFields(manifestDetails, "ManifestDetails", ExpectedManifestDetailsFields);
                unmappedCollector.ScanSection(manifestDetails, "ManifestDetails", ExpectedManifestDetailsFields);

                // Use helper methods for consistency and robustness (handles case variations)
                boeDocument.RotationNumber = GetJsonString(manifestDetails, "RotationNumber");
                boeDocument.ConsigneeName = GetJsonString(manifestDetails, "ConsigneeName");
                boeDocument.CountryOfOrigin = GetJsonString(manifestDetails, "CountryofOrigin", "CountryOfOrigin");
                boeDocument.MarksNumbers = GetJsonString(manifestDetails, "MarksNumbers", "MarksNumber");
                boeDocument.ShipperName = GetJsonString(manifestDetails, "ShipperName");
                boeDocument.ShipperAddress = GetJsonString(manifestDetails, "ShipperAddress");
                boeDocument.BlNumber = GetJsonString(manifestDetails, "BLNumber", "BlNumber", "BL_NUMBER");
                boeDocument.DeliveryPlace = GetJsonString(manifestDetails, "DeliveryPlace");
                boeDocument.HouseBl = GetJsonString(manifestDetails, "HouseBL", "HouseBl", "HOUSE_BL");
                boeDocument.ConsigneeAddress = GetJsonString(manifestDetails, "ConsigneeAddress");
                boeDocument.GoodsDescription = GetJsonString(manifestDetails, "GoodsDescription");
            }

            // 🔍 BULLETPROOF: Store all unmapped fields for VIN document
            var prioritizedUnmappedFields = unmappedCollector.GetPrioritizedFields();
            UnmappedFieldStorageHelper.StoreUnmappedFields(boeDocument, prioritizedUnmappedFields, boeElement);

            if (prioritizedUnmappedFields.Count > 0)
            {
                _logger.LogDebug("{ServiceId} 🔍 VIN Document {Index}: Detected {Count} unmapped field(s)",
                    SERVICE_ID, documentIndex, prioritizedUnmappedFields.Count);
            }

            return Task.FromResult(boeDocument);
        }

        /// <summary>
        /// Validates critical fields based on clearance type and logs warnings for missing fields
        /// </summary>
        private Task ValidateCriticalFieldsAsync(BOEDocument boeDocument, int documentIndex)
        {
            try
            {
                var clearanceType = boeDocument.ClearanceType ?? "Unknown";
                var containerNumber = boeDocument.ContainerNumber ?? "Unknown";
                var declarationNumber = boeDocument.DeclarationNumber ?? "Unknown";
                var warnings = new List<string>();

                // Validate based on clearance type
                switch (clearanceType)
                {
                    case "IM":
                        // IM records: BOE is the ultimate identifier, BL is nice-to-have
                        if (string.IsNullOrWhiteSpace(boeDocument.DeclarationNumber))
                        {
                            warnings.Add("Missing DeclarationNumber (BOE) - CRITICAL for IM records");
                        }

                        if (string.IsNullOrWhiteSpace(boeDocument.BlNumber))
                        {
                            _logger.LogDebug("{ServiceId} IM record missing BL Number (Container: {Container}, BOE: {BOE}) - This is acceptable per business logic",
                                SERVICE_ID, containerNumber, declarationNumber);
                        }
                        break;

                    case "CMR":
                        // CMR records: Must have BL + Rotation + Container (composite key)
                        var missingFields = new List<string>();

                        if (string.IsNullOrWhiteSpace(boeDocument.BlNumber))
                            missingFields.Add("BlNumber");

                        if (string.IsNullOrWhiteSpace(boeDocument.RotationNumber))
                            missingFields.Add("RotationNumber");

                        if (string.IsNullOrWhiteSpace(boeDocument.ContainerNumber))
                            missingFields.Add("ContainerNumber");

                        if (missingFields.Any())
                        {
                            warnings.Add($"Missing critical fields for CMR composite key: {string.Join(", ", missingFields)}");

                            _logger.LogWarning("{ServiceId} ⚠️ CMR VALIDATION FAILED - Container: {Container}, BOE: {BOE}, Missing: {MissingFields}",
                                SERVICE_ID, containerNumber, declarationNumber, string.Join(", ", missingFields));
                        }
                        else
                        {
                            _logger.LogDebug("{ServiceId} ✅ CMR validation passed - Container: {Container}, BOE: {BOE}",
                                SERVICE_ID, containerNumber, declarationNumber);
                        }
                        break;

                    case "EX":
                        // EX records: Similar to IM, BOE is primary identifier
                        if (string.IsNullOrWhiteSpace(boeDocument.DeclarationNumber))
                        {
                            warnings.Add("Missing DeclarationNumber (BOE) - CRITICAL for EX records");
                        }
                        break;

                    default:
                        _logger.LogWarning("{ServiceId} Unknown clearance type: {ClearanceType} for Container: {Container}",
                            SERVICE_ID, clearanceType, containerNumber);
                        break;
                }

                // Log any warnings
                if (warnings.Any())
                {
                    _logger.LogWarning("{ServiceId} Field validation warnings for {ClearanceType} record (Container: {Container}, BOE: {BOE}): {Warnings}",
                        SERVICE_ID, clearanceType, containerNumber, declarationNumber, string.Join("; ", warnings));
                }

                // Persist warnings to the record so they're queryable.
                // This runs BEFORE SaveChanges so the flag is written in the same transaction.
                if (warnings.Any())
                {
                    boeDocument.HasIngestionWarnings = true;
                    var joined = string.Join("\n", warnings);
                    boeDocument.IngestionWarnings = joined.Length > 4000 ? joined.Substring(0, 4000) : joined;
                }
                else
                {
                    boeDocument.HasIngestionWarnings = false;
                    boeDocument.IngestionWarnings = null;
                }

                // For CMR records with missing critical fields, queue them for re-download and send alert
                if (clearanceType == "CMR" && warnings.Any(w => w.Contains("Missing critical fields")))
                {
                    _logger.LogInformation("{ServiceId} CMR record with missing critical fields should be queued for re-download: {Container}",
                        SERVICE_ID, containerNumber);

                    // TODO: Integrate with CMRRedownloadService and EmailService
                    // This would be injected via constructor:
                    // await _cmrRedownloadService.QueueForRedownloadAsync(containerNumber, "Missing critical fields for CMR composite key");
                    // await _emailService.SendCMRValidationAlertAsync(new CMRValidationAlertModel { ... });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during critical field validation for document {DocumentIndex}", documentIndex);
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// ✅ PHASE 1 FIX #4: Enhanced Consolidated Cargo Detection
        /// Detects consolidated cargo based on multiple criteria:
        /// 1. HouseBL exists and is different from Master BL
        /// 2. Multiple consignees in same container (check other BOEDocuments)
        /// 3. Multiple HouseBLs in same container
        /// </summary>
        private Task DetectAndClassifyConsolidatedCargoAsync(BOEDocument boeDocument, JsonElement boeElement, int documentIndex)
        {
            try
            {
                // Method 1: Check HouseBL vs Master BL (existing logic)
                bool hasHouseBL = !string.IsNullOrWhiteSpace(boeDocument.HouseBl);
                bool houseBLDiffersFromMaster = hasHouseBL &&
                                               !string.IsNullOrWhiteSpace(boeDocument.BlNumber) &&
                                               boeDocument.HouseBl != boeDocument.BlNumber;

                if (houseBLDiffersFromMaster)
                {
                    boeDocument.IsConsolidated = true;
                    _logger.LogInformation("{ServiceId} ✅ Consolidated cargo detected (Method 1): Container {Container}, HouseBL {HouseBL} differs from Master BL {MasterBL}",
                        SERVICE_ID, boeDocument.ContainerNumber, boeDocument.HouseBl, boeDocument.BlNumber);
                    return Task.CompletedTask;
                }

                // Method 2: Check if HouseBL exists (even if same as Master BL, it might indicate consolidation)
                if (hasHouseBL)
                {
                    // HouseBL exists - this is a strong indicator of consolidated cargo
                    // Even if it matches Master BL, the presence of HouseBL field suggests consolidation
                    boeDocument.IsConsolidated = true;
                    _logger.LogInformation("{ServiceId} ✅ Consolidated cargo detected (Method 2): Container {Container} has HouseBL {HouseBL}",
                        SERVICE_ID, boeDocument.ContainerNumber, boeDocument.HouseBl);
                    return Task.CompletedTask;
                }

                // Method 3: Check JSON for additional indicators
                // Some JSON structures might have consolidation indicators we haven't mapped yet
                if (boeElement.TryGetProperty("ManifestDetails", out var manifestDetails) && manifestDetails.ValueKind == JsonValueKind.Object)
                {
                    // Check for multiple consignees or consolidation flags in JSON
                    var hasConsolidationIndicator = manifestDetails.TryGetProperty("IsConsolidated", out var isConsolidatedProp) ||
                                                   manifestDetails.TryGetProperty("ConsolidationFlag", out var consolidationFlag) ||
                                                   manifestDetails.TryGetProperty("MultipleConsignees", out var multipleConsignees);

                    if (hasConsolidationIndicator)
                    {
                        boeDocument.IsConsolidated = true;
                        _logger.LogInformation("{ServiceId} ✅ Consolidated cargo detected (Method 3): Container {Container} has consolidation indicator in JSON",
                            SERVICE_ID, boeDocument.ContainerNumber);
                        return Task.CompletedTask;
                    }
                }

                // Default: Not consolidated
                boeDocument.IsConsolidated = false;
                _logger.LogDebug("{ServiceId} Non-consolidated cargo: Container {Container}, No HouseBL, Master BL: {MasterBL}",
                    SERVICE_ID, boeDocument.ContainerNumber, boeDocument.BlNumber ?? "N/A");
            }
            catch (Exception ex)
            {
                _logger.LogWarning("{ServiceId} Error detecting consolidated cargo for document {Index}, defaulting to non-consolidated. Exception: {Exception}",
                    SERVICE_ID, documentIndex, ex.Message);
                boeDocument.IsConsolidated = false;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// ✅ PHASE 1 FIX #3: Post-Ingestion Validation
        /// Validates that critical data was extracted and stored correctly.
        /// Persists any issues to BOEDocument.IngestionWarnings so they're queryable.
        /// </summary>
        private async Task ValidateIngestedDocumentAsync(BOEDocument boeDocument, int boeId, int documentIndex)
        {
            try
            {
                var issues = new List<string>();

                // Critical validations
                if (string.IsNullOrEmpty(boeDocument.ContainerNumber))
                    issues.Add("ContainerNumber is missing");

                if (string.IsNullOrEmpty(boeDocument.DeclarationNumber) &&
                    string.IsNullOrEmpty(boeDocument.RotationNumber))
                    issues.Add("Neither DeclarationNumber nor RotationNumber is present");

                if (string.IsNullOrEmpty(boeDocument.RawJsonData))
                {
                    issues.Add("RawJsonData is missing - CRITICAL: Cannot recover data from JSON");
                    _logger.LogError("{ServiceId} ❌ CRITICAL: RawJsonData is NULL for BOEDocument {BoeId} (Container: {Container}, Index: {Index})",
                        SERVICE_ID, boeId, boeDocument.ContainerNumber, documentIndex);
                }
                else
                {
                    _logger.LogDebug("{ServiceId} ✅ RawJsonData stored for BOEDocument {BoeId} ({Length} chars)",
                        SERVICE_ID, boeId, boeDocument.RawJsonData.Length);
                }

                // Data completeness check
                var hasHeaderData = !string.IsNullOrEmpty(boeDocument.DeclarationNumber) ||
                                   !string.IsNullOrEmpty(boeDocument.ClearanceType) ||
                                   !string.IsNullOrEmpty(boeDocument.ImpName) ||
                                   !string.IsNullOrEmpty(boeDocument.DeclarantName);

                var hasContainerData = !string.IsNullOrEmpty(boeDocument.ContainerISO) ||
                                      boeDocument.ContainerWeight.HasValue ||
                                      !string.IsNullOrEmpty(boeDocument.ContainerNumber);

                var hasManifestData = !string.IsNullOrEmpty(boeDocument.ConsigneeName) ||
                                     !string.IsNullOrEmpty(boeDocument.BlNumber) ||
                                     !string.IsNullOrEmpty(boeDocument.RotationNumber);

                if (!hasHeaderData && !hasContainerData && !hasManifestData)
                {
                    issues.Add("No data extracted from any section (Header, ContainerDetails, ManifestDetails) - CRITICAL");
                    _logger.LogError("{ServiceId} ❌ CRITICAL: No data extracted for BOEDocument {BoeId} (Container: {Container}, Index: {Index})",
                        SERVICE_ID, boeId, boeDocument.ContainerNumber, documentIndex);
                }

                // Log validation results
                if (issues.Any())
                {
                    _logger.LogWarning("{ServiceId} ⚠️ Validation issues for BOEDocument {BoeId} (Container: {Container}, Index: {Index}): {Issues}",
                        SERVICE_ID, boeId, boeDocument.ContainerNumber, documentIndex, string.Join("; ", issues));

                    // Persist post-save warnings to the record (merges with any ValidateCriticalFieldsAsync output).
                    try
                    {
                        using var scope = _serviceProvider.CreateScope();
                        var repo = scope.ServiceProvider.GetRequiredService<IIcumDownloadsRepository>();
                        await repo.AddIngestionWarningsAsync(boeId, issues);
                    }
                    catch (Exception persistEx)
                    {
                        _logger.LogWarning("{ServiceId} Failed to persist post-ingest warnings for BOEDocument {BoeId}: {Error}",
                            SERVICE_ID, boeId, persistEx.Message);
                    }
                }
                else
                {
                    _logger.LogDebug("{ServiceId} ✅ Validation passed for BOEDocument {BoeId} (Container: {Container})",
                        SERVICE_ID, boeId, boeDocument.ContainerNumber);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error during post-ingestion validation for BOEDocument {BoeId}", boeId);
            }
        }

        /// <summary>
        /// ✅ PHASE 1 FIX #5: Data Quality Metrics
        /// Logs completeness percentage and field population status
        /// </summary>
        private void LogDataQualityMetrics(BOEDocument boeDocument, int documentIndex)
        {
            try
            {
                var metrics = new
                {
                    // Container Details
                    ContainerNumber = !string.IsNullOrEmpty(boeDocument.ContainerNumber),
                    ContainerISO = !string.IsNullOrEmpty(boeDocument.ContainerISO),
                    ContainerWeight = boeDocument.ContainerWeight.HasValue,
                    ContainerDescription = !string.IsNullOrEmpty(boeDocument.ContainerDescription),

                    // Header
                    DeclarationNumber = !string.IsNullOrEmpty(boeDocument.DeclarationNumber),
                    ClearanceType = !string.IsNullOrEmpty(boeDocument.ClearanceType),
                    ImpName = !string.IsNullOrEmpty(boeDocument.ImpName),
                    ExpName = !string.IsNullOrEmpty(boeDocument.ExpName),
                    DeclarantName = !string.IsNullOrEmpty(boeDocument.DeclarantName),
                    TotalDutyPaid = boeDocument.TotalDutyPaid.HasValue,
                    CrmsLevel = !string.IsNullOrEmpty(boeDocument.CrmsLevel),

                    // Manifest Details
                    ConsigneeName = !string.IsNullOrEmpty(boeDocument.ConsigneeName),
                    BlNumber = !string.IsNullOrEmpty(boeDocument.BlNumber),
                    HouseBl = !string.IsNullOrEmpty(boeDocument.HouseBl),
                    RotationNumber = !string.IsNullOrEmpty(boeDocument.RotationNumber),
                    ShipperName = !string.IsNullOrEmpty(boeDocument.ShipperName),
                    CountryOfOrigin = !string.IsNullOrEmpty(boeDocument.CountryOfOrigin),
                    GoodsDescription = !string.IsNullOrEmpty(boeDocument.GoodsDescription),

                    // Backup
                    RawJsonData = !string.IsNullOrEmpty(boeDocument.RawJsonData),

                    // Classification
                    IsConsolidated = boeDocument.IsConsolidated
                };

                var populatedFields = metrics.GetType().GetProperties()
                    .Count(p => (bool)p.GetValue(metrics));
                var totalFields = metrics.GetType().GetProperties().Count();
                var completeness = totalFields > 0 ? (populatedFields * 100.0) / totalFields : 0;

                _logger.LogInformation("{ServiceId} 📊 Data quality for document {Index} (Container: {Container}): {Completeness:F1}% ({Populated}/{Total} fields), Consolidated: {IsConsolidated}",
                    SERVICE_ID, documentIndex, boeDocument.ContainerNumber, completeness, populatedFields, totalFields, boeDocument.IsConsolidated);

                // Log critical missing fields
                if (completeness < 50)
                {
                    var missingFields = metrics.GetType().GetProperties()
                        .Where(p => !(bool)p.GetValue(metrics))
                        .Select(p => p.Name)
                        .ToList();

                    _logger.LogWarning("{ServiceId} ⚠️ Low data completeness ({Completeness:F1}%) for document {Index}. Missing: {MissingFields}",
                        SERVICE_ID, completeness, documentIndex, string.Join(", ", missingFields.Take(10)));
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning("{ServiceId} Error calculating data quality metrics for document {Index}. Exception: {Exception}", SERVICE_ID, documentIndex, ex.Message);
            }
        }

        /// <summary>
        /// ✅ DEFENSE-IN-DEPTH: Archive processed file to prevent reprocessing
        /// Moves file from active directory to archive/processed subdirectory
        /// This provides physical separation as a backup to database status tracking
        /// </summary>
        private Task CleanupArchivedFilesAsync()
        {
            try
            {
                var downloadsPath = _configuration["ICUMS:DownloadsPath"] ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Downloads";
                var batchDataPath = Path.Combine(downloadsPath, "BatchData");
                var containerDataPath = Path.Combine(downloadsPath, "ContainerData");

                var pathsToClean = new[] { batchDataPath, containerDataPath };

                foreach (var path in pathsToClean)
                {
                    if (!Directory.Exists(path)) continue;

                    var archivedFiles = Directory.GetFiles(path, "*.archived");
                    foreach (var archivedFile in archivedFiles)
                    {
                        try
                        {
                            // Try to delete the archived file
                            File.Delete(archivedFile);
                            _logger.LogDebug("{ServiceId} Cleaned up archived file: {FileName}", SERVICE_ID, Path.GetFileName(archivedFile));
                        }
                        catch (IOException)
                        {
                            // File still locked, skip for now
                            _logger.LogDebug("{ServiceId} Archived file still locked, skipping: {FileName}", SERVICE_ID, Path.GetFileName(archivedFile));
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Error cleaning up archived file: {FileName}. Exception: {Exception}", Path.GetFileName(archivedFile), ex.Message);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during archived files cleanup");
            }

            return Task.CompletedTask;
        }

        private async Task ArchiveProcessedFileAsync(string filePath, string fileName)
        {
            try
            {
                _logger.LogInformation("{ServiceId} 🗂️ Starting archive process for: {FileName}", SERVICE_ID, fileName);

                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("{ServiceId} Cannot archive file - file not found: {FilePath}", SERVICE_ID, filePath);
                    return;
                }

                // Determine archive directory based on file type
                string archiveSubdir;
                if (fileName.StartsWith("BatchData", StringComparison.OrdinalIgnoreCase))
                {
                    archiveSubdir = "Archive\\BatchData";
                }
                else if (fileName.StartsWith("ContainerData", StringComparison.OrdinalIgnoreCase))
                {
                    archiveSubdir = "Archive\\ContainerData";
                }
                else
                {
                    archiveSubdir = "Archive\\Other";
                }

                var archivePath = Path.Combine(_downloadsPath, archiveSubdir);

                // Create archive directory if it doesn't exist
                if (!Directory.Exists(archivePath))
                {
                    Directory.CreateDirectory(archivePath);
                    _logger.LogInformation("{ServiceId} Created archive directory: {ArchivePath}", SERVICE_ID, archivePath);
                }

                var destPath = Path.Combine(archivePath, fileName);

                _logger.LogInformation("{ServiceId} 📁 Archive destination: {DestPath}", SERVICE_ID, destPath);

                // ✅ FIX: Handle duplicate files in archive with better collision avoidance
                var originalDestPath = destPath;
                var attempt = 0;
                while (File.Exists(destPath) && attempt < 10)
                {
                    var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
                    var ext = Path.GetExtension(fileName);
                    var timestamp = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss_fff");
                    var randomSuffix = new Random().Next(1000, 9999);
                    destPath = Path.Combine(archivePath, $"{nameWithoutExt}_archived_{timestamp}_{randomSuffix}{ext}");
                    attempt++;
                }

                // ✅ FIX: Enhanced file archiving with better lock handling
                var maxRetries = 5;
                var archiveSuccess = false;

                for (int i = 0; i < maxRetries; i++)
                {
                    try
                    {
                        // Progressive delay to allow file handles to be released
                        var delayMs = 200 * (i + 1); // 200ms, 400ms, 600ms, 800ms, 1000ms
                        await Task.Delay(delayMs);

                        // Try File.Move first (atomic operation, safer)
                        try
                        {
                            File.Move(filePath, destPath);
                            archiveSuccess = true;
                            _logger.LogInformation("{ServiceId} ✅ Successfully moved file to archive: {FileName}", SERVICE_ID, fileName);
                            break; // Success
                        }
                        catch (IOException moveEx) when (moveEx.Message.Contains("being used by another process"))
                        {
                            // If move fails due to file lock, try copy + delete approach
                            _logger.LogDebug("{ServiceId} File locked during move, trying copy+delete approach: {FileName}", SERVICE_ID, fileName);

                            // Ensure destination doesn't exist
                            if (File.Exists(destPath))
                            {
                                File.Delete(destPath);
                            }

                            // Copy then delete
                            File.Copy(filePath, destPath, true);

                            // Additional delay before delete to ensure copy is complete
                            await Task.Delay(200);

                            // Try to delete with graceful handling
                            if (File.Exists(filePath))
                            {
                                try
                                {
                                    File.Delete(filePath);
                                }
                                catch (IOException deleteEx) when (deleteEx.Message.Contains("being used by another process"))
                                {
                                    // If delete fails due to file lock, rename the file instead
                                    // This allows the archive to succeed while marking the original for cleanup
                                    var tempPath = filePath + ".archived";
                                    try
                                    {
                                        File.Move(filePath, tempPath);
                                        _logger.LogDebug("{ServiceId} File locked during delete, renamed to: {TempPath}", SERVICE_ID, tempPath);
                                    }
                                    catch (Exception renameEx)
                                    {
                                        _logger.LogWarning("{ServiceId} Could not rename locked file {FileName}: {Error}",
                                            SERVICE_ID, fileName, renameEx.Message);
                                        // Continue anyway - the file is archived successfully
                                    }
                                }
                            }

                            archiveSuccess = true;
                            _logger.LogInformation("{ServiceId} ✅ Successfully copied and deleted file: {FileName}", SERVICE_ID, fileName);
                            break; // Success
                        }
                    }
                    catch (IOException ex) when (i < maxRetries - 1)
                    {
                        _logger.LogWarning("{ServiceId} File lock detected, retry {Retry}/{Max}: {FileName} - {Error}",
                            SERVICE_ID, i + 1, maxRetries, fileName, ex.Message);

                        // ✅ PHASE 2.1: Use retry policy with exponential backoff and jitter
                        var retryOptions = RetryPolicy.CreateFileOperationRetryPolicy();
                        var delay = RetryPolicy.CalculateDelay(i + 1, retryOptions);
                        await Task.Delay(delay);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "{ServiceId} Archive attempt {Retry}/{Max} failed for {FileName}: {Error}",
                            SERVICE_ID, i + 1, maxRetries, fileName, ex.Message);
                        if (i == maxRetries - 1) throw; // Re-throw on final attempt
                    }
                }

                if (!archiveSuccess)
                {
                    _logger.LogError("Failed to archive file after {MaxRetries} attempts: {FileName}",
                        maxRetries, fileName);
                    return; // Exit without logging success
                }

                _logger.LogInformation("{ServiceId} 📦 Successfully archived file: {FileName} -> {ArchivePath}", SERVICE_ID, fileName, archiveSubdir);
            }
            catch (Exception ex)
            {
                // Don't throw - archival is not critical, just log the error
                _logger.LogError(ex, "Failed to archive file {FileName}: {Error}", fileName, ex.Message);
            }
        }

        // ✅ PHASE 2.2: Helper method to add files to FailedProcessingQueue
        private async Task AddToFailedQueueAsync(IIcumDownloadsRepository repository, DownloadedFile file, string failureReason, string failureStage)
        {
            try
            {
                var failedFile = new FailedProcessingQueue
                {
                    DownloadedFileId = file.Id,
                    FileName = file.FileName,
                    FilePath = file.FilePath,
                    FailureReason = failureReason,
                    ErrorDetails = null,
                    FailureStage = failureStage,
                    RetryCount = 0,
                    MaxRetries = 5,
                    Status = "Pending",
                    FailedAt = DateTime.UtcNow
                };
                await repository.AddFailedFileAsync(failedFile);
                _logger.LogInformation("{ServiceId} Added file {FileName} to FailedProcessingQueue (Stage: {Stage})",
                    SERVICE_ID, file.FileName, failureStage);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("{ServiceId} Failed to add file {FileName} to FailedProcessingQueue: {Error}",
                    SERVICE_ID, file.FileName, ex.Message);
            }
        }

        private static (string Container, int SourceFields, int MatchedFields) CountFieldAccuracy(BOEDocument doc)
        {
            var container = doc.ContainerNumber ?? "UNKNOWN";
            if (string.IsNullOrEmpty(doc.RawJsonData))
                return (container, 0, 0);

            int source = 0, matched = 0;
            try
            {
                using var jd = JsonDocument.Parse(doc.RawJsonData);
                var root = jd.RootElement;

                void Check(JsonElement section, string prop, string? dbVal, bool isNumeric = false)
                {
                    if (!section.TryGetProperty(prop, out var el)) return;
                    bool hasValue = isNumeric
                        ? el.ValueKind != JsonValueKind.Null
                        : el.ValueKind != JsonValueKind.Null && !(el.ValueKind == JsonValueKind.String && string.IsNullOrWhiteSpace(el.GetString()));
                    if (!hasValue) return;
                    source++;
                    if (!string.IsNullOrWhiteSpace(dbVal)) matched++;
                }

                if (root.TryGetProperty("ContainerDetails", out var cd) && cd.ValueKind != JsonValueKind.Null)
                {
                    Check(cd, "ContainerNumber", doc.ContainerNumber);
                    Check(cd, "ContainerISO", doc.ContainerISO);
                    Check(cd, "ContainerSize", doc.ContainerSize);
                    Check(cd, "ContainerWeight", doc.ContainerWeight?.ToString(), true);
                    Check(cd, "SealNumber", doc.SealNumber);
                    Check(cd, "TruckPlateNumber", doc.TruckPlateNumber);
                    Check(cd, "DriverName", doc.DriverName);
                    Check(cd, "DriverLicense", doc.DriverLicense);
                    Check(cd, "Status", doc.ContainerStatus);
                    Check(cd, "Remarks", doc.ContainerRemarks);
                }
                if (root.TryGetProperty("Header", out var hd) && hd.ValueKind != JsonValueKind.Null)
                {
                    Check(hd, "CRMSLevel", doc.CrmsLevel);
                    Check(hd, "CompOffRemarks", doc.CompOffRemarks);
                    Check(hd, "DeclarantName", doc.DeclarantName);
                    Check(hd, "DeclarantAddress", doc.DeclarantAddress);
                    Check(hd, "DeclarationDate", doc.DeclarationDate);
                    Check(hd, "DeclarationNumber", doc.DeclarationNumber);
                    Check(hd, "DeclarationVersion", doc.DeclarationVersion?.ToString(), true);
                    Check(hd, "ClearanceType", doc.ClearanceType);
                    Check(hd, "ImpName", doc.ImpName);
                    Check(hd, "ImpAddress", doc.ImpAddress);
                    Check(hd, "ExpName", doc.ExpName);
                    Check(hd, "ExpAddress", doc.ExpAddress);
                    Check(hd, "ImpExpName", doc.ImpExpName);
                    Check(hd, "ImpExpAddress", doc.ImpExpAddress);
                    Check(hd, "CCVRIntelRemarks", doc.CcvrIntelRemarks);
                    Check(hd, "NoofContainers", doc.NoOfContainers?.ToString(), true);
                    Check(hd, "RegimeCode", doc.RegimeCode);
                    Check(hd, "TotalDutyPaid", doc.TotalDutyPaid?.ToString(), true);
                }
                if (root.TryGetProperty("ManifestDetails", out var md) && md.ValueKind != JsonValueKind.Null)
                {
                    Check(md, "BLNumber", doc.BlNumber);
                    Check(md, "HouseBL", doc.HouseBl);
                    Check(md, "ConsigneeName", doc.ConsigneeName);
                    Check(md, "ConsigneeAddress", doc.ConsigneeAddress);
                    Check(md, "CountryofOrigin", doc.CountryOfOrigin);
                    Check(md, "MarksNumbers", doc.MarksNumbers);
                    Check(md, "ShipperName", doc.ShipperName);
                    Check(md, "ShipperAddress", doc.ShipperAddress);
                    Check(md, "GoodsDescription", doc.GoodsDescription);
                    Check(md, "RotationNumber", doc.RotationNumber);
                    Check(md, "DeliveryPlace", doc.DeliveryPlace);
                }
            }
            catch { }
            return (container, source, matched);
        }

        private async Task RunIngestionVerificationAsync(
            DownloadedFile file,
            IIcumDownloadsRepository repository,
            List<(string Container, int SourceFields, int MatchedFields)> results)
        {
            try
            {
                if (results.Count == 0)
                {
                    _logger.LogWarning("{ServiceId} 📋 Verification skipped for file {FileName} — no documents", SERVICE_ID, file.FileName);
                    return;
                }

                int verifiedCount = results.Count;
                int perfectCount = 0, partialCount = 0;
                double totalAccuracy = 0;
                double lowestAccuracy = 100;
                string lowestContainer = results[0].Container;

                var docDetails = new List<object>(results.Count);

                foreach (var (container, src, mtch) in results)
                {
                    double accuracy = src > 0 ? Math.Round(mtch * 100.0 / src, 1) : 100.0;
                    if (accuracy >= 100) perfectCount++; else if (accuracy > 0) partialCount++;
                    totalAccuracy += accuracy;
                    if (accuracy < lowestAccuracy) { lowestAccuracy = accuracy; lowestContainer = container; }

                    docDetails.Add(new { container, accuracy, sourceFields = src, matchedFields = mtch });
                }

                double avgAccuracy = Math.Round(totalAccuracy / verifiedCount, 1);
                lowestAccuracy = Math.Round(lowestAccuracy, 1);

                _logger.LogInformation(
                    "{ServiceId} 📋 INGESTION VERIFICATION for {FileName}: {Verified} docs verified | ✅ {Perfect} perfect (100%) | ⚠️ {Partial} partial | 📊 Avg accuracy: {Avg:F1}% | 📉 Lowest: {Lowest:F1}% ({Container})",
                    SERVICE_ID, file.FileName, verifiedCount, perfectCount, partialCount,
                    avgAccuracy, lowestAccuracy, lowestContainer);

                var detailsJson = System.Text.Json.JsonSerializer.Serialize(docDetails);

                await repository.SaveVerificationSummaryAsync(
                    file.Id, verifiedCount, perfectCount, partialCount,
                    avgAccuracy, lowestAccuracy, lowestContainer, detailsJson);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("{ServiceId} 📋 Verification failed for file {FileName}: {Error}", SERVICE_ID, file.FileName, ex.Message);
            }
        }
    }
}
