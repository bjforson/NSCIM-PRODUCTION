using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NickScanCentralImagingPortal.API.Logging;
using NickScanCentralImagingPortal.Core.DTOs.CargoGroup;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Core.Models;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NLog;

namespace NickScanCentralImagingPortal.API.Controllers
{
    // Models for Container Details API
    public class ScannerDataRecord
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public DateTime? Timestamp { get; set; }
    }

    public class ICUMSDataRecord
    {
        public string Field { get; set; } = string.Empty;
        public string Value { get; set; } = string.Empty;
        public string Category { get; set; } = string.Empty;
        public bool IsRequired { get; set; }
        public string? HouseBL { get; set; } // For grouping consolidated cargo by House BL
    }

    public class PagedResult<T>
    {
        public List<T> Data { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages { get; set; }
        public bool HasPreviousPage => Page > 1;
        public bool HasNextPage => Page < TotalPages;
    }

    // DTOs for Phase 2: Full record responses
    /// <summary>
    /// Full scanner data record with all fields in dictionary format
    /// Returned when ?full=true parameter is used on scanner endpoint
    /// </summary>
    public class FullScannerDataRecordDto
    {
        /// <summary>
        /// Container number
        /// </summary>
        public string ContainerNumber { get; set; } = string.Empty;

        /// <summary>
        /// Scanner type (e.g., "ASE", "FS6000")
        /// </summary>
        public string ScannerType { get; set; } = string.Empty;

        /// <summary>
        /// Scan timestamp
        /// </summary>
        public DateTime ScanTime { get; set; }

        /// <summary>
        /// Dictionary of all scanner fields with their values
        /// Key: Field name (e.g., "Container Number", "Scanner Type")
        /// Value: Field value as object
        /// </summary>
        public Dictionary<string, object> AllFields { get; set; } = new();

        /// <summary>
        /// List of available field names
        /// </summary>
        public List<string> AvailableFields { get; set; } = new();

        /// <summary>
        /// List of missing/expected fields that are not available
        /// </summary>
        public List<string> MissingFields { get; set; } = new();
    }

    /// <summary>
    /// Full BOE (ICUMS) data record with all fields in dictionary format
    /// Returned when ?full=true parameter is used on ICUMS endpoint
    /// </summary>
    public class FullBOEDataRecordDto
    {
        /// <summary>
        /// Container number
        /// </summary>
        public string ContainerNumber { get; set; } = string.Empty;

        /// <summary>
        /// Declaration number (if available)
        /// </summary>
        public string? DeclarationNumber { get; set; }

        /// <summary>
        /// BOE number (if available)
        /// </summary>
        public string? BOENumber { get; set; }

        /// <summary>
        /// Rotation number (if available)
        /// </summary>
        public string? RotationNumber { get; set; }

        /// <summary>
        /// Consignee name (if available)
        /// </summary>
        public string? ConsigneeName { get; set; }

        /// <summary>
        /// Bill of Lading number (if available)
        /// </summary>
        public string? BlNumber { get; set; }

        /// <summary>
        /// House BL number (for consolidated cargo, if available)
        /// </summary>
        public string? HouseBl { get; set; }

        /// <summary>
        /// Clearance type (e.g., "IM", "EX", if available)
        /// </summary>
        public string? ClearanceType { get; set; }

        /// <summary>
        /// Dictionary of all ICUMS/BOE fields with their values
        /// Key: Field name (e.g., "BOE Number", "Declaration Number")
        /// Value: Field value as object
        /// Note: Duplicate field names from multiple BOE documents are handled by keeping the last value
        /// </summary>
        public Dictionary<string, object> AllFields { get; set; } = new();

        /// <summary>
        /// List of available field names (unique, duplicates removed)
        /// </summary>
        public List<string> AvailableFields { get; set; } = new();

        /// <summary>
        /// List of missing/expected fields that are not available
        /// </summary>
        public List<string> MissingFields { get; set; } = new();
    }

    public class ContainerBasicInfo
    {
        public string ContainerNumber { get; set; } = string.Empty;
        public string ScannerType { get; set; } = string.Empty;
        public int ScannerRecordCount { get; set; }
        public int ICUMSRecordCount { get; set; }
        public int ImageCount { get; set; }
        public DateTime LastUpdated { get; set; }
        public string ValidationStatus { get; set; } = string.Empty;
        public int DataCompletenessScore { get; set; }
    }

    // NOTE: ImageMetadataDto is now imported from NickScanCentralImagingPortal.Core.DTOs.CargoGroup
    // This avoids Swagger schema naming conflicts with the duplicate class name

    public class ContainerFullDetails
    {
        public string ScannerType { get; set; } = string.Empty;
        public DateTime ScanDate { get; set; }
        public string ValidationStatus { get; set; } = string.Empty;
        public int CompletenessScore { get; set; }
        public string ClearanceType { get; set; } = string.Empty;
        public int ImageCount { get; set; }
        public bool HasScannerData { get; set; }
        public bool HasICUMSData { get; set; }
        public string? BOENumber { get; set; }
        public string? Consignee { get; set; }
        public string? OriginPort { get; set; }
        public string? Destination { get; set; }
        public string? VesselName { get; set; }
        public int VehicleCount { get; set; }
        public string? ScanLocation { get; set; }
        public string? Operator { get; set; }
        public string? ContainerSize { get; set; }
    }

    /// <summary>
    /// Container details controller - requires authentication
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class ContainerDetailsController : ControllerBase
    {
        private readonly ThrottledLogger _logger;
        private readonly IContainerDataMapperService _containerDataMapperService;
        private readonly IcumDownloadsDbContext _icumDownloadsDbContext;
        private readonly ApplicationDbContext _context;
        private readonly IImageProcessingService _imageProcessingService;
        private readonly NickScanCentralImagingPortal.Services.ImageProcessing.ASE.IASEImageConverterService _aseConverter;
        private const string SERVICE_ID = "CONTAINER-DETAILS-API";

        private readonly IConfiguration _configuration;

        public ContainerDetailsController(
            ILogger<ContainerDetailsController> logger,
            IContainerDataMapperService containerDataMapperService,
            IcumDownloadsDbContext icumDownloadsDbContext,
            ApplicationDbContext context,
            IImageProcessingService imageProcessingService,
            NickScanCentralImagingPortal.Services.ImageProcessing.ASE.IASEImageConverterService aseConverter,
            IConfiguration configuration)
        {
            _logger = new ThrottledLogger(logger, SERVICE_ID);
            _containerDataMapperService = containerDataMapperService;
            _icumDownloadsDbContext = icumDownloadsDbContext;
            _context = context;
            _imageProcessingService = imageProcessingService;
            _aseConverter = aseConverter;
            _configuration = configuration;
        }

        /// <summary>
        /// Get basic container information (fast, lightweight) - Uses unified pipeline
        /// </summary>
        [HttpGet("basic/{containerNumber}")]
        public async Task<ActionResult<ContainerBasicInfo>> GetBasicInfo(string containerNumber)
        {
            try
            {
                _logger.LogInfo("GetBasicInfo", "Getting basic info for container: {ContainerNumber}", new { ContainerNumber = containerNumber });

                // Step 1: Use Image Processing Pipeline to detect scanner type and locate scanner data
                var scannerType = await _imageProcessingService.DetectScannerTypeAsync(containerNumber);

                int scannerRecordCount = 0;
                int imageCount = 0;
                DateTime? scanDate = null;
                string scannerTypeStr = "Unknown";

                if (scannerType != Core.Interfaces.ScannerType.Unknown)
                {
                    scannerTypeStr = scannerType.ToString();

                    // Get scanner data count based on detected type
                    if (scannerType == Core.Interfaces.ScannerType.FS6000)
                    {
                        var fs6000Scan = await _context.FS6000Scans
                            .Include(s => s.Images)
                            .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                        if (fs6000Scan != null)
                        {
                            scannerRecordCount = 1;
                            imageCount = fs6000Scan.Images?.Count ?? 0;
                            scanDate = fs6000Scan.ScanTime;
                        }
                    }
                    else if (scannerType == Core.Interfaces.ScannerType.ASE)
                    {
                        var aseScan = await _context.AseScans
                            .FirstOrDefaultAsync(s => s.ContainerNumber == containerNumber);

                        if (aseScan != null)
                        {
                            scannerRecordCount = 1;
                            imageCount = (aseScan.ScanImage != null && aseScan.ScanImage.Length > 0) ? 1 : 0;
                            scanDate = aseScan.ScanTime;
                        }
                    }
                }

                // Step 2: Check ICUMS data
                var icumsRecordCount = await _icumDownloadsDbContext.BOEDocuments
                    .Where(b => b.ContainerNumber == containerNumber)
                    .CountAsync();

                var icumsData = await _icumDownloadsDbContext.BOEDocuments
                    .Where(b => b.ContainerNumber == containerNumber)
                    .OrderByDescending(b => b.CreatedAt)
                    .FirstOrDefaultAsync();

                // If no scanner data and no ICUMS data, return 404
                if (scannerRecordCount == 0 && icumsRecordCount == 0)
                {
                    _logger.LogWarning("Container {ContainerNumber} not found in any data source", containerNumber);
                    return NotFound($"Container {containerNumber} not found");
                }

                var basicInfo = new ContainerBasicInfo
                {
                    ContainerNumber = containerNumber,
                    ScannerType = scannerTypeStr,
                    ScannerRecordCount = scannerRecordCount,
                    ICUMSRecordCount = icumsRecordCount,
                    ImageCount = imageCount,
                    LastUpdated = scanDate ?? icumsData?.CreatedAt ?? DateTime.UtcNow,
                    ValidationStatus = scannerRecordCount > 0 && icumsRecordCount > 0 ? "Complete" : "Partial",
                    DataCompletenessScore = CalculateCompletenessScore(scannerRecordCount, icumsRecordCount, imageCount)
                };

                _logger.LogInfo("GetBasicInfo", "Found basic info for container {ContainerNumber} - Scanner: {Scanner}, ICUMS: {ICUMS}, Images: {Images}",
                    new { ContainerNumber = containerNumber, Scanner = scannerRecordCount, ICUMS = icumsRecordCount, Images = imageCount });

                return Ok(basicInfo);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetBasicInfo", "Error getting basic info for container {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        private int CalculateCompletenessScore(int scannerRecords, int icumsRecords, int images)
        {
            int score = 0;
            if (scannerRecords > 0) score += 33;
            if (icumsRecords > 0) score += 34;
            if (images > 0) score += 33;
            return score;
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
                _logger.LogWarning("Failed to parse RawJsonData for grouping fields extraction: {Error}", ex.Message);
                return (null, null);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Unexpected error extracting grouping fields from RawJsonData: {Error}", ex.Message);
                return (null, null);
            }
        }

        /// <summary>
        /// Get full container details including all data (for Container Details page)
        /// </summary>
        [HttpGet("full/{containerNumber}")]
        public async Task<ActionResult<ContainerFullDetails>> GetFullContainerDetails(string containerNumber)
        {
            try
            {
                _logger.LogInfo("GetFullContainerDetails", "Getting full details for container {ContainerNumber}", new { ContainerNumber = containerNumber });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                // Get scanner data from ContainerSubmissionData
                var readyContainers = await _containerDataMapperService.GetContainersReadyForSubmissionAsync(100);
                var scannerContainer = readyContainers.FirstOrDefault(c => c.ContainerNumber == containerNumber);

                // Get ICUMS data from BOEDocuments
                var boeDocument = await _icumDownloadsDbContext.BOEDocuments
                    .Where(b => b.ContainerNumber == containerNumber)
                    .OrderByDescending(b => b.CreatedAt)
                    .FirstOrDefaultAsync(cts.Token);

                if (scannerContainer == null && boeDocument == null)
                {
                    return NotFound($"Container {containerNumber} not found");
                }

                var fullDetails = new ContainerFullDetails
                {
                    ScannerType = scannerContainer?.ScannerType ?? "Unknown",
                    ScanDate = scannerContainer?.ScanDate ?? DateTime.MinValue,
                    ValidationStatus = "Pending",
                    CompletenessScore = 85, // Mock score
                    ClearanceType = boeDocument?.ClearanceType ?? "Unknown",
                    ImageCount = scannerContainer?.ImagePaths?.Count ?? 0,
                    HasScannerData = scannerContainer != null,
                    HasICUMSData = boeDocument != null,
                    BOENumber = boeDocument?.DeclarationNumber,
                    Consignee = boeDocument?.ConsigneeName,
                    OriginPort = boeDocument?.CountryOfOrigin,
                    Destination = boeDocument?.DeliveryPlace,
                    VesselName = "N/A", // Not in BOEDocument
                    VehicleCount = 0, // Would need to query VIN records
                    ScanLocation = "Tema Port", // Mock
                    Operator = "System", // Mock
                    ContainerSize = boeDocument?.ContainerISO
                };

                _logger.LogInfo("GetFullContainerDetails", "Found full details for container {ContainerNumber}", new { ContainerNumber = containerNumber });

                return Ok(fullDetails);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetFullContainerDetails", "Error getting full details for container {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get scanner data for a container
        /// </summary>
        /// <remarks>
        /// Returns scanner data in two formats:
        /// - **Paginated format (default)**: Returns `PagedResult&lt;ScannerDataRecord&gt;` with field/value pairs as a list
        /// - **Full format (?full=true)**: Returns `FullScannerDataRecordDto` with all fields in a dictionary structure
        /// 
        /// **Use Cases:**
        /// - Paginated format: For displaying data in tables with pagination
        /// - Full format: For getting all data in a single request without pagination
        /// 
        /// **Example Requests:**
        /// - `GET /api/containerdetails/scanner/MSMU6938402` - Returns paginated data
        /// - `GET /api/containerdetails/scanner/MSMU6938402?full=true` - Returns full record
        /// </remarks>
        /// <param name="containerNumber">Container number to retrieve scanner data for</param>
        /// <param name="page">Page number for pagination (default: 1). Ignored if `full=true`</param>
        /// <param name="pageSize">Number of records per page (default: 50). Ignored if `full=true`</param>
        /// <param name="full">If `true`, returns full record with AllFields dictionary. If `false` (default), returns paginated list</param>
        /// <returns>
        /// - If `full=false`: `PagedResult&lt;ScannerDataRecord&gt;` with pagination metadata
        /// - If `full=true`: `FullScannerDataRecordDto` with AllFields dictionary, AvailableFields, and MissingFields
        /// </returns>
        /// <response code="200">Returns scanner data successfully</response>
        /// <response code="404">No scanner data found for the container (only when full=true)</response>
        /// <response code="500">Internal server error</response>
        [HttpGet("scanner/{containerNumber}")]
        [ProducesResponseType(typeof(PagedResult<ScannerDataRecord>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(FullScannerDataRecordDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> GetScannerData(
            string containerNumber,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] bool full = false)
        {
            try
            {
                _logger.LogInfo("GetScannerData", "Getting scanner data for container {ContainerNumber}, page {Page}, size {PageSize}",
                    new { ContainerNumber = containerNumber, Page = page, PageSize = pageSize });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                var scannerRecords = new List<ScannerDataRecord>();

                // Check ASE scanner data
                var aseScan = await _context.AseScans
                    .Where(a => a.ContainerNumber == containerNumber)
                    .OrderByDescending(a => a.ScanTime)
                    .FirstOrDefaultAsync(cts.Token);

                if (aseScan != null)
                {
                    scannerRecords.AddRange(new List<ScannerDataRecord>
                    {
                        new ScannerDataRecord
                        {
                            Field = "Container Number",
                            Value = aseScan.ContainerNumber ?? "N/A",
                            Category = "Container Info",
                            Timestamp = aseScan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Scanner Type",
                            Value = "ASE",
                            Category = "Scanner Info",
                            Timestamp = aseScan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Scan Time",
                            Value = aseScan.ScanTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            Category = "Scanner Info",
                            Timestamp = aseScan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Inspection ID",
                            Value = aseScan.InspectionId.ToString(),
                            Category = "Scanner Info",
                            Timestamp = aseScan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Inspection UUID",
                            Value = aseScan.InspectionUuid,
                            Category = "Scanner Info",
                            Timestamp = aseScan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Vehicle Number",
                            Value = aseScan.TruckPlate ?? "N/A",
                            Category = "Vehicle Info",
                            Timestamp = aseScan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Image Display Name",
                            Value = aseScan.ImageDisplayName ?? "N/A",
                            Category = "Image Info",
                            Timestamp = aseScan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Has Scan Image",
                            Value = aseScan.ScanImage != null ? "Yes" : "No",
                            Category = "Image Info",
                            Timestamp = aseScan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Image Size",
                            Value = aseScan.ScanImage?.Length.ToString() + " bytes" ?? "N/A",
                            Category = "Image Info",
                            Timestamp = aseScan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Synced At",
                            Value = aseScan.SyncedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                            Category = "Status",
                            Timestamp = aseScan.ScanTime
                        }
                    });
                }

                // Check FS6000 scanner data
                var fs6000Scan = await _context.FS6000Scans
                    .Where(f => f.ContainerNumber == containerNumber)
                    .OrderByDescending(f => f.ScanTime)
                    .FirstOrDefaultAsync(cts.Token);

                if (fs6000Scan != null)
                {
                    scannerRecords.AddRange(new List<ScannerDataRecord>
                    {
                        new ScannerDataRecord
                        {
                            Field = "Container Number",
                            Value = fs6000Scan.ContainerNumber,
                            Category = "Container Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Scanner Type",
                            Value = "FS6000",
                            Category = "Scanner Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Scan Time",
                            Value = fs6000Scan.ScanTime.ToString("yyyy-MM-dd HH:mm:ss"),
                            Category = "Scanner Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Picture Number",
                            Value = fs6000Scan.PicNumber,
                            Category = "Scanner Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Vessel Name",
                            Value = fs6000Scan.VesselName ?? "N/A",
                            Category = "Vessel Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Operator ID",
                            Value = fs6000Scan.OperatorId ?? "N/A",
                            Category = "Operator Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Scan Result",
                            Value = fs6000Scan.ScanResult ?? "N/A",
                            Category = "Scan Result",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Goods Description",
                            Value = fs6000Scan.GoodsDescription ?? "N/A",
                            Category = "Cargo Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Shipping Company",
                            Value = fs6000Scan.ShippingCompany ?? "N/A",
                            Category = "Shipping Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Consignee",
                            Value = fs6000Scan.Consignee ?? "N/A",
                            Category = "Party Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "FYCO Present",
                            Value = fs6000Scan.FycoPresent ?? "N/A",
                            Category = "Security Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "File Path",
                            Value = fs6000Scan.FilePath ?? "N/A",
                            Category = "File Info",
                            Timestamp = fs6000Scan.ScanTime
                        },
                        new ScannerDataRecord
                        {
                            Field = "Sync Status",
                            Value = fs6000Scan.SyncStatus,
                            Category = "Status",
                            Timestamp = fs6000Scan.ScanTime
                        }
                    });
                }

                if (!scannerRecords.Any())
                {
                    if (full)
                    {
                        return NotFound(new { message = $"No scanner data found for container {containerNumber}" });
                    }
                    return Ok(new PagedResult<ScannerDataRecord>
                    {
                        Data = new List<ScannerDataRecord>(),
                        TotalCount = 0,
                        Page = page,
                        PageSize = pageSize,
                        TotalPages = 0
                    });
                }

                // ✅ Phase 2: Return FullScannerDataRecord if full=true
                if (full)
                {
                    var fullRecord = new FullScannerDataRecordDto
                    {
                        ContainerNumber = containerNumber,
                        ScannerType = scannerRecords.FirstOrDefault(r => r.Field == "Scanner Type")?.Value ?? "Unknown",
                        ScanTime = scannerRecords.FirstOrDefault()?.Timestamp ?? DateTime.UtcNow,
                        AllFields = scannerRecords.ToDictionary(r => r.Field, r => (object)r.Value),
                        AvailableFields = scannerRecords.Select(r => r.Field).Distinct().ToList(),
                        MissingFields = new List<string>() // Could compare against expected fields if needed
                    };

                    _logger.LogInfo("GetScannerData", "Returning full scanner data for container {ContainerNumber} with {FieldCount} fields",
                        new { ContainerNumber = containerNumber, FieldCount = fullRecord.AllFields.Count });

                    return Ok(fullRecord);
                }

                // Default: Return paginated result
                var result = new PagedResult<ScannerDataRecord>
                {
                    Data = scannerRecords,
                    TotalCount = scannerRecords.Count,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = 1
                };

                _logger.LogInfo("GetScannerData", "Found {Count} scanner records for container {ContainerNumber} (page {Page})",
                    new { Count = 1, ContainerNumber = containerNumber, Page = page });

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetScannerData", "Error getting scanner data for container {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get ICUMS (BOE) data for a container
        /// </summary>
        /// <remarks>
        /// Returns ICUMS data in two formats:
        /// - **Paginated format (default)**: Returns `PagedResult&lt;ICUMSDataRecord&gt;` with field/value pairs as a list
        /// - **Full format (?full=true)**: Returns `FullBOEDataRecordDto` with all fields in a dictionary structure
        /// 
        /// **Supported Cargo Types:**
        /// - **Consolidated cargo**: Multiple House BLs per container - use `boeDocumentId` to specify which BOE document
        /// - **Non-consolidated cargo**: Single declaration per container - use `declarationNumber` to query by declaration
        /// 
        /// **Use Cases:**
        /// - Paginated format: For displaying data in tables with pagination
        /// - Full format: For getting all data in a single request without pagination
        /// 
        /// **Example Requests:**
        /// - `GET /api/containerdetails/icums/MSMU6938402` - Returns paginated data
        /// - `GET /api/containerdetails/icums/MSMU6938402?full=true` - Returns full record
        /// - `GET /api/containerdetails/icums/MSMU6938402?boeDocumentId=123` - Returns data for specific BOE document
        /// - `GET /api/containerdetails/icums/MSMU6938402?declarationNumber=DEC123` - Returns data for specific declaration
        /// </remarks>
        /// <param name="containerNumber">Container number to retrieve ICUMS data for</param>
        /// <param name="page">Page number for pagination (default: 1). Ignored if `full=true`</param>
        /// <param name="pageSize">Number of records per page (default: 50). Ignored if `full=true`</param>
        /// <param name="full">If `true`, returns full record with AllFields dictionary. If `false` (default), returns paginated list</param>
        /// <param name="boeDocumentId">Optional BOE document ID to retrieve data for a specific BOE document (useful for consolidated cargo with multiple House BLs)</param>
        /// <param name="declarationNumber">Optional declaration number to query by declaration (useful for non-consolidated cargo)</param>
        /// <returns>
        /// - If `full=false`: `PagedResult&lt;ICUMSDataRecord&gt;` with pagination metadata
        /// - If `full=true`: `FullBOEDataRecordDto` with AllFields dictionary, AvailableFields, and MissingFields
        /// </returns>
        /// <response code="200">Returns ICUMS data successfully</response>
        /// <response code="404">No ICUMS data found for the container (only when full=true)</response>
        /// <response code="500">Internal server error</response>
        [HttpGet("icums/{containerNumber}")]
        [ProducesResponseType(typeof(PagedResult<ICUMSDataRecord>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(FullBOEDataRecordDto), StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        [ProducesResponseType(StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult> GetICUMSData(
            string containerNumber,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] bool full = false,
            [FromQuery] int? boeDocumentId = null,
            [FromQuery] string? declarationNumber = null)
        {
            try
            {
                _logger.LogInfo("GetICUMSData", "Getting ICUMS data for container {ContainerNumber}, declaration {DeclarationNumber}, page {Page}, size {PageSize}, BOEDocumentId {BOEDocumentId}",
                    new { ContainerNumber = containerNumber, DeclarationNumber = declarationNumber, Page = page, PageSize = pageSize, BOEDocumentId = boeDocumentId });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

                List<BOEDocument> boeDocuments;

                // ✅ FIX: Use unified lookup logic - prefer BOEDocumentId from ContainerCompletenessStatus
                if (boeDocumentId.HasValue)
                {
                    // Use the specific BOEDocument that was used for clearance type determination
                    var specificBOE = await _icumDownloadsDbContext.BOEDocuments
                        .FirstOrDefaultAsync(b => b.Id == boeDocumentId.Value, cts.Token);

                    if (specificBOE != null)
                    {
                        boeDocuments = new List<BOEDocument> { specificBOE };
                        _logger.LogInfo("GetICUMSData", "Using specific BOEDocument {BOEDocumentId} for container {ContainerNumber}",
                            new { BOEDocumentId = boeDocumentId.Value, ContainerNumber = containerNumber });
                    }
                    else
                    {
                        // BOEDocumentId not found, fall back to container number lookup
                        _logger.LogWarning("GetICUMSData", "BOEDocument {BOEDocumentId} not found, falling back to container number lookup",
                            new { BOEDocumentId = boeDocumentId.Value });

                        // ✅ FIX: Check if container is consolidated - if so, get ALL house BLs
                        var consolidatedBOEs = await _icumDownloadsDbContext.BOEDocuments
                            .Where(b => b.ContainerNumber == containerNumber && b.IsConsolidated)
                            .ToListAsync(cts.Token);

                        if (consolidatedBOEs.Any())
                        {
                            // Container is consolidated - return ALL house BLs
                            boeDocuments = consolidatedBOEs;
                            _logger.LogInfo("GetICUMSData", "Container {ContainerNumber} is consolidated - returning {Count} house BL(s) (fallback from BOEDocumentId)",
                                new { ContainerNumber = containerNumber, Count = boeDocuments.Count });
                        }
                        else
                        {
                            // Container is not consolidated - return non-consolidated BOE documents
                            boeDocuments = await _icumDownloadsDbContext.BOEDocuments
                                .Where(b => b.ContainerNumber == containerNumber && !b.IsConsolidated)
                                .OrderByDescending(b => b.CreatedAt)
                                .ToListAsync(cts.Token);
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(declarationNumber))
                {
                    // ✅ NEW: For non-consolidated cargo, query by declaration number to get ALL BOE documents
                    // ✅ FIX: Filter by !IsConsolidated to ensure we only get non-consolidated cargo BOE documents
                    boeDocuments = await _icumDownloadsDbContext.BOEDocuments
                        .Where(b => b.DeclarationNumber == declarationNumber && !b.IsConsolidated)
                        .OrderByDescending(b => b.CreatedAt)
                        .ToListAsync(cts.Token);
                    _logger.LogInfo("GetICUMSData", "Querying by declaration number {DeclarationNumber} (non-consolidated only), found {Count} BOE document(s)",
                        new { DeclarationNumber = declarationNumber, Count = boeDocuments.Count });
                }
                else
                {
                    // ✅ FIX: Check if container has consolidated cargo (multiple house BLs)
                    // For consolidated cargo, we need ALL house BLs, not just one from ContainerCompletenessStatus
                    var consolidatedBOEs = await _icumDownloadsDbContext.BOEDocuments
                        .Where(b => b.ContainerNumber == containerNumber && b.IsConsolidated)
                        .ToListAsync(cts.Token);

                    if (consolidatedBOEs.Any())
                    {
                        // ✅ FIX: Container has consolidated cargo - return ALL house BLs
                        boeDocuments = consolidatedBOEs;
                        _logger.LogInfo("GetICUMSData", "Container {ContainerNumber} is consolidated - returning {Count} house BL(s)",
                            new { ContainerNumber = containerNumber, Count = boeDocuments.Count });
                    }
                    else
                    {
                        // ✅ FALLBACK: Try to get BOEDocumentId from ContainerCompletenessStatus first (for non-consolidated)
                        var completenessStatus = await _context.ContainerCompletenessStatuses
                            .Where(c => c.ContainerNumber == containerNumber)
                            .OrderByDescending(c => c.UpdatedAt)
                            .FirstOrDefaultAsync(cts.Token);

                        if (completenessStatus?.BOEDocumentId.HasValue == true)
                        {
                            // Use the BOEDocumentId from completeness status (same one used for clearance type)
                            var specificBOE = await _icumDownloadsDbContext.BOEDocuments
                                .FirstOrDefaultAsync(b => b.Id == completenessStatus.BOEDocumentId.Value, cts.Token);

                            if (specificBOE != null)
                            {
                                boeDocuments = new List<BOEDocument> { specificBOE };
                                _logger.LogInfo("GetICUMSData", "Using BOEDocument {BOEDocumentId} from ContainerCompletenessStatus for container {ContainerNumber}",
                                    new { BOEDocumentId = completenessStatus.BOEDocumentId.Value, ContainerNumber = containerNumber });
                            }
                            else
                            {
                                // BOEDocumentId not found, fall back to container number lookup
                                boeDocuments = await _icumDownloadsDbContext.BOEDocuments
                                    .Where(b => b.ContainerNumber == containerNumber && !b.IsConsolidated)
                                    .OrderByDescending(b => b.CreatedAt)
                                    .ToListAsync(cts.Token);
                            }
                        }
                        else
                        {
                            // No BOEDocumentId in completeness status, query by container number (non-consolidated only)
                            boeDocuments = await _icumDownloadsDbContext.BOEDocuments
                                .Where(b => b.ContainerNumber == containerNumber && !b.IsConsolidated)
                                .OrderByDescending(b => b.CreatedAt)
                                .ToListAsync(cts.Token);
                        }
                    }
                }

                if (!boeDocuments.Any())
                {
                    _logger.LogWarning("GetICUMSData", "No BOE documents found for container {ContainerNumber}",
                        new { ContainerNumber = containerNumber });
                    if (full)
                    {
                        return NotFound(new { message = $"No ICUMS data found for container {containerNumber}" });
                    }
                    return Ok(new PagedResult<ICUMSDataRecord>
                    {
                        Data = new List<ICUMSDataRecord>(),
                        TotalCount = 0,
                        Page = page,
                        PageSize = pageSize,
                        TotalPages = 0
                    });
                }

                var firstBOE = boeDocuments.First();
                _logger.LogInfo("GetICUMSData", "Found {Count} BOE document(s) for container {ContainerNumber}. First BOE ID: {BOEId}, ClearanceType: {ClearanceType}, HasConsigneeName: {HasConsignee}, HasShipperName: {HasShipper}, HasRawJson: {HasRawJson}",
                    new
                    {
                        Count = boeDocuments.Count,
                        ContainerNumber = containerNumber,
                        BOEId = firstBOE.Id,
                        ClearanceType = firstBOE.ClearanceType,
                        HasConsignee = !string.IsNullOrWhiteSpace(firstBOE.ConsigneeName),
                        HasShipper = !string.IsNullOrWhiteSpace(firstBOE.ShipperName),
                        HasRawJson = !string.IsNullOrEmpty(firstBOE.RawJsonData)
                    });

                // 🔍 DIAGNOSTIC: Log sample of entity properties to verify data exists
                _logger.LogInfo("GetICUMSData", "BOE Entity Properties Sample - DeclarantName: {DeclarantName}, ImpName: {ImpName}, ExpName: {ExpName}, ConsigneeName: {ConsigneeName}, ContainerISO: {ContainerISO}, ContainerWeight: {ContainerWeight}",
                    new
                    {
                        DeclarantName = firstBOE.DeclarantName ?? "NULL",
                        ImpName = firstBOE.ImpName ?? "NULL",
                        ExpName = firstBOE.ExpName ?? "NULL",
                        ConsigneeName = firstBOE.ConsigneeName ?? "NULL",
                        ContainerISO = firstBOE.ContainerISO ?? "NULL",
                        ContainerWeight = firstBOE.ContainerWeight?.ToString() ?? "NULL"
                    });

                var allICUMSRecords = new List<ICUMSDataRecord>();

                // ✅ MEMORY FIX: Limit to max 10 BOE documents to prevent loading excessive RawJsonData into memory
                // Each RawJsonData can be MBs, and parsing multiple JsonDocument instances simultaneously causes memory bloat
                // For consolidated cargo with many House BLs, prioritize the most recent documents
                var boeDocumentsToProcess = boeDocuments
                    .OrderByDescending(b => b.CreatedAt)
                    .Take(10)
                    .ToList();

                _logger.LogInfo("GetICUMSData", "Processing {Count} of {Total} BOE documents for container {ContainerNumber} (limited to prevent memory bloat)",
                    boeDocumentsToProcess.Count, boeDocuments.Count, containerNumber);

                // ✅ FIX: Process BOE documents (limited to prevent memory bloat)
                // Previously only processed the first document, missing data from other House BLs
                foreach (var boeDocument in boeDocumentsToProcess)
                {
                    // Dictionary to store extracted JSON values (extracted while JsonDocument is alive)
                    var jsonFallbackValues = new Dictionary<string, string?>();

                    if (!string.IsNullOrEmpty(boeDocument.RawJsonData))
                    {
                        try
                        {
                            // ✅ MEMORY FIX: Use using statement to ensure JsonDocument is disposed immediately after use
                            // JsonDocument holds references to the JSON data and can accumulate in memory
                            // ✅ FIX: Extract all needed values WHILE the JsonDocument is alive
                            using var rawJsonDoc = JsonDocument.Parse(boeDocument.RawJsonData);
                            var rootElement = rawJsonDoc.RootElement;
                            JsonElement? headerElement = null;
                            JsonElement? containerDetailsElement = null;
                            JsonElement? manifestDetailsElement = null;

                            if (rootElement.ValueKind == JsonValueKind.Object)
                            {
                                // Try exact property names first
                                if (rootElement.TryGetProperty("Header", out var header))
                                    headerElement = header;
                                if (rootElement.TryGetProperty("ContainerDetails", out var containerDetails))
                                    containerDetailsElement = containerDetails;
                                if (rootElement.TryGetProperty("ManifestDetails", out var manifestDetails))
                                    manifestDetailsElement = manifestDetails;

                                // Fallback: Try case-insensitive matching for section names
                                if (!headerElement.HasValue || !containerDetailsElement.HasValue || !manifestDetailsElement.HasValue)
                                {
                                    foreach (var prop in rootElement.EnumerateObject())
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

                                // ✅ CRITICAL FIX: Extract all values from JsonElements WHILE JsonDocument is alive
                                // Store them in a dictionary to use later after JsonDocument is disposed
                                jsonFallbackValues["ClearanceType"] = GetValueFromJsonElement(headerElement, rootElement, "ClearanceType", "CLEARANCETYPE");
                                jsonFallbackValues["RotationNumber"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "RotationNumber");
                                jsonFallbackValues["DeclarationNumber"] = GetValueFromJsonElement(headerElement, rootElement, "DeclarationNumber", "DECLARATIONNUMBER");
                                jsonFallbackValues["BlNumber"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "BLNumber", "BlNumber", "BL_NUMBER");
                                jsonFallbackValues["CrmsLevel"] = GetValueFromJsonElement(headerElement, rootElement, "CRMSLevel", "CrmsLevel", "CRMS_LEVEL");
                                jsonFallbackValues["RegimeCode"] = GetValueFromJsonElement(headerElement, rootElement, "RegimeCode", "REGIMECODE");
                                jsonFallbackValues["NoOfContainers"] = GetValueFromJsonElement(headerElement, rootElement, "NoofContainers", "NoOfContainers", "NOOFCONTAINERS");
                                jsonFallbackValues["DeclarationVersion"] = GetValueFromJsonElement(headerElement, rootElement, "DeclarationVersion", "DECLARATIONVERSION");
                                jsonFallbackValues["DeclarationDate"] = GetValueFromJsonElement(headerElement, rootElement, "DeclarationDate", "DECLARATIONDATE");
                                jsonFallbackValues["DeclarantName"] = GetValueFromJsonElement(headerElement, rootElement, "DeclarantName", "DECLARANTNAME");
                                jsonFallbackValues["DeclarantAddress"] = GetValueFromJsonElement(headerElement, rootElement, "DeclarantAddress", "DECLARANTADDRESS", "DECLARANT_ADDRESS");
                                jsonFallbackValues["ConsigneeName"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "ConsigneeName");
                                jsonFallbackValues["ConsigneeAddress"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "ConsigneeAddress");
                                jsonFallbackValues["ImpName"] = GetValueFromJsonElement(headerElement, rootElement, "ImpName", "IMPNAME");
                                jsonFallbackValues["ImpAddress"] = GetValueFromJsonElement(headerElement, rootElement, "ImpAddress", "IMPADDRESS", "IMP_ADDRESS");
                                jsonFallbackValues["ExpName"] = GetValueFromJsonElement(headerElement, rootElement, "ExpName", "EXPNAME", "EXP_NAME");
                                jsonFallbackValues["ExpAddress"] = GetValueFromJsonElement(headerElement, rootElement, "ExpAddress", "EXPADDRESS", "EXP_ADDRESS");
                                jsonFallbackValues["ShipperName"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "ShipperName");
                                jsonFallbackValues["ShipperAddress"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "ShipperAddress");
                                jsonFallbackValues["ImpExpName"] = GetValueFromJsonElement(headerElement, rootElement, "ImpExpName", "IMPEXPNAME", "IMP_EXP_NAME");
                                jsonFallbackValues["ImpExpAddress"] = GetValueFromJsonElement(headerElement, rootElement, "ImpExpAddress", "IMPEXPADDRESS", "IMP_EXP_ADDRESS");
                                jsonFallbackValues["CountryOfOrigin"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "CountryofOrigin", "CountryOfOrigin");
                                jsonFallbackValues["DeliveryPlace"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "DeliveryPlace");
                                jsonFallbackValues["ContainerISO"] = GetValueFromJsonElement(containerDetailsElement, rootElement, "ContainerISO", "ISO");
                                jsonFallbackValues["ContainerDescription"] = GetValueFromJsonElement(containerDetailsElement, rootElement, "ContainerDescription", "ContainerType", "Description");
                                jsonFallbackValues["ContainerWeight"] = GetValueFromJsonElement(containerDetailsElement, rootElement, "ContainerWeight", "Weight");
                                jsonFallbackValues["ContainerQuantity"] = GetValueFromJsonElement(containerDetailsElement, rootElement, "ContainerQuantity", "Quantity");
                                jsonFallbackValues["TotalDutyPaid"] = GetValueFromJsonElement(headerElement, rootElement, "TotalDutyPaid", "TOTALDUTYPAID");
                                jsonFallbackValues["HouseBl"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "HouseBL", "HouseBl", "HOUSE_BL");
                                jsonFallbackValues["MarksNumbers"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "MarksNumbers", "MarksNumber");
                                jsonFallbackValues["GoodsDescription"] = GetValueFromJsonElement(manifestDetailsElement, rootElement, "GoodsDescription", "Goods_Description", "GOODSDESCRIPTION", "Description", "DESCRIPTION");
                            }
                        }
                        catch (Exception ex)
                        {
                            // Log JSON parsing errors for debugging
                            _logger.LogWarning("GetICUMSData", "Failed to parse RawJsonData for BOEDocument {BOEId}: {Error}",
                                boeDocument.Id, ex.Message);
                            // Continue with entity properties only
                        }
                    }
                    else
                    {
                        _logger.LogWarning("GetICUMSData", "BOEDocument {BOEId} has no RawJsonData", boeDocument.Id);
                    }

                    // ✅ Determine if this is CMR or BOE clearance
                    bool isCMR = boeDocument.ClearanceType == "CMR";

                    // Convert to ICUMS data records with proper field-value structure using actual BOEDocument data
                    // ✅ ENHANCED: Extract from RawJsonData when entity properties are null

                    // 🔍 BULLETPROOF: Add unmapped fields to ICUMS data display
                    var unmappedFields = ExtractUnmappedFieldsForICUMS(boeDocument);

                    var icumsRecords = new List<ICUMSDataRecord>
                {
                    new ICUMSDataRecord
                    {
                        Field = "Container Number",
                        Value = boeDocument.ContainerNumber,
                        Category = "Container Info",
                        IsRequired = true
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Clearance Type",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ClearanceType) && boeDocument.ClearanceType != "Not available" && boeDocument.ClearanceType != "N/A")
                            ? boeDocument.ClearanceType
                            : (jsonFallbackValues.GetValueOrDefault("ClearanceType") ?? "Not available"),
                        Category = "Declaration Info",
                        IsRequired = true
                    },
                    // ✅ CMR-Specific: Rotation Number is REQUIRED for CMR
                    new ICUMSDataRecord
                    {
                        Field = "Rotation Number",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.RotationNumber) && boeDocument.RotationNumber != "Not available" && boeDocument.RotationNumber != "N/A")
                            ? boeDocument.RotationNumber
                            : (jsonFallbackValues.GetValueOrDefault("RotationNumber") ?? (isCMR ? "⚠️ MISSING (Required for CMR)" : "Not applicable")),
                        Category = isCMR ? "CMR Info" : "Manifest Info",
                        IsRequired = isCMR // ✅ Required for CMR, optional for BOE
                    },
                    // ✅ BOE-Specific: Declaration Number is REQUIRED for IM/EX, N/A for CMR
                    new ICUMSDataRecord
                    {
                        Field = "Declaration Number (BOE)",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.DeclarationNumber) && boeDocument.DeclarationNumber != "Not available" && boeDocument.DeclarationNumber != "N/A")
                            ? boeDocument.DeclarationNumber
                            : (jsonFallbackValues.GetValueOrDefault("DeclarationNumber") ?? (isCMR ? "N/A (CMR clearance)" : "⚠️ MISSING (Required for IM/EX)")),
                        Category = "Declaration Info",
                        IsRequired = !isCMR // ✅ Required for IM/EX only
                    },
                    new ICUMSDataRecord
                    {
                        Field = "BL Number",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.BlNumber) && boeDocument.BlNumber != "Not available" && boeDocument.BlNumber != "N/A")
                            ? boeDocument.BlNumber
                            : (jsonFallbackValues.GetValueOrDefault("BlNumber") ?? "Not available"),
                        Category = "Manifest Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "CRMS Risk Level",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.CrmsLevel) && boeDocument.CrmsLevel != "Not available" && boeDocument.CrmsLevel != "N/A")
                            ? boeDocument.CrmsLevel
                            : (jsonFallbackValues.GetValueOrDefault("CrmsLevel") ?? "Not assessed"),
                        Category = "Risk Assessment",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Regime Code",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.RegimeCode) && boeDocument.RegimeCode != "Not available" && boeDocument.RegimeCode != "N/A")
                            ? boeDocument.RegimeCode
                            : (jsonFallbackValues.GetValueOrDefault("RegimeCode") ?? "Not available"),
                        Category = "Declaration Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Number of Containers",
                        Value = (boeDocument.NoOfContainers.HasValue)
                            ? boeDocument.NoOfContainers.Value.ToString()
                            : (jsonFallbackValues.GetValueOrDefault("NoOfContainers") ?? "Not available"),
                        Category = "Declaration Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Declaration Version",
                        Value = (boeDocument.DeclarationVersion.HasValue)
                            ? boeDocument.DeclarationVersion.Value.ToString()
                            : (jsonFallbackValues.GetValueOrDefault("DeclarationVersion") ?? "Not available"),
                        Category = "Declaration Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Declaration Date",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.DeclarationDate) && boeDocument.DeclarationDate != "Not available" && boeDocument.DeclarationDate != "N/A")
                            ? boeDocument.DeclarationDate
                            : (jsonFallbackValues.GetValueOrDefault("DeclarationDate") ?? "Not available"),
                        Category = "Declaration Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Declarant Name",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.DeclarantName) && boeDocument.DeclarantName != "Not available" && boeDocument.DeclarantName != "N/A")
                            ? boeDocument.DeclarantName
                            : (jsonFallbackValues.GetValueOrDefault("DeclarantName") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Declarant Address",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.DeclarantAddress) && boeDocument.DeclarantAddress != "Not available" && boeDocument.DeclarantAddress != "N/A")
                            ? boeDocument.DeclarantAddress
                            : (jsonFallbackValues.GetValueOrDefault("DeclarantAddress") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Consignee Name",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ConsigneeName) && boeDocument.ConsigneeName != "Not available" && boeDocument.ConsigneeName != "N/A")
                            ? boeDocument.ConsigneeName
                            : (jsonFallbackValues.GetValueOrDefault("ConsigneeName") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Consignee Address",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ConsigneeAddress) && boeDocument.ConsigneeAddress != "Not available" && boeDocument.ConsigneeAddress != "N/A")
                            ? boeDocument.ConsigneeAddress
                            : (jsonFallbackValues.GetValueOrDefault("ConsigneeAddress") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Importer Name",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ImpName) && boeDocument.ImpName != "Not available" && boeDocument.ImpName != "N/A")
                            ? boeDocument.ImpName
                            : (jsonFallbackValues.GetValueOrDefault("ImpName") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Importer Address",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ImpAddress) && boeDocument.ImpAddress != "Not available" && boeDocument.ImpAddress != "N/A")
                            ? boeDocument.ImpAddress
                            : (jsonFallbackValues.GetValueOrDefault("ImpAddress") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Exporter Name",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ExpName) && boeDocument.ExpName != "Not available" && boeDocument.ExpName != "N/A")
                            ? boeDocument.ExpName
                            : (jsonFallbackValues.GetValueOrDefault("ExpName") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Exporter Address",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ExpAddress) && boeDocument.ExpAddress != "Not available" && boeDocument.ExpAddress != "N/A")
                            ? boeDocument.ExpAddress
                            : (jsonFallbackValues.GetValueOrDefault("ExpAddress") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Shipper Name",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ShipperName) && boeDocument.ShipperName != "Not available" && boeDocument.ShipperName != "N/A")
                            ? boeDocument.ShipperName
                            : (jsonFallbackValues.GetValueOrDefault("ShipperName") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Shipper Address",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ShipperAddress) && boeDocument.ShipperAddress != "Not available" && boeDocument.ShipperAddress != "N/A")
                            ? boeDocument.ShipperAddress
                            : (jsonFallbackValues.GetValueOrDefault("ShipperAddress") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Importer/Exporter Name",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ImpExpName) && boeDocument.ImpExpName != "Not available" && boeDocument.ImpExpName != "N/A")
                            ? boeDocument.ImpExpName
                            : (jsonFallbackValues.GetValueOrDefault("ImpExpName") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Importer/Exporter Address",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ImpExpAddress) && boeDocument.ImpExpAddress != "Not available" && boeDocument.ImpExpAddress != "N/A")
                            ? boeDocument.ImpExpAddress
                            : (jsonFallbackValues.GetValueOrDefault("ImpExpAddress") ?? "Not available"),
                        Category = "Party Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Country of Origin",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.CountryOfOrigin) && boeDocument.CountryOfOrigin != "Not available" && boeDocument.CountryOfOrigin != "N/A")
                            ? boeDocument.CountryOfOrigin
                            : (jsonFallbackValues.GetValueOrDefault("CountryOfOrigin") ?? "Not available"),
                        Category = "Location Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Delivery Place",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.DeliveryPlace) && boeDocument.DeliveryPlace != "Not available" && boeDocument.DeliveryPlace != "N/A")
                            ? boeDocument.DeliveryPlace
                            : (jsonFallbackValues.GetValueOrDefault("DeliveryPlace") ?? "Not available"),
                        Category = "Location Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Container ISO",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ContainerISO) && boeDocument.ContainerISO != "Not available" && boeDocument.ContainerISO != "N/A")
                            ? boeDocument.ContainerISO
                            : (jsonFallbackValues.GetValueOrDefault("ContainerISO") ?? "Not available"),
                        Category = "Container Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Container Description",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.ContainerDescription) && boeDocument.ContainerDescription != "Not available" && boeDocument.ContainerDescription != "N/A")
                            ? boeDocument.ContainerDescription
                            : (jsonFallbackValues.GetValueOrDefault("ContainerDescription") ?? "Not available"),
                        Category = "Container Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Container Weight",
                        Value = (boeDocument.ContainerWeight.HasValue)
                            ? boeDocument.ContainerWeight.Value.ToString("N2")
                            : (jsonFallbackValues.GetValueOrDefault("ContainerWeight") ?? "Not available"),
                        Category = "Container Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Container Quantity",
                        Value = (boeDocument.ContainerQuantity.HasValue)
                            ? boeDocument.ContainerQuantity.Value.ToString()
                            : (jsonFallbackValues.GetValueOrDefault("ContainerQuantity") ?? "Not available"),
                        Category = "Container Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Total Duty Paid",
                        Value = (boeDocument.TotalDutyPaid.HasValue)
                            ? $"{boeDocument.TotalDutyPaid.Value:N2} GHS"
                            : (jsonFallbackValues.GetValueOrDefault("TotalDutyPaid") ?? "Not available"),
                        Category = "Financial Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "House BL",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.HouseBl) && boeDocument.HouseBl != "Not available" && boeDocument.HouseBl != "N/A")
                            ? boeDocument.HouseBl
                            : (jsonFallbackValues.GetValueOrDefault("HouseBl") ?? "Not available"),
                        Category = "Manifest Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Marks & Numbers",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.MarksNumbers) && boeDocument.MarksNumbers != "Not available" && boeDocument.MarksNumbers != "N/A")
                            ? boeDocument.MarksNumbers
                            : (jsonFallbackValues.GetValueOrDefault("MarksNumbers") ?? "Not available"),
                        Category = "Cargo Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Goods Description",
                        Value = (!string.IsNullOrWhiteSpace(boeDocument.GoodsDescription) && boeDocument.GoodsDescription != "Not available" && boeDocument.GoodsDescription != "N/A")
                            ? boeDocument.GoodsDescription
                            : (jsonFallbackValues.GetValueOrDefault("GoodsDescription") ?? "Not available"),
                        Category = "Cargo Info",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Compliance Officer Remarks",
                        Value = boeDocument.CompOffRemarks ?? "None",
                        Category = "Risk Assessment",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "CCVR Intelligence Remarks",
                        Value = boeDocument.CcvrIntelRemarks ?? "None",
                        Category = "Risk Assessment",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Processing Status",
                        Value = boeDocument.ProcessingStatus,
                        Category = "Status",
                        IsRequired = false
                    },
                    new ICUMSDataRecord
                    {
                        Field = "Created At",
                        Value = boeDocument.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        Category = "Data Info",
                        IsRequired = false
                    },

                    // ── Part B: full-field visibility additions (parity with CargoGroupService.ExtractICUMSRecords) ──
                    // Note: JSON fallback elements are scoped to an inner block here — use entity properties directly.
                    new ICUMSDataRecord { Field = "Master BL Number",
                        Value = string.IsNullOrWhiteSpace(boeDocument.MasterBlNumber) ? "Not available" : boeDocument.MasterBlNumber,
                        Category = "Manifest Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Original Clearance Type",
                        Value = boeDocument.OriginalClearanceType ?? "Not available",
                        Category = "Declaration Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "CMR Upgraded At",
                        Value = boeDocument.CmrUpgradedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not available",
                        Category = "Declaration Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Container Size",
                        Value = string.IsNullOrWhiteSpace(boeDocument.ContainerSize) ? "Not available" : boeDocument.ContainerSize,
                        Category = "Container Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Container Quantity",
                        Value = boeDocument.ContainerQuantity?.ToString() ?? "Not available",
                        Category = "Container Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Container Status",
                        Value = string.IsNullOrWhiteSpace(boeDocument.ContainerStatus) ? "Not available" : boeDocument.ContainerStatus,
                        Category = "Container Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Container Remarks",
                        Value = string.IsNullOrWhiteSpace(boeDocument.ContainerRemarks) ? "Not available" : boeDocument.ContainerRemarks,
                        Category = "Container Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Container Description",
                        Value = string.IsNullOrWhiteSpace(boeDocument.ContainerDescription) ? "Not available" : boeDocument.ContainerDescription,
                        Category = "Container Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Seal Number",
                        Value = string.IsNullOrWhiteSpace(boeDocument.SealNumber) ? "Not available" : boeDocument.SealNumber,
                        Category = "Container Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Truck Plate Number",
                        Value = string.IsNullOrWhiteSpace(boeDocument.TruckPlateNumber) ? "Not available" : boeDocument.TruckPlateNumber,
                        Category = "Container Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Driver Name",
                        Value = string.IsNullOrWhiteSpace(boeDocument.DriverName) ? "Not available" : boeDocument.DriverName,
                        Category = "Container Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Driver License",
                        Value = string.IsNullOrWhiteSpace(boeDocument.DriverLicense) ? "Not available" : boeDocument.DriverLicense,
                        Category = "Container Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Consolidated",
                        Value = boeDocument.IsConsolidated ? "Yes" : "No",
                        Category = "Manifest Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Ingestion Warnings",
                        Value = boeDocument.HasIngestionWarnings
                            ? (string.IsNullOrWhiteSpace(boeDocument.IngestionWarnings) ? "(flagged — no detail)" : boeDocument.IngestionWarnings!.Replace('\n', ';'))
                            : "None",
                        Category = "Integrity", IsRequired = false },
                    new ICUMSDataRecord { Field = "Has Warnings",
                        Value = boeDocument.HasIngestionWarnings ? "Yes" : "No",
                        Category = "Integrity", IsRequired = false },
                    new ICUMSDataRecord { Field = "Processed At",
                        Value = boeDocument.ProcessedAt?.ToString("yyyy-MM-dd HH:mm:ss") ?? "Not yet processed",
                        Category = "Status", IsRequired = false },
                    new ICUMSDataRecord { Field = "Updated At",
                        Value = boeDocument.UpdatedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                        Category = "Data Info", IsRequired = false },
                    new ICUMSDataRecord { Field = "Error Message",
                        Value = string.IsNullOrWhiteSpace(boeDocument.ErrorMessage) ? "None" : boeDocument.ErrorMessage,
                        Category = "Status", IsRequired = false }
                };

                    // 🔍 BULLETPROOF: Add unmapped fields to ICUMS records
                    icumsRecords.AddRange(unmappedFields);

                    // ✅ CARGO INFO FIX: Extract cargo fields from ManifestItems
                    // Include ALL statuses (92% of items are Transferred, not Completed)
                    var manifestItems = await _icumDownloadsDbContext.ManifestItems
                        .Where(m => m.BOEDocumentId == boeDocument.Id)
                        .OrderBy(m => m.ItemIndex)
                        .ToListAsync();

                    // ✅ GOODS DESCRIPTION FIX: If GoodsDescription is missing, try to get it from ManifestItems
                    var goodsDescriptionRecord = icumsRecords.FirstOrDefault(r => r.Field == "Goods Description");
                    if (goodsDescriptionRecord != null &&
                        (string.IsNullOrWhiteSpace(goodsDescriptionRecord.Value) ||
                         goodsDescriptionRecord.Value == "Not available" ||
                         goodsDescriptionRecord.Value == "N/A"))
                    {
                        // Get descriptions from ManifestItems (already loaded above without status filter)
                        var itemDescriptions = manifestItems
                            .Where(m => !string.IsNullOrWhiteSpace(m.Description))
                            .Select(m => m.Description!.Trim())
                            .Distinct()
                            .ToList();

                        if (itemDescriptions.Any())
                        {
                            // Aggregate descriptions intelligently
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
                            _logger.LogInfo("GetICUMSData", "✅ Extracted Goods Description from ManifestItems for BOE {BOEId} (Declaration: {DeclarationNumber}): {Description}",
                                boeDocument.Id, boeDocument.DeclarationNumber ?? "N/A", goodsDescriptionRecord.Value);
                        }
                    }

                    if (manifestItems.Any())
                    {
                        _logger.LogInfo("GetICUMSData", "Found {Count} manifest item(s) for BOE {BOEId}",
                            manifestItems.Count, boeDocument.Id);

                        // Extract cargo fields from manifest items
                        // For multiple items, aggregate or show per-item
                        var manifestItemsCount = manifestItems.Count;
                        foreach (var item in manifestItems)
                        {
                            var itemPrefix = manifestItemsCount > 1 ? $"Item {item.ItemNo ?? item.ItemIndex}: " : "";

                            if (!string.IsNullOrWhiteSpace(item.HsCode))
                            {
                                icumsRecords.Add(new ICUMSDataRecord
                                {
                                    Field = $"{itemPrefix}HS Code",
                                    Value = item.HsCode,
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boeDocument.HouseBl ?? boeDocument.BlNumber ?? "N/A"
                                });
                            }

                            if (!string.IsNullOrWhiteSpace(item.Description))
                            {
                                icumsRecords.Add(new ICUMSDataRecord
                                {
                                    Field = $"{itemPrefix}Item Description",
                                    Value = item.Description,
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boeDocument.HouseBl ?? boeDocument.BlNumber ?? "N/A"
                                });
                            }

                            if (item.Quantity.HasValue && item.Quantity.Value > 0)
                            {
                                icumsRecords.Add(new ICUMSDataRecord
                                {
                                    Field = $"{itemPrefix}Quantity",
                                    Value = $"{item.Quantity.Value:N2} {item.Unit ?? ""}".Trim(),
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boeDocument.HouseBl ?? boeDocument.BlNumber ?? "N/A"
                                });
                            }

                            if (item.Weight.HasValue && item.Weight.Value > 0)
                            {
                                icumsRecords.Add(new ICUMSDataRecord
                                {
                                    Field = $"{itemPrefix}Weight",
                                    Value = $"{item.Weight.Value:N2} kg",
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boeDocument.HouseBl ?? boeDocument.BlNumber ?? "N/A"
                                });
                            }

                            if (item.ItemFob.HasValue && item.ItemFob.Value > 0)
                            {
                                icumsRecords.Add(new ICUMSDataRecord
                                {
                                    Field = $"{itemPrefix}FOB Value",
                                    Value = $"{item.ItemFob.Value:C} {item.FobCurrency ?? ""}".Trim(),
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boeDocument.HouseBl ?? boeDocument.BlNumber ?? "N/A"
                                });
                            }

                            if (item.ItemDutyPaid.HasValue && item.ItemDutyPaid.Value > 0)
                            {
                                icumsRecords.Add(new ICUMSDataRecord
                                {
                                    Field = $"{itemPrefix}Duty Paid",
                                    Value = $"{item.ItemDutyPaid.Value:N2} GHS",
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boeDocument.HouseBl ?? boeDocument.BlNumber ?? "N/A"
                                });
                            }

                            if (!string.IsNullOrWhiteSpace(item.CountryOfOrigin))
                            {
                                icumsRecords.Add(new ICUMSDataRecord
                                {
                                    Field = $"{itemPrefix}Item Country of Origin",
                                    Value = item.CountryOfOrigin,
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boeDocument.HouseBl ?? boeDocument.BlNumber ?? "N/A"
                                });
                            }

                            if (!string.IsNullOrWhiteSpace(item.Cpc))
                            {
                                icumsRecords.Add(new ICUMSDataRecord
                                {
                                    Field = $"{itemPrefix}CPC",
                                    Value = item.Cpc,
                                    Category = "Cargo Info",
                                    IsRequired = false,
                                    HouseBL = boeDocument.HouseBl ?? boeDocument.BlNumber ?? "N/A"
                                });
                            }
                        }
                    }
                    else
                    {
                        _logger.LogWarning("GetICUMSData", "No manifest items found for BOE {BOEId}",
                            new { BOEId = boeDocument.Id });
                    }

                    // Add House BL identifier to each record for grouping
                    foreach (var record in icumsRecords)
                    {
                        record.HouseBL = boeDocument.HouseBl ?? boeDocument.BlNumber ?? "N/A";
                    }

                    allICUMSRecords.AddRange(icumsRecords);

                    // ✅ MEMORY FIX: JsonDocument is automatically disposed by 'using' statement in the if block above
                    // No need to manually dispose here
                } // End foreach boeDocument

                _logger.LogInfo("GetICUMSData", "Generated {Count} total ICUMS record(s) from {BoeCount} BOE document(s)",
                    allICUMSRecords.Count, boeDocuments.Count);

                // ✅ Phase 2: Return FullBOEDataRecord if full=true
                if (full)
                {
                    // Handle duplicate field names by taking the last value (or first, depending on preference)
                    var allFieldsDict = new Dictionary<string, object>();
                    foreach (var record in allICUMSRecords)
                    {
                        allFieldsDict[record.Field] = record.Value; // This will overwrite duplicates with the last value
                    }

                    // ✅ FALLBACK: Extract grouping fields from RawJsonData if missing from records
                    var extractedBlNumber = allICUMSRecords.FirstOrDefault(r => r.Field == "BL Number")?.Value;
                    var extractedDeclarationNumber = allICUMSRecords.FirstOrDefault(r => r.Field == "Declaration Number")?.Value;

                    // If grouping fields are missing, try to extract from RawJsonData
                    if (string.IsNullOrWhiteSpace(extractedBlNumber) || string.IsNullOrWhiteSpace(extractedDeclarationNumber))
                    {
                        var firstBoeWithJson = boeDocuments.FirstOrDefault(b => !string.IsNullOrWhiteSpace(b.RawJsonData));
                        if (firstBoeWithJson != null)
                        {
                            var (jsonBlNumber, jsonDeclNumber) = ExtractGroupingFieldsFromRawJson(firstBoeWithJson.RawJsonData);

                            if (string.IsNullOrWhiteSpace(extractedBlNumber) && !string.IsNullOrWhiteSpace(jsonBlNumber))
                            {
                                extractedBlNumber = jsonBlNumber;
                                _logger.LogInfo("GetICUMSData", "Extracted BlNumber from RawJsonData for container {Container}: {BlNumber}",
                                    new { Container = containerNumber, BlNumber = extractedBlNumber });
                            }

                            if (string.IsNullOrWhiteSpace(extractedDeclarationNumber) && !string.IsNullOrWhiteSpace(jsonDeclNumber))
                            {
                                extractedDeclarationNumber = jsonDeclNumber;
                                _logger.LogInfo("GetICUMSData", "Extracted DeclarationNumber from RawJsonData for container {Container}: {DeclarationNumber}",
                                    new { Container = containerNumber, DeclarationNumber = extractedDeclarationNumber });
                            }
                        }
                    }

                    var fullRecord = new FullBOEDataRecordDto
                    {
                        ContainerNumber = containerNumber,
                        BOENumber = allICUMSRecords.FirstOrDefault(r => r.Field == "BOE Number")?.Value,
                        DeclarationNumber = extractedDeclarationNumber,
                        RotationNumber = allICUMSRecords.FirstOrDefault(r => r.Field == "Rotation Number")?.Value,
                        ConsigneeName = allICUMSRecords.FirstOrDefault(r => r.Field == "Consignee Name")?.Value,
                        BlNumber = extractedBlNumber,
                        HouseBl = allICUMSRecords.FirstOrDefault(r => r.Field == "House BL")?.Value,
                        ClearanceType = allICUMSRecords.FirstOrDefault(r => r.Field == "Clearance Type")?.Value,
                        AllFields = allFieldsDict,
                        AvailableFields = allICUMSRecords.Select(r => r.Field).Distinct().ToList(),
                        MissingFields = new List<string>() // Could compare against expected fields if needed
                    };

                    _logger.LogInfo("GetICUMSData", "Returning full BOE data for container {ContainerNumber} with {FieldCount} fields",
                        new { ContainerNumber = containerNumber, FieldCount = fullRecord.AllFields.Count });

                    return Ok(fullRecord);
                }

                // Default: Return paginated result
                var result = new PagedResult<ICUMSDataRecord>
                {
                    Data = allICUMSRecords,
                    TotalCount = allICUMSRecords.Count,
                    Page = page,
                    PageSize = pageSize,
                    TotalPages = (int)Math.Ceiling((double)allICUMSRecords.Count / pageSize)
                };

                _logger.LogInfo("GetICUMSData", "Found {Count} ICUMS records for container {ContainerNumber} (page {Page})",
                    new { Count = allICUMSRecords.Count, ContainerNumber = containerNumber, Page = page });

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetICUMSData", "Error getting ICUMS data for container {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Extracts unmapped fields from BOEDocument and converts them to ICUMSDataRecord format
        /// </summary>
        /// <summary>
        /// Extract value from JsonElement while JsonDocument is alive (helper for pre-extraction)
        /// </summary>
        private string? GetValueFromJsonElement(JsonElement? jsonElement, JsonElement? rootElement, params string[] jsonPropertyNames)
        {
            // Try to extract from JSON section element if available
            if (jsonElement.HasValue)
            {
                var element = jsonElement.Value;

                if (element.ValueKind == JsonValueKind.Object)
                {
                    // First, try exact property name matches (case-sensitive)
                    foreach (var propName in jsonPropertyNames)
                    {
                        if (element.TryGetProperty(propName, out var prop))
                        {
                            var value = ExtractJsonValue(prop);
                            if (value != null)
                                return value;
                        }
                    }

                    // If exact matches failed, try case-insensitive matching
                    var propNameUpperSet = new HashSet<string>(jsonPropertyNames.Select(p => p.ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in element.EnumerateObject())
                    {
                        if (propNameUpperSet.Contains(prop.Name.ToUpperInvariant()))
                        {
                            var value = ExtractJsonValue(prop.Value);
                            if (value != null)
                                return value;
                        }
                    }
                }
            }

            // If section element didn't yield results, try root element
            if (rootElement.HasValue)
            {
                var root = rootElement.Value;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    foreach (var propName in jsonPropertyNames)
                    {
                        if (root.TryGetProperty(propName, out var prop))
                        {
                            var value = ExtractJsonValue(prop);
                            if (value != null)
                                return value;
                        }
                    }

                    // Case-insensitive search in root
                    var propNameUpperSet = new HashSet<string>(jsonPropertyNames.Select(p => p.ToUpperInvariant()), StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in root.EnumerateObject())
                    {
                        if (propNameUpperSet.Contains(prop.Name.ToUpperInvariant()))
                        {
                            var value = ExtractJsonValue(prop.Value);
                            if (value != null)
                                return value;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Get value from entity property, falling back to RawJsonData if property is null/empty
        /// Enhanced with case-insensitive matching and broader property search
        /// Also searches root element if section element is not provided
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

                // ✅ FIX: Check if element is an Object type before trying to access properties
                // Null elements will cause "requires an element of type 'Object', but the target element has type 'Null'" error
                if (element.ValueKind != JsonValueKind.Object && element.ValueKind != JsonValueKind.Null)
                {
                    // If it's not an object or null, try to extract value directly
                    var directValue = ExtractJsonValue(element);
                    if (directValue != null)
                        return directValue;
                }

                // Only proceed with property access if element is an Object
                if (element.ValueKind == JsonValueKind.Object)
                {
                    // First, try exact property name matches (case-sensitive)
                    foreach (var propName in jsonPropertyNames)
                    {
                        if (element.TryGetProperty(propName, out var prop))
                        {
                            var value = ExtractJsonValue(prop);
                            if (value != null)
                                return value;
                        }
                    }
                }

                // If exact matches failed, try case-insensitive matching
                if (element.ValueKind == JsonValueKind.Object)
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
                    // This helps find properties like "ClearanceType" when searching for "ClearanceType" or "CLEARANCETYPE"
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

        private List<ICUMSDataRecord> ExtractUnmappedFieldsForICUMS(Core.Models.BOEDocument document)
        {
            var unmappedFields = new List<ICUMSDataRecord>();

            // Extract from structured columns (fields 1-20)
            for (int i = 1; i <= 20; i++)
            {
                var labelProp = typeof(Core.Models.BOEDocument).GetProperty($"UnmappedField{i}Label");
                var valueProp = typeof(Core.Models.BOEDocument).GetProperty($"UnmappedField{i}Value");

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
                    _ => "Data Info"
                };

                unmappedFields.Add(new ICUMSDataRecord
                {
                    Field = $"{section}:{fieldName}",  // Keep section prefix for identification
                    Value = value ?? "N/A",
                    Category = "Additional Fields",  // Special category for unmapped fields
                    IsRequired = false  // Unmapped fields are never required
                });
            }

            // If there are unmapped fields and overflow, add an indicator
            if (document.UnmappedFieldsCount > 20 && document.UnmappedFieldsOverflow)
            {
                unmappedFields.Add(new ICUMSDataRecord
                {
                    Field = "⚠️ Additional Unmapped Fields",
                    Value = $"{document.UnmappedFieldsCount - 20} more field(s) available in Raw JSON",
                    Category = "Data Info",
                    IsRequired = false
                });
            }

            return unmappedFields;
        }

        /// <summary>
        /// Get image metadata for a container (uses Image Processing Pipeline for detection, existing endpoints for serving)
        /// </summary>
        [HttpGet("images/{containerNumber}")]
        public async Task<ActionResult<List<ImageMetadataDto>>> GetImageMetadata(string containerNumber)
        {
            try
            {
                _logger.LogInfo("GetImageMetadata", "🔍 Getting image metadata for container {ContainerNumber} using Image Processing Pipeline", new { ContainerNumber = containerNumber });

                // ✅ FIX: Add timeout to prevent long-running queries
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

                var images = new List<ImageMetadataDto>();

                // ✅ FIX: Get ALL images for container, not just one
                // Check FS6000 scanner (can have multiple images per scan)
                // ✅ FIX: Use projection to load only metadata, not BLOB data
                // ✅ FIX: Use DATALENGTH to get ImageData size without loading BLOB
                var fs6000Scans = await _context.FS6000Scans
                    .Where(s => s.ContainerNumber == containerNumber)
                    .OrderByDescending(s => s.ScanTime)
                    .Select(s => new
                    {
                        s.Id,
                        s.ContainerNumber,
                        s.ScanTime,
                        Images = s.Images.Select(i => new
                        {
                            i.Id,
                            i.ImageType,
                            i.FileName,
                            // ✅ FIX: Use FileSizeBytes if available, otherwise use DATALENGTH to get size without loading BLOB
                            FileSizeBytes = i.FileSizeBytes ?? (i.ImageData != null ? (int?)i.ImageData.Length : null) ?? 0,
                            i.CreatedAt
                        }).ToList()
                    })
                    .ToListAsync(cts.Token);

                if (fs6000Scans.Any())
                {
                    var publicBaseUrl = _configuration["ApiSettings:PublicBaseUrl"];
                    if (string.IsNullOrEmpty(publicBaseUrl))
                    {
                        publicBaseUrl = $"{Request.Scheme}://{Request.Host}";
                    }

                    int imageId = 1;
                    foreach (var scan in fs6000Scans)
                    {
                        if (scan.Images != null && scan.Images.Any())
                        {
                            // ✅ FIX: Return ALL images from ALL scans, not just one
                            // ✅ Use unified image processing pipeline endpoint for all heavy lifting
                            // v2.10.0: filter out the 3 raw-channel blobs (HighEnergy, LowEnergy,
                            // Material). They're composite INPUTS, not viewable inspection images —
                            // exposing them as separate image cards was the "4 images but only 1
                            // loads" UX bug. The single-canvas viewer's mode toolbar makes these
                            // channels reachable as named recipes (bw / inverse / high-pen /
                            // organic-strip / metal-strip / diff) built from the 3 raw channels,
                            // so no data access is lost — just cleaner presentation.
                            var userFacing = scan.Images
                                .Where(i => i.ImageType != "HighEnergy"
                                         && i.ImageType != "LowEnergy"
                                         && i.ImageType != "Material")
                                .OrderBy(i => i.ImageType);
                            foreach (var image in userFacing)
                            {
                                // ✅ Use unified pipeline endpoint with imageType parameter
                                // Pipeline handles all processing, conversion, and serving correctly
                                var imageCacheBuster = $"&v={scan.ScanTime.Ticks}"; // ✅ FIX: Use & since we already have imageType param
                                var imageTypeParam = image.ImageType; // Main, Icon, CCR, LPR, etc.
                                images.Add(new ImageMetadataDto
                                {
                                    Id = imageId++,
                                    ImageType = $"FS6000-{image.ImageType}",
                                    FileName = image.FileName,
                                    // ✅ FIX: Use calculated FileSizeBytes (already computed in query above)
                                    FileSizeBytes = image.FileSizeBytes,
                                    CreatedAt = scan.ScanTime,
                                    // ✅ Use unified image processing pipeline endpoint - handles all heavy lifting
                                    ThumbnailUrl = $"{publicBaseUrl}/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?imageType={Uri.EscapeDataString(imageTypeParam)}&size=thumbnail{imageCacheBuster}",
                                    FullImageUrl = $"{publicBaseUrl}/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?imageType={Uri.EscapeDataString(imageTypeParam)}&size=full{imageCacheBuster}"
                                });
                            }
                        }
                        else
                        {
                            // Fallback: Use unified endpoint if no images in database.
                            // ✅ FIX: Only add fallback ONCE per container to avoid "load twice, image not" issue.
                            // Multiple FS6000 scans with no images would each add a fallback - skip if we already have one.
                            // Also skip if we'll add ASE below - avoid duplicate entries for same physical image.
                            if (!images.Any(i => i.ImageType == "FS6000" || i.ImageType?.StartsWith("FS6000-") == true))
                            {
                                var imageMetadata = await _imageProcessingService.GetImageMetadataAsync(containerNumber);
                                if (imageMetadata != null && imageMetadata.FileSizeBytes > 0)
                                {
                                    var cacheBuster = $"&v={scan.ScanTime.Ticks}";
                                    images.Add(new ImageMetadataDto
                                    {
                                        Id = imageId++,
                                        ImageType = "FS6000",
                                        FileName = imageMetadata.ScannerId ?? $"FS6000_Scan_{containerNumber}.jpg",
                                        FileSizeBytes = imageMetadata.FileSizeBytes,
                                        CreatedAt = scan.ScanTime,
                                        ThumbnailUrl = $"{publicBaseUrl}/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?size=thumbnail{cacheBuster}",
                                        FullImageUrl = $"{publicBaseUrl}/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?size=full{cacheBuster}"
                                    });
                                }
                            }
                        }
                    }
                }

                // Check ASE scanner (single image per scan)
                // ✅ FIX: Only include ASE scans that have actual image data (ScanImage) - avoids 404 when ImageDisplayName exists but ScanImage is null
                // ✅ Use EF.Functions.DataLength - does NOT load BLOB, translates to DATALENGTH() in SQL
                // Require > 1000 bytes: real ASE proprietary images are 100KB+; tiny blobs are corrupted/empty
                try
                {
                    var aseScans = await _context.AseScans
                        .Where(a => a.ContainerNumber == containerNumber
                            && !string.IsNullOrEmpty(a.ImageDisplayName)
                            && a.ScanImage != null && a.ScanImage.Length > 1000)
                        .OrderByDescending(a => a.ScanTime)
                        .Select(a => new
                        {
                            a.ImageDisplayName,
                            // ✅ Don't access ScanImage - it forces BLOB loading and causes timeout
                            // ImageSize will be set to 0 (we don't need exact size for metadata)
                            a.ScanTime
                        })
                        .ToListAsync(cts.Token);

                    if (aseScans.Any())
                    {
                        var publicBaseUrl = _configuration["ApiSettings:PublicBaseUrl"];
                        if (string.IsNullOrEmpty(publicBaseUrl))
                        {
                            publicBaseUrl = $"{Request.Scheme}://{Request.Host}";
                        }

                        // ✅ FIX: Avoid duplicate entries when we already have FS6000 fallback for same container.
                        // FS6000 fallback (no imageType) and ASE (imageType=ASE) can serve same image - skip ASE to avoid "load twice".
                        var hasOverlappingFallback = images.Any(i => i.ImageType == "FS6000");
                        if (!hasOverlappingFallback)
                        {
                            int imageId = images.Count + 1;
                            foreach (var scan in aseScans)
                            {
                                var cacheBuster = $"&v={scan.ScanTime.Ticks}";
                                images.Add(new ImageMetadataDto
                                {
                                    Id = imageId++,
                                    ImageType = "ASE",
                                    FileName = scan.ImageDisplayName ?? $"ASE_Scan_{containerNumber}.jpg",
                                    FileSizeBytes = 0, // ✅ Size not available without loading BLOB - not needed for metadata
                                    CreatedAt = scan.ScanTime,
                                    ThumbnailUrl = $"{publicBaseUrl}/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?imageType=ASE&size=thumbnail{cacheBuster}",
                                    FullImageUrl = $"{publicBaseUrl}/api/ImageProcessing/container/{Uri.EscapeDataString(containerNumber)}/complete/image?imageType=ASE&size=full{cacheBuster}"
                                });
                            }
                        }
                        else
                        {
                            _logger.LogInfo("GetImageMetadata", "Skipping ASE metadata for {Container} - FS6000 fallback already added (avoids duplicate load)", new { Container = containerNumber });
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    _logger.LogWarning("GetImageMetadata", "⏱️ ASE query timed out for container {ContainerNumber} - skipping ASE images", new { ContainerNumber = containerNumber });
                }
                catch (Exception ex)
                {
                    _logger.LogError("GetImageMetadata", "⚠️ Error querying ASE scans for container {ContainerNumber} - continuing with other scanners", ex, new { ContainerNumber = containerNumber });
                }

                if (!images.Any())
                {
                    _logger.LogWarning("GetImageMetadata", "⚠️ No images found for container {ContainerNumber}", new { ContainerNumber = containerNumber });
                }

                _logger.LogInfo("GetImageMetadata", "Returning {Count} image(s) for container {ContainerNumber}", new { Count = images.Count, ContainerNumber = containerNumber });

                return Ok(images);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetImageMetadata", "❌ Error getting image metadata for container {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get ASE image thumbnail - converts proprietary ASE format to browser-friendly JPEG
        /// </summary>
        [AllowAnonymous]
        [HttpGet("image/ase/thumbnail")]
        public async Task<ActionResult> GetAseImageThumbnail([FromQuery] string container)
        {
            try
            {
                _logger.LogInfo("GetAseImageThumbnail", "🔍 Getting ASE thumbnail for container {Container}", new { Container = container });

                var aseScan = await _context.AseScans
                    .Where(a => a.ContainerNumber.Contains(container) && a.ScanImage != null)
                    .OrderByDescending(a => a.ScanTime)
                    .FirstOrDefaultAsync();

                if (aseScan?.ScanImage == null)
                {
                    _logger.LogWarning("GetAseImageThumbnail", "❌ No ASE image found for container {Container}", new { Container = container });
                    return NotFound("ASE image not found");
                }

                // Convert ASE proprietary format to JPEG
                var conversionResult = await _aseConverter.ConvertAseImageToJpegAsync(aseScan.ScanImage);

                if (!conversionResult.Success || conversionResult.ImageData == null)
                {
                    _logger.LogError("GetAseImageThumbnail", "❌ Failed to convert ASE image: {Error}", null, new { Error = conversionResult.ErrorMessage });
                    return StatusCode(500, $"Image conversion failed: {conversionResult.ErrorMessage}");
                }

                _logger.LogInfo("GetAseImageThumbnail", "✅ Converted ASE to JPEG ({Size} KB) for container {Container}",
                    new { Size = conversionResult.ImageData.Length / 1024, Container = container });

                // Add cache control headers to prevent stale images
                Response.Headers.Append("Cache-Control", "no-cache, must-revalidate");
                Response.Headers.Append("Pragma", "no-cache");
                Response.Headers.Append("Expires", "0");

                // Generate ETag based on scan time and data hash
                var etag = $"\"{aseScan.ScanTime.Ticks}-{conversionResult.ImageData.Length}\"";
                Response.Headers.Append("ETag", etag);

                return File(conversionResult.ImageData, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError("GetAseImageThumbnail", "Error getting ASE thumbnail for container {Container}", ex, new { Container = container });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get ASE full image - converts proprietary ASE format to browser-friendly JPEG
        /// </summary>
        [AllowAnonymous]
        [HttpGet("image/ase/full")]
        public async Task<ActionResult> GetAseImageFull([FromQuery] string container)
        {
            try
            {
                _logger.LogInfo("GetAseImageFull", "🔍 Getting ASE full image for container {Container}", new { Container = container });

                var aseScan = await _context.AseScans
                    .Where(a => a.ContainerNumber.Contains(container) && a.ScanImage != null)
                    .OrderByDescending(a => a.ScanTime)
                    .FirstOrDefaultAsync();

                if (aseScan?.ScanImage == null)
                {
                    _logger.LogWarning("GetAseImageFull", "❌ No ASE image found for container {Container}", new { Container = container });
                    return NotFound("ASE image not found");
                }

                // Convert ASE proprietary format to JPEG
                var conversionResult = await _aseConverter.ConvertAseImageToJpegAsync(aseScan.ScanImage);

                if (!conversionResult.Success || conversionResult.ImageData == null)
                {
                    _logger.LogError("GetAseImageFull", "❌ Failed to convert ASE image: {Error}", null, new { Error = conversionResult.ErrorMessage });
                    return StatusCode(500, $"Image conversion failed: {conversionResult.ErrorMessage}");
                }

                _logger.LogInfo("GetAseImageFull", "✅ Converted ASE to JPEG ({Size} MB) for container {Container}",
                    new { Size = Math.Round(conversionResult.ImageData.Length / 1024.0 / 1024.0, 2), Container = container });

                // Add cache control headers to prevent stale images
                Response.Headers.Append("Cache-Control", "no-cache, must-revalidate");
                Response.Headers.Append("Pragma", "no-cache");
                Response.Headers.Append("Expires", "0");

                // Generate ETag based on scan time and data hash
                var etag = $"\"{aseScan.ScanTime.Ticks}-{conversionResult.ImageData.Length}\"";
                Response.Headers.Append("ETag", etag);

                return File(conversionResult.ImageData, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError("GetAseImageFull", "Error getting ASE full image for container {Container}", ex, new { Container = container });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get FS6000 image thumbnail
        /// </summary>
        [HttpGet("image/fs6000/{imageId}/thumbnail")]
        public async Task<ActionResult> GetFS6000ImageThumbnail(Guid imageId)
        {
            try
            {
                var fs6000Image = await _context.FS6000Images
                    .Where(fi => fi.Id == imageId)
                    .FirstOrDefaultAsync();

                if (fs6000Image?.ImageData == null)
                {
                    return NotFound("FS6000 image not found");
                }

                // ✅ FIX: Determine correct MIME type based on image type (Main=JPEG, Icon=PNG, CCR=BMP, LPR=TIFF)
                var mimeType = GetMimeTypeFromImageType(fs6000Image.ImageType);

                // Create thumbnail (resize image)
                using var originalStream = new MemoryStream(fs6000Image.ImageData);
                using var thumbnailStream = new MemoryStream();

                // For now, return original image as thumbnail
                // In production, you'd resize it here
                await originalStream.CopyToAsync(thumbnailStream);

                return File(thumbnailStream.ToArray(), mimeType);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetFS6000ImageThumbnail", "Error getting FS6000 thumbnail for image {ImageId}", ex, new { ImageId = imageId });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get FS6000 full image
        /// </summary>
        [HttpGet("image/fs6000/{imageId}/full")]
        public async Task<ActionResult> GetFS6000ImageFull(Guid imageId)
        {
            try
            {
                var fs6000Image = await _context.FS6000Images
                    .Where(fi => fi.Id == imageId)
                    .FirstOrDefaultAsync();

                if (fs6000Image?.ImageData == null)
                {
                    return NotFound("FS6000 image not found");
                }

                // ✅ FIX: Determine correct MIME type based on image type (Main=JPEG, Icon=PNG, CCR=BMP, LPR=TIFF)
                var mimeType = GetMimeTypeFromImageType(fs6000Image.ImageType);

                return File(fs6000Image.ImageData, mimeType);
            }
            catch (Exception ex)
            {
                _logger.LogError("GetFS6000ImageFull", "Error getting FS6000 full image for image {ImageId}", ex, new { ImageId = imageId });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get FS6000 image thumbnail by container number
        /// </summary>
        [HttpGet("image/fs6000/thumbnail")]
        public async Task<ActionResult> GetFS6000ImageThumbnailByContainer([FromQuery] string container)
        {
            try
            {
                _logger.LogInfo("GetFS6000ImageThumbnailByContainer", "🔍 Getting FS6000 thumbnail for container {Container}", new { Container = container });

                var fs6000Scan = await _context.FS6000Scans
                    .Include(s => s.Images)
                    .Where(s => s.ContainerNumber == container)
                    .OrderByDescending(s => s.ScanTime)
                    .FirstOrDefaultAsync();

                if (fs6000Scan?.Images == null || !fs6000Scan.Images.Any())
                {
                    _logger.LogWarning("GetFS6000ImageThumbnailByContainer", "❌ No FS6000 images found for container {Container}", new { Container = container });
                    return NotFound("FS6000 image not found");
                }

                var image = fs6000Scan.Images.FirstOrDefault(i => i.ImageData != null);
                if (image?.ImageData == null)
                {
                    return NotFound("FS6000 image data not available");
                }

                _logger.LogInfo("GetFS6000ImageThumbnailByContainer", "✅ Returning FS6000 image ({Size} KB) for container {Container}",
                    new { Size = Math.Round(image.ImageData.Length / 1024.0, 2), Container = container });

                // Add cache control headers
                Response.Headers.Append("Cache-Control", "no-cache, must-revalidate");
                Response.Headers.Append("Pragma", "no-cache");
                Response.Headers.Append("Expires", "0");

                return File(image.ImageData, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError("GetFS6000ImageThumbnailByContainer", "Error getting FS6000 thumbnail for container {Container}", ex, new { Container = container });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get FS6000 full image by container number
        /// </summary>
        [HttpGet("image/fs6000/full")]
        public async Task<ActionResult> GetFS6000ImageFullByContainer([FromQuery] string container)
        {
            try
            {
                _logger.LogInfo("GetFS6000ImageFullByContainer", "🔍 Getting FS6000 full image for container {Container}", new { Container = container });

                var fs6000Scan = await _context.FS6000Scans
                    .Include(s => s.Images)
                    .Where(s => s.ContainerNumber == container)
                    .OrderByDescending(s => s.ScanTime)
                    .FirstOrDefaultAsync();

                if (fs6000Scan?.Images == null || !fs6000Scan.Images.Any())
                {
                    _logger.LogWarning("GetFS6000ImageFullByContainer", "❌ No FS6000 images found for container {Container}", new { Container = container });
                    return NotFound("FS6000 image not found");
                }

                var image = fs6000Scan.Images.FirstOrDefault(i => i.ImageData != null);
                if (image?.ImageData == null)
                {
                    return NotFound("FS6000 image data not available");
                }

                _logger.LogInfo("GetFS6000ImageFullByContainer", "✅ Returning FS6000 full image ({Size} MB) for container {Container}",
                    new { Size = Math.Round(image.ImageData.Length / 1024.0 / 1024.0, 2), Container = container });

                // Add cache control headers
                Response.Headers.Append("Cache-Control", "no-cache, must-revalidate");
                Response.Headers.Append("Pragma", "no-cache");
                Response.Headers.Append("Expires", "0");

                return File(image.ImageData, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogError("GetFS6000ImageFullByContainer", "Error getting FS6000 full image for container {Container}", ex, new { Container = container });
                return StatusCode(500, "Internal server error");
            }
        }

        /// <summary>
        /// Get full image data with manipulation tools
        /// </summary>
        [HttpGet("image/{imageId}/full")]
        public Task<ActionResult<ImageWithTools>> GetFullImage(int imageId)
        {
            try
            {
                _logger.LogInfo("GetFullImage", "Getting full image data for image ID {ImageId}", new { ImageId = imageId });

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));

                // For now, return mock data
                var imageWithTools = new ImageWithTools
                {
                    Id = imageId,
                    ContainerNumber = "MSKU1234567",
                    ImagePath = $"/images/scan_{imageId}.jpg",
                    FileName = $"Image_{imageId}",
                    FileSize = 2 * 1024 * 1024,
                    ScanDate = DateTime.UtcNow.AddDays(-1),
                    ScannerType = "FS6000",
                    ProcessingStatus = "Completed",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    AvailableTools = new List<string>
                    {
                        "zoom", "pan", "rotate", "brightness", "contrast", "annotate", "measure", "export"
                    }
                };

                _logger.LogInfo("GetFullImage", "Found full image data for image ID {ImageId}", new { ImageId = imageId });

                return Task.FromResult<ActionResult<ImageWithTools>>(Ok(imageWithTools));
            }
            catch (Exception ex)
            {
                _logger.LogError("GetFullImage", "Error getting full image data for image ID {ImageId}", ex, new { ImageId = imageId });
                return Task.FromResult<ActionResult<ImageWithTools>>(StatusCode(500, "Internal server error"));
            }
        }

        /// <summary>
        /// Search across all data types for a container
        /// </summary>
        [HttpGet("search/{containerNumber}")]
        public async Task<ActionResult<UnifiedSearchResults>> SearchContainerData(
            string containerNumber,
            [FromQuery] string query = "")
        {
            try
            {
                _logger.LogInfo("SearchContainerData", "Searching container data for {ContainerNumber} with query: {Query}",
                    new { ContainerNumber = containerNumber, Query = query });

                if (string.IsNullOrWhiteSpace(query))
                {
                    return Ok(new UnifiedSearchResults { ContainerNumber = containerNumber, Results = new List<SearchResult>() });
                }

                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));

                var searchTerm = query.ToLower();
                var results = new List<SearchResult>();

                // Get containers ready for submission
                var readyContainers = await _containerDataMapperService.GetContainersReadyForSubmissionAsync(100);
                var container = readyContainers.FirstOrDefault(c => c.ContainerNumber == containerNumber);

                if (container != null)
                {
                    // Search in container data
                    if (container.ContainerNumber.ToLower().Contains(searchTerm) ||
                        container.ScannerType.ToLower().Contains(searchTerm))
                    {
                        results.Add(new SearchResult
                        {
                            Type = "Scanner",
                            Id = container.ScannerDataId,
                            Title = $"Container {container.ContainerNumber}",
                            Description = $"{container.ScannerType} - Completed",
                            Date = container.ScanDate,
                            Relevance = CalculateRelevance(container.ContainerNumber, container.ScannerType, "Completed", searchTerm)
                        });
                    }

                    // Search in ICUMS data
                    if (container.ContainerNumber.ToLower().Contains(searchTerm))
                    {
                        results.Add(new SearchResult
                        {
                            Type = "ICUMS",
                            Id = container.ICUMSDataId,
                            Title = $"BOE_{container.ContainerNumber}",
                            Description = $"DEC_{container.ContainerNumber} - Active",
                            Date = container.ICUMSDataDate,
                            Relevance = CalculateRelevance($"BOE_{container.ContainerNumber}", $"DEC_{container.ContainerNumber}", "Active", searchTerm)
                        });
                    }
                }

                // Sort by relevance
                results = results.OrderByDescending(r => r.Relevance).ToList();

                var searchResults = new UnifiedSearchResults
                {
                    ContainerNumber = containerNumber,
                    Query = query,
                    Results = results,
                    TotalResults = results.Count
                };

                _logger.LogInfo("SearchContainerData", "Found {Count} search results for container {ContainerNumber}",
                    new { Count = results.Count, ContainerNumber = containerNumber });

                return Ok(searchResults);
            }
            catch (Exception ex)
            {
                _logger.LogError("SearchContainerData", "Error searching container data for {ContainerNumber}", ex, new { ContainerNumber = containerNumber });
                return StatusCode(500, "Internal server error");
            }
        }

        private int CalculateRelevance(string field1, string field2, string field3, string searchTerm)
        {
            var score = 0;
            if (field1.ToLower().Contains(searchTerm)) score += 3;
            if (field2.ToLower().Contains(searchTerm)) score += 2;
            if (field3.ToLower().Contains(searchTerm)) score += 1;
            return score;
        }

        /// <summary>
        /// Get MIME type based on FS6000 image type
        /// </summary>
        private static string GetMimeTypeFromImageType(string imageType)
        {
            return imageType?.ToLowerInvariant() switch
            {
                "main" => "image/jpeg",
                "icon" => "image/png",
                "ccr" => "image/bmp",
                "lpr" => "image/tiff",
                "manifest" => "application/pdf",
                _ => "image/jpeg" // Default to JPEG
            };
        }
    }

}
