using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;
using NickScanCentralImagingPortal.Core.Entities.ASE;
using NickScanCentralImagingPortal.Core.Entities.FS6000;
using NickScanCentralImagingPortal.Core.Helpers;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Repositories;

namespace NickScanCentralImagingPortal.Services.CargoGrouping
{
    /// <summary>
    /// Service for building standardized cargo group data structures
    /// Handles both consolidated and non-consolidated cargo grouping
    /// </summary>
    public class CargoGroupService : ICargoGroupService
    {
        private readonly IcumDownloadsDbContext _icumDbContext;
        private readonly ApplicationDbContext _appDbContext;
        private readonly ConsolidatedCargoQueries _cargoQueries;
        private readonly ILogger<CargoGroupService> _logger;
        private readonly IConfiguration _configuration;
        private readonly IHttpContextAccessor _httpContextAccessor;
        private readonly NickScanCentralImagingPortal.Core.Security.ISignedImageUrlSigner _urlSigner;

        public CargoGroupService(
            IcumDownloadsDbContext icumDbContext,
            ApplicationDbContext appDbContext,
            ILogger<CargoGroupService> logger,
            IConfiguration configuration,
            IHttpContextAccessor httpContextAccessor,
            NickScanCentralImagingPortal.Core.Security.ISignedImageUrlSigner urlSigner)
        {
            _icumDbContext = icumDbContext;
            _appDbContext = appDbContext;
            _cargoQueries = new ConsolidatedCargoQueries(icumDbContext);
            _logger = logger;
            _configuration = configuration;
            _httpContextAccessor = httpContextAccessor;
            _urlSigner = urlSigner;
        }

        /// <summary>
        /// Get complete cargo group by identifier (Master BL for consolidated, Declaration Number for non-consolidated)
        /// </summary>
        /// <param name="groupIdentifier">Master BL (consolidated) or Declaration Number (non-consolidated)</param>
        /// <param name="type">Cargo type (optional, will be determined if not provided)</param>
        /// <param name="loadScannerData">Whether to load scanner data (default: true)</param>
        /// <param name="loadImageData">Whether to load image data (default: true)</param>
        /// <param name="loadICUMSData">Whether to load ICUMS data (default: true)</param>
        public async Task<CargoGroupDto?> GetCargoGroupAsync(
            string groupIdentifier,
            CargoType? type = null,
            bool loadScannerData = true,
            bool loadImageData = true,
            bool loadICUMSData = true)
        {
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var typeDeterminationStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var groupQueryStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var dataLoadStopwatch = System.Diagnostics.Stopwatch.StartNew();

            _logger.LogInformation("⏱️ [GetCargoGroupAsync] START - Identifier: {Identifier}, Type: {Type}, Scanner: {Scanner}, Image: {Image}, ICUMS: {ICUMS}",
                groupIdentifier, type, loadScannerData, loadImageData, loadICUMSData);

            try
            {
                if (CmrCompositeKeyHelper.IsOperationalKey(groupIdentifier))
                {
                    groupQueryStopwatch.Restart();
                    var cmrGroup = await BuildCmrCompositeGroupAsync(
                        groupIdentifier,
                        loadScannerData,
                        loadImageData,
                        loadICUMSData);
                    groupQueryStopwatch.Stop();

                    totalStopwatch.Stop();
                    if (cmrGroup == null)
                    {
                        _logger.LogInformation(
                            "⏱️ [GetCargoGroupAsync] COMPLETE (CMR composite not found) - Identifier: {Identifier}, Total: {Total}ms",
                            groupIdentifier,
                            totalStopwatch.ElapsedMilliseconds);
                        return null;
                    }

                    _logger.LogInformation(
                        "⏱️ [GetCargoGroupAsync] COMPLETE (CMR composite) - Identifier: {Identifier}, Total: {Total}ms ({TotalSeconds:F2}s), Containers: {ContainerCount}",
                        groupIdentifier,
                        totalStopwatch.ElapsedMilliseconds,
                        totalStopwatch.Elapsed.TotalSeconds,
                        cmrGroup.ContainerNumbers?.Count ?? 0);
                    return cmrGroup;
                }

                // Determine cargo type if not provided
                if (!type.HasValue)
                {
                    typeDeterminationStopwatch.Restart();
                    type = await DetermineCargoTypeAsync(groupIdentifier);
                    typeDeterminationStopwatch.Stop();
                    _logger.LogInformation("⏱️ [GetCargoGroupAsync] Type determination took: {Time}ms", typeDeterminationStopwatch.ElapsedMilliseconds);
                }

                if (!type.HasValue)
                {
                    _logger.LogWarning("Could not determine cargo type for identifier: {Identifier}", groupIdentifier);
                    return null;
                }

                CargoGroupDto? group = null;

                groupQueryStopwatch.Restart();
                if (type == CargoType.Consolidated)
                {
                    // Consolidated: Group by Master BL
                    group = await BuildConsolidatedGroupAsync(groupIdentifier);
                }
                else
                {
                    // Non-Consolidated: Group by Declaration Number
                    group = await BuildNonConsolidatedGroupAsync(groupIdentifier);
                }

                // ✅ FALLBACK: If the requested type failed, try the other type (with RawJsonData fallback)
                // This handles cases where the type was incorrectly specified or the identifier exists in RawJsonData
                if (group == null && type.HasValue)
                {
                    _logger.LogInformation("Cargo group not found for {Type} with identifier {Identifier}, trying opposite type", type.Value, groupIdentifier);

                    if (type == CargoType.Consolidated)
                    {
                        // Try as non-consolidated (Declaration Number)
                        group = await BuildNonConsolidatedGroupAsync(groupIdentifier);
                        if (group != null)
                        {
                            // Update type to match the found group
                            type = CargoType.NonConsolidated;
                        }
                    }
                    else
                    {
                        // Try as consolidated (Master BL)
                        group = await BuildConsolidatedGroupAsync(groupIdentifier);
                        if (group != null)
                        {
                            // Update type to match the found group
                            type = CargoType.Consolidated;
                        }
                    }

                    if (group != null)
                    {
                        _logger.LogInformation("Found cargo group with opposite type: {FoundType} (requested: {RequestedType}) for identifier {Identifier}",
                            group.Type, type.Value, groupIdentifier);
                    }
                }

                if (group == null)
                {
                    totalStopwatch.Stop();
                    _logger.LogInformation("⏱️ [GetCargoGroupAsync] COMPLETE (not found) - Total: {Total}ms", totalStopwatch.ElapsedMilliseconds);
                    return null;
                }

                groupQueryStopwatch.Stop();
                _logger.LogInformation("⏱️ [GetCargoGroupAsync] Group query took: {Time}ms", groupQueryStopwatch.ElapsedMilliseconds);

                // Load data based on requested data types
                // Use the actual type of the found group (may differ from requested type)
                dataLoadStopwatch.Restart();
                group.Data = await GetCargoGroupDataAsync(
                    groupIdentifier,
                    type!.Value,
                    loadScannerData: loadScannerData,
                    loadImageData: loadImageData,
                    loadICUMSData: loadICUMSData);
                dataLoadStopwatch.Stop();
                _logger.LogInformation("⏱️ [GetCargoGroupAsync] Data loading took: {Time}ms", dataLoadStopwatch.ElapsedMilliseconds);

                totalStopwatch.Stop();
                _logger.LogInformation("⏱️ [GetCargoGroupAsync] COMPLETE - Total: {Total}ms ({TotalSeconds:F2}s), Type: {Type}, Containers: {ContainerCount}",
                    totalStopwatch.ElapsedMilliseconds,
                    totalStopwatch.Elapsed.TotalSeconds,
                    type.Value,
                    group.ContainerNumbers?.Count ?? 0);

                return group;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cargo group for identifier: {Identifier}", groupIdentifier);
                throw;
            }
        }

        /// <summary>
        /// Get all data for a cargo group (ICUMS, Scanner, Images)
        /// </summary>
        /// <param name="groupIdentifier">Master BL (consolidated) or Declaration Number (non-consolidated)</param>
        /// <param name="type">Cargo type</param>
        /// <param name="loadScannerData">Whether to load scanner data (default: true)</param>
        /// <param name="loadImageData">Whether to load image data (default: true)</param>
        /// <param name="loadICUMSData">Whether to load ICUMS data (default: true)</param>
        public async Task<CargoGroupDataDto> GetCargoGroupDataAsync(
            string groupIdentifier,
            CargoType type,
            bool loadScannerData = true,
            bool loadImageData = true,
            bool loadICUMSData = true)
        {
            var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var containerQueryStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var icumsStopwatch = System.Diagnostics.Stopwatch.StartNew();

            _logger.LogInformation("⏱️ [GetCargoGroupDataAsync] START - Identifier: {Identifier}, Type: {Type}, Scanner: {Scanner}, Image: {Image}, ICUMS: {ICUMS}",
                groupIdentifier, type, loadScannerData, loadImageData, loadICUMSData);

            var data = new CargoGroupDataDto();
            List<string> containerNumbers = new();

            try
            {
                // ✅ FIX: Increased timeout to 55 seconds (before frontend's 60s timeout)
                // Cargo group requests can take 15-20 seconds for large groups with many containers
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_configuration.GetValue<int>("CargoGroup:RequestTimeoutSeconds", 55)));

                if (CmrCompositeKeyHelper.IsOperationalKey(groupIdentifier))
                {
                    var cmrDocuments = await FindCmrDocumentsForOperationalKeyAsync(groupIdentifier, cts.Token);
                    if (cmrDocuments.Any())
                    {
                        var cmrContainers = GetDistinctContainerNumbers(cmrDocuments);
                        data = await GetCmrCompositeDataAsync(
                            groupIdentifier,
                            cmrDocuments,
                            cmrContainers,
                            loadScannerData,
                            loadImageData,
                            loadICUMSData,
                            cts.Token);
                    }

                    return data;
                }

                if (type == CargoType.Consolidated)
                {
                    // Get all containers under this Master BL
                    containerNumbers = await _cargoQueries.GetContainersByMasterBLAsync(groupIdentifier) ?? new List<string>();
                }
                else
                {
                    // Get all containers under this Declaration
                    containerNumbers = await _cargoQueries.GetContainersByDeclarationAsync(groupIdentifier) ?? new List<string>();
                }

                containerQueryStopwatch.Stop();
                _logger.LogInformation("⏱️ [GetCargoGroupDataAsync] Container query took: {Time}ms, Found {Count} containers",
                    containerQueryStopwatch.ElapsedMilliseconds, containerNumbers?.Count ?? 0);

                // ✅ PERFORMANCE FIX: Load only requested data types
                // ICUMS uses different DbContext (_icumDbContext) so it can run in parallel
                // Scanner and Image share _appDbContext, so they must run sequentially to avoid concurrency errors

                Task<List<ICUMSDataGroupDto>>? icumsTask = null;
                if (loadICUMSData)
                {
                    icumsStopwatch.Restart();
                    icumsTask = GetICUMSDataForGroupAsync(groupIdentifier, type, containerNumbers, cts.Token);
                }

                // Run scanner and image sequentially (same DbContext) - only if requested
                var scannerStopwatch = System.Diagnostics.Stopwatch.StartNew();
                if (loadScannerData)
                {
                    data.ScannerData = await GetScannerDataForGroupAsync(containerNumbers, cts.Token);
                }
                else
                {
                    data.ScannerData = new List<ScannerDataGroupDto>();
                }
                scannerStopwatch.Stop();
                if (loadScannerData)
                {
                    _logger.LogInformation("⏱️ [GetCargoGroupDataAsync] Scanner data loading took: {Time}ms", scannerStopwatch.ElapsedMilliseconds);
                }

                var imageStopwatch = System.Diagnostics.Stopwatch.StartNew();
                if (loadImageData)
                {
                    data.ImageData = await GetImageDataForGroupAsync(containerNumbers, cts.Token);
                }
                else
                {
                    data.ImageData = new List<ImageDataGroupDto>();
                }
                imageStopwatch.Stop();
                if (loadImageData)
                {
                    _logger.LogInformation("⏱️ [GetCargoGroupDataAsync] Image data loading took: {Time}ms", imageStopwatch.ElapsedMilliseconds);
                }

                // Wait for ICUMS (runs in parallel with above operations) - only if requested
                if (loadICUMSData && icumsTask != null)
                {
                    data.ICUMSData = await icumsTask;
                    icumsStopwatch.Stop();
                    _logger.LogInformation("⏱️ [GetCargoGroupDataAsync] ICUMS data loading took: {Time}ms, Groups: {Count}",
                        icumsStopwatch.ElapsedMilliseconds, data.ICUMSData?.Count ?? 0);
                }
                else
                {
                    data.ICUMSData = new List<ICUMSDataGroupDto>();
                }
            }
            catch (OperationCanceledException)
            {
                totalStopwatch.Stop();
                _logger.LogWarning("⏱️ [GetCargoGroupDataAsync] TIMEOUT after {Time}ms for identifier: {Identifier}",
                    totalStopwatch.ElapsedMilliseconds, groupIdentifier);
            }
            catch (Exception ex)
            {
                totalStopwatch.Stop();
                _logger.LogError(ex, "⏱️ [GetCargoGroupDataAsync] ERROR after {Time}ms for identifier: {Identifier}",
                    totalStopwatch.ElapsedMilliseconds, groupIdentifier);
            }
            finally
            {
                totalStopwatch.Stop();
                _logger.LogInformation("⏱️ [GetCargoGroupDataAsync] COMPLETE - Total: {Total}ms ({TotalSeconds:F2}s), Containers: {ContainerCount}, ICUMS Groups: {ICUMSCount}",
                    totalStopwatch.ElapsedMilliseconds,
                    totalStopwatch.Elapsed.TotalSeconds,
                    containerNumbers?.Count ?? 0,
                    data.ICUMSData?.Count ?? 0);
            }

            return data;
        }

        /// <summary>
        /// Get list of cargo groups with filtering
        /// </summary>
        public async Task<List<CargoGroupSummaryDto>> GetCargoGroupsAsync(
            CargoType? type = null,
            string? clearanceType = null,
            int page = 1,
            int pageSize = 50)
        {
            var summaries = new List<CargoGroupSummaryDto>();

            try
            {
                if (type == null || type == CargoType.NonConsolidated)
                {
                    // Get non-consolidated groups
                    var nonConsolidated = await _cargoQueries.GetNonConsolidatedCargoGroupsAsync(clearanceType, limit: pageSize);
                    summaries.AddRange(nonConsolidated.Select(g => new CargoGroupSummaryDto
                    {
                        GroupIdentifier = g.DeclarationNumber,
                        Type = CargoType.NonConsolidated,
                        DisplayName = g.DeclarationNumber,
                        ClearanceType = g.ClearanceType,
                        TotalContainers = g.Containers.Count,
                        TotalHouseBLs = 0,
                        TotalBOEs = 1, // One BOE per group
                        LatestUpdateDate = g.DeclarationDate != null ? DateTime.TryParse(g.DeclarationDate, out var date) ? date : null : null
                    }));
                }

                if (type == null || type == CargoType.Consolidated)
                {
                    // Get consolidated groups
                    var consolidated = await _cargoQueries.GetConsolidatedCargoGroupsAsync(masterBL: null, clearanceType: clearanceType, limit: pageSize);
                    summaries.AddRange(consolidated.Select(g => new CargoGroupSummaryDto
                    {
                        GroupIdentifier = g.MasterBL,
                        Type = CargoType.Consolidated,
                        DisplayName = g.MasterBL,
                        ClearanceType = g.HouseBLDetails.FirstOrDefault()?.ClearanceType ?? "",
                        TotalContainers = g.ContainerNumbers.Count,
                        TotalHouseBLs = g.HouseBLDetails.Count,
                        TotalBOEs = g.HouseBLDetails.Count,
                        LatestUpdateDate = g.HouseBLDetails
                            .Select(h => h.DeclarationDate != null && DateTime.TryParse(h.DeclarationDate, out var date) ? (DateTime?)date : null)
                            .Where(d => d.HasValue)
                            .Select(d => d!.Value)
                            .DefaultIfEmpty()
                            .Max()
                    }));
                }

                // Apply pagination
                var skip = (page - 1) * pageSize;
                return summaries.OrderByDescending(s => s.LatestUpdateDate).Skip(skip).Take(pageSize).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cargo groups list");
                throw;
            }
        }

        #region Private Methods

        private async Task<CargoType?> DetermineCargoTypeAsync(string groupIdentifier)
        {
            // Try consolidated first (Master BL)
            var consolidated = await _icumDbContext.BOEDocuments
                .AsNoTracking()
                .Where(b => b.BlNumber == groupIdentifier && b.IsConsolidated)
                .AnyAsync();

            if (consolidated)
            {
                return CargoType.Consolidated;
            }

            // ✅ FALLBACK: Try to find BlNumber in RawJsonData for consolidated cargo
            // Only check if column query failed and limit to recent documents to avoid loading all data
            if (!consolidated)
            {
                var recentConsolidatedWithJson = await _icumDbContext.BOEDocuments
                    .AsNoTracking()
                    .Where(b => b.IsConsolidated && !string.IsNullOrWhiteSpace(b.RawJsonData))
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(1000) // ✅ MEMORY FIX: Limit to 1000 most recent documents
                    .ToListAsync();

                foreach (var doc in recentConsolidatedWithJson)
                {
                    var (blNumber, _) = ExtractGroupingFieldsFromRawJson(doc.RawJsonData);
                    if (blNumber == groupIdentifier)
                    {
                        _logger.LogInformation("Found BlNumber {BlNumber} in RawJsonData for consolidated cargo", groupIdentifier);
                        return CargoType.Consolidated;
                    }
                }
            }

            // Try non-consolidated (Declaration Number)
            // 2026-05-04: Removed `!b.IsConsolidated` filter — declaration-number lookup ignores
            // the consolidated flag because some BOE rows are mis-tagged at ingest (see ConsolidatedCargoQueries.cs).
            var nonConsolidated = await _icumDbContext.BOEDocuments
                .AsNoTracking()
                .Where(b => b.DeclarationNumber == groupIdentifier)
                .AnyAsync();

            if (nonConsolidated)
            {
                return CargoType.NonConsolidated;
            }

            // ✅ FALLBACK: Try to find DeclarationNumber in RawJsonData for non-consolidated cargo
            // Only check if column query failed and limit to recent documents to avoid loading all data
            if (!nonConsolidated)
            {
                // 2026-05-04: Dropped `!b.IsConsolidated` filter for consistency with the column-based
                // declaration lookup above. RawJsonData fallback now considers all BOEs.
                var recentNonConsolidatedWithJson = await _icumDbContext.BOEDocuments
                    .AsNoTracking()
                    .Where(b => !string.IsNullOrWhiteSpace(b.RawJsonData))
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(1000) // ✅ MEMORY FIX: Limit to 1000 most recent documents
                    .ToListAsync();

                foreach (var doc in recentNonConsolidatedWithJson)
                {
                    var (_, declarationNumber) = ExtractGroupingFieldsFromRawJson(doc.RawJsonData);
                    if (declarationNumber == groupIdentifier)
                    {
                        _logger.LogInformation("Found DeclarationNumber {DeclarationNumber} in RawJsonData for non-consolidated cargo", groupIdentifier);
                        return CargoType.NonConsolidated;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Extract grouping fields (BlNumber/DeclarationNumber) from RawJsonData as fallback
        /// Handles both flat and nested JSON structures
        /// </summary>
        private (string? blNumber, string? declarationNumber) ExtractGroupingFieldsFromRawJson(string? rawJsonData)
        {
            if (string.IsNullOrWhiteSpace(rawJsonData))
                return (null, null);

            try
            {
                using var doc = JsonDocument.Parse(rawJsonData);
                var root = doc.RootElement;

                // Try various field name variations that might exist in the JSON
                string? blNumber = null;
                string? declarationNumber = null;

                // Helper function to safely get string value from JsonElement
                string? GetStringValue(JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.String)
                    {
                        var value = element.GetString();
                        return string.IsNullOrWhiteSpace(value) ? null : value;
                    }
                    return null;
                }

                // Try to find BL Number - check both root level and nested structures
                // Root level (flat structure)
                if (root.TryGetProperty("BL Number", out var blNumberElement) ||
                    root.TryGetProperty("BlNumber", out blNumberElement) ||
                    root.TryGetProperty("blNumber", out blNumberElement) ||
                    root.TryGetProperty("Master BL", out blNumberElement) ||
                    root.TryGetProperty("masterBL", out blNumberElement))
                {
                    blNumber = GetStringValue(blNumberElement);
                }

                // Nested structure: Check ManifestDetails section
                if (string.IsNullOrWhiteSpace(blNumber) && root.TryGetProperty("ManifestDetails", out var manifestDetails))
                {
                    if (manifestDetails.TryGetProperty("BLNumber", out blNumberElement) ||
                        manifestDetails.TryGetProperty("BL Number", out blNumberElement) ||
                        manifestDetails.TryGetProperty("Master BL", out blNumberElement))
                    {
                        blNumber = GetStringValue(blNumberElement);
                    }
                }

                // Try to find Declaration Number - check both root level and nested structures
                // Root level (flat structure)
                if (root.TryGetProperty("Declaration Number", out var declNumberElement) ||
                    root.TryGetProperty("DeclarationNumber", out declNumberElement) ||
                    root.TryGetProperty("declarationNumber", out declNumberElement) ||
                    root.TryGetProperty("Declaration", out declNumberElement))
                {
                    declarationNumber = GetStringValue(declNumberElement);
                }

                // Nested structure: Check Header section
                if (string.IsNullOrWhiteSpace(declarationNumber) && root.TryGetProperty("Header", out var header))
                {
                    if (header.TryGetProperty("DeclarationNumber", out declNumberElement) ||
                        header.TryGetProperty("Declaration Number", out declNumberElement) ||
                        header.TryGetProperty("Declaration", out declNumberElement))
                    {
                        declarationNumber = GetStringValue(declNumberElement);
                    }
                }

                return (blNumber, declarationNumber);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse RawJsonData for grouping fields extraction");
                return (null, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Unexpected error extracting grouping fields from RawJsonData");
                return (null, null);
            }
        }

        private async Task<CargoGroupDto> BuildConsolidatedGroupAsync(string masterBL)
        {
            // ✅ FIX: Query directly by Master BL instead of loading all groups
            var group = await _cargoQueries.GetConsolidatedCargoGroupByMasterBLAsync(masterBL);

            // ✅ FALLBACK: If not found in column, try RawJsonData extraction
            if (group == null)
            {
                _logger.LogInformation("Master BL {MasterBL} not found in column, trying RawJsonData extraction", masterBL);

                // Load BOE documents with RawJsonData and check in memory
                // Limit to recent documents to avoid loading all data
                var boeDocuments = await _icumDbContext.BOEDocuments
                    .AsNoTracking()
                    .Where(b => b.IsConsolidated && !string.IsNullOrWhiteSpace(b.RawJsonData))
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(1000) // ✅ MEMORY FIX: Limit to 1000 most recent documents
                    .ToListAsync();

                // Find documents where RawJsonData contains the Master BL
                var matchingDocs = new List<BOEDocument>();
                foreach (var doc in boeDocuments)
                {
                    var (extractedBl, _) = ExtractGroupingFieldsFromRawJson(doc.RawJsonData);
                    if (extractedBl == masterBL)
                    {
                        matchingDocs.Add(doc);
                    }
                }

                if (matchingDocs.Any())
                {
                    _logger.LogInformation("Found {Count} BOE document(s) with Master BL {MasterBL} in RawJsonData",
                        matchingDocs.Count, masterBL);

                    // Build group from matching documents
                    var containers = matchingDocs
                        .Select(b => b.ContainerNumber)
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct()
                        .ToList();

                    var fallbackHouseBLGroups = matchingDocs
                        .GroupBy(b => b.HouseBl ?? "")
                        .Select(g => new HouseBLGroupDto
                        {
                            HouseBL = g.Key,
                            MasterBL = masterBL,
                            DeclarationNumber = g.First().DeclarationNumber ?? "",
                            ConsigneeName = g.First().ConsigneeName ?? "",
                            ClearanceType = g.First().ClearanceType ?? "",
                            BOEIds = g.Select(b => b.Id).ToList(),
                            ContainerNumbers = g.Select(b => b.ContainerNumber ?? "").Where(c => !string.IsNullOrWhiteSpace(c)).Distinct().ToList(),
                            // Full-field visibility: use CreateBOEDetail (without items — fallback path)
                            BOEDetails = g.Select(b => CreateBOEDetail(b)).ToList()
                        })
                        .ToList();

                    return new CargoGroupDto
                    {
                        GroupIdentifier = masterBL,
                        Type = CargoType.Consolidated,
                        GroupingKey = "MasterBL",
                        MasterBL = masterBL,
                        HouseBLGroups = fallbackHouseBLGroups,
                        ContainerNumbers = containers,
                        TotalContainers = containers.Count,
                        TotalHouseBLs = fallbackHouseBLGroups.Count,
                        TotalBOEs = matchingDocs.Count,
                        ClearanceType = matchingDocs.FirstOrDefault()?.ClearanceType ?? "",
                        LatestUpdateDate = matchingDocs
                            .Select(b => b.DeclarationDate != null && DateTime.TryParse(b.DeclarationDate, out var date) ? (DateTime?)date : null)
                            .Where(d => d.HasValue)
                            .Select(d => d!.Value)
                            .DefaultIfEmpty()
                            .Max()
                    };
                }
            }

            if (group == null)
            {
                _logger.LogWarning("No consolidated cargo group found for Master BL: {MasterBL}", masterBL);
                return null;
            }

            _logger.LogInformation("Found consolidated cargo group for Master BL {MasterBL} with {ContainerCount} container(s) and {HouseBLCount} House BL(s)",
                masterBL, group.ContainerNumbers.Count, group.HouseBLDetails.Count);

            // Full-field visibility: fetch full BOE entities + manifest items once so each
            // BOEDetailDto carries DeliveryPlace, Party addresses, Container fields, warnings, etc.
            // Previously this path used HouseBLDetail (only 10 projected fields).
            var allBoeIds = group.HouseBLDetails.Select(h => h.BOEId).Distinct().ToList();
            var boeLookup = await _icumDbContext.BOEDocuments
                .AsNoTracking()
                .Where(b => allBoeIds.Contains(b.Id))
                .ToDictionaryAsync(b => b.Id);
            var allItems = await _icumDbContext.ManifestItems
                .AsNoTracking()
                .Where(i => allBoeIds.Contains(i.BOEDocumentId))
                .ToListAsync();

            // Group House BLs by House BL number
            var houseBLGroups = group.HouseBLDetails
                .GroupBy(h => h.HouseBL)
                .Select(g => new HouseBLGroupDto
                {
                    HouseBL = g.Key,
                    MasterBL = masterBL,
                    DeclarationNumber = g.First().DeclarationNumber,
                    ConsigneeName = g.First().ConsigneeName,
                    ClearanceType = g.First().ClearanceType,
                    BOEIds = g.Select(h => h.BOEId).ToList(),
                    ContainerNumbers = g.Select(h => h.ContainerNumber).Distinct().ToList(),
                    BOEDetails = g
                        .Select(h => boeLookup.TryGetValue(h.BOEId, out var boe) ? CreateBOEDetail(boe, allItems) : null)
                        .Where(d => d != null)
                        .Cast<BOEDetailDto>()
                        .ToList()
                }).ToList();

            return new CargoGroupDto
            {
                GroupIdentifier = masterBL,
                Type = CargoType.Consolidated,
                GroupingKey = "MasterBL",
                MasterBL = masterBL,
                HouseBLGroups = houseBLGroups,
                ContainerNumbers = group.ContainerNumbers,
                TotalContainers = group.ContainerNumbers.Count,
                TotalHouseBLs = houseBLGroups.Count,
                TotalBOEs = group.HouseBLDetails.Count,
                ClearanceType = group.HouseBLDetails.FirstOrDefault()?.ClearanceType ?? "",
                LatestUpdateDate = group.HouseBLDetails
                    .Select(h => h.DeclarationDate != null && DateTime.TryParse(h.DeclarationDate, out var date) ? (DateTime?)date : null)
                    .Where(d => d.HasValue)
                    .Select(d => d!.Value)
                    .DefaultIfEmpty()
                    .Max()
            };
        }

        private async Task<CargoGroupDto> BuildNonConsolidatedGroupAsync(string declarationNumber)
        {
            // ✅ FIX: Query directly by declaration number instead of loading all groups
            var group = await _cargoQueries.GetNonConsolidatedCargoGroupByDeclarationAsync(declarationNumber);

            // ✅ FALLBACK: If not found in column, try RawJsonData extraction
            if (group == null)
            {
                _logger.LogInformation("Declaration number {DeclarationNumber} not found in column, trying RawJsonData extraction", declarationNumber);

                // ✅ FALLBACK: Load BOE documents with RawJsonData and check in memory
                // Limit to recent documents to avoid loading all data
                // 2026-05-04: Dropped `!b.IsConsolidated` filter — declaration-number lookup is the
                // unambiguous discriminator; RawJsonData fallback considers all BOEs.
                var boeDocuments = await _icumDbContext.BOEDocuments
                    .AsNoTracking()
                    .Where(b => !string.IsNullOrWhiteSpace(b.RawJsonData))
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(1000) // ✅ MEMORY FIX: Limit to 1000 most recent documents
                    .ToListAsync();

                // Find documents where RawJsonData contains the declaration number
                var matchingDocs = new List<BOEDocument>();
                foreach (var doc in boeDocuments)
                {
                    var (_, extractedDecl) = ExtractGroupingFieldsFromRawJson(doc.RawJsonData);
                    if (extractedDecl == declarationNumber)
                    {
                        matchingDocs.Add(doc);
                    }
                }

                if (matchingDocs.Any())
                {
                    _logger.LogInformation("Found {Count} BOE document(s) with DeclarationNumber {DeclarationNumber} in RawJsonData",
                        matchingDocs.Count, declarationNumber);

                    // Build group from matching documents
                    var containers = matchingDocs
                        .Select(b => b.ContainerNumber)
                        .Where(c => !string.IsNullOrWhiteSpace(c))
                        .Distinct()
                        .ToList();

                    var firstDoc = matchingDocs.First();
                    group = new NonConsolidatedCargoGroup
                    {
                        DeclarationNumber = declarationNumber,
                        MasterBL = firstDoc.BlNumber ?? "",
                        Containers = containers,
                        NoOfContainers = containers.Count,
                        ClearanceType = firstDoc.ClearanceType ?? "",
                        ConsigneeName = firstDoc.ConsigneeName ?? "",
                        RotationNumber = firstDoc.RotationNumber ?? "",
                        GoodsDescription = firstDoc.GoodsDescription ?? "",
                        TotalDutyPaid = firstDoc.TotalDutyPaid,
                        DeclarationDate = firstDoc.DeclarationDate,
                        SampleBOEId = firstDoc.Id
                    };
                }
            }

            if (group == null)
            {
                _logger.LogWarning("No non-consolidated cargo group found for declaration number: {DeclarationNumber}", declarationNumber);
                return null;
            }

            _logger.LogInformation("Found non-consolidated cargo group for declaration {DeclarationNumber} with {ContainerCount} container(s)",
                declarationNumber, group.Containers.Count);

            return new CargoGroupDto
            {
                GroupIdentifier = declarationNumber,
                Type = CargoType.NonConsolidated,
                GroupingKey = "Declaration",
                DeclarationNumber = declarationNumber,
                ConsigneeName = group.ConsigneeName,
                ContainerNumbers = group.Containers,
                TotalContainers = group.Containers.Count,
                TotalHouseBLs = 0,
                TotalBOEs = 1,
                ClearanceType = group.ClearanceType,
                LatestUpdateDate = group.DeclarationDate != null ? DateTime.TryParse(group.DeclarationDate, out var date) ? date : null : null
            };
        }

        private async Task<CargoGroupDto?> BuildCmrCompositeGroupAsync(
            string operationalKey,
            bool loadScannerData,
            bool loadImageData,
            bool loadICUMSData,
            CancellationToken cancellationToken = default)
        {
            var cmrDocuments = await FindCmrDocumentsForOperationalKeyAsync(operationalKey, cancellationToken);
            if (!cmrDocuments.Any())
            {
                _logger.LogWarning("No CMR BOE documents found for operational key {OperationalKey}", operationalKey);
                return null;
            }

            var containers = GetDistinctContainerNumbers(cmrDocuments);
            var firstDoc = cmrDocuments
                .OrderByDescending(d => d.UpdatedAt)
                .ThenByDescending(d => d.CreatedAt)
                .First();

            var group = new CargoGroupDto
            {
                GroupIdentifier = operationalKey,
                Type = CargoType.NonConsolidated,
                GroupingKey = "CmrComposite",
                DeclarationNumber = operationalKey,
                MasterBL = firstDoc.BlNumber,
                ConsigneeName = firstDoc.ConsigneeName,
                ContainerNumbers = containers,
                TotalContainers = containers.Count,
                TotalHouseBLs = 0,
                TotalBOEs = cmrDocuments.Select(d => d.Id).Distinct().Count(),
                ClearanceType = "CMR",
                LatestUpdateDate = cmrDocuments
                    .Select(d => (DateTime?)d.UpdatedAt)
                    .DefaultIfEmpty(firstDoc.CreatedAt)
                    .Max()
            };

            group.Data = await GetCmrCompositeDataAsync(
                operationalKey,
                cmrDocuments,
                containers,
                loadScannerData,
                loadImageData,
                loadICUMSData,
                cancellationToken);

            _logger.LogInformation(
                "Built CMR composite cargo group {OperationalKey}: Containers={ContainerCount}, BOEs={BoeCount}",
                operationalKey,
                group.TotalContainers,
                group.TotalBOEs);

            return group;
        }

        private async Task<List<BOEDocument>> FindCmrDocumentsForOperationalKeyAsync(
            string operationalKey,
            CancellationToken cancellationToken = default)
        {
            var trimmedKey = operationalKey.Trim();
            var upperKey = trimmedKey.ToUpperInvariant();

            var rcs = await _appDbContext.RecordCompletenessStatuses
                .AsNoTracking()
                .Where(r => r.DeclarationNumber == trimmedKey || r.DeclarationNumber == upperKey)
                .Where(r => r.ClearanceType == null || r.ClearanceType == "CMR")
                .OrderByDescending(r => r.UpdatedAtUtc)
                .FirstOrDefaultAsync(cancellationToken);

            if (rcs != null)
            {
                var expectedContainers = await _appDbContext.RecordExpectedContainers
                    .AsNoTracking()
                    .Where(rec => rec.RecordId == rcs.Id)
                    .Select(rec => new
                    {
                        rec.ContainerNumber,
                        rec.BoeDocumentId
                    })
                    .ToListAsync(cancellationToken);

                var boeIds = expectedContainers
                    .Where(rec => rec.BoeDocumentId.HasValue)
                    .Select(rec => rec.BoeDocumentId!.Value)
                    .Distinct()
                    .ToList();

                var containerNumbers = expectedContainers
                    .Select(rec => rec.ContainerNumber)
                    .Where(c => !string.IsNullOrWhiteSpace(c))
                    .Select(c => c.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();

                var query = _icumDbContext.BOEDocuments
                    .AsNoTracking()
                    .Where(b => b.ClearanceType == "CMR");

                if (boeIds.Any())
                {
                    query = query.Where(b => boeIds.Contains(b.Id));
                }
                else
                {
                    if (containerNumbers.Any())
                    {
                        query = query.Where(b => containerNumbers.Contains(b.ContainerNumber));
                    }

                    if (!string.IsNullOrWhiteSpace(rcs.RotationNumber))
                    {
                        var rotation = rcs.RotationNumber.Trim();
                        query = query.Where(b => b.RotationNumber == rotation);
                    }

                    if (!string.IsNullOrWhiteSpace(rcs.BlNumber))
                    {
                        var blNumber = rcs.BlNumber.Trim();
                        query = query.Where(b => b.BlNumber == blNumber);
                    }
                }

                var linkedRows = await query
                    .OrderByDescending(b => b.UpdatedAt)
                    .ThenByDescending(b => b.CreatedAt)
                    .ToListAsync(cancellationToken);

                var exactLinkedRows = FilterCmrDocumentsByOperationalKey(linkedRows, upperKey);
                if (exactLinkedRows.Any())
                {
                    return exactLinkedRows;
                }

                _logger.LogWarning(
                    "RCS {RecordId} exists for CMR operational key {OperationalKey}, but linked BOE rows did not recompute to the same key",
                    rcs.Id,
                    operationalKey);
            }

            var recentCandidates = await _icumDbContext.BOEDocuments
                .AsNoTracking()
                .Where(b => b.ClearanceType == "CMR")
                .Where(b => b.RotationNumber != null && b.RotationNumber != "")
                .Where(b => b.ContainerNumber != null && b.ContainerNumber != "")
                .Where(b => b.BlNumber != null && b.BlNumber != "")
                .OrderByDescending(b => b.UpdatedAt)
                .ThenByDescending(b => b.CreatedAt)
                .Take(5000)
                .ToListAsync(cancellationToken);

            return FilterCmrDocumentsByOperationalKey(recentCandidates, upperKey);
        }

        private static List<BOEDocument> FilterCmrDocumentsByOperationalKey(
            IEnumerable<BOEDocument> documents,
            string operationalKey)
        {
            return documents
                .Where(doc =>
                    CmrCompositeKeyHelper.TryCreate(
                        doc.RotationNumber,
                        doc.ContainerNumber,
                        doc.BlNumber,
                        out var key)
                    && string.Equals(key.OperationalKey, operationalKey, StringComparison.OrdinalIgnoreCase))
                .GroupBy(doc => doc.Id)
                .Select(group => group.First())
                .ToList();
        }

        private async Task<CargoGroupDataDto> GetCmrCompositeDataAsync(
            string operationalKey,
            IReadOnlyList<BOEDocument> cmrDocuments,
            List<string> containerNumbers,
            bool loadScannerData,
            bool loadImageData,
            bool loadICUMSData,
            CancellationToken cancellationToken = default)
        {
            var data = new CargoGroupDataDto
            {
                ICUMSData = loadICUMSData
                    ? await BuildCmrCompositeIcumsGroupsAsync(operationalKey, cmrDocuments, containerNumbers, cancellationToken)
                    : new List<ICUMSDataGroupDto>(),
                ScannerData = new List<ScannerDataGroupDto>(),
                ImageData = new List<ImageDataGroupDto>()
            };

            if (loadScannerData)
            {
                data.ScannerData = await GetScannerDataForGroupAsync(containerNumbers, cancellationToken);
            }

            if (loadImageData)
            {
                data.ImageData = await GetImageDataForGroupAsync(containerNumbers, cancellationToken);
            }

            return data;
        }

        private async Task<List<ICUMSDataGroupDto>> BuildCmrCompositeIcumsGroupsAsync(
            string operationalKey,
            IReadOnlyList<BOEDocument> cmrDocuments,
            List<string> containerNumbers,
            CancellationToken cancellationToken = default)
        {
            var boeIds = cmrDocuments.Select(d => d.Id).Distinct().ToList();
            var manifestItems = boeIds.Any()
                ? await _icumDbContext.ManifestItems
                    .AsNoTracking()
                    .Where(i => boeIds.Contains(i.BOEDocumentId))
                    .ToListAsync(cancellationToken)
                : new List<DownloadedManifestItem>();

            var sharedRecords = new Dictionary<string, ICUMSDataRecordDto>(StringComparer.OrdinalIgnoreCase);
            var sharedBoeDetails = new List<BOEDetailDto>();

            foreach (var boe in cmrDocuments)
            {
                var records = await ExtractICUMSRecords(boe);
                foreach (var record in records)
                {
                    var key = $"{record.Category}|{record.Field}";
                    if (!sharedRecords.TryGetValue(key, out var existing)
                        || (!HasUsefulValue(existing.Value) && HasUsefulValue(record.Value)))
                    {
                        sharedRecords[key] = record;
                    }
                }

                if (!sharedBoeDetails.Any(detail => detail.BOEId == boe.Id))
                {
                    sharedBoeDetails.Add(CreateBOEDetail(boe, manifestItems));
                }
            }

            var containersToUse = containerNumbers.Any()
                ? containerNumbers
                : GetDistinctContainerNumbers(cmrDocuments);

            if (!containersToUse.Any())
            {
                containersToUse = new List<string> { operationalKey };
            }

            return containersToUse
                .Select(containerNumber => new ICUMSDataGroupDto
                {
                    GroupKey = containerNumber,
                    ContainerNumber = containerNumber,
                    Records = sharedRecords.Values.ToList(),
                    BOEDetails = sharedBoeDetails
                })
                .ToList();
        }

        private static List<string> GetDistinctContainerNumbers(IEnumerable<BOEDocument> documents)
        {
            return documents
                .Select(doc => doc.ContainerNumber)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => c.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static bool HasUsefulValue(string? value)
        {
            return !string.IsNullOrWhiteSpace(value)
                && !string.Equals(value, "Not available", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(value, "N/A", StringComparison.OrdinalIgnoreCase);
        }

        private async Task<List<ICUMSDataGroupDto>> GetICUMSDataForGroupAsync(
            string groupIdentifier,
            CargoType type,
            List<string> containerNumbers,
            CancellationToken cancellationToken = default)
        {
            var icumsTotalStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var houseBLQueryStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var boeQueryStopwatch = System.Diagnostics.Stopwatch.StartNew();
            var extractionStopwatch = System.Diagnostics.Stopwatch.StartNew();

            _logger.LogInformation("⏱️ [GetICUMSDataForGroupAsync] START - Identifier: {Identifier}, Type: {Type}, Containers: {Count}",
                groupIdentifier, type, containerNumbers?.Count ?? 0);

            var icumsGroups = new List<ICUMSDataGroupDto>();

            try
            {
                if (type == CargoType.Consolidated)
                {
                    // For consolidated: Group by House BL
                    houseBLQueryStopwatch.Restart();
                    var houseBLs = await _cargoQueries.GetHouseBLsByMasterBLAsync(groupIdentifier);
                    houseBLQueryStopwatch.Stop();
                    _logger.LogInformation("⏱️ [GetICUMSDataForGroupAsync] GetHouseBLsByMasterBLAsync took: {Time}ms, Found {Count} House BLs",
                        houseBLQueryStopwatch.ElapsedMilliseconds, houseBLs?.Count ?? 0);

                    // ✅ PERFORMANCE: Batch query all BOE documents at once
                    // ✅ FIX: Batch Contains() to avoid EF Core CTE generation with large lists
                    var houseBLKeys = houseBLs.Select(h => h.HouseBL).Distinct().ToList();
                    var allBOEDocuments = new List<BOEDocument>();

                    boeQueryStopwatch.Restart();
                    // ✅ FIX: Use FromSqlRaw to avoid EF Core CTE generation for Contains()
                    // EF Core generates CTEs for Contains() which causes "WITH" syntax errors
                    // Using FromSqlRaw with escaped values avoids CTE generation
                    const int batchSize = 100;
                    if (houseBLKeys.Count > 0)
                    {
                        for (int i = 0; i < houseBLKeys.Count; i += batchSize)
                        {
                            var batch = houseBLKeys.Skip(i).Take(batchSize).ToList();
                            var batchStopwatch = System.Diagnostics.Stopwatch.StartNew();
                            // ✅ FIX: Use FromSqlRaw to avoid CTE generation and semicolon issues
                            // Escape single quotes in HouseBL values to prevent SQL injection
                            var escapedValues = batch.Select(h => $"'{h?.Replace("'", "''") ?? ""}'");
                            var placeholders = string.Join(",", escapedValues);
                            var sql = $"SELECT * FROM BOEDocuments WHERE HouseBl IN ({placeholders}) AND BlNumber = '{groupIdentifier?.Replace("'", "''") ?? ""}'";

                            var batchDocuments = await _icumDbContext.BOEDocuments
                                .FromSqlRaw(sql)
                                .AsNoTracking()
                                .ToListAsync(cancellationToken);
                            batchStopwatch.Stop();
                            allBOEDocuments.AddRange(batchDocuments);
                            _logger.LogInformation("⏱️ [GetICUMSDataForGroupAsync] BOE batch {BatchNum} query took: {Time}ms, Found {Count} documents",
                                (i / batchSize) + 1, batchStopwatch.ElapsedMilliseconds, batchDocuments.Count);
                        }
                    }
                    boeQueryStopwatch.Stop();
                    _logger.LogInformation("⏱️ [GetICUMSDataForGroupAsync] Total BOE query time: {Time}ms, Total documents: {Count}",
                        boeQueryStopwatch.ElapsedMilliseconds, allBOEDocuments.Count);

                    // Batch-load manifest items across all BOEs so BOEDetailDto can carry line items
                    var consolidatedBoeIds = allBOEDocuments.Select(b => b.Id).ToList();
                    var consolidatedItems = await _icumDbContext.ManifestItems
                        .AsNoTracking()
                        .Where(i => consolidatedBoeIds.Contains(i.BOEDocumentId))
                        .ToListAsync(cancellationToken);

                    extractionStopwatch.Restart();
                    foreach (var houseBL in houseBLs.GroupBy(h => h.HouseBL))
                    {
                        var houseBLKey = houseBL.Key;
                        var boeDocuments = allBOEDocuments
                            .Where(b => b.HouseBl == houseBLKey)
                            .ToList();

                        var records = new List<ICUMSDataRecordDto>();
                        var boeDetails = new List<BOEDetailDto>();

                        var houseBLExtractionStopwatch = System.Diagnostics.Stopwatch.StartNew();
                        foreach (var boe in boeDocuments)
                        {
                            // Extract fields from BOE document
                            records.AddRange(await ExtractICUMSRecords(boe));
                            boeDetails.Add(CreateBOEDetail(boe, consolidatedItems));
                        }
                        houseBLExtractionStopwatch.Stop();
                        _logger.LogInformation("⏱️ [GetICUMSDataForGroupAsync] House BL {HouseBL} extraction took: {Time}ms, Records: {RecordCount}, BOEs: {BOECount}",
                            houseBLKey, houseBLExtractionStopwatch.ElapsedMilliseconds, records.Count, boeDocuments.Count);

                        icumsGroups.Add(new ICUMSDataGroupDto
                        {
                            GroupKey = houseBLKey,
                            HouseBL = houseBLKey,
                            Records = records,
                            BOEDetails = boeDetails
                        });
                    }
                }
                else
                {
                    // ✅ FIX: For non-consolidated cargo, ALL containers share the SAME BOE data
                    // Query ALL BOE documents for this declaration (not filtered by container)
                    // Note: containerNumbers may be empty, so we query by declaration number only
                    boeQueryStopwatch.Restart();
                    // 2026-05-04: Dropped `!b.IsConsolidated` filter — declaration-number lookup is
                    // the unambiguous key; rows mis-tagged IsConsolidated=true at ingest must still
                    // load their ICUMS data when accessed via declaration.
                    var allBOEDocuments = await _icumDbContext.BOEDocuments
                        .AsNoTracking()
                        .Where(b => b.DeclarationNumber == groupIdentifier)
                        .OrderByDescending(b => b.CreatedAt)
                        .ToListAsync(cancellationToken);
                    boeQueryStopwatch.Stop();
                    _logger.LogInformation("⏱️ [GetICUMSDataForGroupAsync] Non-consolidated BOE query took: {Time}ms, Found {Count} documents",
                        boeQueryStopwatch.ElapsedMilliseconds, allBOEDocuments.Count);

                    if (allBOEDocuments.Any())
                    {
                        // Batch-load manifest items across all BOEs for this non-consolidated declaration
                        var nonConsolidatedBoeIds = allBOEDocuments.Select(b => b.Id).ToList();
                        var nonConsolidatedItems = await _icumDbContext.ManifestItems
                            .AsNoTracking()
                            .Where(i => nonConsolidatedBoeIds.Contains(i.BOEDocumentId))
                            .ToListAsync(cancellationToken);

                        extractionStopwatch.Restart();
                        // ✅ Extract shared records from ALL BOE documents (deduplicated by field name)
                        var sharedRecords = new Dictionary<string, ICUMSDataRecordDto>(); // Key: Field name
                        var sharedBOEDetails = new List<BOEDetailDto>();

                        foreach (var boe in allBOEDocuments)
                        {
                            var records = await ExtractICUMSRecords(boe);

                            // Merge records by field name (keep first non-empty value)
                            foreach (var record in records)
                            {
                                if (!sharedRecords.ContainsKey(record.Field))
                                {
                                    sharedRecords[record.Field] = record;
                                }
                                else if (string.IsNullOrEmpty(sharedRecords[record.Field].Value) ||
                                         sharedRecords[record.Field].Value == "Not available" ||
                                         sharedRecords[record.Field].Value == "N/A")
                                {
                                    // Replace empty value with non-empty value
                                    if (!string.IsNullOrEmpty(record.Value) &&
                                        record.Value != "Not available" &&
                                        record.Value != "N/A")
                                    {
                                        sharedRecords[record.Field] = record;
                                    }
                                }
                            }

                            // Add BOE details (deduplicated by BOEId)
                            if (!sharedBOEDetails.Any(b => b.BOEId == boe.Id))
                            {
                                sharedBOEDetails.Add(CreateBOEDetail(boe, nonConsolidatedItems));
                            }
                        }

                        // ✅ Get container numbers from BOE documents if not provided
                        var containersToUse = containerNumbers.Any()
                            ? containerNumbers
                            : allBOEDocuments.Select(b => b.ContainerNumber).Distinct().ToList();

                        // ✅ Create ONE ICUMSDataGroupDto per container, all with the SAME shared records
                        foreach (var containerNumber in containersToUse)
                        {
                            icumsGroups.Add(new ICUMSDataGroupDto
                            {
                                GroupKey = containerNumber,
                                ContainerNumber = containerNumber,
                                Records = sharedRecords.Values.ToList(), // ✅ Same shared records for all containers
                                BOEDetails = sharedBOEDetails // ✅ Same shared BOE details for all containers
                            });
                        }

                        extractionStopwatch.Stop();
                        _logger.LogInformation("⏱️ [GetICUMSDataForGroupAsync] Non-consolidated extraction took: {Time}ms", extractionStopwatch.ElapsedMilliseconds);
                        _logger.LogInformation("Created {Count} ICUMS data groups for non-consolidated declaration {Declaration} with {RecordCount} shared records across {ContainerCount} container(s)",
                            icumsGroups.Count, groupIdentifier, sharedRecords.Count, containersToUse.Count);
                    }
                    else
                    {
                        _logger.LogWarning("No BOE documents found for non-consolidated declaration {Declaration}", groupIdentifier);
                    }
                }
            }
            catch (Exception ex)
            {
                icumsTotalStopwatch.Stop();
                _logger.LogError(ex, "⏱️ [GetICUMSDataForGroupAsync] ERROR after {Time}ms for group: {Identifier}",
                    icumsTotalStopwatch.ElapsedMilliseconds, groupIdentifier);
            }
            finally
            {
                icumsTotalStopwatch.Stop();
                _logger.LogInformation("⏱️ [GetICUMSDataForGroupAsync] COMPLETE - Total: {Total}ms ({TotalSeconds:F2}s), Groups: {Count}",
                    icumsTotalStopwatch.ElapsedMilliseconds,
                    icumsTotalStopwatch.Elapsed.TotalSeconds,
                    icumsGroups.Count);
            }

            return icumsGroups;
        }

        private async Task<List<ICUMSDataRecordDto>> ExtractICUMSRecords(BOEDocument boe)
        {
            var records = new List<ICUMSDataRecordDto>();

            // Parse RawJsonData if available for fallback extraction
            JsonDocument? rawJsonDoc = null;
            JsonElement? headerElement = null;
            JsonElement? containerDetailsElement = null;
            JsonElement? manifestDetailsElement = null;
            JsonElement? rootElement = null;

            if (!string.IsNullOrEmpty(boe.RawJsonData))
            {
                try
                {
                    rawJsonDoc = JsonDocument.Parse(boe.RawJsonData);
                    rootElement = rawJsonDoc.RootElement;

                    if (rootElement.Value.ValueKind == JsonValueKind.Object)
                    {
                        // Try exact property names first
                        if (rootElement.Value.TryGetProperty("Header", out var header))
                            headerElement = header;
                        if (rootElement.Value.TryGetProperty("ContainerDetails", out var containerDetails))
                            containerDetailsElement = containerDetails;
                        if (rootElement.Value.TryGetProperty("ManifestDetails", out var manifestDetails))
                            manifestDetailsElement = manifestDetails;

                        // Fallback: Try case-insensitive matching for section names
                        if (!headerElement.HasValue || !containerDetailsElement.HasValue || !manifestDetailsElement.HasValue)
                        {
                            foreach (var prop in rootElement.Value.EnumerateObject())
                            {
                                var propNameUpper = prop.Name.ToUpperInvariant();
                                if (propNameUpper == "HEADER" && !headerElement.HasValue)
                                    headerElement = prop.Value;
                                else if ((propNameUpper.Contains("CONTAINER") || propNameUpper == "CONTAINERDETAILS") && !containerDetailsElement.HasValue)
                                    containerDetailsElement = prop.Value;
                                else if ((propNameUpper.Contains("MANIFEST") || propNameUpper == "MANIFESTDETAILS") && !manifestDetailsElement.HasValue)
                                    manifestDetailsElement = prop.Value;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to parse RawJsonData for BOEDocument {BOEId}", boe.Id);
                    // Continue with entity properties only
                }
            }

            // Determine if this is CMR or BOE clearance
            bool isCMR = boe.ClearanceType == "CMR";

            // ✅ COMPREHENSIVE: Extract ALL fields like ContainerDetailsController does
            var icumsRecords = new List<ICUMSDataRecordDto>
            {
                new ICUMSDataRecordDto
                {
                    Field = "Container Number",
                    Value = boe.ContainerNumber ?? "Not available",
                    Category = "Container Info",
                    IsRequired = true,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Clearance Type",
                    Value = GetValueWithJsonFallback(boe.ClearanceType, headerElement, rootElement, "ClearanceType", "CLEARANCETYPE") ?? "Not available",
                    Category = "Declaration Info",
                    IsRequired = true,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Rotation Number",
                    Value = GetValueWithJsonFallback(boe.RotationNumber, manifestDetailsElement, rootElement, "RotationNumber")
                        ?? (isCMR ? "⚠️ MISSING (Required for CMR)" : "Not applicable"),
                    Category = isCMR ? "CMR Info" : "Manifest Info",
                    IsRequired = isCMR,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Declaration Number (BOE)",
                    Value = GetValueWithJsonFallback(boe.DeclarationNumber, headerElement, rootElement, "DeclarationNumber", "DECLARATIONNUMBER")
                        ?? (isCMR ? "N/A (CMR clearance)" : "⚠️ MISSING (Required for IM/EX)"),
                    Category = "Declaration Info",
                    IsRequired = !isCMR,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "BL Number",
                    Value = GetValueWithJsonFallback(boe.BlNumber, manifestDetailsElement, rootElement, "BLNumber", "BlNumber", "BL_NUMBER") ?? "Not available",
                    Category = "Manifest Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "CRMS Risk Level",
                    Value = GetValueWithJsonFallback(boe.CrmsLevel, headerElement, rootElement, "CRMSLevel", "CrmsLevel", "CRMS_LEVEL") ?? "Not assessed",
                    Category = "Risk Assessment",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Regime Code",
                    Value = GetValueWithJsonFallback(boe.RegimeCode, headerElement, rootElement, "RegimeCode", "REGIMECODE") ?? "Not available",
                    Category = "Declaration Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Number of Containers",
                    Value = GetValueWithJsonFallback(boe.NoOfContainers?.ToString(), headerElement, rootElement, "NoofContainers", "NoOfContainers", "NOOFCONTAINERS") ?? "Not available",
                    Category = "Declaration Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Declaration Version",
                    Value = GetValueWithJsonFallback(boe.DeclarationVersion?.ToString(), headerElement, rootElement, "DeclarationVersion", "DECLARATIONVERSION") ?? "Not available",
                    Category = "Declaration Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Declaration Date",
                    Value = GetValueWithJsonFallback(boe.DeclarationDate, headerElement, rootElement, "DeclarationDate", "DECLARATIONDATE") ?? "Not available",
                    Category = "Declaration Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Declarant Name",
                    Value = GetValueWithJsonFallback(boe.DeclarantName, headerElement, rootElement, "DeclarantName", "DECLARANTNAME") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Declarant Address",
                    Value = GetValueWithJsonFallback(boe.DeclarantAddress, headerElement, rootElement, "DeclarantAddress", "DECLARANTADDRESS", "DECLARANT_ADDRESS") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Consignee Name",
                    Value = GetValueWithJsonFallback(boe.ConsigneeName, manifestDetailsElement, rootElement, "ConsigneeName") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Consignee Address",
                    Value = GetValueWithJsonFallback(boe.ConsigneeAddress, manifestDetailsElement, rootElement, "ConsigneeAddress") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Importer Name",
                    Value = GetValueWithJsonFallback(boe.ImpName, headerElement, rootElement, "ImpName", "IMPNAME") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Importer Address",
                    Value = GetValueWithJsonFallback(boe.ImpAddress, headerElement, rootElement, "ImpAddress", "IMPADDRESS", "IMP_ADDRESS") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Exporter Name",
                    Value = GetValueWithJsonFallback(boe.ExpName, headerElement, rootElement, "ExpName", "EXPNAME", "EXP_NAME") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Exporter Address",
                    Value = GetValueWithJsonFallback(boe.ExpAddress, headerElement, rootElement, "ExpAddress", "EXPADDRESS", "EXP_ADDRESS") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Shipper Name",
                    Value = GetValueWithJsonFallback(boe.ShipperName, manifestDetailsElement, rootElement, "ShipperName") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Shipper Address",
                    Value = GetValueWithJsonFallback(boe.ShipperAddress, manifestDetailsElement, rootElement, "ShipperAddress") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Importer/Exporter Name",
                    Value = GetValueWithJsonFallback(boe.ImpExpName, headerElement, rootElement, "ImpExpName", "IMPEXPNAME", "IMP_EXP_NAME") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Importer/Exporter Address",
                    Value = GetValueWithJsonFallback(boe.ImpExpAddress, headerElement, rootElement, "ImpExpAddress", "IMPEXPADDRESS", "IMP_EXP_ADDRESS") ?? "Not available",
                    Category = "Party Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Country of Origin",
                    Value = GetValueWithJsonFallback(boe.CountryOfOrigin, manifestDetailsElement, rootElement, "CountryofOrigin", "CountryOfOrigin") ?? "Not available",
                    Category = "Location Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Delivery Place",
                    Value = GetValueWithJsonFallback(boe.DeliveryPlace, manifestDetailsElement, rootElement, "DeliveryPlace") ?? "Not available",
                    Category = "Location Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Container ISO",
                    Value = GetValueWithJsonFallback(boe.ContainerISO, containerDetailsElement, rootElement, "ContainerISO", "ISO") ?? "Not available",
                    Category = "Container Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Container Weight",
                    Value = GetValueWithJsonFallback(boe.ContainerWeight?.ToString("N2"), containerDetailsElement, rootElement, "ContainerWeight", "Weight") ?? "Not available",
                    Category = "Container Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Total Duty Paid",
                    Value = GetValueWithJsonFallback(
                        boe.TotalDutyPaid?.ToString("N2") + " GHS",
                        headerElement,
                        rootElement,
                        "TotalDutyPaid",
                        "TOTALDUTYPAID") ?? "Not available",
                    Category = "Financial Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "House BL",
                    Value = GetValueWithJsonFallback(boe.HouseBl, manifestDetailsElement, rootElement, "HouseBL", "HouseBl", "HOUSE_BL") ?? "Not available",
                    Category = "Manifest Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Marks & Numbers",
                    Value = GetValueWithJsonFallback(boe.MarksNumbers, manifestDetailsElement, rootElement, "MarksNumbers", "MarksNumber") ?? "Not available",
                    Category = "Cargo Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Goods Description",
                    Value = GetValueWithJsonFallback(boe.GoodsDescription, manifestDetailsElement, rootElement,
                        "GoodsDescription", "Goods_Description", "GOODSDESCRIPTION", "Description", "DESCRIPTION") ?? "Not available",
                    Category = "Cargo Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Compliance Officer Remarks",
                    Value = boe.CompOffRemarks ?? "None",
                    Category = "Risk Assessment",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "CCVR Intelligence Remarks",
                    Value = boe.CcvrIntelRemarks ?? "None",
                    Category = "Risk Assessment",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Processing Status",
                    Value = boe.ProcessingStatus ?? "Unknown",
                    Category = "Status",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Created At",
                    Value = boe.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    Category = "Data Info",
                    IsRequired = false,
                    HouseBL = boe.HouseBl
                },

                // ── Part B: full-field visibility additions ──
                new ICUMSDataRecordDto
                {
                    Field = "Master BL Number",
                    Value = GetValueWithJsonFallback(boe.MasterBlNumber, manifestDetailsElement, rootElement, "MasterBlNumber", "MasterBLNumber", "MASTERBLNUMBER") ?? "Not available",
                    Category = "Manifest Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Original Clearance Type",
                    Value = boe.OriginalClearanceType ?? "Not available",
                    Category = "Declaration Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "CMR Upgraded At",
                    Value = boe.CmrUpgradedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not available",
                    Category = "Declaration Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Container Size",
                    Value = GetValueWithJsonFallback(boe.ContainerSize, containerDetailsElement, rootElement, "ContainerSize") ?? "Not available",
                    Category = "Container Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Container Quantity",
                    Value = boe.ContainerQuantity?.ToString() ?? "Not available",
                    Category = "Container Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Container Status",
                    Value = GetValueWithJsonFallback(boe.ContainerStatus, containerDetailsElement, rootElement, "Status", "ContainerStatus") ?? "Not available",
                    Category = "Container Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Container Remarks",
                    Value = GetValueWithJsonFallback(boe.ContainerRemarks, containerDetailsElement, rootElement, "Remarks", "ContainerRemarks") ?? "Not available",
                    Category = "Container Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Container Description",
                    Value = GetValueWithJsonFallback(boe.ContainerDescription, containerDetailsElement, rootElement, "Description", "ContainerDescription") ?? "Not available",
                    Category = "Container Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Seal Number",
                    Value = GetValueWithJsonFallback(boe.SealNumber, containerDetailsElement, rootElement, "SealNumber") ?? "Not available",
                    Category = "Container Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Truck Plate Number",
                    Value = GetValueWithJsonFallback(boe.TruckPlateNumber, containerDetailsElement, rootElement, "TruckPlateNumber") ?? "Not available",
                    Category = "Container Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Driver Name",
                    Value = GetValueWithJsonFallback(boe.DriverName, containerDetailsElement, rootElement, "DriverName") ?? "Not available",
                    Category = "Container Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Driver License",
                    Value = GetValueWithJsonFallback(boe.DriverLicense, containerDetailsElement, rootElement, "DriverLicense") ?? "Not available",
                    Category = "Container Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Consolidated",
                    Value = boe.IsConsolidated ? "Yes" : "No",
                    Category = "Manifest Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Ingestion Warnings",
                    Value = boe.HasIngestionWarnings
                        ? (string.IsNullOrWhiteSpace(boe.IngestionWarnings) ? "(flagged — no detail)" : boe.IngestionWarnings!.Replace('\n', ';'))
                        : "None",
                    Category = "Integrity", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Has Warnings",
                    Value = boe.HasIngestionWarnings ? "Yes" : "No",
                    Category = "Integrity", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Processed At",
                    Value = boe.ProcessedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not yet processed",
                    Category = "Status", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Updated At",
                    Value = boe.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                    Category = "Data Info", IsRequired = false, HouseBL = boe.HouseBl
                },
                new ICUMSDataRecordDto
                {
                    Field = "Error Message",
                    Value = string.IsNullOrWhiteSpace(boe.ErrorMessage) ? "None" : boe.ErrorMessage,
                    Category = "Status", IsRequired = false, HouseBL = boe.HouseBl
                }
            };

            // Add unmapped fields
            var unmappedFields = ExtractUnmappedFieldsForICUMS(boe);
            icumsRecords.AddRange(unmappedFields);

            // ✅ CARGO INFO FIX: Extract cargo fields from ManifestItems
            // Include ALL statuses — don't filter by ProcessingStatus at all
            var manifestItems = await _icumDbContext.ManifestItems
                .AsNoTracking()
                .Where(m => m.BOEDocumentId == boe.Id)
                .OrderBy(m => m.ItemIndex)
                .ToListAsync();

            _logger.LogWarning("[MANIFEST-DEBUG] BOE {BOEId} (Decl: {Decl}): Found {Count} manifest items. Statuses: {Statuses}",
                boe.Id, boe.DeclarationNumber ?? "N/A", manifestItems.Count,
                string.Join(", ", manifestItems.Select(m => m.ProcessingStatus).Distinct()));

            // ✅ GOODS DESCRIPTION FIX: If GoodsDescription is missing, try to get it from ManifestItems
            var goodsDescriptionRecord = icumsRecords.FirstOrDefault(r => r.Field == "Goods Description");
            if (goodsDescriptionRecord != null &&
                (string.IsNullOrWhiteSpace(goodsDescriptionRecord.Value) ||
                 goodsDescriptionRecord.Value == "Not available" ||
                 goodsDescriptionRecord.Value == "N/A"))
            {
                // Try to get description from ManifestItems (already loaded above with Completed+Transferred)
                var itemDescriptions = manifestItems
                    .Where(m => !string.IsNullOrWhiteSpace(m.Description))
                    .Select(m => m.Description.Trim())
                    .Distinct()
                    .ToList();

                if (itemDescriptions.Any())
                {
                    // Show ALL descriptions joined together (no truncation)
                    goodsDescriptionRecord.Value = string.Join("; ", itemDescriptions);
                    _logger.LogInformation("✅ Extracted Goods Description from ManifestItems for BOE {BOEId} (Declaration: {DeclarationNumber}): {Description}",
                        boe.Id, boe.DeclarationNumber ?? "N/A", goodsDescriptionRecord.Value);
                }
                else
                {
                    _logger.LogWarning("⚠️ Could not extract Goods Description from ManifestItems for BOE {BOEId} (Declaration: {DeclarationNumber}). " +
                        "BOE.GoodsDescription: '{BOEGoodsDesc}', ManifestItems count: {ManifestCount}",
                        boe.Id, boe.DeclarationNumber ?? "N/A", boe.GoodsDescription ?? "null", manifestItems.Count);
                }
            }

            if (manifestItems.Any())
            {
                _logger.LogInformation("Found {Count} manifest item(s) for BOE {BOEId}", manifestItems.Count, boe.Id);

                // Extract cargo fields from manifest items
                // For multiple items, aggregate or show per-item
                foreach (var item in manifestItems)
                {
                    var itemPrefix = manifestItems.Count > 1 ? $"Item {item.ItemNo ?? item.ItemIndex}: " : "";

                    if (!string.IsNullOrWhiteSpace(item.HsCode))
                    {
                        icumsRecords.Add(new ICUMSDataRecordDto
                        {
                            Field = $"{itemPrefix}HS Code",
                            Value = item.HsCode,
                            Category = "Cargo Info",
                            IsRequired = false,
                            HouseBL = boe.HouseBl
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(item.Description))
                    {
                        icumsRecords.Add(new ICUMSDataRecordDto
                        {
                            Field = $"{itemPrefix}Item Description",
                            Value = item.Description,
                            Category = "Cargo Info",
                            IsRequired = false,
                            HouseBL = boe.HouseBl
                        });
                    }

                    if (item.Quantity.HasValue && item.Quantity.Value > 0)
                    {
                        icumsRecords.Add(new ICUMSDataRecordDto
                        {
                            Field = $"{itemPrefix}Quantity",
                            Value = $"{item.Quantity.Value:N2} {item.Unit ?? ""}".Trim(),
                            Category = "Cargo Info",
                            IsRequired = false,
                            HouseBL = boe.HouseBl
                        });
                    }

                    if (item.Weight.HasValue && item.Weight.Value > 0)
                    {
                        icumsRecords.Add(new ICUMSDataRecordDto
                        {
                            Field = $"{itemPrefix}Weight",
                            Value = $"{item.Weight.Value:N2} kg",
                            Category = "Cargo Info",
                            IsRequired = false,
                            HouseBL = boe.HouseBl
                        });
                    }

                    if (item.ItemFob.HasValue && item.ItemFob.Value > 0)
                    {
                        icumsRecords.Add(new ICUMSDataRecordDto
                        {
                            Field = $"{itemPrefix}FOB Value",
                            Value = $"{item.ItemFob.Value:C} {item.FobCurrency ?? ""}".Trim(),
                            Category = "Cargo Info",
                            IsRequired = false,
                            HouseBL = boe.HouseBl
                        });
                    }

                    if (item.ItemDutyPaid.HasValue && item.ItemDutyPaid.Value > 0)
                    {
                        icumsRecords.Add(new ICUMSDataRecordDto
                        {
                            Field = $"{itemPrefix}Duty Paid",
                            Value = $"{item.ItemDutyPaid.Value:N2} GHS",
                            Category = "Cargo Info",
                            IsRequired = false,
                            HouseBL = boe.HouseBl
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(item.CountryOfOrigin))
                    {
                        icumsRecords.Add(new ICUMSDataRecordDto
                        {
                            Field = $"{itemPrefix}Item Country of Origin",
                            Value = item.CountryOfOrigin,
                            Category = "Cargo Info",
                            IsRequired = false,
                            HouseBL = boe.HouseBl
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(item.Cpc))
                    {
                        icumsRecords.Add(new ICUMSDataRecordDto
                        {
                            Field = $"{itemPrefix}CPC",
                            Value = item.Cpc,
                            Category = "Cargo Info",
                            IsRequired = false,
                            HouseBL = boe.HouseBl
                        });
                    }
                }
            }
            else
            {
                _logger.LogWarning("No manifest items found in database for BOE {BOEId}, attempting fallback extraction from RawJsonData", boe.Id);

                // ✅ FALLBACK FIX: If no ManifestItems in database, extract from RawJsonData
                if (rootElement.HasValue && rootElement.Value.TryGetProperty("ManifestItems", out var manifestItemsElement) && manifestItemsElement.ValueKind == JsonValueKind.Array)
                {
                    var fallbackItems = new List<(string? HsCode, string? Description, decimal? Quantity, string? Unit, decimal? Weight, decimal? ItemFob, string? FobCurrency, decimal? ItemDutyPaid, string? CountryOfOrigin, string? Cpc, int? ItemNo, int ItemIndex)>();
                    var itemIndex = 0;

                    foreach (var itemElement in manifestItemsElement.EnumerateArray())
                    {
                        // Extract fields from JSON element (support both uppercase and PascalCase)
                        var hsCode = GetJsonStringValue(itemElement, "HSCODE", "HsCode");
                        var description = GetJsonStringValue(itemElement, "DESCRIPTION", "Description");
                        var quantity = GetJsonDecimalValue(itemElement, "QUANTITY", "Quantity");
                        var unit = GetJsonStringValue(itemElement, "UNIT", "Unit");
                        var weight = GetJsonDecimalValue(itemElement, "WEIGHT", "Weight");
                        var itemFob = GetJsonDecimalValue(itemElement, "ITEMFOB", "ItemFob");
                        var fobCurrency = GetJsonStringValue(itemElement, "FOBCURRENCY", "FobCurrency");
                        var itemDutyPaid = GetJsonDecimalValue(itemElement, "ITEMDUTYPAID", "ItemDutyPaid");
                        var countryOfOrigin = GetJsonStringValue(itemElement, "COUNTRYOFORIGIN", "CountryOfOrigin", "CountryofOrigin");
                        var cpc = GetJsonStringValue(itemElement, "CPC", "Cpc");
                        var itemNo = GetJsonIntValue(itemElement, "ITEMNO", "ItemNo");

                        fallbackItems.Add((hsCode, description, quantity, unit, weight, itemFob, fobCurrency, itemDutyPaid, countryOfOrigin, cpc, itemNo, itemIndex));
                        itemIndex++;
                    }

                    if (fallbackItems.Any())
                    {
                        _logger.LogInformation("✅ FALLBACK: Extracted {Count} manifest item(s) from RawJsonData for BOE {BOEId}", fallbackItems.Count, boe.Id);

                        // Process fallback items same as database items
                        foreach (var item in fallbackItems)
                        {
                            var itemPrefix = fallbackItems.Count > 1 ? $"Item {item.ItemNo ?? item.ItemIndex}: " : "";

                            if (!string.IsNullOrWhiteSpace(item.HsCode))
                            {
                                icumsRecords.Add(new ICUMSDataRecordDto
                                {
                                    Field = $"{itemPrefix}HS Code",
                                    Value = item.HsCode,
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boe.HouseBl
                                });
                            }

                            if (!string.IsNullOrWhiteSpace(item.Description))
                            {
                                icumsRecords.Add(new ICUMSDataRecordDto
                                {
                                    Field = $"{itemPrefix}Item Description",
                                    Value = item.Description,
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boe.HouseBl
                                });
                            }

                            if (item.Quantity.HasValue && item.Quantity.Value > 0)
                            {
                                icumsRecords.Add(new ICUMSDataRecordDto
                                {
                                    Field = $"{itemPrefix}Quantity",
                                    Value = $"{item.Quantity.Value:N2} {item.Unit ?? ""}".Trim(),
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boe.HouseBl
                                });
                            }

                            if (item.Weight.HasValue && item.Weight.Value > 0)
                            {
                                icumsRecords.Add(new ICUMSDataRecordDto
                                {
                                    Field = $"{itemPrefix}Weight",
                                    Value = $"{item.Weight.Value:N2} kg",
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boe.HouseBl
                                });
                            }

                            if (item.ItemFob.HasValue && item.ItemFob.Value > 0)
                            {
                                icumsRecords.Add(new ICUMSDataRecordDto
                                {
                                    Field = $"{itemPrefix}FOB Value",
                                    Value = $"{item.ItemFob.Value:C} {item.FobCurrency ?? ""}".Trim(),
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boe.HouseBl
                                });
                            }

                            if (item.ItemDutyPaid.HasValue && item.ItemDutyPaid.Value > 0)
                            {
                                icumsRecords.Add(new ICUMSDataRecordDto
                                {
                                    Field = $"{itemPrefix}Duty Paid",
                                    Value = $"{item.ItemDutyPaid.Value:N2} GHS",
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boe.HouseBl
                                });
                            }

                            if (!string.IsNullOrWhiteSpace(item.CountryOfOrigin))
                            {
                                icumsRecords.Add(new ICUMSDataRecordDto
                                {
                                    Field = $"{itemPrefix}Item Country of Origin",
                                    Value = item.CountryOfOrigin,
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boe.HouseBl
                                });
                            }

                            if (!string.IsNullOrWhiteSpace(item.Cpc))
                            {
                                icumsRecords.Add(new ICUMSDataRecordDto
                                {
                                    Field = $"{itemPrefix}CPC",
                                    Value = item.Cpc,
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boe.HouseBl
                                });
                            }
                        }

                        // ✅ Also update Goods Description if missing, using fallback items
                        if (goodsDescriptionRecord != null &&
                            (string.IsNullOrWhiteSpace(goodsDescriptionRecord.Value) ||
                             goodsDescriptionRecord.Value == "Not available" ||
                             goodsDescriptionRecord.Value == "N/A"))
                        {
                            var itemDescriptions = fallbackItems
                                .Where(m => !string.IsNullOrWhiteSpace(m.Description))
                                .Select(m => m.Description!.Trim())
                                .Distinct()
                                .ToList();

                            if (itemDescriptions.Any())
                            {
                                if (itemDescriptions.Count == 1)
                                {
                                    goodsDescriptionRecord.Value = itemDescriptions.First();
                                }
                                else if (itemDescriptions.Count <= 3)
                                {
                                    goodsDescriptionRecord.Value = string.Join("; ", itemDescriptions);
                                }
                                else
                                {
                                    goodsDescriptionRecord.Value = $"{itemDescriptions.First()} (and {itemDescriptions.Count - 1} other item type(s))";
                                }
                                _logger.LogInformation("✅ FALLBACK: Extracted Goods Description from RawJsonData ManifestItems for BOE {BOEId}: {Description}",
                                    boe.Id, goodsDescriptionRecord.Value);
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No manifest items found in RawJsonData for BOE {BOEId}", boe.Id);
                    }
                }
                else
                {
                    _logger.LogWarning("RawJsonData does not contain ManifestItems array for BOE {BOEId}", boe.Id);
                }
            }

            // Dispose JSON document if created
            rawJsonDoc?.Dispose();

            return icumsRecords;
        }

        private BOEDetailDto CreateBOEDetail(BOEDocument boe, IReadOnlyList<DownloadedManifestItem>? items = null)
        {
            var detail = new BOEDetailDto
            {
                BOEId = boe.Id,

                // Container Details
                ContainerNumber       = boe.ContainerNumber,
                ContainerDescription  = boe.ContainerDescription,
                ContainerISO          = boe.ContainerISO,
                ContainerQuantity     = boe.ContainerQuantity,
                ContainerWeight       = boe.ContainerWeight,
                ContainerSize         = boe.ContainerSize,
                ContainerStatus       = boe.ContainerStatus,
                ContainerRemarks      = boe.ContainerRemarks,
                SealNumber            = boe.SealNumber,
                TruckPlateNumber      = boe.TruckPlateNumber,
                DriverName            = boe.DriverName,
                DriverLicense         = boe.DriverLicense,

                // Header / Declaration
                DeclarationNumber     = boe.DeclarationNumber,
                DeclarationDate       = boe.DeclarationDate,
                DeclarationVersion    = boe.DeclarationVersion,
                RegimeCode            = boe.RegimeCode,
                ClearanceType         = boe.ClearanceType,
                OriginalClearanceType = boe.OriginalClearanceType,
                CmrUpgradedAt         = boe.CmrUpgradedAt,
                NoOfContainers        = boe.NoOfContainers,
                TotalDutyPaid         = boe.TotalDutyPaid,
                CrmsLevel             = boe.CrmsLevel,
                CompOffRemarks        = boe.CompOffRemarks,
                CcvrIntelRemarks      = boe.CcvrIntelRemarks,

                // Parties
                ImpName          = boe.ImpName,
                ImpAddress       = boe.ImpAddress,
                ExpName          = boe.ExpName,
                ExpAddress       = boe.ExpAddress,
                ImpExpName       = boe.ImpExpName,
                ImpExpAddress    = boe.ImpExpAddress,
                DeclarantName    = boe.DeclarantName,
                DeclarantAddress = boe.DeclarantAddress,

                // Manifest / BL / Shipping
                BlNumber         = boe.BlNumber,
                HouseBL          = boe.HouseBl,
                MasterBL         = boe.BlNumber,          // legacy alias
                MasterBlNumber   = boe.MasterBlNumber,
                RotationNumber   = boe.RotationNumber,
                DeliveryPlace    = boe.DeliveryPlace,
                GoodsDescription = boe.GoodsDescription,
                CountryOfOrigin  = boe.CountryOfOrigin,
                MarksNumbers     = boe.MarksNumbers,
                ConsigneeName    = boe.ConsigneeName,
                ConsigneeAddress = boe.ConsigneeAddress,
                ShipperName      = boe.ShipperName,
                ShipperAddress   = boe.ShipperAddress,

                IsConsolidated   = boe.IsConsolidated,

                // Legacy RawJsonData (admin code should prefer IngestionMetadata.RawJsonData)
                RawJsonData = boe.RawJsonData,

                // Admin-only ingestion metadata — UI tier decides whether to render it.
                IngestionMetadata = new BOEIngestionMetadataDto
                {
                    ProcessingStatus       = boe.ProcessingStatus,
                    ProcessedAt            = boe.ProcessedAt,
                    ErrorMessage           = boe.ErrorMessage,
                    CreatedAt              = boe.CreatedAt,
                    UpdatedAt              = boe.UpdatedAt,
                    HasIngestionWarnings   = boe.HasIngestionWarnings,
                    IngestionWarnings      = string.IsNullOrWhiteSpace(boe.IngestionWarnings)
                        ? new List<string>()
                        : boe.IngestionWarnings.Split('\n', StringSplitOptions.RemoveEmptyEntries).ToList(),
                    UnmappedFieldsCount    = boe.UnmappedFieldsCount,
                    UnmappedFieldsOverflow = boe.UnmappedFieldsOverflow,
                    UnmappedFields         = CollectUnmappedFields(boe),
                    RawJsonData            = boe.RawJsonData,
                    DownloadedFileId       = boe.DownloadedFileId,
                    DocumentIndex          = boe.DocumentIndex
                }
            };

            // Attach pre-loaded manifest items (caller batch-loads to avoid N+1).
            if (items != null && items.Count > 0)
            {
                detail.ManifestItems = items
                    .Where(i => i.BOEDocumentId == boe.Id)
                    .OrderBy(i => i.ItemIndex)
                    .Select(i => new ManifestItemDto
                    {
                        Id              = i.Id,
                        ItemIndex       = i.ItemIndex,
                        ItemNo          = i.ItemNo,
                        HsCode          = i.HsCode,
                        Description     = i.Description,
                        Quantity        = i.Quantity,
                        Unit            = i.Unit,
                        Weight          = i.Weight,
                        ItemFob         = i.ItemFob,
                        ItemDutyPaid    = i.ItemDutyPaid,
                        FobCurrency     = i.FobCurrency,
                        CountryOfOrigin = i.CountryOfOrigin,
                        Cpc             = i.Cpc
                    }).ToList();
            }

            // Legacy: also populate AllFields dict from RawJsonData (existing consumers may rely on it)
            if (!string.IsNullOrEmpty(boe.RawJsonData))
            {
                try
                {
                    var jsonDoc = JsonDocument.Parse(boe.RawJsonData);
                    ExtractAllFields(jsonDoc.RootElement, detail.AllFields);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error parsing RawJsonData for BOE {BOEId}", boe.Id);
                }
            }

            return detail;
        }

        /// <summary>
        /// Gathers any populated UnmappedField1..20 Label/Value pairs into a DTO list.
        /// </summary>
        private static List<UnmappedFieldDto> CollectUnmappedFields(BOEDocument boe)
        {
            var pairs = new[]
            {
                (boe.UnmappedField1Label,  boe.UnmappedField1Value),
                (boe.UnmappedField2Label,  boe.UnmappedField2Value),
                (boe.UnmappedField3Label,  boe.UnmappedField3Value),
                (boe.UnmappedField4Label,  boe.UnmappedField4Value),
                (boe.UnmappedField5Label,  boe.UnmappedField5Value),
                (boe.UnmappedField6Label,  boe.UnmappedField6Value),
                (boe.UnmappedField7Label,  boe.UnmappedField7Value),
                (boe.UnmappedField8Label,  boe.UnmappedField8Value),
                (boe.UnmappedField9Label,  boe.UnmappedField9Value),
                (boe.UnmappedField10Label, boe.UnmappedField10Value),
                (boe.UnmappedField11Label, boe.UnmappedField11Value),
                (boe.UnmappedField12Label, boe.UnmappedField12Value),
                (boe.UnmappedField13Label, boe.UnmappedField13Value),
                (boe.UnmappedField14Label, boe.UnmappedField14Value),
                (boe.UnmappedField15Label, boe.UnmappedField15Value),
                (boe.UnmappedField16Label, boe.UnmappedField16Value),
                (boe.UnmappedField17Label, boe.UnmappedField17Value),
                (boe.UnmappedField18Label, boe.UnmappedField18Value),
                (boe.UnmappedField19Label, boe.UnmappedField19Value),
                (boe.UnmappedField20Label, boe.UnmappedField20Value)
            };
            return pairs
                .Where(p => !string.IsNullOrWhiteSpace(p.Item1))
                .Select(p => new UnmappedFieldDto { Label = p.Item1 ?? string.Empty, Value = p.Item2 ?? string.Empty })
                .ToList();
        }

        private void ExtractAllFields(JsonElement element, Dictionary<string, string> fields, string prefix = "")
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in element.EnumerateObject())
                {
                    var key = string.IsNullOrEmpty(prefix) ? prop.Name : $"{prefix}.{prop.Name}";
                    if (prop.Value.ValueKind == JsonValueKind.Object || prop.Value.ValueKind == JsonValueKind.Array)
                    {
                        ExtractAllFields(prop.Value, fields, key);
                    }
                    else
                    {
                        fields[key] = prop.Value.ToString();
                    }
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                int index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ExtractAllFields(item, fields, $"{prefix}[{index}]");
                    index++;
                }
            }
        }

        private async Task<List<ScannerDataGroupDto>> GetScannerDataForGroupAsync(
            List<string> containerNumbers,
            CancellationToken cancellationToken = default)
        {
            var scannerGroups = new List<ScannerDataGroupDto>();

            if (!containerNumbers.Any())
            {
                return scannerGroups;
            }

            try
            {
                // ✅ PERFORMANCE FIX: Use batched FromSqlRaw queries with IN clause instead of loading entire tables
                // This avoids loading millions of rows into memory and reduces query time from 10-30s to <1s
                var containerNumbersHashSet = new HashSet<string>(containerNumbers, StringComparer.OrdinalIgnoreCase);
                var fs6000Scans = new List<Core.Entities.FS6000.FS6000Scan>();
                var aseScans = new List<Core.Entities.ASE.AseScan>();

                // ✅ PERFORMANCE: Batch container numbers to avoid large IN clauses
                // ✅ FIX: Reduced batch size to 500 to avoid SQL Server parameter limits and improve performance
                const int batchSize = 500;

                // Load FS6000 scans in batches
                for (int i = 0; i < containerNumbers.Count; i += batchSize)
                {
                    var batch = containerNumbers.Skip(i).Take(batchSize).Where(c => !string.IsNullOrEmpty(c)).ToList();
                    if (!batch.Any()) continue;

                    // ✅ FIX: Escape single quotes to prevent SQL injection
                    var placeholders = string.Join(",", batch.Select(c => $"'{c!.Replace("'", "''")}'"));
                    var batchQuery = $"SELECT * FROM FS6000Scans WHERE ContainerNumber IN ({placeholders}) ORDER BY ScanTime DESC";

                    var batchScans = await _appDbContext.FS6000Scans
                        .FromSqlRaw(batchQuery)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);

                    fs6000Scans.AddRange(batchScans);
                }

                // Load ASE scans in batches
                for (int i = 0; i < containerNumbers.Count; i += batchSize)
                {
                    var batch = containerNumbers.Skip(i).Take(batchSize).Where(c => !string.IsNullOrEmpty(c)).ToList();
                    if (!batch.Any()) continue;

                    // ✅ FIX: Escape single quotes to prevent SQL injection
                    var placeholders = string.Join(",", batch.Select(c => $"'{c!.Replace("'", "''")}'"));
                    var batchQuery = $"SELECT * FROM AseScans WHERE ContainerNumber IN ({placeholders}) ORDER BY ScanTime DESC";

                    var batchScans = await _appDbContext.AseScans
                        .FromSqlRaw(batchQuery)
                        .AsNoTracking()
                        .ToListAsync(cancellationToken);

                    aseScans.AddRange(batchScans);
                }

                // Group by container
                var fs6000ByContainer = fs6000Scans.GroupBy(s => s.ContainerNumber).ToList();
                var aseByContainer = aseScans.GroupBy(s => s.ContainerNumber).ToList();

                foreach (var containerNumber in containerNumbers)
                {
                    var containerFS6000 = fs6000ByContainer.FirstOrDefault(g => g.Key == containerNumber);
                    var containerASE = aseByContainer.FirstOrDefault(g => g.Key == containerNumber);

                    if (containerFS6000 != null)
                    {
                        var allRecords = new List<ScannerDataRecordDto>();
                        foreach (var fs6000 in containerFS6000)
                        {
                            allRecords.AddRange(ExtractFS6000ScannerRecords(fs6000));
                        }

                        scannerGroups.Add(new ScannerDataGroupDto
                        {
                            ContainerNumber = containerNumber,
                            ScannerType = "FS6000",
                            ScanDate = containerFS6000.First().ScanTime,
                            Records = allRecords
                        });
                    }

                    if (containerASE != null)
                    {
                        var allRecords = new List<ScannerDataRecordDto>();
                        foreach (var ase in containerASE)
                        {
                            allRecords.AddRange(ExtractASEScannerRecords(ase));
                        }

                        scannerGroups.Add(new ScannerDataGroupDto
                        {
                            ContainerNumber = containerNumber,
                            ScannerType = "ASE",
                            ScanDate = containerASE.First().ScanTime,
                            Records = allRecords
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scanner data for containers");
            }

            return scannerGroups;
        }

        private List<ScannerDataRecordDto> ExtractFS6000ScannerRecords(Core.Entities.FS6000.FS6000Scan scan)
        {
            var records = new List<ScannerDataRecordDto>();

            try
            {
                records.AddRange(new List<ScannerDataRecordDto>
                {
                    new ScannerDataRecordDto
                    {
                        Field = "Container Number",
                        Value = scan.ContainerNumber ?? "N/A",
                        Category = "Container Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Scanner Type",
                        Value = "FS6000",
                        Category = "Scanner Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Scan Time",
                        Value = scan.ScanTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Category = "Scanner Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Picture Number",
                        Value = scan.PicNumber ?? "N/A",
                        Category = "Scanner Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Vessel Name",
                        Value = scan.VesselName ?? "N/A",
                        Category = "Vessel Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Operator ID",
                        Value = scan.OperatorId ?? "N/A",
                        Category = "Operator Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Scan Result",
                        Value = scan.ScanResult ?? "N/A",
                        Category = "Scan Result",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Goods Description",
                        Value = scan.GoodsDescription ?? "N/A",
                        Category = "Cargo Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Shipping Company",
                        Value = scan.ShippingCompany ?? "N/A",
                        Category = "Shipping Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Consignee",
                        Value = scan.Consignee ?? "N/A",
                        Category = "Party Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "FYCO Present",
                        Value = scan.FycoPresent ?? "N/A",
                        Category = "Security Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "File Path",
                        Value = scan.FilePath ?? "N/A",
                        Category = "File Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Sync Status",
                        Value = scan.SyncStatus ?? "Unknown",
                        Category = "Status",
                        Timestamp = scan.ScanTime
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting FS6000 scanner records");
            }

            return records;
        }

        private List<ScannerDataRecordDto> ExtractASEScannerRecords(Core.Entities.ASE.AseScan scan)
        {
            var records = new List<ScannerDataRecordDto>();

            try
            {
                records.AddRange(new List<ScannerDataRecordDto>
                {
                    new ScannerDataRecordDto
                    {
                        Field = "Container Number",
                        Value = scan.ContainerNumber ?? "N/A",
                        Category = "Container Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Scanner Type",
                        Value = "ASE",
                        Category = "Scanner Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Scan Time",
                        Value = scan.ScanTime.ToString("yyyy-MM-dd HH:mm:ss"),
                        Category = "Scanner Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Inspection ID",
                        Value = scan.InspectionId.ToString(),
                        Category = "Scanner Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Inspection UUID",
                        Value = scan.InspectionUuid ?? "N/A",
                        Category = "Scanner Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Vehicle Number",
                        Value = scan.TruckPlate ?? "N/A",
                        Category = "Vehicle Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Image Display Name",
                        Value = scan.ImageDisplayName ?? "N/A",
                        Category = "Image Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Has Scan Image",
                        Value = scan.ScanImage != null ? "Yes" : "No",
                        Category = "Image Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Image Size",
                        Value = scan.ScanImage?.Length.ToString() + " bytes" ?? "N/A",
                        Category = "Image Info",
                        Timestamp = scan.ScanTime
                    },
                    new ScannerDataRecordDto
                    {
                        Field = "Synced At",
                        Value = scan.SyncedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        Category = "Status",
                        Timestamp = scan.ScanTime
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error extracting ASE scanner records");
            }

            return records;
        }

        private string GetCategoryForProperty(string propertyName)
        {
            if (propertyName.Contains("Container", StringComparison.OrdinalIgnoreCase))
                return "Container Info";
            if (propertyName.Contains("Scan", StringComparison.OrdinalIgnoreCase) || propertyName.Contains("Time", StringComparison.OrdinalIgnoreCase))
                return "Scan Info";
            if (propertyName.Contains("Image", StringComparison.OrdinalIgnoreCase) || propertyName.Contains("Path", StringComparison.OrdinalIgnoreCase))
                return "Image Info";
            return "General";
        }

        /// <summary>
        /// Get value from entity property, falling back to RawJsonData if property is null/empty
        /// Enhanced with case-insensitive matching and broader property search
        /// </summary>
        private string? GetValueWithJsonFallback(string? entityValue, JsonElement? jsonElement, JsonElement? rootElement = null, params string[] jsonPropertyNames)
        {
            // If entity property has a value, use it
            if (!string.IsNullOrWhiteSpace(entityValue) && entityValue != "Not available" && entityValue != "N/A")
                return entityValue;

            // Try to extract from JSON if available
            if (jsonElement.HasValue)
            {
                var element = jsonElement.Value;

                // ✅ FIX: Check if element is null or not an object before accessing properties
                if (element.ValueKind == JsonValueKind.Null || element.ValueKind == JsonValueKind.Undefined)
                {
                    return null;
                }

                // ✅ FIX: Must be an object type to access properties - check this first
                if (element.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                // First, try exact property name matches (case-sensitive)
                foreach (var propName in jsonPropertyNames)
                {
                    if (element.TryGetProperty(propName, out var prop))
                    {
                        // ✅ FIX: Check if property value is valid before extracting
                        if (prop.ValueKind != JsonValueKind.Null && prop.ValueKind != JsonValueKind.Undefined)
                        {
                            var value = ExtractJsonValue(prop);
                            if (value != null)
                                return value;
                        }
                    }
                }

                // If exact matches failed, try case-insensitive matching
                {
                    foreach (var propName in jsonPropertyNames)
                    {
                        foreach (var jsonProp in element.EnumerateObject())
                        {
                            if (string.Equals(jsonProp.Name, propName, StringComparison.OrdinalIgnoreCase))
                            {
                                var value = ExtractJsonValue(jsonProp.Value);
                                if (value != null)
                                    return value;
                            }
                        }
                    }

                    // Last resort: search for properties containing the key words (partial match)
                    var searchTerms = jsonPropertyNames.SelectMany(n =>
                        new[] { n, n.Replace("_", ""), n.Replace("_", " "), n.ToUpper(), n.ToLower() }
                    ).Distinct().ToList();

                    foreach (var jsonProp in element.EnumerateObject())
                    {
                        var propNameUpper = jsonProp.Name.ToUpperInvariant();
                        foreach (var searchTerm in searchTerms)
                        {
                            if (propNameUpper.Contains(searchTerm.ToUpperInvariant()) ||
                                searchTerm.ToUpperInvariant().Contains(propNameUpper))
                            {
                                var value = ExtractJsonValue(jsonProp.Value);
                                if (value != null && !string.IsNullOrWhiteSpace(value))
                                    return value;
                            }
                        }
                    }
                }

                // If section element didn't have the property, try searching the root element
                if (rootElement.HasValue && rootElement.Value.ValueKind == JsonValueKind.Object)
                {
                    foreach (var propName in jsonPropertyNames)
                    {
                        if (rootElement.Value.TryGetProperty(propName, out var prop))
                        {
                            var value = ExtractJsonValue(prop);
                            if (value != null)
                                return value;
                        }
                    }

                    // Case-insensitive search in root
                    foreach (var propName in jsonPropertyNames)
                    {
                        foreach (var jsonProp in rootElement.Value.EnumerateObject())
                        {
                            if (string.Equals(jsonProp.Name, propName, StringComparison.OrdinalIgnoreCase))
                            {
                                var value = ExtractJsonValue(jsonProp.Value);
                                if (value != null)
                                    return value;
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Extract string value from JsonElement, handling different value kinds
        /// </summary>
        private string? ExtractJsonValue(JsonElement prop)
        {
            if (prop.ValueKind == JsonValueKind.String)
            {
                var strValue = prop.GetString();
                if (!string.IsNullOrWhiteSpace(strValue) && strValue != "null" && strValue != "NULL")
                    return strValue;
            }
            else if (prop.ValueKind == JsonValueKind.Number)
            {
                return prop.GetRawText();
            }
            else if (prop.ValueKind == JsonValueKind.True || prop.ValueKind == JsonValueKind.False)
            {
                return prop.GetBoolean().ToString();
            }
            else if (prop.ValueKind == JsonValueKind.Null)
            {
                return null;
            }

            return null;
        }

        /// <summary>
        /// Helper method to extract string value from JsonElement with multiple field name options
        /// </summary>
        private string? GetJsonStringValue(JsonElement element, params string[] fieldNames)
        {
            foreach (var fieldName in fieldNames)
            {
                if (element.TryGetProperty(fieldName, out var prop))
                {
                    var value = ExtractJsonValue(prop);
                    if (value != null)
                        return value;
                }
                // Try case-insensitive match
                foreach (var jsonProp in element.EnumerateObject())
                {
                    if (string.Equals(jsonProp.Name, fieldName, StringComparison.OrdinalIgnoreCase))
                    {
                        var value = ExtractJsonValue(jsonProp.Value);
                        if (value != null)
                            return value;
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Helper method to extract decimal value from JsonElement with multiple field name options
        /// </summary>
        private decimal? GetJsonDecimalValue(JsonElement element, params string[] fieldNames)
        {
            var strValue = GetJsonStringValue(element, fieldNames);
            if (string.IsNullOrWhiteSpace(strValue))
                return null;

            if (decimal.TryParse(strValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result))
                return result;

            return null;
        }

        /// <summary>
        /// Helper method to extract int value from JsonElement with multiple field name options
        /// </summary>
        private int? GetJsonIntValue(JsonElement element, params string[] fieldNames)
        {
            var strValue = GetJsonStringValue(element, fieldNames);
            if (string.IsNullOrWhiteSpace(strValue))
                return null;

            // Try direct numeric extraction first
            foreach (var fieldName in fieldNames)
            {
                if (element.TryGetProperty(fieldName, out var prop))
                {
                    if (prop.ValueKind == JsonValueKind.Number)
                    {
                        if (prop.TryGetInt32(out var intVal))
                            return intVal;
                    }
                }
            }

            // Fallback to string parsing
            if (int.TryParse(strValue, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result))
                return result;

            return null;
        }

        /// <summary>
        /// Extract unmapped fields from BOEDocument and converts them to ICUMSDataRecordDto format
        /// </summary>
        private List<ICUMSDataRecordDto> ExtractUnmappedFieldsForICUMS(BOEDocument document)
        {
            var unmappedFields = new List<ICUMSDataRecordDto>();

            // Extract from structured columns (fields 1-20)
            for (int i = 1; i <= 20; i++)
            {
                var labelProp = typeof(BOEDocument).GetProperty($"UnmappedField{i}Label");
                var valueProp = typeof(BOEDocument).GetProperty($"UnmappedField{i}Value");

                if (labelProp == null || valueProp == null) continue;

                var label = labelProp.GetValue(document) as string;
                var value = valueProp.GetValue(document) as string;

                if (string.IsNullOrEmpty(label)) continue;

                // Parse label format: "Header:NewField"
                var parts = label.Split(':', 2);
                var section = parts.Length > 0 ? parts[0] : "Unknown";
                var fieldName = parts.Length > 1 ? parts[1] : label;

                // Determine category based on section
                var category = section switch
                {
                    "Header" => "Declaration Info",
                    "ContainerDetails" => "Container Info",
                    "ManifestDetails" => "Manifest Info",
                    "ManifestItem" => "Cargo Info",
                    _ => "Additional Fields"
                };

                unmappedFields.Add(new ICUMSDataRecordDto
                {
                    Field = $"{section}:{fieldName}",  // Keep section prefix for identification
                    Value = value ?? "N/A",
                    Category = category,
                    IsRequired = false,  // Unmapped fields are never required
                    HouseBL = document.HouseBl
                });
            }

            // If there are unmapped fields and overflow, add an indicator
            if (document.UnmappedFieldsCount > 20 && document.UnmappedFieldsOverflow)
            {
                unmappedFields.Add(new ICUMSDataRecordDto
                {
                    Field = "⚠️ Additional Unmapped Fields",
                    Value = $"{document.UnmappedFieldsCount - 20} more field(s) available in Raw JSON",
                    Category = "Additional Fields",
                    IsRequired = false,
                    HouseBL = document.HouseBl
                });
            }

            return unmappedFields;
        }

        private async Task<List<ImageDataGroupDto>> GetImageDataForGroupAsync(
            List<string> containerNumbers,
            CancellationToken cancellationToken = default)
        {
            var imageGroups = new List<ImageDataGroupDto>();

            if (!containerNumbers.Any())
            {
                return imageGroups;
            }

            try
            {
                // ✅ PERFORMANCE: Use projection to avoid loading BLOB data (same fix as ContainerDetailsController)
                // ✅ FIX: Add error handling for database queries - wrap each in try-catch to allow partial success
                var fs6000ImageMetadata = new List<dynamic>();
                var aseImageMetadata = new List<dynamic>();

                try
                {
                    // ✅ PERFORMANCE FIX: Use batched FromSqlRaw queries instead of loading entire table
                    // This avoids loading millions of rows into memory
                    var containerNumbersHashSet = new HashSet<string>(containerNumbers, StringComparer.OrdinalIgnoreCase);
                    var fs6000Results = new List<dynamic>();

                    // ✅ FIX: Reduced batch size (20) to minimize connection pool usage and prevent timeouts
                    // Smaller batches = faster queries = connections released sooner
                    // EF Core generates CTEs for Contains() with Include() even with smaller lists
                    // This causes SQL Server "Incorrect syntax near WITH" errors when multiple queries execute
                    const int batchSize = 20;
                    const int queryTimeoutSeconds = 30; // Timeout per query to prevent connection pool exhaustion

                    // Load FS6000 scans with images in batches
                    // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid EF Core CTE generation (SQL Server 2014 compatibility)
                    for (int i = 0; i < containerNumbers.Count; i += batchSize)
                    {
                        var batch = containerNumbers.Skip(i).Take(batchSize).Where(c => !string.IsNullOrEmpty(c)).ToList();
                        if (!batch.Any()) continue;

                        // Build parameterized IN clause for container numbers
                        var parameters = new List<object>();
                        var parameterPlaceholders = new List<string>();

                        for (int j = 0; j < batch.Count; j++)
                        {
                            parameterPlaceholders.Add($"{{{j}}}");
                            parameters.Add(batch[j]);
                        }

                        var inClause = string.Join(",", parameterPlaceholders);

                        // ✅ FIX: Add explicit timeout to prevent long-running queries from exhausting connection pool
                        // Query scans using FromSqlRaw to avoid CTE generation
                        var sqlScans = $";SELECT * FROM FS6000Scans WHERE ContainerNumber IN ({inClause}) ORDER BY ScanTime DESC";
                        List<Core.Entities.FS6000.FS6000Scan> batchScans;
                        try
                        {
                            // Set timeout before query to ensure connections are released promptly
                            _appDbContext.Database.SetCommandTimeout(queryTimeoutSeconds);
                            batchScans = await _appDbContext.FS6000Scans
                                .FromSqlRaw(sqlScans, parameters.ToArray())
                                .AsNoTracking()
                                .ToListAsync(cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to load FS6000 scans for batch {BatchIndex}, skipping", i / batchSize);
                            continue; // Skip this batch rather than failing entire request
                        }

                        // Get scan IDs to query images
                        var scanIds = batchScans.Select(s => s.Id).ToList();
                        var scanImagesDict = new Dictionary<Guid, List<FS6000Image>>();

                        if (scanIds.Any() && scanIds.Count <= 100) // Limit image query to prevent huge result sets
                        {
                            // Query images separately using FromSqlRaw with timeout
                            var imageParameters = new List<object>();
                            var imageParameterPlaceholders = new List<string>();

                            for (int j = 0; j < scanIds.Count; j++)
                            {
                                imageParameterPlaceholders.Add($"{{{j}}}");
                                imageParameters.Add(scanIds[j]);
                            }

                            var imageInClause = string.Join(",", imageParameterPlaceholders);
                            var sqlImages = $";SELECT * FROM FS6000Images WHERE ScanId IN ({imageInClause})";

                            try
                            {
                                _appDbContext.Database.SetCommandTimeout(queryTimeoutSeconds);
                                var batchImages = await _appDbContext.FS6000Images
                                    .FromSqlRaw(sqlImages, imageParameters.ToArray())
                                    .AsNoTracking()
                                    .ToListAsync(cancellationToken);

                                // Group images by ScanId
                                scanImagesDict = batchImages
                                    .GroupBy(img => img.ScanId)
                                    .ToDictionary(g => g.Key, g => g.ToList());
                            }
                            catch (Exception imgEx)
                            {
                                _logger.LogWarning(imgEx, "Failed to load images for {Count} scans, continuing without images", scanIds.Count);
                                // Continue without images rather than failing entire request
                            }
                        }

                        // Project to anonymous type for dynamic casting
                        foreach (var scan in batchScans)
                        {
                            if (string.IsNullOrEmpty(scan.ContainerNumber) || !containerNumbersHashSet.Contains(scan.ContainerNumber))
                                continue;

                            object imagesList;
                            if (scanImagesDict.TryGetValue(scan.Id, out var images) && images.Any())
                            {
                                imagesList = images.Select(i => new
                                {
                                    i.Id,
                                    i.ImageType,
                                    i.FileName,
                                    i.FileSizeBytes,
                                    CreatedAt = i.CreatedAt
                                }).ToList();
                            }
                            else
                            {
                                imagesList = new List<object>();
                            }

                            fs6000Results.Add(new
                            {
                                ContainerNumber = scan.ContainerNumber,
                                ScanTime = scan.ScanTime,
                                FilePath = scan.FilePath,
                                Images = imagesList
                            });
                        }
                    }

                    // Convert to dynamic list for easier property access
                    fs6000ImageMetadata = fs6000Results.Cast<dynamic>().ToList();
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("FS6000 image metadata query was cancelled or timed out for {Count} containers", containerNumbers.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading FS6000 image metadata for {Count} containers", containerNumbers.Count);
                }

                try
                {
                    // ✅ PERFORMANCE FIX: Use batched FromSqlRaw queries instead of loading entire table
                    // This avoids loading millions of rows into memory
                    var containerNumbersHashSet2 = new HashSet<string>(containerNumbers, StringComparer.OrdinalIgnoreCase);
                    var aseResults = new List<dynamic>();

                    // ✅ FIX: Reduced batch size (20) to minimize connection pool usage and prevent timeouts
                    // Smaller batches = faster queries = connections released sooner
                    // EF Core generates CTEs for Contains() with Select() projections even with smaller lists
                    // This causes SQL Server "Incorrect syntax near WITH" errors when multiple queries execute
                    const int batchSize = 20;
                    const int queryTimeoutSeconds = 30; // Timeout per query to prevent connection pool exhaustion

                    // Load ASE scans in batches (projection to avoid BLOB)
                    // ✅ FIX: Use FromSqlRaw with semicolon prefix to avoid EF Core CTE generation (SQL Server 2014 compatibility)
                    for (int i = 0; i < containerNumbers.Count; i += batchSize)
                    {
                        var batch = containerNumbers.Skip(i).Take(batchSize).Where(c => !string.IsNullOrEmpty(c)).ToList();
                        if (!batch.Any()) continue;

                        // Build parameterized IN clause for container numbers
                        var parameters = new List<object>();
                        var parameterPlaceholders = new List<string>();

                        for (int j = 0; j < batch.Count; j++)
                        {
                            parameterPlaceholders.Add($"{{{j}}}");
                            parameters.Add(batch[j]);
                        }

                        var inClause = string.Join(",", parameterPlaceholders);

                        // ✅ FIX: Use FromSqlRaw with semicolon prefix and explicit timeout to avoid CTE generation
                        // Query scans - EF Core will load all properties but we'll project in memory
                        var sql = $";SELECT * FROM AseScans WHERE ContainerNumber IN ({inClause}) AND ScanImage IS NOT NULL ORDER BY ScanTime DESC";
                        List<AseScan> batchScans;
                        try
                        {
                            _appDbContext.Database.SetCommandTimeout(queryTimeoutSeconds);
                            batchScans = await _appDbContext.AseScans
                                .FromSqlRaw(sql, parameters.ToArray())
                                .AsNoTracking()
                                .ToListAsync(cancellationToken);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning(ex, "Failed to load ASE scans for batch {BatchIndex}, skipping", i / batchSize);
                            continue; // Skip this batch rather than failing entire request
                        }

                        // Project to result list in memory (avoiding BLOB in final result)
                        foreach (var scan in batchScans)
                        {
                            if (string.IsNullOrEmpty(scan.ContainerNumber) || !containerNumbersHashSet2.Contains(scan.ContainerNumber))
                                continue;

                            aseResults.Add(new
                            {
                                ContainerNumber = scan.ContainerNumber,
                                ScanTime = scan.ScanTime,
                                ImageDisplayName = scan.ImageDisplayName,
                                ImageSize = scan.ScanImage != null ? scan.ScanImage.Length : 0
                            });
                        }
                    }

                    // Convert to dynamic list for easier property access
                    aseImageMetadata = aseResults.Cast<dynamic>().ToList();
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("ASE image metadata query was cancelled or timed out for {Count} containers", containerNumbers.Count);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error loading ASE image metadata for {Count} containers", containerNumbers.Count);
                }

                // ✅ FIX: Get public base URL from configuration or HTTP context
                var publicBaseUrl = _configuration["ApiSettings:PublicBaseUrl"];
                if (string.IsNullOrEmpty(publicBaseUrl))
                {
                    // Try to get from HTTP context (if available)
                    var httpContext = _httpContextAccessor?.HttpContext;
                    if (httpContext != null)
                    {
                        publicBaseUrl = $"{httpContext.Request.Scheme}://{httpContext.Request.Host}";
                    }
                    else
                    {
                        // Fallback to BaseUrl from config
                        publicBaseUrl = _configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
                    }
                }

                // Group by container using dynamic property access
                var fs6000ByContainer = fs6000ImageMetadata
                    .GroupBy(s => (string)s.ContainerNumber)
                    .ToList();

                var aseByContainer = aseImageMetadata
                    .GroupBy(s => (string)s.ContainerNumber)
                    .ToList();

                foreach (var containerNumber in containerNumbers)
                {
                    var images = new List<ImageMetadataDto>();

                    var containerFS6000 = fs6000ByContainer.FirstOrDefault(g => g.Key == containerNumber);
                    if (containerFS6000 != null)
                    {
                        int imageId = 1;
                        foreach (var scan in containerFS6000)
                        {
                            if (scan.Images != null && ((IEnumerable<dynamic>)scan.Images).Any())
                            {
                                // v2.10.0: same raw-channel filter as ContainerDetailsController —
                                // HighEnergy/LowEnergy/Material are composite inputs, reached via
                                // the single-canvas viewer's mode toolbar, not separate cards.
                                var userFacing = ((IEnumerable<dynamic>)scan.Images)
                                    .Where(i =>
                                    {
                                        string? t = i.ImageType?.ToString();
                                        return t != "HighEnergy" && t != "LowEnergy" && t != "Material";
                                    })
                                    .OrderBy(i => (string)i.ImageType);
                                foreach (var image in userFacing)
                                {
                                    var imageType = image.ImageType?.ToString() ?? "Main";
                                    var imageCacheBuster = $"&v={((DateTime)scan.ScanTime).Ticks}";
                                    images.Add(new ImageMetadataDto
                                    {
                                        Id = imageId++,
                                        ImageType = $"FS6000-{imageType}",
                                        FileName = image.FileName?.ToString() ?? scan.FilePath?.ToString() ?? "",
                                        FileSizeBytes = image.FileSizeBytes != null ? (long)image.FileSizeBytes : 0,
                                        CreatedAt = (DateTime)scan.ScanTime,
                                        // ✅ Signed URL — browser <img src> can't carry JWT.
                                        ThumbnailUrl = publicBaseUrl + _urlSigner.SignRelative($"/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?imageType={Uri.EscapeDataString(imageType)}&size=thumbnail{imageCacheBuster}"),
                                        FullImageUrl = publicBaseUrl + _urlSigner.SignRelative($"/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?imageType={Uri.EscapeDataString(imageType)}&size=full{imageCacheBuster}")
                                    });
                                }
                            }
                            else if (!string.IsNullOrEmpty(scan.FilePath?.ToString()))
                            {
                                var cacheBuster = $"&v={((DateTime)scan.ScanTime).Ticks}";
                                images.Add(new ImageMetadataDto
                                {
                                    Id = imageId++,
                                    ImageType = "FS6000",
                                    FileName = scan.FilePath?.ToString() ?? "",
                                    FileSizeBytes = 0L,
                                    CreatedAt = (DateTime)scan.ScanTime,
                                    // ✅ Signed URL (see earlier).
                                    ThumbnailUrl = publicBaseUrl + _urlSigner.SignRelative($"/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?size=thumbnail{cacheBuster}"),
                                    FullImageUrl = publicBaseUrl + _urlSigner.SignRelative($"/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?size=full{cacheBuster}")
                                });
                            }
                        }
                    }

                    var containerASE = aseByContainer.FirstOrDefault(g => g.Key == containerNumber);
                    if (containerASE != null)
                    {
                        int imageId = images.Count + 1;
                        foreach (var scan in containerASE)
                        {
                            var cacheBuster = $"&v={((DateTime)scan.ScanTime).Ticks}";
                            images.Add(new ImageMetadataDto
                            {
                                Id = imageId++,
                                ImageType = "ASE",
                                FileName = scan.ImageDisplayName?.ToString() ?? $"ASE_Scan_{containerNumber}.jpg",
                                FileSizeBytes = scan.ImageSize != null ? (int)scan.ImageSize : 0,
                                CreatedAt = (DateTime)scan.ScanTime,
                                // ✅ Signed URL (see earlier).
                                ThumbnailUrl = publicBaseUrl + _urlSigner.SignRelative($"/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?imageType=ASE&size=thumbnail{cacheBuster}"),
                                FullImageUrl = publicBaseUrl + _urlSigner.SignRelative($"/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?imageType=ASE&size=full{cacheBuster}")
                            });
                        }
                    }

                    if (images.Any())
                    {
                        imageGroups.Add(new ImageDataGroupDto
                        {
                            ContainerNumber = containerNumber,
                            Images = images
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting image data for containers");
            }

            return imageGroups;
        }

        #endregion

        #region AI Cargo Summary

        public async Task<string?> GetAiCargoSummaryAsync(string groupIdentifier)
        {
            var group = await _appDbContext.AnalysisGroups
                .AsNoTracking()
                .FirstOrDefaultAsync(g => g.GroupIdentifier == groupIdentifier
                    || g.NormalizedGroupIdentifier == groupIdentifier);

            return group?.AiCargoSummary;
        }

        public async Task SaveAiCargoSummaryAsync(string groupIdentifier, string summary)
        {
            var group = await _appDbContext.AnalysisGroups
                .AsTracking()
                .FirstOrDefaultAsync(g => g.GroupIdentifier == groupIdentifier
                    || g.NormalizedGroupIdentifier == groupIdentifier);

            if (group != null)
            {
                group.AiCargoSummary = summary.Length > 2000 ? summary[..2000] : summary;
                group.UpdatedAtUtc = DateTime.UtcNow;
                await _appDbContext.SaveChangesAsync();
            }
        }

        #endregion
    }
}

