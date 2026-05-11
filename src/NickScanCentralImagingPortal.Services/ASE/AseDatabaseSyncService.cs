using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.Core.Configuration;
using NickScanCentralImagingPortal.Core.Entities;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Services.ASE
{
    public class AseDatabaseSyncService : IAseDatabaseSyncService
    {
        private readonly ILogger<AseDatabaseSyncService> _logger;
        private readonly AseConfiguration _config;
        private readonly IServiceProvider _serviceProvider;
        private readonly GoLiveOptions _goLiveOptions;
        private readonly DataRetentionOptions _dataRetention;
        private int _lastSyncedInspectionId = 0;
        private const string SERVICE_ID = "[ASE-DATABASE-SYNC]";

        public AseDatabaseSyncService(
            ILogger<AseDatabaseSyncService> logger,
            IOptions<AseConfiguration> config,
            IServiceProvider serviceProvider,
            IOptions<GoLiveOptions> goLiveOptions,
            IOptions<DataRetentionOptions> dataRetention)
        {
            _logger = logger;
            _config = config.Value;
            _serviceProvider = serviceProvider;
            _goLiveOptions = goLiveOptions?.Value ?? new GoLiveOptions();
            _dataRetention = dataRetention?.Value ?? new DataRetentionOptions();
        }

        public async Task SyncDataAsync()
        {
            try
            {
                // ✅ FIX: Check if sync is enabled
                if (!_config.EnableRealTimeSync)
                {
                    _logger.LogDebug("{ServiceId} Sync is disabled (EnableRealTimeSync=false). Skipping sync.", SERVICE_ID);
                    return;
                }

                // ✅ FIX: Check if connection string is configured
                if (string.IsNullOrEmpty(_config.ConnectionString))
                {
                    _logger.LogError("{ServiceId} Cannot sync: ConnectionString is not configured. Please set ASE:ConnectionString in appsettings.json or environment variable.", SERVICE_ID);
                    return;
                }

                // ✅ FIX: Check if connection string contains password placeholder
                if (_config.ConnectionString.Contains("***USE_ENV_VAR") || _config.ConnectionString.Contains("***USE_ENV"))
                {
                    var asePassword = Environment.GetEnvironmentVariable("NICKSCAN_ASE_PASSWORD");
                    if (string.IsNullOrEmpty(asePassword))
                    {
                        _logger.LogError("{ServiceId} Cannot sync: ConnectionString contains password placeholder and NICKSCAN_ASE_PASSWORD environment variable is not set. Please set NICKSCAN_ASE_PASSWORD environment variable.", SERVICE_ID);
                        return;
                    }
                    else
                    {
                        // Try to replace placeholder in case PostConfigure didn't work
                        // Escape special characters in password for SQL connection string
                        var escapedPassword = asePassword.Replace(";", ";;").Replace("=", "==");
                        _config.ConnectionString = _config.ConnectionString
                            .Replace("***USE_ENV_VAR_NICKSCAN_ASE_PASSWORD***", escapedPassword)
                            .Replace("***USE_ENV_VAR***", escapedPassword)
                            .Replace("***USE_ENV***", escapedPassword);
                        _logger.LogInformation("{ServiceId} Replaced password placeholder in connection string", SERVICE_ID);
                    }
                }

                // Initialize last synced ID if not set
                if (_lastSyncedInspectionId == 0)
                {
                    await InitializeLastSyncedIdAsync();
                }

                _logger.LogInformation("{ServiceId} Starting ASE sync from InspectionID: {InspectionId}", SERVICE_ID, _lastSyncedInspectionId);

                var newRecords = await GetNewRecordsFromAseDatabaseAsync();

                // Go-live cutoff: filter any records before GoLiveDate (defensive)
                var effectiveStartDate = GetEffectiveStartDate();
                if (effectiveStartDate > DateTime.MinValue)
                {
                    var beforeCount = newRecords.Count;
                    newRecords = newRecords.Where(r => r.ScanTime >= effectiveStartDate).ToList();
                    if (beforeCount != newRecords.Count)
                        _logger.LogDebug("{ServiceId} Filtered {Count} ASE records before GoLiveDate", SERVICE_ID, beforeCount - newRecords.Count);
                }

                if (newRecords.Count > 0)
                {
                    _logger.LogInformation("{ServiceId} Found {Count} new ASE records to sync", SERVICE_ID, newRecords.Count);
                    await ProcessNewRecordsAsync(newRecords);
                }
                else
                {
                    _logger.LogInformation("{ServiceId} No new ASE records found to sync (LastSyncedId: {LastSyncedId}, StartDate: {StartDate})",
                        SERVICE_ID, _lastSyncedInspectionId, GetEffectiveStartDate().ToString("yyyy-MM-dd"));
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Error during ASE data sync: {Message}", SERVICE_ID, ex.Message);
            }
        }

        private DateTime GetEffectiveStartDate()
        {
            var goLiveDate = _goLiveOptions.EffectiveGoLiveDate;
            var retentionCutoff = _dataRetention.EffectiveCutoffDate;
            var cutoff = DateTime.MinValue;
            if (goLiveDate > DateTime.MinValue && retentionCutoff > DateTime.MinValue)
                cutoff = goLiveDate > retentionCutoff ? goLiveDate : retentionCutoff;
            else if (goLiveDate > DateTime.MinValue)
                cutoff = goLiveDate;
            else if (retentionCutoff > DateTime.MinValue)
                cutoff = retentionCutoff;
            if (cutoff <= DateTime.MinValue)
                return _config.StartDate;
            return _config.StartDate > cutoff ? _config.StartDate : cutoff;
        }

        private async Task InitializeLastSyncedIdAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var latestSyncLog = await GetLatestSyncLogAsync(context);
                if (latestSyncLog != null)
                {
                    _lastSyncedInspectionId = latestSyncLog.LastSyncedInspectionId;
                }
                else
                {
                    // If no sync log exists, get the max InspectionId from asescans table
                    var maxInspectionId = await context.AseScans.MaxAsync(s => (int?)s.InspectionId) ?? 0;
                    _lastSyncedInspectionId = maxInspectionId;
                    _logger.LogInformation("Max InspectionId from asescans table: {MaxId}", maxInspectionId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error initializing last synced ID");
                _lastSyncedInspectionId = 0;
            }
        }

        private async Task<AseSyncLog?> GetLatestSyncLogAsync(ApplicationDbContext context)
        {
            return await context.AseSyncLogs
                .OrderByDescending(s => s.LastSyncTime)
                .FirstOrDefaultAsync();
        }

        private async Task ProcessNewRecordsAsync(List<AseScanData> newRecords)
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                var queuePublisher = scope.ServiceProvider.GetService<IContainerScanQueuePublisher>();

                // Get existing InspectionIds in bulk
                // ✅ FIX: SQL Server 2014 compatibility - Batch Contains() to avoid CTE syntax issues
                // EF Core's Contains() with large lists can generate CTEs that require semicolons in SQL Server 2014
                var inspectionIds = newRecords.Select(r => r.InspectionId).ToList();
                var existingIds = new List<int>();

                if (inspectionIds.Count > 0)
                {
                    // Process in batches of 100 to avoid CTE generation and parameter limit issues
                    // ✅ FIX: Use raw SQL to avoid CTE generation that requires semicolons in SQL Server 2014
                    const int batchSize = 100;
                    for (int i = 0; i < inspectionIds.Count; i += batchSize)
                    {
                        var batch = inspectionIds.Skip(i).Take(batchSize).ToList();

                        // Build parameterized IN clause to avoid CTE generation
                        // ✅ FIX: Use object list pattern matching other FromSqlRaw usage in codebase
                        var parameters = new List<object>();
                        var parameterPlaceholders = new List<string>();

                        for (int j = 0; j < batch.Count; j++)
                        {
                            var paramName = $"@p{j}";
                            parameterPlaceholders.Add($"{{{j}}}"); // EF Core parameter placeholder format
                            parameters.Add(batch[j]);
                        }

                        var inClause = string.Join(",", parameterPlaceholders);
                        // ✅ FIX: EF Core's FromSqlRaw requires all entity columns, so we select all columns
                        // but only need InspectionId, so we project after the query
                        var sql = $"SELECT * FROM AseScans WHERE InspectionId IN ({inClause})";

                        // Use FromSqlRaw with proper parameter array - EF Core will map to AseScan entity
                        // Then project to just InspectionId to get the values we need
                        var batchResults = await context.AseScans
                            .FromSqlRaw(sql, parameters.ToArray())
                            .Select(s => s.InspectionId)
                            .ToListAsync();

                        existingIds.AddRange(batchResults);
                    }
                }

                // Filter out duplicates
                var newRecordsToAdd = newRecords
                    .Where(r => !existingIds.Contains(r.InspectionId))
                    .ToList();

                if (newRecordsToAdd.Count > 0)
                {
                    // Create OriginalScanRecord entries for each new ASE record (preserving raw data)
                    var originalRecords = new List<OriginalScanRecord>();
                    foreach (var r in newRecordsToAdd)
                    {
                        var rawSnapshot = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            r.InspectionId,
                            r.ScanTime,
                            r.InspectionUuid,
                            r.ContainerNumber,
                            r.TruckPlate,
                            r.ImageDisplayName,
                            HasImage = r.ScanImage != null,
                            ImageSizeBytes = r.ScanImage?.Length ?? 0
                        });

                        var containerNumbers = (r.ContainerNumber ?? "")
                            .Split(',', StringSplitOptions.RemoveEmptyEntries)
                            .Select(c => c.Trim())
                            .Where(c => !string.IsNullOrEmpty(c))
                            .ToList();

                        originalRecords.Add(new OriginalScanRecord
                        {
                            ScannerType = "ASE",
                            OriginalContainerNumbers = r.ContainerNumber ?? "Unknown",
                            DerivedRecordCount = Math.Max(containerNumbers.Count, 1),
                            InspectionId = r.InspectionId.ToString(),
                            ScanTime = DateTime.SpecifyKind(r.ScanTime, DateTimeKind.Utc),
                            RawData = rawSnapshot,
                            IngestedAt = DateTime.UtcNow
                        });
                    }

                    context.OriginalScanRecords.AddRange(originalRecords);
                    await context.SaveChangesAsync();

                    // Build AseScan records linked to their OriginalScanRecord
                    var aseScans = new List<AseScan>();
                    for (int i = 0; i < newRecordsToAdd.Count; i++)
                    {
                        var r = newRecordsToAdd[i];
                        aseScans.Add(new AseScan
                        {
                            Id = Guid.NewGuid(),
                            InspectionId = r.InspectionId,
                            ScanTime = DateTime.SpecifyKind(r.ScanTime, DateTimeKind.Utc),
                            InspectionUuid = r.InspectionUuid,
                            ContainerNumber = r.ContainerNumber,
                            TruckPlate = r.TruckPlate,
                            ScanImage = r.ScanImage,
                            ImageDisplayName = r.ImageDisplayName,
                            CreatedAt = DateTime.UtcNow,
                            UpdatedAt = DateTime.UtcNow,
                            OriginalScanRecordId = originalRecords[i].Id
                        });
                    }

                    context.AseScans.AddRange(aseScans);

                    context.Database.SetCommandTimeout(180);

                    await context.SaveChangesAsync();

                    _logger.LogInformation("{ServiceId} Added {Count} new ASE records with {OriginalCount} original scan audit records",
                        SERVICE_ID, newRecordsToAdd.Count, originalRecords.Count);

                    // ✅ QUEUE ARCHITECTURE: Publish scans to completeness queue for processing
                    // This enables event-driven completeness checking instead of polling scanner tables
                    if (queuePublisher != null && aseScans.Any())
                    {
                        try
                        {
                            // Defense-in-depth: don't pollute the completeness queue with
                            // non-cargo "Unknown" scans (calibration/transmission/pass-through
                            // inspections where the ASE source returned no container number).
                            //
                            // 1.18.0 — COMMA-SPLIT AT PUBLISH:
                            // The asescans source table preserves the verbatim source string
                            // (e.g. "C1, C2") for audit. But the completeness pipeline
                            // (containerscanqueues → containercompletenessstatuses →
                            // analysisgroups → analysisrecords) is keyed on containernumber
                            // and CANNOT handle comma-joined values — every downstream BOE
                            // lookup fails, the analyst sees an empty ICUMS panel, and
                            // ContainerDataMapperService's existing split at the
                            // ContainerBOERelation layer comes too late to help.
                            //
                            // Fix: split the container number at the queue publish step so
                            // each physical container gets its own queue item and its own
                            // completeness row. The InspectionId is suffixed with a token
                            // index (`-a`, `-b`, etc.) to preserve queue uniqueness. The
                            // audit trail lives in asescans (verbatim) and in the metadata
                            // field here (original joined string).
                            var queueItems = aseScans
                                .Where(s => !string.IsNullOrWhiteSpace(s.ContainerNumber)
                                            && !string.Equals(s.ContainerNumber, "Unknown", StringComparison.OrdinalIgnoreCase))
                                .SelectMany(scan => SplitAseScanIntoQueueItems(scan))
                                .ToList();

                            if (queueItems.Any())
                            {
                                var publishedCount = await queuePublisher.PublishScansBatchAsync(queueItems);
                                _logger.LogInformation("{ServiceId} 📤 Published {PublishedCount} scans to completeness queue (from {TotalCount} new scans, after comma-split)",
                                    SERVICE_ID, publishedCount, aseScans.Count);
                            }
                        }
                        catch (Exception queueEx)
                        {
                            // ✅ CRITICAL: Queue publishing failures should NOT break scanner ingestion
                            // Log error but continue - scans are saved, queue publishing can retry later
                            _logger.LogWarning(queueEx, "{ServiceId} ⚠️ Failed to publish scans to queue (non-critical - scans are saved)", SERVICE_ID);
                        }
                    }
                    else if (queuePublisher == null)
                    {
                        _logger.LogWarning("{ServiceId} ⚠️ IContainerScanQueuePublisher not available - scans saved but not queued", SERVICE_ID);
                    }

                    await EnsureTwoContainerSplitJobsAsync(
                        scope.ServiceProvider,
                        originalRecords.Where(record => record.DerivedRecordCount == 2).Select(record => record.Id).ToList());
                }
                else
                {
                    _logger.LogInformation("All {Count} ASE records were duplicates, no new records added", newRecords.Count);
                }

                // Update last synced ID to the maximum processed
                var maxProcessedId = newRecords.Max(r => r.InspectionId);
                if (maxProcessedId > _lastSyncedInspectionId)
                {
                    _lastSyncedInspectionId = maxProcessedId;
                    await UpdateLastSyncedIdAsync(context, maxProcessedId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing new records");
            }
        }

        private async Task EnsureTwoContainerSplitJobsAsync(IServiceProvider serviceProvider, IReadOnlyCollection<int> originalScanRecordIds)
        {
            if (originalScanRecordIds.Count == 0)
                return;

            var splitIntake = serviceProvider.GetService<ITwoContainerSplitIntakeService>();
            if (splitIntake == null)
            {
                _logger.LogDebug("{ServiceId} Two-container split intake service is not registered", SERVICE_ID);
                return;
            }

            foreach (var originalScanRecordId in originalScanRecordIds)
            {
                try
                {
                    await splitIntake.EnsureSplitJobForOriginalAsync(originalScanRecordId);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "{ServiceId} Failed to ensure split job for OriginalScanRecord {OriginalScanRecordId}",
                        SERVICE_ID,
                        originalScanRecordId);
                }
            }
        }

        private async Task UpdateLastSyncedIdAsync(ApplicationDbContext context, int lastSyncedId)
        {
            try
            {
                var syncLog = new AseSyncLog
                {
                    LastSyncedInspectionId = lastSyncedId,
                    LastSyncTime = DateTime.UtcNow,
                    RecordsProcessed = 0,
                    SyncStatus = "Completed",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                context.AseSyncLogs.Add(syncLog);
                await context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating last synced ID");
            }
        }

        /// <summary>
        /// 1.18.0 — Splits an ASE scan's (possibly comma-joined) container number into one
        /// or more queue items, one per physical container.
        ///
        /// The ASE source database stores "C1, C2" verbatim when a single inspection event
        /// covers multiple containers (e.g. a truck carrying two 20ft containers past the
        /// portal). Before this fix, the queue publish step forwarded the joined string
        /// unchanged, and every downstream consumer (containerscanqueues,
        /// containercompletenessstatuses, analysisgroups, analysisrecords) faithfully
        /// propagated the garbage. Analysts ended up with groups whose BOE lookups failed
        /// because no boedocuments row has containernumber = "C1, C2". Split at the publish
        /// step so each token gets its own row all the way down.
        ///
        /// Preserves queue uniqueness by suffixing InspectionId with a token index when
        /// there's more than one token (e.g. "123456-a", "123456-b"). Single-container
        /// scans pass through unchanged so no bookkeeping churn.
        ///
        /// The audit trail of the original joined string lives in asescans.containernumber
        /// (verbatim) and in the queue metadata field (OriginalContainerNumber).
        /// </summary>
        private static IEnumerable<ContainerScanInfo> SplitAseScanIntoQueueItems(AseScan scan)
        {
            var raw = (scan.ContainerNumber ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(raw))
                yield break;

            // Fast path: no comma → single token, no bookkeeping
            if (!raw.Contains(','))
            {
                yield return new ContainerScanInfo
                {
                    ContainerNumber = raw,
                    ScannerType = CommonScannerTypes.ASE,
                    InspectionId = scan.InspectionId.ToString(),
                    ScanDate = scan.ScanTime,
                    Priority = 0,
                    Metadata = $"{{ \"TruckPlate\": \"{scan.TruckPlate}\", \"InspectionUuid\": \"{scan.InspectionUuid}\" }}"
                };
                yield break;
            }

            // Split, trim, dedupe, drop "Unknown" and empties
            var tokens = raw
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(t => !string.IsNullOrWhiteSpace(t)
                            && !string.Equals(t, "Unknown", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (tokens.Count == 0)
                yield break;

            // Single valid token after cleaning → same as fast path (but preserve original raw in metadata)
            if (tokens.Count == 1)
            {
                yield return new ContainerScanInfo
                {
                    ContainerNumber = tokens[0],
                    ScannerType = CommonScannerTypes.ASE,
                    InspectionId = scan.InspectionId.ToString(),
                    ScanDate = scan.ScanTime,
                    Priority = 0,
                    Metadata = $"{{ \"TruckPlate\": \"{scan.TruckPlate}\", \"InspectionUuid\": \"{scan.InspectionUuid}\", \"OriginalContainerNumber\": \"{raw.Replace("\"", "\\\"")}\" }}"
                };
                yield break;
            }

            // Multi-container inspection — emit one queue item per token with a unique InspectionId suffix
            for (int i = 0; i < tokens.Count; i++)
            {
                var suffix = (char)('a' + i);
                yield return new ContainerScanInfo
                {
                    ContainerNumber = tokens[i],
                    ScannerType = CommonScannerTypes.ASE,
                    InspectionId = $"{scan.InspectionId}-{suffix}",
                    ScanDate = scan.ScanTime,
                    Priority = 0,
                    Metadata = $"{{ \"TruckPlate\": \"{scan.TruckPlate}\", \"InspectionUuid\": \"{scan.InspectionUuid}\", \"OriginalContainerNumber\": \"{raw.Replace("\"", "\\\"")}\", \"MultiContainerScan\": true, \"SplitTokenIndex\": {i}, \"SplitTokenCount\": {tokens.Count} }}"
                };
            }
        }

        private async Task<List<AseScanData>> GetNewRecordsFromAseDatabaseAsync()
        {
            var records = new List<AseScanData>();
            var connectionString = _config.ConnectionString;

            // ✅ FIX: Check if connection string is configured
            if (string.IsNullOrEmpty(connectionString))
            {
                _logger.LogError("{ServiceId} Cannot sync: ConnectionString is not configured. Please set ASE:ConnectionString in appsettings.json or environment variable.", SERVICE_ID);
                return records;
            }

            // ✅ FIX: Check if connection string contains password placeholder
            if (connectionString.Contains("***USE_ENV_VAR") || connectionString.Contains("***USE_ENV"))
            {
                var asePassword = Environment.GetEnvironmentVariable("NICKSCAN_ASE_PASSWORD");
                if (string.IsNullOrEmpty(asePassword))
                {
                    _logger.LogError("{ServiceId} Cannot sync: ConnectionString contains password placeholder and NICKSCAN_ASE_PASSWORD environment variable is not set. Please set NICKSCAN_ASE_PASSWORD environment variable.", SERVICE_ID);
                    return records;
                }
                else
                {
                    // Try to replace placeholder in case PostConfigure didn't work
                    // Escape special characters in password for SQL connection string
                    var escapedPassword = asePassword.Replace(";", ";;").Replace("=", "==");
                    connectionString = connectionString
                        .Replace("***USE_ENV_VAR_NICKSCAN_ASE_PASSWORD***", escapedPassword)
                        .Replace("***USE_ENV_VAR***", escapedPassword)
                        .Replace("***USE_ENV***", escapedPassword);
                    _logger.LogInformation("{ServiceId} Replaced password placeholder in connection string", SERVICE_ID);
                }
            }

            _logger.LogInformation("{ServiceId} ASE Connection String: {ConnectionString}", SERVICE_ID, RedactPassword(connectionString));

            // ✅ Verify password was replaced (log check, but don't log password itself)
            if (connectionString.Contains("***USE_ENV_VAR") || connectionString.Contains("***USE_ENV"))
            {
                _logger.LogError("{ServiceId} Connection string still contains password placeholder after replacement attempts. Cannot connect.", SERVICE_ID);
                return records;
            }

            try
            {
                using var connection = new SqlConnection(connectionString);
                _logger.LogDebug("{ServiceId} Attempting to open connection to ASE database...", SERVICE_ID);
                await connection.OpenAsync();

                _logger.LogInformation("{ServiceId} Successfully connected to ASE database", SERVICE_ID);

                var query = @"
                    SELECT TOP (@BatchSize) 
                        ic.InspectionID,
                        ic.TimeStamp,
                        ic.InspectionUuid,
                        icf.FieldValue AS ContainerNumber,
                        icargo.TruckPlate,
                        iobj.InspectionImage AS ScanImage,
                        iobj.DisplayName AS ImageDisplayName
                    FROM InspectionCore ic
                    INNER JOIN InspectionCustomField icf ON ic.InspectionID = icf.InspectionID
                    INNER JOIN InspectionCargo icargo ON ic.InspectionID = icargo.InspectionID
                    INNER JOIN InspectionObject iobj ON ic.InspectionUuid = iobj.InspectionUuid
                    WHERE icf.FieldNameID = 7
                        AND iobj.DisplayName = 'Transmission'
                        AND ic.InspectionID > @LastSyncedId
                        AND ic.TimeStamp >= @StartDate
                    ORDER BY ic.InspectionID ASC";

                var effectiveStartDate = GetEffectiveStartDate();

                using var command = new SqlCommand(query, connection);
                command.CommandTimeout = 120; // 2 minutes timeout
                command.Parameters.AddWithValue("@StartDate", effectiveStartDate);
                command.Parameters.AddWithValue("@LastSyncedId", _lastSyncedInspectionId);
                command.Parameters.AddWithValue("@BatchSize", _config.BatchSize);

                using var reader = await command.ExecuteReaderAsync();

                while (await reader.ReadAsync())
                {
                    records.Add(new AseScanData
                    {
                        InspectionId = reader.GetInt32(0),
                        ScanTime = reader.GetDateTime(1),
                        // Fixed: Handle both string and Guid types for InspectionUuid
                        InspectionUuid = reader.IsDBNull(2) ? string.Empty :
                            reader.GetFieldType(2) == typeof(Guid) ? reader.GetGuid(2).ToString() : reader.GetString(2),
                        ContainerNumber = reader.IsDBNull(3) ? null : reader.GetString(3),
                        TruckPlate = reader.IsDBNull(4) ? null : reader.GetString(4),
                        ScanImage = reader.IsDBNull(5) ? null : (byte[])reader[5],
                        ImageDisplayName = reader.IsDBNull(6) ? null : reader.GetString(6)
                    });
                }

                _logger.LogInformation("{ServiceId} Retrieved {Count} records from ASE database (LastSyncedId: {LastSyncedId}, StartDate: {StartDate})",
                    SERVICE_ID, records.Count, _lastSyncedInspectionId, GetEffectiveStartDate().ToString("yyyy-MM-dd"));

                // ✅ Enhanced logging: If no records found, check what records exist
                if (records.Count == 0)
                {
                    _logger.LogWarning("{ServiceId} No records found matching criteria. Checking if any records exist with different criteria...", SERVICE_ID);

                    // Check if there are any records with InspectionID > LastSyncedId (without other filters)
                    var diagnosticQuery = @"
                        SELECT COUNT(*) as TotalCount,
                               MAX(ic.InspectionID) as MaxInspectionID,
                               MIN(ic.InspectionID) as MinInspectionID
                        FROM InspectionCore ic
                        WHERE ic.InspectionID > @LastSyncedId
                          AND ic.TimeStamp >= @StartDate";

                    using var diagCommand = new SqlCommand(diagnosticQuery, connection);
                    diagCommand.Parameters.AddWithValue("@StartDate", GetEffectiveStartDate());
                    diagCommand.Parameters.AddWithValue("@LastSyncedId", _lastSyncedInspectionId);

                    using var diagReader = await diagCommand.ExecuteReaderAsync();
                    if (await diagReader.ReadAsync())
                    {
                        var totalCount = diagReader.GetInt32(0);
                        var maxId = diagReader.IsDBNull(1) ? 0 : diagReader.GetInt32(1);
                        var minId = diagReader.IsDBNull(2) ? 0 : diagReader.GetInt32(2);

                        _logger.LogInformation("{ServiceId} Diagnostic: Found {Count} total records with InspectionID > {LastSyncedId} (MaxID: {MaxId}, MinID: {MinId})",
                            SERVICE_ID, totalCount, _lastSyncedInspectionId, maxId, minId);

                        if (totalCount > 0)
                        {
                            _logger.LogWarning("{ServiceId} Records exist but don't match all filters (FieldNameID=7 AND DisplayName='Transmission'). Check if these filters are correct.", SERVICE_ID);
                        }
                    }
                }

                return records;
            }
            catch (SqlException sqlEx)
            {
                // Check if password placeholder is still in connection string
                var connStringPreview = RedactPassword(connectionString);
                if (connStringPreview.Contains("***USE_ENV_VAR") || connStringPreview.Contains("***USE_ENV"))
                {
                    _logger.LogError(sqlEx, "{ServiceId} SQL error - Connection string still contains password placeholder. Password replacement failed. Please check PostConfigure and environment variable NICKSCAN_ASE_PASSWORD. Error: {Message}",
                        SERVICE_ID, sqlEx.Message);
                }
                else if (sqlEx.Number == 18456) // Login failed error
                {
                    _logger.LogError(sqlEx, "{ServiceId} SQL authentication failed for ASE database user (Error 18456). This usually means the password is incorrect. Please verify NICKSCAN_ASE_PASSWORD environment variable is correct. Error: {Message}",
                        SERVICE_ID, sqlEx.Message);
                }
                else
                {
                    _logger.LogError(sqlEx, "{ServiceId} SQL error connecting to ASE database (Server: {Server}, Database: {Database}). Error: {Message} (SQL Error Number: {ErrorNumber})",
                        SERVICE_ID, _config.ServerHost, _config.DatabaseName, sqlEx.Message, sqlEx.Number);
                }
                return new List<AseScanData>();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "{ServiceId} Unexpected error connecting to ASE database. Error: {Message}. Service will continue without ASE data sync.",
                    SERVICE_ID, ex.Message);
                return new List<AseScanData>();
            }
        }

        /// <summary>
        /// Redact password from connection string for safe logging
        /// </summary>
        private static string RedactPassword(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString))
                return connectionString;

            // Use regex to replace password value with asterisks
            // Matches: Password=<anything>;  or  Pwd=<anything>;
            // Case-insensitive and handles passwords at end of string (no semicolon)
            var redacted = Regex.Replace(
                connectionString,
                @"(Password|Pwd)\s*=\s*[^;]*",
                "$1=***",
                RegexOptions.IgnoreCase
            );

            return redacted;
        }
    }

    public class AseScanData
    {
        public int InspectionId { get; set; }
        public DateTime ScanTime { get; set; }
        public string InspectionUuid { get; set; } = string.Empty;
        public string? ContainerNumber { get; set; }
        public string? TruckPlate { get; set; }
        public byte[]? ScanImage { get; set; }
        public string? ImageDisplayName { get; set; }
    }
}
