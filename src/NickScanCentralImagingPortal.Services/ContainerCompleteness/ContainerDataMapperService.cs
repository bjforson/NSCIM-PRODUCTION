using System.IO;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ContainerCompleteness
{
    /// <summary>
    /// Service for mapping scanner data to ICUMS BOE data
    /// </summary>
    public class ContainerDataMapperService : BackgroundService, IContainerDataMapperService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<ContainerDataMapperService> _logger;
        private const string SERVICE_ID = "[CONTAINER-DATA-MAPPER]";
        private int _consecutiveDatabaseUnavailableCount = 0;
        private const int MAX_WARNING_LOGS = 3; // Only log warnings for first 3 attempts

        // ✅ MEMORY FIX: Removed direct injection of scoped IContainerDataRepository
        // Background services (singletons) cannot inject scoped services directly
        // We'll use IServiceProvider to create scopes when needed
        public ContainerDataMapperService(
            IServiceProvider serviceProvider,
            ILogger<ContainerDataMapperService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Add random startup delay to prevent all services from starting simultaneously
            var startupDelay = new Random().Next(1000, 5000);
            await Task.Delay(startupDelay, stoppingToken);

            _logger.LogInformation("ContainerDataMapperService started - mapping containers at configured interval from settings");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Check if we can access databases before processing
                    if (await CanAccessDatabasesAsync())
                    {
                        // Reset counter when database is accessible
                        if (_consecutiveDatabaseUnavailableCount > 0)
                        {
                            _logger.LogInformation("{ServiceId} Database connectivity restored after {Count} attempts",
                                SERVICE_ID, _consecutiveDatabaseUnavailableCount);
                            _consecutiveDatabaseUnavailableCount = 0;
                        }

                        await ProcessPendingMappingsAsync(stoppingToken);
                        _logger.LogDebug("Container data mapping processing completed successfully");
                    }
                    else
                    {
                        _consecutiveDatabaseUnavailableCount++;
                        // Only log warnings for first few attempts, then use Debug to reduce noise
                        if (_consecutiveDatabaseUnavailableCount <= MAX_WARNING_LOGS)
                        {
                            _logger.LogWarning("{ServiceId} Databases not accessible, skipping mapping processing cycle (attempt {Count}/{Max})",
                                SERVICE_ID, _consecutiveDatabaseUnavailableCount, MAX_WARNING_LOGS);
                        }
                        else
                        {
                            _logger.LogDebug("{ServiceId} Databases not accessible, skipping mapping processing cycle (attempt {Count})",
                                SERVICE_ID, _consecutiveDatabaseUnavailableCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Check if it's a database connectivity error
                    if (IsDatabaseConnectivityException(ex))
                    {
                        _consecutiveDatabaseUnavailableCount++;
                        if (_consecutiveDatabaseUnavailableCount <= MAX_WARNING_LOGS)
                        {
                            _logger.LogWarning(ex, "{ServiceId} Database connectivity error during mapping processing (attempt {Count}/{Max})",
                                SERVICE_ID, _consecutiveDatabaseUnavailableCount, MAX_WARNING_LOGS);
                        }
                        else
                        {
                            _logger.LogDebug(ex, "{ServiceId} Database connectivity error during mapping processing (attempt {Count})",
                                SERVICE_ID, _consecutiveDatabaseUnavailableCount);
                        }
                    }
                    else
                    {
                        // Actual error (not connectivity) - always log
                        _logger.LogError(ex, "{ServiceId} Error during container data mapping processing", SERVICE_ID);
                    }
                }

                // Wait for configured interval (read from database settings)
                using (var scope = _serviceProvider.CreateScope())
                {
                    var settingsProvider = scope.ServiceProvider.GetRequiredService<ISettingsProvider>();
                    var mappingIntervalMinutes = await settingsProvider.GetIntAsync("BackgroundServices", "ContainerDataMapperService.MappingIntervalMinutes", 5);
                    _logger.LogDebug("⏰ Next data mapping in {Interval} minutes (from settings)", mappingIntervalMinutes);
                    await Task.Delay(TimeSpan.FromMinutes(mappingIntervalMinutes), stoppingToken);
                }
            }

            _logger.LogInformation("ContainerDataMapperService stopped");
        }

        // ✅ MEMORY FIX: Create scope to access scoped repository
        private async Task<bool> CanAccessDatabasesAsync()
        {
            using var scope = _serviceProvider.CreateScope();
            var containerDataRepository = scope.ServiceProvider.GetRequiredService<IContainerDataRepository>();
            return await containerDataRepository.CanConnectToDatabasesAsync();
        }

        private static bool IsDatabaseConnectivityException(Exception ex)
        {
            if (ex is NpgsqlException or PostgresException)
            {
                var msg = ex.Message;
                return msg.Contains("connection", StringComparison.OrdinalIgnoreCase) ||
                       msg.Contains("refused", StringComparison.OrdinalIgnoreCase) ||
                       msg.Contains("timeout", StringComparison.OrdinalIgnoreCase) ||
                       msg.Contains("broken pipe", StringComparison.OrdinalIgnoreCase);
            }

            var message = ex.Message.ToLowerInvariant();
            return message.Contains("could not open a connection") ||
                   message.Contains("connection refused") ||
                   message.Contains("timeout") ||
                   (ex.InnerException != null && IsDatabaseConnectivityException(ex.InnerException));
        }

        public async Task ProcessPendingMappingsAsync(CancellationToken stoppingToken)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            _logger.LogInformation("Starting container data mapping processing");

            try
            {
                // Get containers that have both scanner data and ICUMS data but no mapping
                var pendingMappings = await GetPendingMappingsAsync(dbContext);
                _logger.LogInformation("Found {Count} containers pending mapping", pendingMappings.Count);

                var processedCount = 0;
                foreach (var mapping in pendingMappings)
                {
                    try
                    {
                        await MapContainerDataAsync(
                            mapping.ContainerNumber,
                            mapping.ScannerType,
                            mapping.ScannerDataId,
                            mapping.ICUMSDataId);

                        processedCount++;
                        _logger.LogDebug("Mapped container {ContainerNumber} ({ScannerType})",
                            mapping.ContainerNumber, mapping.ScannerType);

                        if (processedCount % 50 == 0)
                        {
                            _logger.LogDebug("Processed {Count} mappings", processedCount);
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error mapping container {ContainerNumber}", mapping.ContainerNumber);
                    }
                }

                _logger.LogInformation("Container data mapping processing completed - processed {Count} mappings", processedCount);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during container data mapping processing");
                throw;
            }
        }

        public async Task<ContainerBOERelation?> MapContainerDataAsync(string containerNumber, string scannerType, int scannerDataId, int icumsDataId)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            // ✅ CONSOLIDATED CARGO: Check if this BOE is consolidated
            var icumDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();
            var boeDocument = await icumDbContext.BOEDocuments
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.Id == icumsDataId);

            // ── CARDINAL PORT RULE (belt-and-braces) ──
            // The upstream queue-driven gate at ContainerCompletenessService.cs:395-454
            // already zeroes CCS.HasICUMSData on port mismatch, which normally keeps
            // the mapper from being called. But other writers (CMR-upgrade cascade at
            // IcumJsonIngestionService.cs:1433, manual SQL, admin tools) can flip
            // HasICUMSData back to true without consulting the port rule. So the
            // mapper must independently enforce the cardinal rule before INSERTing
            // a ContainerBOERelation row. FS6000 -> DP must contain TKD; ASE -> TMA.
            // Null DP allowed (upstream gap) — matches IsLocationMatch semantics.
            if (boeDocument != null && !ScannerLocationMap.IsLocationMatch(scannerType, boeDocument.DeliveryPlace))
            {
                var expectedPort = ScannerLocationMap.GetExpectedPortCode(scannerType);
                var actualPort = ScannerLocationMap.ExtractPortCode(boeDocument.DeliveryPlace) ?? "UNKNOWN";
                _logger.LogWarning(
                    "PORT MISMATCH (mapper, cardinal): {Container} scanner={Scanner} (expected {Expected}) but BOE.DeliveryPlace='{Dp}' (port={Actual}). Rejecting CBR INSERT.",
                    containerNumber, scannerType, expectedPort, boeDocument.DeliveryPlace, actualPort);

                await UpsertPortMismatchFlagAsync(dbContext, containerNumber, scannerType, boeDocument.Id,
                    description: $"Mapper-side cardinal port-rule rejection. Scanner={scannerType} (expected {expectedPort}) vs BOE.DeliveryPlace='{boeDocument.DeliveryPlace}' (port={actualPort}). Match suppressed at INSERT time.");

                return null;
            }

            // ── 3-LAYER FYCO RULE (mapper belt-and-braces, layers 2 + 3) ──
            // The cardinal port rule above SKIPS when DeliveryPlace is null/short
            // (matches IsLocationMatch semantics) — but a fyco-direction conflict
            // can still slip through that gap. MEDU7718311 (sweep 2026-05-04) was
            // exactly this case: regime-40 IM BOE with null DP + FS6000 fyco=
            // WAYBILL/EXPORT — port rule skipped, mapper had no fyco check, CBR
            // got rebuilt after the morning unmatch.
            //
            // Audit 3.03 (2026-05-05): the rule body now lives in
            // FycoRuleEvaluator (Core); the mapper only does its own scan fetch +
            // its own flag write so the existing "Mapper-side ..." description
            // shape is preserved. Step 1 / Step 2 / mapper all consult the same
            // evaluator — three sources of truth collapsed to one.
            if (boeDocument != null && string.Equals(scannerType, CommonScannerTypes.FS6000, StringComparison.OrdinalIgnoreCase))
            {
                var fs6000Scan = await dbContext.FS6000Scans
                    .AsNoTracking()
                    .Where(s => s.ContainerNumber == containerNumber)
                    .OrderByDescending(s => s.ScanTime)
                    .Select(s => new { s.FycoPresent })
                    .FirstOrDefaultAsync();

                var fycoResult = FycoRuleEvaluator.Evaluate(
                    scannerType,
                    fs6000Scan?.FycoPresent,
                    boeDocument.ClearanceType,
                    boeDocument.RegimeCode);

                if (fycoResult.IsBlockingFailure)
                {
                    var layerLabel = fycoResult.Outcome == FycoRuleOutcome.FailLayer2_ClearanceTypeImport ? "layer 2" : "layer 3";
                    var rejectionReason = $"Mapper-side fyco rule ({layerLabel}): {fycoResult.FlagDescription}";
                    _logger.LogWarning("FYCO MISMATCH (mapper, {Layer}): {Container} -> {Reason}", layerLabel, containerNumber, rejectionReason);
                    await UpsertFycoMismatchFlagAsync(dbContext, containerNumber, scannerType, boeDocument.Id, rejectionReason);
                    return null;
                }
            }

            var mapping = await UpsertActiveContainerBoeRelationAsync(
                dbContext,
                containerNumber,
                scannerType,
                scannerDataId,
                icumsDataId,
                boeDocument?.IsConsolidated == true);

            // ✅ MEMORY FIX: Clear change tracker to release tracked entities
            dbContext.ChangeTracker.Clear();

            var relationType = boeDocument?.IsConsolidated == true ? "CONSOLIDATED" : "PRIMARY";
            _logger.LogInformation("Upserted {RelationType} mapping for container {ContainerNumber} ({ScannerType}) -> ICUMS ID {ICUMSId}",
                relationType, containerNumber, scannerType, icumsDataId);

            return mapping;
        }

        private async Task<ContainerBOERelation> UpsertActiveContainerBoeRelationAsync(
            ApplicationDbContext dbContext,
            string containerNumber,
            string scannerType,
            int scannerDataId,
            int icumsDataId,
            bool isConsolidated)
        {
            try
            {
                return await UpsertActiveContainerBoeRelationCoreAsync(
                    dbContext,
                    containerNumber,
                    scannerType,
                    scannerDataId,
                    icumsDataId,
                    isConsolidated,
                    allowInsert: true);
            }
            catch (DbUpdateException ex) when (IsActiveContainerRelationUniqueViolation(ex))
            {
                _logger.LogWarning(
                    ex,
                    "{ServiceId} Active CBR unique index raced for {ContainerNumber}; retrying as update",
                    SERVICE_ID,
                    containerNumber);

                dbContext.ChangeTracker.Clear();
                return await UpsertActiveContainerBoeRelationCoreAsync(
                    dbContext,
                    containerNumber,
                    scannerType,
                    scannerDataId,
                    icumsDataId,
                    isConsolidated,
                    allowInsert: false);
            }
        }

        private static async Task<ContainerBOERelation> UpsertActiveContainerBoeRelationCoreAsync(
            ApplicationDbContext dbContext,
            string containerNumber,
            string scannerType,
            int scannerDataId,
            int icumsDataId,
            bool isConsolidated,
            bool allowInsert)
        {
            var now = DateTime.UtcNow;
            var relationType = isConsolidated ? "Consolidated-HouseBL" : "Primary";
            var relations = await dbContext.ContainerBOERelations
                .Where(r => r.ContainerNumber == containerNumber)
                .OrderByDescending(r => r.IsActive)
                .ThenByDescending(r => r.CreatedAt)
                .ToListAsync();

            var mapping = relations.FirstOrDefault(r => IsSameMapping(r, scannerType, scannerDataId, icumsDataId))
                ?? relations.FirstOrDefault(r => r.IsActive)
                ?? relations.FirstOrDefault();

            if (mapping == null)
            {
                if (!allowInsert)
                {
                    mapping = await dbContext.ContainerBOERelations
                        .Where(r => r.ContainerNumber == containerNumber && r.IsActive)
                        .OrderByDescending(r => r.CreatedAt)
                        .FirstOrDefaultAsync();
                }

                if (mapping == null)
                {
                    mapping = new ContainerBOERelation
                    {
                        ContainerNumber = containerNumber,
                    };
                    dbContext.ContainerBOERelations.Add(mapping);
                }
            }

            var sameMapping = IsSameMapping(mapping, scannerType, scannerDataId, icumsDataId)
                && string.Equals(mapping.RelationType, relationType, StringComparison.Ordinal);
            var wasInactive = !mapping.IsActive;
            var activeRowsToDeactivate = relations
                .Where(r => r.Id != mapping.Id && r.IsActive)
                .ToList();

            if (wasInactive && activeRowsToDeactivate.Count > 0)
            {
                foreach (var staleActive in activeRowsToDeactivate)
                {
                    staleActive.IsActive = false;
                    staleActive.LastValidatedAt = now;
                }

                await dbContext.SaveChangesAsync();
            }

            mapping.ScannerType = scannerType;
            mapping.ScannerDataId = scannerDataId;
            mapping.ICUMSBOEId = icumsDataId;
            mapping.RelationType = relationType;
            mapping.IsActive = true;
            mapping.LastValidatedAt = now;

            if (!sameMapping || wasInactive || mapping.CreatedAt == default)
            {
                mapping.CreatedAt = now;
            }

            foreach (var staleActive in activeRowsToDeactivate.Where(r => r.IsActive))
            {
                staleActive.IsActive = false;
                staleActive.LastValidatedAt = now;
            }

            await dbContext.SaveChangesAsync();
            return mapping;
        }

        private static bool IsSameMapping(
            ContainerBOERelation relation,
            string scannerType,
            int scannerDataId,
            int icumsDataId)
        {
            return string.Equals(relation.ScannerType, scannerType, StringComparison.OrdinalIgnoreCase)
                && relation.ScannerDataId == scannerDataId
                && relation.ICUMSBOEId == icumsDataId;
        }

        private static bool IsActiveContainerRelationUniqueViolation(DbUpdateException ex)
        {
            for (var inner = ex.InnerException; inner != null; inner = inner.InnerException)
            {
                if (inner is PostgresException pgEx
                    && pgEx.SqlState == "23505"
                    && (string.Equals(pgEx.ConstraintName, "ix_cbr_active_per_container", StringComparison.OrdinalIgnoreCase)
                        || pgEx.MessageText.Contains("ix_cbr_active_per_container", StringComparison.OrdinalIgnoreCase)))
                {
                    return true;
                }
            }

            return ex.ToString().Contains("ix_cbr_active_per_container", StringComparison.OrdinalIgnoreCase);
        }

        // Idempotent MatchQualityFlag upsert for the cardinal port-rejection path.
        // Mirrors ContainerCompletenessService.WriteMatchQualityFlagAsync (private
        // there) so the mapper-side rejection is visible in /validation/match-corrections
        // alongside its sibling flags written by the matching pipeline.
        private static async Task UpsertPortMismatchFlagAsync(
            ApplicationDbContext db,
            string containerNumber,
            string? scannerType,
            int boeDocumentId,
            string description)
        {
            try
            {
                var existing = await db.MatchQualityFlags
                    .Where(f => f.ContainerNumber == containerNumber
                                && f.FlagType == "PortMismatch"
                                && !f.IsResolved)
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    existing.Description = description;
                    existing.Severity = "Critical";
                    existing.BOEDocumentId = boeDocumentId;
                    existing.ScannerType = scannerType;
                    await db.SaveChangesAsync();
                    return;
                }

                db.MatchQualityFlags.Add(new MatchQualityFlag
                {
                    ContainerNumber = containerNumber,
                    ScannerType = scannerType,
                    BOEDocumentId = boeDocumentId,
                    FlagType = "PortMismatch",
                    Severity = "Critical",
                    Description = description,
                    IsResolved = false,
                    CreatedAtUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            catch
            {
                // Best-effort flag write — never let it block the rejection path.
            }
        }

        // Same idempotent-upsert shape as UpsertPortMismatchFlagAsync, for the
        // mapper-side fyco-direction rejection. Belt-and-braces flag visible in
        // /validation/match-corrections under FlagType=FycoMismatch.
        private static async Task UpsertFycoMismatchFlagAsync(
            ApplicationDbContext db,
            string containerNumber,
            string? scannerType,
            int boeDocumentId,
            string description)
        {
            try
            {
                var existing = await db.MatchQualityFlags
                    .Where(f => f.ContainerNumber == containerNumber
                                && f.FlagType == "FycoMismatch"
                                && !f.IsResolved)
                    .FirstOrDefaultAsync();

                if (existing != null)
                {
                    existing.Description = description;
                    existing.Severity = "Critical";
                    existing.BOEDocumentId = boeDocumentId;
                    existing.ScannerType = scannerType;
                    await db.SaveChangesAsync();
                    return;
                }

                db.MatchQualityFlags.Add(new MatchQualityFlag
                {
                    ContainerNumber = containerNumber,
                    ScannerType = scannerType,
                    BOEDocumentId = boeDocumentId,
                    FlagType = "FycoMismatch",
                    Severity = "Critical",
                    Description = description,
                    IsResolved = false,
                    CreatedAtUtc = DateTime.UtcNow,
                });
                await db.SaveChangesAsync();
            }
            catch
            {
                // Best-effort flag write — never let it block the rejection path.
            }
        }

        public async Task<List<ContainerBOERelation>> GetContainerMappingsAsync(string containerNumber)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            return await dbContext.ContainerBOERelations
                .Where(r => r.ContainerNumber == containerNumber && r.IsActive)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();
        }

        public async Task<MappingValidationResult> ValidateMappingAsync(int relationId)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var icumDbContext = scope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

            var result = new MappingValidationResult { IsValid = true };

            try
            {
                var relation = await dbContext.ContainerBOERelations
                    .FirstOrDefaultAsync(r => r.Id == relationId);

                if (relation == null)
                {
                    result.IsValid = false;
                    result.Issues.Add("Mapping relation not found");
                    return result;
                }

                // Validate scanner data exists
                bool scannerDataExists = false;
                switch (relation.ScannerType.ToUpper())
                {
                    case "FS6000":
                        scannerDataExists = await dbContext.FS6000Scans.AnyAsync(s => s.Id.GetHashCode() == relation.ScannerDataId);
                        break;
                    case "ASE":
                        scannerDataExists = await dbContext.AseScans.AnyAsync(s => s.Id.GetHashCode() == relation.ScannerDataId);
                        break;
                }

                if (!scannerDataExists)
                {
                    result.IsValid = false;
                    result.Issues.Add($"Scanner data not found for {relation.ScannerType} ID {relation.ScannerDataId}");
                }

                // Validate ICUMS data exists
                var icumsDataExists = await icumDbContext.BOEDocuments
                    .AnyAsync(i => i.Id == relation.ICUMSBOEId);

                if (!icumsDataExists)
                {
                    result.IsValid = false;
                    result.Issues.Add($"ICUMS data not found for ID {relation.ICUMSBOEId}");
                }

                // Validate container numbers match
                string? scannerContainerNumber = null;
                switch (relation.ScannerType.ToUpper())
                {
                    case "FS6000":
                        var fs6000Scan = await dbContext.FS6000Scans.FirstOrDefaultAsync(s => s.Id.GetHashCode() == relation.ScannerDataId);
                        scannerContainerNumber = fs6000Scan?.ContainerNumber;
                        break;
                    case "ASE":
                        var aseScan = await dbContext.AseScans.FirstOrDefaultAsync(s => s.Id.GetHashCode() == relation.ScannerDataId);
                        scannerContainerNumber = aseScan?.ContainerNumber;
                        break;
                }

                var icumsData = await icumDbContext.BOEDocuments
                    .FirstOrDefaultAsync(i => i.Id == relation.ICUMSBOEId);

                if (scannerContainerNumber != null && icumsData != null)
                {
                    if (!string.Equals(scannerContainerNumber, icumsData.ContainerNumber, StringComparison.OrdinalIgnoreCase))
                    {
                        result.IsValid = false;
                        result.Issues.Add($"Container number mismatch: Scanner='{scannerContainerNumber}', ICUMS='{icumsData.ContainerNumber}'");
                    }
                }

                result.ValidationMessage = result.IsValid ? "Mapping is valid" : "Mapping has validation issues";
            }
            catch (Exception ex)
            {
                result.IsValid = false;
                result.Issues.Add($"Validation error: {ex.Message}");
                _logger.LogError(ex, "Error validating mapping {RelationId}", relationId);
            }

            return result;
        }

        public async Task<List<ContainerSubmissionData>> GetContainersReadyForSubmissionAsync(int limit = 100)
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            try
            {
                _logger.LogInformation("{ServiceId} Getting containers ready for submission with limit {Limit}", SERVICE_ID, limit);

                // Get active mappings with limit for performance - simplified query
                // Exclude VIN records (17 characters) - only include valid container numbers (11 characters)
                var mappings = await dbContext.ContainerBOERelations
                    .Where(r => r.IsActive && r.ContainerNumber.Length == 11)
                    .OrderByDescending(r => r.CreatedAt)
                    .Take(Math.Min(limit, 20)) // Further reduce limit for faster response
                    .Select(r => new
                    {
                        r.ContainerNumber,
                        r.ScannerType,
                        r.CreatedAt,
                        r.ScannerDataId,
                        ICUMSDataId = r.ICUMSBOEId
                    })
                    .ToListAsync();

                _logger.LogInformation("{ServiceId} Found {Count} active mappings", SERVICE_ID, mappings.Count);

                // Convert to ContainerSubmissionData with image paths
                var readyContainers = new List<ContainerSubmissionData>();

                foreach (var mapping in mappings)
                {
                    var imagePaths = await GetImagePathsForContainer(dbContext, mapping.ContainerNumber);
                    var sourceIdentity = await dbContext.ContainerCompletenessStatuses
                        .AsNoTracking()
                        .Where(status => status.ContainerNumber == mapping.ContainerNumber
                            && status.ScannerType == mapping.ScannerType
                            && status.HasImageData)
                        .OrderByDescending(status => status.ScanDate)
                        .Select(status => new
                        {
                            status.ScanImageAssetId,
                            status.OriginalScanRecordId,
                            status.SourceContainerLabel
                        })
                        .FirstOrDefaultAsync();

                    readyContainers.Add(new ContainerSubmissionData
                    {
                        ContainerNumber = mapping.ContainerNumber,
                        ScannerType = mapping.ScannerType,
                        ScannerDataId = mapping.ScannerDataId,
                        ICUMSDataId = mapping.ICUMSDataId,
                        RelationId = readyContainers.Count + 1,
                        ScanImageAssetId = sourceIdentity?.ScanImageAssetId,
                        OriginalScanRecordId = sourceIdentity?.OriginalScanRecordId,
                        SourceContainerLabel = sourceIdentity?.SourceContainerLabel,
                        ImagePaths = imagePaths,
                        ReportData = new Dictionary<string, object>(),
                        ScanDate = mapping.CreatedAt,
                        ICUMSDataDate = mapping.CreatedAt
                    });
                }

                _logger.LogInformation("{ServiceId} Returning {Count} containers ready for submission", SERVICE_ID, readyContainers.Count);
                return readyContainers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting containers ready for submission");
                return new List<ContainerSubmissionData>(); // Return empty list on error
            }
        }

        private async Task<List<PendingMapping>> GetPendingMappingsAsync(ApplicationDbContext dbContext)
        {
            var pendingMappings = new List<PendingMapping>();
            const int maxRetries = 3;
            const int baseDelayMs = 100;

            // ✅ FIX: Resolve ICUMSDataId (which is actually a BOEDocuments.Id) from the
            // IcumDownloadsDbContext. The placeholder `= 0` below was never being
            // resolved, so ContainerBOERelation.ICUMSBOEId rows were all written as 0.
            using var icumScope = _serviceProvider.CreateScope();
            var icumDownloadsContext = icumScope.ServiceProvider.GetRequiredService<IcumDownloadsDbContext>();

            for (int attempt = 1; attempt <= maxRetries; attempt++)
            {
                try
                {
                    // ✅ MEMORY OPTIMIZATION: Get FS6000 containers with ICUMS data but no mapping
                    // Use efficient query with projections instead of loading full entities
                    var fs6000Scans = await (from scan in dbContext.FS6000Scans.AsNoTracking()
                                             join completeness in dbContext.ContainerCompletenessStatuses.AsNoTracking()
                                                 on scan.ContainerNumber equals completeness.ContainerNumber
                                             where !string.IsNullOrEmpty(scan.ContainerNumber) &&
                                                   scan.ContainerNumber.Length == 11 &&
                                                   completeness.HasICUMSData &&
                                                   completeness.ScannerType == "FS6000"
                                             select new { scan.ContainerNumber, scan.Id }).ToListAsync();

                    // Get existing mappings to filter out already mapped containers
                    var existingMappings = await dbContext.ContainerBOERelations
                        .Where(r => r.ScannerType == "FS6000")
                        .Select(r => new { r.ContainerNumber, r.ScannerDataId })
                        .ToListAsync();

                    var fs6000Pending = fs6000Scans
                        .Where(scan => !existingMappings.Any(m =>
                            m.ContainerNumber == scan.ContainerNumber &&
                            m.ScannerDataId == scan.Id.GetHashCode()))
                        .Select(scan => new PendingMapping
                        {
                            ContainerNumber = scan.ContainerNumber,
                            ScannerType = "FS6000",
                            ScannerDataId = scan.Id.GetHashCode(),
                            ICUMSDataId = 0 // Resolved below from IcumDownloadsDbContext.BOEDocuments
                        }).ToList();

                    // ✅ MEMORY OPTIMIZATION: Get ASE containers with ICUMS data but no mapping
                    // Instead of loading entire AseScans table (24GB), use efficient query with projections
                    // ✅ CRITICAL FIX: Add date filter to prevent loading ALL AseScans into buffer pool
                    var thirtyDaysAgo = DateTime.UtcNow.AddDays(-30);
                    var aseScans = await (from scan in dbContext.AseScans.AsNoTracking()
                                          join completeness in dbContext.ContainerCompletenessStatuses.AsNoTracking()
                                              on scan.ContainerNumber equals completeness.ContainerNumber
                                          where scan.ScanTime >= thirtyDaysAgo &&
                                                !string.IsNullOrEmpty(scan.ContainerNumber) &&
                                                completeness.HasICUMSData &&
                                                completeness.ScannerType == "ASE"
                                          select new { scan.ContainerNumber, scan.Id }).ToListAsync();

                    // Get existing ASE mappings to filter out already mapped containers
                    var existingAseMappings = await dbContext.ContainerBOERelations
                        .Where(r => r.ScannerType == "ASE")
                        .Select(r => new { r.ContainerNumber, r.ScannerDataId })
                        .ToListAsync();

                    // ✅ FIX: ASE source DB stores comma-joined container numbers when
                    // a single inspection/truck carried more than one container (e.g.
                    // "MSMU2238000, MSMU1593191"). Previously the mapper wrote those
                    // merged strings verbatim into ContainerBOERelation.ContainerNumber,
                    // where they could never resolve to a BOE document (boedocuments
                    // rows are keyed on a single container number). Now we split on ','
                    // and emit one PendingMapping per container. We also filter out
                    // ASE "Unknown" scans (non-cargo / calibration / transmission scans
                    // with no container number) — they would never resolve and just
                    // create junk rows.
                    var asePending = new List<PendingMapping>();
                    var aseDedup = existingAseMappings
                        .Select(m => (m.ContainerNumber, m.ScannerDataId))
                        .ToHashSet();
                    foreach (var scan in aseScans)
                    {
                        if (string.IsNullOrWhiteSpace(scan.ContainerNumber)) continue;

                        // Split on ',' and trim whitespace; drop empties and "Unknown".
                        var tokens = scan.ContainerNumber
                            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                            .Where(t => !string.IsNullOrWhiteSpace(t)
                                        && !string.Equals(t, "Unknown", StringComparison.OrdinalIgnoreCase))
                            .Distinct()
                            .ToList();

                        var scannerDataId = scan.Id.GetHashCode();
                        foreach (var token in tokens)
                        {
                            // Dedup against existing rows AND against anything we already
                            // queued this cycle (same scan producing multiple tokens).
                            if (aseDedup.Contains((token, scannerDataId))) continue;
                            aseDedup.Add((token, scannerDataId));

                            asePending.Add(new PendingMapping
                            {
                                ContainerNumber = token,
                                ScannerType = "ASE",
                                ScannerDataId = scannerDataId,
                                ICUMSDataId = 0 // Resolved below from IcumDownloadsDbContext.BOEDocuments
                            });
                        }
                    }

                    pendingMappings.AddRange(fs6000Pending);
                    pendingMappings.AddRange(asePending);

                    // ✅ FIX: Resolve the actual BOE document id for each pending mapping.
                    // ContainerBOERelation.ICUMSBOEId is a foreign key to
                    // nickscan_downloads.boedocuments.id (despite the historical name).
                    // Previously this was left as 0 and never repaired, which is why
                    // all existing ContainerBOERelation.ICUMSBOEId rows are 0.
                    if (pendingMappings.Count > 0)
                    {
                        var containerNumbers = pendingMappings
                            .Select(m => m.ContainerNumber)
                            .Where(c => !string.IsNullOrWhiteSpace(c))
                            .Distinct()
                            .ToList();

                        if (containerNumbers.Count > 0)
                        {
                            // Audit 3.06 (2026-05-05): align the per-container tie-break
                            // with the canonical helper used by CCS Step 1 / Step 2 /
                            // ValidatePortMatchAsync — ProcessingStatus="Transferred" +
                            // OrderByDescending Id (NOT CreatedAt; see
                            // CanonicalBoeQueryExtensions doc-comment for rationale).
                            var boeLookup = await icumDownloadsContext.BOEDocuments
                                .AsNoTracking()
                                .Where(b => containerNumbers.Contains(b.ContainerNumber)
                                            && b.ProcessingStatus == "Transferred")
                                .GroupBy(b => b.ContainerNumber)
                                .Select(g => new
                                {
                                    ContainerNumber = g.Key,
                                    LatestId = g.OrderByDescending(b => b.Id).Select(b => b.Id).FirstOrDefault()
                                })
                                .ToDictionaryAsync(x => x.ContainerNumber, x => x.LatestId);

                            foreach (var mapping in pendingMappings)
                            {
                                if (boeLookup.TryGetValue(mapping.ContainerNumber, out var boeId) && boeId > 0)
                                {
                                    mapping.ICUMSDataId = boeId;
                                }
                                // Else leave at 0; downstream filtering / logging below handles it.
                            }

                            var unresolved = pendingMappings.Where(m => m.ICUMSDataId == 0).ToList();
                            if (unresolved.Count > 0)
                            {
                                _logger.LogWarning("[MAPPER] {Count} pending mappings could not resolve an ICUMS BOE document id. First few: {Samples}",
                                    unresolved.Count, string.Join(", ", unresolved.Select(u => u.ContainerNumber).Take(5)));
                            }
                        }
                    }

                    // Success - break out of retry loop
                    break;
                }
                catch (PostgresException pgEx) when (pgEx.SqlState == "40P01" && attempt < maxRetries)
                {
                    // Deadlock detected (PostgreSQL 40P01) - retry with exponential backoff
                    var delay = baseDelayMs * Math.Pow(2, attempt - 1);
                    _logger.LogWarning("Deadlock detected in GetPendingMappingsAsync (attempt {Attempt}/{MaxRetries}). Retrying in {Delay}ms...",
                        attempt, maxRetries, delay);

                    await Task.Delay((int)delay);
                    continue;
                }
                catch (Exception ex)
                {
                    if (attempt == maxRetries)
                    {
                        _logger.LogError(ex, "Error getting pending mappings after {MaxRetries} attempts", maxRetries);
                    }
                    else
                    {
                        _logger.LogWarning(ex, "Error getting pending mappings (attempt {Attempt}/{MaxRetries}). Retrying...",
                            attempt, maxRetries);
                        await Task.Delay(baseDelayMs * attempt);
                    }
                }
            }

            return pendingMappings;
        }

        private async Task<List<string>> GetContainerImagePathsAsync(ApplicationDbContext dbContext, string containerNumber, string scannerType)
        {
            try
            {
                var imagePaths = new List<string>();

                // Get image paths based on scanner type
                switch (scannerType.ToUpper())
                {
                    case "FS6000":
                        var fs6000Scan = await dbContext.FS6000Scans
                            .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);
                        if (fs6000Scan != null && !string.IsNullOrEmpty(fs6000Scan.FilePath))
                        {
                            imagePaths.Add(fs6000Scan.FilePath);
                        }
                        break;
                    case "ASE":
                        var aseScan = await dbContext.AseScans
                            .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);
                        if (aseScan != null && !string.IsNullOrEmpty(aseScan.ImageDisplayName))
                        {
                            // ASE stores image as byte array, so we'll use the display name as path
                            imagePaths.Add(aseScan.ImageDisplayName);
                        }
                        break;
                }

                // Fallback: look for images in container images table
                if (!imagePaths.Any())
                {
                    var containerImages = await dbContext.ContainerImages
                        .Where(ci => ci.Container.ContainerId == containerNumber) // Container has ContainerId field
                        .Select(ci => ci.ImagePath)
                        .ToListAsync();

                    imagePaths.AddRange(containerImages.Where(path => !string.IsNullOrEmpty(path)));
                }

                return imagePaths;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image paths for container {ContainerNumber}", containerNumber);
                return new List<string>();
            }
        }

        private async Task<Dictionary<string, object>> GetContainerReportDataAsync(ApplicationDbContext dbContext, ContainerBOERelation mapping)
        {
            var reportData = new Dictionary<string, object>
            {
                ["ContainerNumber"] = mapping.ContainerNumber,
                ["ScannerType"] = mapping.ScannerType,
                ["MappingId"] = mapping.Id,
                ["CreatedAt"] = mapping.CreatedAt
            };

            try
            {
                // Add scanner-specific data
                switch (mapping.ScannerType.ToUpper())
                {
                    case "FS6000":
                        // ✅ FIXED: Query single record instead of loading ALL scans (was loading 1,924 records)
                        var fs6000Scan = await dbContext.FS6000Scans
                            .AsNoTracking()
                            .Where(s => s.Id.GetHashCode() == mapping.ScannerDataId)
                            .Select(s => new
                            {
                                s.ScanTime,
                                s.FilePath,
                                s.FycoPresent,
                                s.PicNumber
                            })
                            .FirstOrDefaultAsync();
                        if (fs6000Scan != null)
                        {
                            reportData["ScanTime"] = fs6000Scan.ScanTime;
                            reportData["FilePath"] = fs6000Scan.FilePath;
                            reportData["FycoPresent"] = fs6000Scan.FycoPresent;
                            reportData["PicNumber"] = fs6000Scan.PicNumber;
                        }
                        break;
                    case "ASE":
                        // ✅ FIXED: Query single record instead of loading ALL 15,002 scans with 1.7MB images each (was loading 24 GB!)
                        var aseScan = await dbContext.AseScans
                            .AsNoTracking()
                            .Where(s => s.Id.GetHashCode() == mapping.ScannerDataId)
                            .Select(s => new
                            {
                                s.ScanTime,
                                s.ImageDisplayName,
                                s.InspectionId,
                                s.InspectionUuid
                                // ✅ ScanImage NOT included - saves 1.7 MB per query!
                            })
                            .FirstOrDefaultAsync();
                        if (aseScan != null)
                        {
                            reportData["ScanTime"] = aseScan.ScanTime;
                            reportData["ImageDisplayName"] = aseScan.ImageDisplayName;
                            reportData["InspectionId"] = aseScan.InspectionId;
                            reportData["InspectionUuid"] = aseScan.InspectionUuid;
                        }
                        break;
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting report data for mapping {MappingId}", mapping.Id);
            }

            return reportData;
        }

        private class PendingMapping
        {
            public string ContainerNumber { get; set; } = string.Empty;
            public string ScannerType { get; set; } = string.Empty;
            public int ScannerDataId { get; set; }
            public int ICUMSDataId { get; set; }
        }

        /// <summary>
        /// Get image paths for a specific container from all scanner types
        /// </summary>
        private async Task<List<string>> GetImagePathsForContainer(ApplicationDbContext dbContext, string containerNumber)
        {
            var imagePaths = new List<string>();

            try
            {
                // ✅ MEMORY OPTIMIZATION: Try to get images from NuctechScannerData (if table exists)
                List<string> nuctechImages = new();
                try
                {
                    nuctechImages = await dbContext.NuctechScannerData
                        .AsNoTracking()
                        .Where(s => s.ContainerId == containerNumber && !string.IsNullOrEmpty(s.ImagePath))
                        .Select(s => s.ImagePath!)
                        .ToListAsync();
                }
                catch (PostgresException ex) when (ex.SqlState == "42P01") // relation does not exist
                {
                    _logger.LogDebug("NuctechScannerData table not found, skipping");
                }

                // ✅ MEMORY OPTIMIZATION: Try to get images from HeimannSmithScannerData (if table exists)
                List<string> heimannSmithImages = new();
                try
                {
                    heimannSmithImages = await dbContext.HeimannSmithScannerData
                        .AsNoTracking()
                        .Where(s => s.ContainerId == containerNumber && !string.IsNullOrEmpty(s.ImagePath))
                        .Select(s => s.ImagePath!)
                        .ToListAsync();
                }
                catch (PostgresException ex) when (ex.SqlState == "42P01") // relation does not exist
                {
                    _logger.LogDebug("HeimannSmithScannerData table not found, skipping");
                }

                // ✅ MEMORY OPTIMIZATION: Get images from FS6000Scans with AsNoTracking
                var fs6000Images = await dbContext.FS6000Scans
                    .AsNoTracking()
                    .Where(s => s.ContainerNumber == containerNumber && !string.IsNullOrEmpty(s.FilePath))
                    .Select(s => s.FilePath!)
                    .ToListAsync();

                // ✅ MEMORY OPTIMIZATION: Get images from AseScans with AsNoTracking
                var aseImages = await dbContext.AseScans
                    .AsNoTracking()
                    .Where(s => s.ContainerNumber == containerNumber && !string.IsNullOrEmpty(s.ImageDisplayName))
                    .Select(s => s.ImageDisplayName!)
                    .ToListAsync();

                // Combine all image paths
                imagePaths.AddRange(nuctechImages);
                imagePaths.AddRange(heimannSmithImages);
                imagePaths.AddRange(fs6000Images);
                imagePaths.AddRange(aseImages);

                _logger.LogDebug("Found {Count} images for container {ContainerNumber}", imagePaths.Count, containerNumber);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image paths for container {ContainerNumber}", containerNumber);
            }

            return imagePaths;
        }
    }
}
