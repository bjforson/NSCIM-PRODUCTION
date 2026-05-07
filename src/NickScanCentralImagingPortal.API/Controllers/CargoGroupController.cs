using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.CargoGrouping;
using NickScanCentralImagingPortal.Services.Logging;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Standard API for cargo group data (consolidated and non-consolidated)
    /// Provides unified access to ICUMS, Scanner, and Image data based on grouping logic
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class CargoGroupController : ControllerBase
    {
        private readonly ICargoGroupService _cargoGroupService;
        private readonly IIcumDownloadsRepository _icumDownloadsRepo;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;
        private readonly ILogger<CargoGroupController> _logger;
        private readonly ThrottledLogger _throttledLogger;
        private readonly IGroupResolver _groupResolver;
        private readonly ApplicationDbContext _appDb;
        private readonly IcumDownloadsDbContext _icumDb;
        private const string SERVICE_ID = "[CARGO-GROUP-API]";

        public CargoGroupController(
            ICargoGroupService cargoGroupService,
            IIcumDownloadsRepository icumDownloadsRepo,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration,
            ILogger<CargoGroupController> logger,
            IGroupResolver groupResolver,
            ApplicationDbContext appDb,
            IcumDownloadsDbContext icumDb)
        {
            _cargoGroupService = cargoGroupService;
            _icumDownloadsRepo = icumDownloadsRepo;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
            _logger = logger;
            _throttledLogger = new ThrottledLogger(logger, SERVICE_ID);
            _groupResolver = groupResolver;
            _appDb = appDb;
            _icumDb = icumDb;
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

        /// <summary>
        /// Get group identifier (Master BL or Declaration Number) for a container
        /// </summary>
        [HttpGet("by-container/{containerNumber}")]
        public async Task<ActionResult<CargoGroupIdentifierDto>> GetGroupIdentifierByContainer(string containerNumber)
        {
            try
            {
                _throttledLogger.LogInfo("GetGroupIdentifierByContainer", "Getting group identifier for container: {Container}",
                    new { Container = containerNumber });

                // ✅ FALLBACK FIX: Get all BOE documents for this container (including RawJsonData)
                var allBoeDocuments = await _icumDownloadsRepo.GetBOEDocumentsByContainerNumberAsync(containerNumber);

                if (!allBoeDocuments.Any())
                {
                    return NotFound(new { message = $"No BOE document found for container: {containerNumber}" });
                }

                // Try consolidated first (by Master BL)
                var consolidatedDoc = allBoeDocuments
                    .FirstOrDefault(b => b.IsConsolidated && !string.IsNullOrEmpty(b.BlNumber));

                if (consolidatedDoc != null)
                {
                    return Ok(new CargoGroupIdentifierDto
                    {
                        GroupIdentifier = consolidatedDoc.BlNumber ?? "",
                        Type = CargoType.Consolidated
                    });
                }

                // ✅ FALLBACK: Try to extract BlNumber from RawJsonData for consolidated cargo
                if (allBoeDocuments.Any(b => b.IsConsolidated))
                {
                    var consolidatedWithJson = allBoeDocuments.FirstOrDefault(b => b.IsConsolidated && !string.IsNullOrWhiteSpace(b.RawJsonData));
                    if (consolidatedWithJson != null)
                    {
                        var (blNumber, _) = ExtractGroupingFieldsFromRawJson(consolidatedWithJson.RawJsonData);
                        if (!string.IsNullOrWhiteSpace(blNumber))
                        {
                            _logger.LogInformation("Extracted BlNumber from RawJsonData for container {Container}: {BlNumber}",
                                containerNumber, blNumber);
                            return Ok(new CargoGroupIdentifierDto
                            {
                                GroupIdentifier = blNumber,
                                Type = CargoType.Consolidated
                            });
                        }
                    }
                }

                // Try non-consolidated (by Declaration Number)
                var nonConsolidatedDoc = allBoeDocuments
                    .FirstOrDefault(b => !b.IsConsolidated && !string.IsNullOrEmpty(b.DeclarationNumber));

                if (nonConsolidatedDoc != null)
                {
                    return Ok(new CargoGroupIdentifierDto
                    {
                        GroupIdentifier = nonConsolidatedDoc.DeclarationNumber ?? "",
                        Type = CargoType.NonConsolidated
                    });
                }

                // ✅ FALLBACK: Try to extract DeclarationNumber from RawJsonData for non-consolidated cargo
                var nonConsolidatedWithJson = allBoeDocuments
                    .FirstOrDefault(b => !b.IsConsolidated && !string.IsNullOrWhiteSpace(b.RawJsonData));

                if (nonConsolidatedWithJson != null)
                {
                    var (_, declarationNumber) = ExtractGroupingFieldsFromRawJson(nonConsolidatedWithJson.RawJsonData);
                    if (!string.IsNullOrWhiteSpace(declarationNumber))
                    {
                        _logger.LogInformation("Extracted DeclarationNumber from RawJsonData for container {Container}: {DeclarationNumber}",
                            containerNumber, declarationNumber);
                        return Ok(new CargoGroupIdentifierDto
                        {
                            GroupIdentifier = declarationNumber,
                            Type = CargoType.NonConsolidated
                        });
                    }
                }

                // ✅ FALLBACK: Try any document with RawJsonData (don't check IsConsolidated flag)
                foreach (var doc in allBoeDocuments.Where(b => !string.IsNullOrWhiteSpace(b.RawJsonData)))
                {
                    var (blNumber, declNumber) = ExtractGroupingFieldsFromRawJson(doc.RawJsonData);

                    // Prioritize BlNumber (consolidated) over DeclarationNumber
                    if (!string.IsNullOrWhiteSpace(blNumber))
                    {
                        _logger.LogInformation("Extracted BlNumber from RawJsonData (any document) for container {Container}: {BlNumber}",
                            containerNumber, blNumber);
                        return Ok(new CargoGroupIdentifierDto
                        {
                            GroupIdentifier = blNumber,
                            Type = CargoType.Consolidated
                        });
                    }

                    if (!string.IsNullOrWhiteSpace(declNumber))
                    {
                        _logger.LogInformation("Extracted DeclarationNumber from RawJsonData (any document) for container {Container}: {DeclarationNumber}",
                            containerNumber, declNumber);
                        return Ok(new CargoGroupIdentifierDto
                        {
                            GroupIdentifier = declNumber,
                            Type = CargoType.NonConsolidated
                        });
                    }
                }

                return NotFound(new { message = $"No cargo group found for container: {containerNumber}. BlNumber and DeclarationNumber are missing in both columns and RawJsonData." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting group identifier for container: {Container}", containerNumber);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get complete cargo group by identifier (Master BL for consolidated, Declaration Number for non-consolidated)
        /// If the identifier looks like a container number, it will be automatically resolved to the group identifier
        /// </summary>
        [HttpGet("{groupIdentifier}")]
        public async Task<ActionResult<CargoGroupDto>> GetCargoGroup(
            string groupIdentifier,
            [FromQuery] CargoType? type = null,
            [FromQuery] bool loadScannerData = true,
            [FromQuery] bool loadImageData = true,
            [FromQuery] bool loadICUMSData = true)
        {
            try
            {
                _throttledLogger.LogInfo("GetCargoGroup", "Getting cargo group for identifier: {Identifier}, Type: {Type}",
                    new { Identifier = groupIdentifier, Type = type });

                // ✅ FIX: Auto-resolve container numbers to group identifiers
                // If the identifier looks like a container number (starts with letters and has digits), try to resolve it
                string actualGroupIdentifier = groupIdentifier;
                CargoType? actualType = type;

                // Wave-AG normalization: dialog passes wave-suffixed identifiers like
                // "10326204603_W1" straight from the assignment table. The cargo
                // service keys ICUMS/BOE data on the underlying BL/declaration, so
                // map wave gids to the AG's NormalizedGroupIdentifier before any
                // other resolution. Without this, the Summary tab is empty for wave
                // AGs whose RCS link wasn't set at creation (Timeout/AutoClose waves).
                if (groupIdentifier.Contains("_W"))
                {
                    var normalizedFromAg = await _appDb.AnalysisGroups
                        .AsNoTracking()
                        .Where(g => g.GroupIdentifier == groupIdentifier)
                        .Select(g => g.NormalizedGroupIdentifier)
                        .FirstOrDefaultAsync();
                    if (!string.IsNullOrWhiteSpace(normalizedFromAg) && normalizedFromAg != groupIdentifier)
                    {
                        _logger.LogInformation("Normalized wave group identifier {Wave} -> {Normalized}",
                            groupIdentifier, normalizedFromAg);
                        actualGroupIdentifier = normalizedFromAg;
                    }
                }

                // Check if it might be a container number (format: 4 letters + digits, e.g., MSKU4670840)
                if (groupIdentifier.Length >= 4 &&
                    char.IsLetter(groupIdentifier[0]) &&
                    char.IsLetter(groupIdentifier[1]) &&
                    char.IsLetter(groupIdentifier[2]) &&
                    char.IsLetter(groupIdentifier[3]))
                {
                    // ✅ FIX: Try to resolve container number to group identifier
                    // First, get ALL BOE documents for this container (regardless of IsConsolidated flag)
                    // This handles cases where the flag might be incorrect or missing
                    var allBoeDocuments = await _icumDownloadsRepo.GetBOEDocumentsByContainerNumberAsync(groupIdentifier);

                    if (allBoeDocuments.Any())
                    {
                        // ✅ FIX: Prioritize documents with BlNumber (consolidated) over DeclarationNumber
                        // Check for consolidated cargo first (has BlNumber)
                        var consolidatedDoc = allBoeDocuments
                            .FirstOrDefault(b => !string.IsNullOrEmpty(b.BlNumber));

                        if (consolidatedDoc != null)
                        {
                            actualGroupIdentifier = consolidatedDoc.BlNumber ?? "";
                            actualType = CargoType.Consolidated;
                            _logger.LogInformation("Auto-resolved container number {Container} to Master BL {GroupIdentifier} (Type: Consolidated)",
                                groupIdentifier, actualGroupIdentifier);
                        }
                        else
                        {
                            // ✅ FALLBACK: Try to extract BlNumber from RawJsonData
                            var consolidatedWithJson = allBoeDocuments
                                .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.RawJsonData));

                            if (consolidatedWithJson != null)
                            {
                                var (blNumber, _) = ExtractGroupingFieldsFromRawJson(consolidatedWithJson.RawJsonData);
                                if (!string.IsNullOrWhiteSpace(blNumber))
                                {
                                    actualGroupIdentifier = blNumber;
                                    actualType = CargoType.Consolidated;
                                    _logger.LogInformation("Auto-resolved container number {Container} to Master BL {GroupIdentifier} from RawJsonData (Type: Consolidated)",
                                        groupIdentifier, actualGroupIdentifier);
                                }
                            }
                        }

                        // If still not resolved, try non-consolidated (has DeclarationNumber)
                        if (string.IsNullOrEmpty(actualGroupIdentifier) || actualGroupIdentifier == groupIdentifier)
                        {
                            var nonConsolidatedDoc = allBoeDocuments
                                .FirstOrDefault(b => !string.IsNullOrEmpty(b.DeclarationNumber));

                            if (nonConsolidatedDoc != null)
                            {
                                actualGroupIdentifier = nonConsolidatedDoc.DeclarationNumber ?? "";
                                actualType = CargoType.NonConsolidated;
                                _logger.LogInformation("Auto-resolved container number {Container} to Declaration Number {GroupIdentifier} (Type: NonConsolidated)",
                                    groupIdentifier, actualGroupIdentifier);
                            }
                            else
                            {
                                // ✅ FALLBACK: Try to extract DeclarationNumber from RawJsonData
                                var nonConsolidatedWithJson = allBoeDocuments
                                    .FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.RawJsonData));

                                if (nonConsolidatedWithJson != null)
                                {
                                    var (_, declarationNumber) = ExtractGroupingFieldsFromRawJson(nonConsolidatedWithJson.RawJsonData);
                                    if (!string.IsNullOrWhiteSpace(declarationNumber))
                                    {
                                        actualGroupIdentifier = declarationNumber;
                                        actualType = CargoType.NonConsolidated;
                                        _logger.LogInformation("Auto-resolved container number {Container} to Declaration Number {GroupIdentifier} from RawJsonData (Type: NonConsolidated)",
                                            groupIdentifier, actualGroupIdentifier);
                                    }
                                }
                            }
                        }

                        // If still not resolved, log warning
                        if (string.IsNullOrEmpty(actualGroupIdentifier) || actualGroupIdentifier == groupIdentifier)
                        {
                            _logger.LogWarning("Container number {Container} has BOE documents but no BlNumber or DeclarationNumber found in columns or RawJsonData",
                                groupIdentifier);
                        }
                    }
                    else
                    {
                        _logger.LogWarning("No BOE documents found for container number {Container}", groupIdentifier);
                    }
                }

                var group = await _cargoGroupService.GetCargoGroupAsync(
                    actualGroupIdentifier,
                    actualType,
                    loadScannerData: loadScannerData,
                    loadImageData: loadImageData,
                    loadICUMSData: loadICUMSData);

                if (group == null)
                {
                    _logger.LogWarning("Cargo group not found for identifier: {Identifier} (original: {Original})",
                        actualGroupIdentifier, groupIdentifier);
                    return NotFound(new { message = $"Cargo group not found for identifier: {groupIdentifier}" });
                }

                _throttledLogger.LogInfo("GetCargoGroup", "Found cargo group: {Type}, Containers: {Containers}, HouseBLs: {HouseBLs}",
                    new { Type = group.Type, Containers = group.TotalContainers, HouseBLs = group.TotalHouseBLs });

                return Ok(group);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cargo group for identifier: {Identifier}", groupIdentifier);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get all data for a cargo group (ICUMS, Scanner, Images)
        /// </summary>
        [HttpGet("{groupIdentifier}/data")]
        public async Task<ActionResult<CargoGroupDataDto>> GetCargoGroupData(
            string groupIdentifier,
            [FromQuery] CargoType type,
            [FromQuery] bool loadScannerData = true,
            [FromQuery] bool loadImageData = true,
            [FromQuery] bool loadICUMSData = true)
        {
            var apiStopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                _logger.LogInformation("⏱️ [CargoGroupController] GetCargoGroupData START - Identifier: {Identifier}, Type: {Type}, Scanner: {Scanner}, Image: {Image}, ICUMS: {ICUMS}",
                    groupIdentifier, type, loadScannerData, loadImageData, loadICUMSData);

                var data = await _cargoGroupService.GetCargoGroupDataAsync(
                    groupIdentifier,
                    type,
                    loadScannerData: loadScannerData,
                    loadImageData: loadImageData,
                    loadICUMSData: loadICUMSData);

                apiStopwatch.Stop();
                _logger.LogInformation("⏱️ [CargoGroupController] GetCargoGroupData COMPLETE - Total API time: {Time}ms ({TotalSeconds:F2}s)",
                    apiStopwatch.ElapsedMilliseconds, apiStopwatch.Elapsed.TotalSeconds);

                return Ok(data);
            }
            catch (Exception ex)
            {
                apiStopwatch.Stop();
                _logger.LogError(ex, "⏱️ [CargoGroupController] GetCargoGroupData ERROR after {Time}ms for identifier: {Identifier}",
                    apiStopwatch.ElapsedMilliseconds, groupIdentifier);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get ICUMS data only for a cargo group
        /// </summary>
        [HttpGet("{groupIdentifier}/icums")]
        public async Task<ActionResult<List<ICUMSDataGroupDto>>> GetCargoGroupICUMS(
            string groupIdentifier,
            [FromQuery] CargoType type)
        {
            try
            {
                var data = await _cargoGroupService.GetCargoGroupDataAsync(groupIdentifier, type);
                return Ok(data.ICUMSData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ICUMS data for cargo group: {Identifier}", groupIdentifier);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get scanner data only for a cargo group
        /// </summary>
        [HttpGet("{groupIdentifier}/scanner")]
        public async Task<ActionResult<List<ScannerDataGroupDto>>> GetCargoGroupScanner(
            string groupIdentifier,
            [FromQuery] CargoType type)
        {
            try
            {
                var data = await _cargoGroupService.GetCargoGroupDataAsync(groupIdentifier, type);
                return Ok(data.ScannerData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting scanner data for cargo group: {Identifier}", groupIdentifier);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get images only for a cargo group
        /// </summary>
        [HttpGet("{groupIdentifier}/images")]
        public async Task<ActionResult<List<ImageDataGroupDto>>> GetCargoGroupImages(
            string groupIdentifier,
            [FromQuery] CargoType type)
        {
            try
            {
                var data = await _cargoGroupService.GetCargoGroupDataAsync(groupIdentifier, type);
                return Ok(data.ImageData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting images for cargo group: {Identifier}", groupIdentifier);
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        /// <summary>
        /// Get list of cargo groups (summary) with filtering
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<CargoGroupSummaryDto>>> GetCargoGroups(
            [FromQuery] CargoType? type = null,
            [FromQuery] string? clearanceType = null,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50)
        {
            try
            {
                _throttledLogger.LogInfo("GetCargoGroups", "Getting cargo groups list - Type: {Type}, ClearanceType: {ClearanceType}, Page: {Page}",
                    new { Type = type, ClearanceType = clearanceType, Page = page });

                var groups = await _cargoGroupService.GetCargoGroupsAsync(type, clearanceType, page, pageSize);

                _throttledLogger.LogInfo("GetCargoGroups", "Found {Count} cargo groups", groups.Count);

                return Ok(groups);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting cargo groups list");
                return StatusCode(500, new { message = "Internal server error", error = ex.Message });
            }
        }

        // ─────────────────────────────────────────────────────────────────────
        // Phase 1 — Server-side disambiguation endpoint (Theme E follow-up).
        // See docs/audit/2026-05-05/follow-up-routing-endpoint-design.md
        //
        // Composes existing services (ICargoGroupService, IIcumDownloadsRepository,
        // ApplicationDbContext.ImageAnalysisDecisions / FS6000Scans / AseScans) — no
        // new SQL. Backend ships dark in Phase 1; the dialog still uses the legacy
        // 5-callsite `IsConsolidated` branch. Phase 2 cuts over after this is
        // verified.
        // ─────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Phase 1 — Single-round-trip aggregator that returns server-resolved
        /// identity + 4 tab-shaped payloads for the Image Analysis dialog.
        /// Replaces the dialog's frontend `IsConsolidated` branching.
        /// </summary>
        [HttpGet("{groupIdentifier}/full")]
        [ProducesResponseType(typeof(CargoGroupFullDto), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(CargoGroupFullDto), StatusCodes.Status404NotFound)]
        public async Task<ActionResult<CargoGroupFullDto>> GetFull(
            string groupIdentifier,
            [FromQuery] string? scannerType = null,
            [FromQuery] bool includeImages = true,
            [FromQuery] bool includeScannerData = true,
            [FromQuery] bool includeIcums = true,
            [FromQuery] int pageSize = 1000,
            CancellationToken ct = default)
        {
            // Phase 1 — Information-level log on every call so we can monitor rollout
            // when CargoGroup:UseFullEndpoint is flipped to true.
            _logger.LogInformation(
                "[CARGO-GROUP-FULL] groupIdentifier={GroupIdentifier} scannerType={ScannerType} includeImages={IncludeImages} includeScanner={IncludeScanner} includeIcums={IncludeIcums} flag={FlagOn}",
                groupIdentifier,
                scannerType,
                includeImages,
                includeScannerData,
                includeIcums,
                _configuration.GetValue<bool>("CargoGroup:UseFullEndpoint", false));

            try
            {
                // 1) Resolve identity (6-step dispatch from §3.1).
                var resolution = await _groupResolver.ResolveAsync(groupIdentifier, scannerType, ct);

                if (resolution.Status == CargoGroupResolutionStatus.GroupIdentifierUnknown)
                {
                    _logger.LogInformation("[CARGO-GROUP-FULL] groupIdentifier={GroupIdentifier} status=GroupIdentifierUnknown", groupIdentifier);
                    return NotFound(new CargoGroupFullDto
                    {
                        Status = resolution.Status,
                        Diagnostics = resolution.Diagnostics
                    });
                }

                if (resolution.Status == CargoGroupResolutionStatus.AmbiguousNeedsHint)
                {
                    _logger.LogInformation("[CARGO-GROUP-FULL] groupIdentifier={GroupIdentifier} status=AmbiguousNeedsHint", groupIdentifier);
                    return Ok(new CargoGroupFullDto
                    {
                        Status = resolution.Status,
                        Diagnostics = resolution.Diagnostics
                    });
                }

                // 2) Fan out to existing services in parallel.
                //    Each branch uses _cargoGroupService / direct EF reads.

                // The lookup key for ICargoGroupService: prefer DeclarationNumber,
                // fall back to MasterBlNumber, finally to the ContainerGroupKey/CN
                // from the resolution.
                var primaryKey = !string.IsNullOrWhiteSpace(resolution.DeclarationNumber)
                    ? resolution.DeclarationNumber
                    : (resolution.MasterBlNumber
                        ?? resolution.ContainerGroupKey
                        ?? resolution.ContainerNumbers.FirstOrDefault()
                        ?? groupIdentifier);

                var cargoTypeHint = resolution.Mode switch
                {
                    CargoGroupingMode.ConsolidatedMultiHouseBl => (CargoType?)CargoType.Consolidated,
                    CargoGroupingMode.SingleDeclarationSingleContainer => CargoType.NonConsolidated,
                    CargoGroupingMode.SingleDeclarationMultipleContainers => CargoType.NonConsolidated,
                    CargoGroupingMode.PatternAUsedCars => CargoType.NonConsolidated,
                    _ => null
                };

                var summaryTask = _cargoGroupService.GetCargoGroupAsync(
                    primaryKey!,
                    cargoTypeHint,
                    loadScannerData: includeScannerData,
                    loadImageData: includeImages,
                    loadICUMSData: includeIcums);

                var aiSummaryTask = _cargoGroupService.GetAiCargoSummaryAsync(primaryKey!);

                var imageDecisionsTask = LoadImageDecisionsAsync(resolution, scannerType, ct);

                await Task.WhenAll(summaryTask, aiSummaryTask, imageDecisionsTask);

                var cargoGroup = await summaryTask;
                var aiSummary = await aiSummaryTask;
                var imageDecisions = await imageDecisionsTask;

                // 3) Build the unified response.
                var containerNumbers = (cargoGroup?.ContainerNumbers?.Count > 0)
                    ? (IReadOnlyList<string>)cargoGroup.ContainerNumbers
                    : resolution.ContainerNumbers;

                var hbls = (cargoGroup?.HouseBLGroups ?? new List<HouseBLGroupDto>())
                    .SelectMany(hbg => (hbg.BOEDetails ?? new List<BOEDetailDto>())
                        .Select(d => new HouseBLDetail
                        {
                            HouseBl = hbg.HouseBL ?? "",
                            MasterBl = hbg.MasterBL,
                            DeclarationNumber = hbg.DeclarationNumber ?? d.DeclarationNumber,
                            ConsigneeName = hbg.ConsigneeName ?? d.ConsigneeName,
                            ClearanceType = hbg.ClearanceType ?? d.ClearanceType,
                            RotationNumber = d.RotationNumber,
                            GoodsDescription = d.GoodsDescription,
                            TotalDutyPaid = d.TotalDutyPaid,
                            DeclarationDate = d.DeclarationDate,
                            BoeId = d.BOEId,
                            ContainerNumber = d.ContainerNumber
                        }))
                    .ToList();

                var icumsGroups = cargoGroup?.Data?.ICUMSData ?? new List<ICUMSDataGroupDto>();
                var scannerGroups = cargoGroup?.Data?.ScannerData ?? new List<ScannerDataGroupDto>();
                var imageGroups = cargoGroup?.Data?.ImageData ?? new List<ImageDataGroupDto>();

                var totalImagesFromGroups = imageGroups.Sum(g => g.ImageCount);
                var totalIcumsRecords = icumsGroups.Sum(g => g.Records?.Count ?? 0);
                var totalScannerRecords = scannerGroups.Sum(g => g.Records?.Count ?? 0);

                // Compute final status — Found if cargoGroup populated, FoundButPartial otherwise.
                var finalStatus = (cargoGroup != null)
                    ? CargoGroupResolutionStatus.Found
                    : CargoGroupResolutionStatus.FoundButPartial;

                if (resolution.Status == CargoGroupResolutionStatus.FoundButPartial)
                {
                    // Honour the resolver's verdict if it had less information than us.
                    finalStatus = CargoGroupResolutionStatus.FoundButPartial;
                }

                var dto = new CargoGroupFullDto
                {
                    AnalysisGroupId = resolution.AnalysisGroupId,
                    RecordCompletenessStatusId = resolution.RecordCompletenessStatusId,
                    DeclarationNumber = resolution.DeclarationNumber ?? primaryKey ?? "",
                    MasterBlNumber = resolution.MasterBlNumber,
                    ContainerGroupKey = resolution.ContainerGroupKey,
                    GroupingMode = resolution.Mode,
                    ScannerType = resolution.ScannerType ?? scannerType,
                    ClearanceType = resolution.ClearanceType ?? cargoGroup?.ClearanceType,
                    RegimeCode = resolution.RegimeCode,
                    ContainerNumbers = containerNumbers,
                    HouseBls = hbls,
                    ScannerData = new ScannerDataPayload
                    {
                        Groups = scannerGroups,
                        TotalRecords = totalScannerRecords
                    },
                    IcumsData = new IcumsDataPayload
                    {
                        Groups = icumsGroups,
                        TotalRecords = totalIcumsRecords
                    },
                    ImageDecisions = imageDecisions,
                    Summary = new CargoSummaryPayload
                    {
                        TotalContainers = cargoGroup?.TotalContainers ?? containerNumbers.Count,
                        TotalHouseBls = cargoGroup?.TotalHouseBLs ?? hbls.Count,
                        TotalBoes = cargoGroup?.TotalBOEs ?? 0,
                        TotalIcumsRecords = totalIcumsRecords,
                        TotalScannerRecords = totalScannerRecords,
                        TotalImages = totalImagesFromGroups + imageDecisions.TotalImages,
                        LatestUpdateDate = cargoGroup?.LatestUpdateDate,
                        AiCargoSummary = aiSummary,
                        ConsigneeName = cargoGroup?.ConsigneeName
                    },
                    Status = finalStatus,
                    Diagnostics = resolution.Diagnostics
                };

                _logger.LogInformation(
                    "[CARGO-GROUP-FULL] groupIdentifier={GroupIdentifier} resolved mode={Mode} ag={AnalysisGroupId} rcs={RcsId} containers={Containers} status={Status}",
                    groupIdentifier,
                    dto.GroupingMode,
                    dto.AnalysisGroupId,
                    dto.RecordCompletenessStatusId,
                    dto.ContainerNumbers.Count,
                    dto.Status);

                return Ok(dto);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CARGO-GROUP-FULL] error for groupIdentifier={GroupIdentifier}", groupIdentifier);
                return StatusCode(500, new CargoGroupFullDto
                {
                    Status = CargoGroupResolutionStatus.FoundButPartial,
                    Diagnostics = new[] { $"unhandled-error: {ex.GetType().Name}" }
                });
            }
        }

        /// <summary>
        /// Builds the Image Decisions tab payload by reading
        /// <see cref="ApplicationDbContext.ImageAnalysisDecisions"/> directly for the
        /// resolved container set. Mirrors the existing dialog's RefreshOverallDecision logic.
        /// </summary>
        private async Task<ImageDecisionsPayload> LoadImageDecisionsAsync(
            GroupResolution resolution,
            string? scannerType,
            CancellationToken ct)
        {
            var containers = resolution.ContainerNumbers;
            if (containers == null || containers.Count == 0)
            {
                return new ImageDecisionsPayload();
            }

            var decisions = await _appDb.ImageAnalysisDecisions
                .AsNoTracking()
                .Where(d => containers.Contains(d.ContainerNumber))
                .Where(d => scannerType == null || d.ScannerType == scannerType)
                .OrderByDescending(d => d.ReviewedAt)
                .ToListAsync(ct);

            // Latest decision per container (per scanner if a hint was given).
            var latestPerContainer = decisions
                .GroupBy(d => $"{d.ContainerNumber}|{d.ScannerType}")
                .Select(g => g.First())
                .ToList();

            var normal = latestPerContainer.Count(d => string.Equals(d.Decision, "Normal", StringComparison.OrdinalIgnoreCase));
            var abnormal = latestPerContainer.Count(d => string.Equals(d.Decision, "Abnormal", StringComparison.OrdinalIgnoreCase));
            var totalImages = latestPerContainer.Count;
            var lastReviewedBy = latestPerContainer
                .OrderByDescending(d => d.ReviewedAt)
                .Select(d => d.ReviewedBy)
                .FirstOrDefault();

            string? overallDecision = null;
            if (totalImages > 0)
            {
                if (abnormal > 0)
                {
                    overallDecision = "Abnormal";
                }
                else if (normal == totalImages)
                {
                    overallDecision = "Normal";
                }
                else
                {
                    overallDecision = "Mixed";
                }
            }

            var rows = latestPerContainer.Select(d => new ContainerDecisionDto
            {
                ContainerNumber = d.ContainerNumber,
                ScannerType = d.ScannerType,
                Decision = d.Decision,
                Comments = d.Comments,
                Tags = d.Tags,
                ReviewedBy = d.ReviewedBy,
                ReviewedAt = d.ReviewedAt
            }).ToList();

            return new ImageDecisionsPayload
            {
                OverallDecision = overallDecision,
                TotalImages = totalImages,
                NormalCount = normal,
                AbnormalCount = abnormal,
                LastReviewedBy = lastReviewedBy,
                ContainerDecisions = rows
            };
        }

        /// <summary>
        /// 1.12.0 — universal BOE / cargo lookup. Searches across container number,
        /// declaration number, BL number, master BL, house BL, rotation number, and
        /// VIN (vehicle imports). Case-insensitive ILIKE; returns up to <paramref name="limit"/>
        /// matches with the field that matched annotated on each row.
        ///
        /// Available to ImageAnalyst and above. Powers /validation/boe-lookup and the
        /// "Find related BOEs" cross-link from /validation/match-corrections.
        /// </summary>
        [HttpGet("lookup")]
        [Authorize(Policy = "ImageAnalyst")]
        public async Task<ActionResult<CargoLookupResponse>> CargoLookup(
            [FromQuery] string q,
            [FromQuery] int limit = 50)
        {
            if (string.IsNullOrWhiteSpace(q) || q.Trim().Length < 3)
            {
                return BadRequest(new { message = "Search term must be at least 3 characters." });
            }

            limit = Math.Clamp(limit, 1, 200);
            var trimmed = q.Trim();
            try
            {
                _logger.LogInformation("[CARGO-LOOKUP] q='{Q}', limit={Limit}", trimmed, limit);

                // BOE document matches across the canonical identifier columns.
                var results = await _icumDownloadsRepo.SearchCargoAsync(trimmed, limit);

                return Ok(new CargoLookupResponse
                {
                    Query = trimmed,
                    TotalReturned = results.Count,
                    Limit = limit,
                    Truncated = results.Count >= limit,
                    Results = results,
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CARGO-LOOKUP] failed for q='{Q}'", trimmed);
                return StatusCode(500, new { message = "Lookup failed", error = ex.Message });
            }
        }

        /// <summary>
        /// Get a previously saved AI cargo summary (if one exists).
        /// </summary>
        [HttpGet("{groupIdentifier}/ai-summary")]
        public async Task<ActionResult<AiCargoSummaryResponse>> GetAiSummary(string groupIdentifier)
        {
            var summary = await _cargoGroupService.GetAiCargoSummaryAsync(groupIdentifier);
            return Ok(new AiCargoSummaryResponse { Summary = summary ?? "" });
        }

        /// <summary>
        /// Generate an AI summary of cargo line item descriptions using local Ollama.
        /// </summary>
        [HttpPost("{groupIdentifier}/ai-summary")]
        public async Task<ActionResult<AiCargoSummaryResponse>> GenerateAiSummary(
            string groupIdentifier,
            [FromBody] AiCargoSummaryRequest request)
        {
            try
            {
                if (request.LineItems == null || !request.LineItems.Any())
                    return BadRequest(new { error = "No line items provided" });

                var ollamaUrl = _configuration["AiWorkflow:OllamaBaseUrl"] ?? "http://localhost:11434";
                var modelId = _configuration["AiWorkflow:OllamaTextModelId"]
                    ?? _configuration["AiWorkflow:OllamaModelId"]
                    ?? "llava:7b";

                // Build the prompt with all line item data
                var sb = new StringBuilder();
                sb.AppendLine("You are a customs cargo analyst. Based on the following manifest line items for a cargo shipment, generate a concise 2-3 sentence summary describing what the cargo contains.");
                sb.AppendLine("Focus on: types of goods, key quantities, weights, and countries of origin. Be specific and factual.");
                sb.AppendLine();
                sb.AppendLine("Line Items:");
                foreach (var item in request.LineItems)
                {
                    sb.Append($"- {item.Description ?? "Unknown"}");
                    if (!string.IsNullOrEmpty(item.HsCode)) sb.Append($" (HS: {item.HsCode})");
                    if (!string.IsNullOrEmpty(item.Quantity)) sb.Append($", Qty: {item.Quantity}");
                    if (!string.IsNullOrEmpty(item.Weight)) sb.Append($", Weight: {item.Weight}");
                    if (!string.IsNullOrEmpty(item.CountryOfOrigin)) sb.Append($", Origin: {item.CountryOfOrigin}");
                    sb.AppendLine();
                }
                sb.AppendLine();
                sb.AppendLine("Generate a concise summary:");

                var prompt = sb.ToString();
                _logger.LogInformation("[AI-SUMMARY] Generating summary for {Group} with {Count} line items via Ollama ({Model})",
                    groupIdentifier, request.LineItems.Count, modelId);

                // Call Ollama
                var client = _httpClientFactory.CreateClient();
                var ollamaRequest = new
                {
                    model = modelId,
                    prompt = prompt,
                    stream = false
                };

                var jsonContent = new StringContent(
                    JsonSerializer.Serialize(ollamaRequest), Encoding.UTF8, "application/json");

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
                var response = await client.PostAsync($"{ollamaUrl}/api/generate", jsonContent, cts.Token);

                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.LogWarning("[AI-SUMMARY] Ollama returned {Status}: {Body}", response.StatusCode, errorBody);
                    return StatusCode(502, new { error = $"Ollama returned {response.StatusCode}" });
                }

                var responseJson = await response.Content.ReadAsStringAsync();
                var ollamaResponse = JsonSerializer.Deserialize<JsonElement>(responseJson);
                var summaryText = ollamaResponse.TryGetProperty("response", out var respProp)
                    ? respProp.GetString()?.Trim()
                    : null;

                if (string.IsNullOrWhiteSpace(summaryText))
                {
                    return Ok(new AiCargoSummaryResponse { Summary = "Unable to generate summary." });
                }

                _logger.LogInformation("[AI-SUMMARY] Generated summary for {Group}: {Summary}",
                    groupIdentifier, summaryText.Length > 100 ? summaryText[..100] + "..." : summaryText);

                // Persist the summary to the AnalysisGroup for reuse
                try
                {
                    await _cargoGroupService.SaveAiCargoSummaryAsync(groupIdentifier, summaryText);
                    _logger.LogInformation("[AI-SUMMARY] Saved summary for {Group}", groupIdentifier);
                }
                catch (Exception saveEx)
                {
                    _logger.LogWarning(saveEx, "[AI-SUMMARY] Could not persist summary to DB for {Group}", groupIdentifier);
                }

                return Ok(new AiCargoSummaryResponse { Summary = summaryText });
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("[AI-SUMMARY] Ollama request timed out for {Group}", groupIdentifier);
                return StatusCode(504, new { error = "AI summary generation timed out" });
            }
            catch (HttpRequestException ex)
            {
                _logger.LogWarning(ex, "[AI-SUMMARY] Cannot reach Ollama for {Group}", groupIdentifier);
                return StatusCode(503, new { error = "Ollama service unavailable" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[AI-SUMMARY] Error generating summary for {Group}", groupIdentifier);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        public sealed class AiCargoSummaryRequest
        {
            public List<AiLineItem> LineItems { get; set; } = new();
        }

        public sealed class AiLineItem
        {
            public string? Description { get; set; }
            public string? HsCode { get; set; }
            public string? Quantity { get; set; }
            public string? Weight { get; set; }
            public string? CountryOfOrigin { get; set; }
        }

        public sealed class AiCargoSummaryResponse
        {
            public string Summary { get; set; } = string.Empty;
        }

        // ── BOE / cargo lookup DTOs (1.12.0) ─────────────────────────────────
        public sealed class CargoLookupResponse
        {
            public string Query { get; set; } = string.Empty;
            public int TotalReturned { get; set; }
            public int Limit { get; set; }
            public bool Truncated { get; set; }
            public List<CargoLookupRowDto> Results { get; set; } = new();
        }

    }
}
