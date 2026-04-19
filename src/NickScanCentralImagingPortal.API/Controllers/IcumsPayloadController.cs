using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using NickScanCentralImagingPortal.Core.Models;
using Npgsql;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class IcumsPayloadController : ControllerBase
    {
        private readonly IConfiguration _configuration;
        private readonly ILogger<IcumsPayloadController> _logger;
        private readonly IHttpClientFactory _httpClientFactory;

        public IcumsPayloadController(
            IConfiguration configuration,
            ILogger<IcumsPayloadController> logger,
            IHttpClientFactory httpClientFactory)
        {
            _configuration = configuration;
            _logger = logger;
            _httpClientFactory = httpClientFactory;
        }

        private string GetOutboxRoot()
        {
            return _configuration["ICUMS:Submission:OutputFolder"]
                ?? Environment.GetEnvironmentVariable("ICUMS_Submission_OutputFolder")
                ?? @"C:\Shared\NSCIM_PRODUCTION\Data\ICUMS\Outbox";
        }

        private async Task<bool> IsLiveSubmitEnabledAsync()
        {
            try
            {
                var connStr = _configuration.GetConnectionString("NS_CIS_Connection");
                if (!string.IsNullOrEmpty(connStr))
                {
                    await using var conn = new NpgsqlConnection(connStr);
                    await conn.OpenAsync();
                    await using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT settingvalue FROM systemsettings WHERE settingkey = 'Submission.LiveSubmitEnabled' AND isactive = true LIMIT 1";
                    var result = await cmd.ExecuteScalarAsync();
                    if (result != null && result != DBNull.Value)
                        return result.ToString()!.Equals("true", StringComparison.OrdinalIgnoreCase);
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Could not read LiveSubmitEnabled from DB, falling back to config");
            }

            return _configuration.GetValue<bool>("ICUMS:Submission:LiveSubmitEnabled", false);
        }

        [HttpGet("list")]
        [Authorize(Policy = "AdminOnly")]
        public IActionResult ListPayloads([FromQuery] string? subfolder = null, [FromQuery] int limit = 50)
        {
            try
            {
                var root = GetOutboxRoot();
                var targetDir = string.IsNullOrEmpty(subfolder)
                    ? root
                    : Path.Combine(root, subfolder);

                if (!Directory.Exists(targetDir))
                    return Ok(new { files = Array.Empty<object>(), folder = targetDir, exists = false });

                var files = Directory.GetFiles(targetDir, "*.json")
                    .Select(f => new FileInfo(f))
                    .OrderByDescending(f => f.LastWriteTimeUtc)
                    .Take(limit)
                    .Select(f => new
                    {
                        name = f.Name,
                        sizeBytes = f.Length,
                        lastModified = f.LastWriteTimeUtc,
                        subfolder = Path.GetRelativePath(root, f.DirectoryName ?? root)
                    })
                    .ToList();

                var subfolders = Directory.GetDirectories(targetDir)
                    .Select(d => Path.GetFileName(d))
                    .ToList();

                return Ok(new { files, folder = targetDir, exists = true, subfolders });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error listing ICUMS payloads");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("read")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> ReadPayload([FromQuery] string fileName, [FromQuery] string? subfolder = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return BadRequest(new { error = "fileName is required" });

                var sanitized = Path.GetFileName(fileName);
                var root = GetOutboxRoot();
                var targetDir = string.IsNullOrEmpty(subfolder)
                    ? root
                    : Path.Combine(root, subfolder);
                var filePath = Path.Combine(targetDir, sanitized);

                if (!filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = "Invalid path" });

                if (!System.IO.File.Exists(filePath))
                    return NotFound(new { error = "File not found", fileName = sanitized });

                var content = await System.IO.File.ReadAllTextAsync(filePath);

                object? parsed = null;
                try { parsed = JsonSerializer.Deserialize<JsonElement>(content); }
                catch { /* return raw if not valid JSON */ }

                var info = new FileInfo(filePath);
                return Ok(new
                {
                    fileName = sanitized,
                    subfolder = subfolder ?? ".",
                    sizeBytes = info.Length,
                    lastModified = info.LastWriteTimeUtc,
                    content = parsed ?? (object)content
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error reading ICUMS payload {FileName}", fileName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("image")]
        [AllowAnonymous]
        public async Task<IActionResult> GetPayloadImage([FromQuery] string fileName, [FromQuery] string? subfolder = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(fileName))
                    return BadRequest(new { error = "fileName is required" });

                var sanitized = Path.GetFileName(fileName);
                var root = GetOutboxRoot();
                var targetDir = string.IsNullOrEmpty(subfolder)
                    ? root
                    : Path.Combine(root, subfolder);
                var filePath = Path.Combine(targetDir, sanitized);

                if (!filePath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                    return BadRequest(new { error = "Invalid path" });

                if (!System.IO.File.Exists(filePath))
                    return NotFound(new { error = "File not found" });

                var content = await System.IO.File.ReadAllTextAsync(filePath);
                using var doc = JsonDocument.Parse(content);
                var rootEl = doc.RootElement;

                string? base64 = null;
                if (rootEl.TryGetProperty("scanData", out var scanData) &&
                    scanData.TryGetProperty("ImageDocument", out var imgProp) &&
                    imgProp.ValueKind == JsonValueKind.String)
                {
                    base64 = imgProp.GetString();
                }
                else if (rootEl.TryGetProperty("ImageDocument", out var imgProp2) &&
                         imgProp2.ValueKind == JsonValueKind.String)
                {
                    base64 = imgProp2.GetString();
                }

                if (string.IsNullOrEmpty(base64))
                    return NotFound(new { error = "No ImageDocument found in payload" });

                var imageBytes = Convert.FromBase64String(base64);

                var contentType = "image/jpeg";
                if (imageBytes.Length >= 8 && imageBytes[0] == 0x89 && imageBytes[1] == 0x50)
                    contentType = "image/png";

                return File(imageBytes, contentType);
            }
            catch (FormatException)
            {
                return BadRequest(new { error = "ImageDocument is not valid base64" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting image from payload {FileName}", fileName);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("summary")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> GetSummary()
        {
            try
            {
                var root = GetOutboxRoot();
                if (!Directory.Exists(root))
                    return Ok(new { exists = false, root });

                var internalFiles = Directory.GetFiles(root, "*.json");
                var icumsDir = Path.Combine(root, "ICUMS");
                var icumsFiles = Directory.Exists(icumsDir)
                    ? Directory.GetFiles(icumsDir, "*.json")
                    : Array.Empty<string>();
                var ackDir = Path.Combine(icumsDir, "Acknowledged");
                var ackFiles = Directory.Exists(ackDir)
                    ? Directory.GetFiles(ackDir, "*.json")
                    : Array.Empty<string>();

                int nullDeliveryPlaceCount = 0;
                try
                {
                    var dlConnStr = _configuration.GetConnectionString("ICUMS_Downloads_Connection");
                    if (!string.IsNullOrEmpty(dlConnStr))
                    {
                        await using var conn = new NpgsqlConnection(dlConnStr);
                        await conn.OpenAsync();
                        await using var cmd = conn.CreateCommand();
                        cmd.CommandText = "SELECT COUNT(*) FROM boedocuments WHERE deliveryplace IS NULL OR TRIM(deliveryplace) = ''";
                        var result = await cmd.ExecuteScalarAsync();
                        if (result != null && result != DBNull.Value)
                            nullDeliveryPlaceCount = Convert.ToInt32(result);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not query NULL DeliveryPlace count from downloads DB");
                }

                return Ok(new
                {
                    exists = true,
                    root,
                    internalPayloads = new
                    {
                        count = internalFiles.Length,
                        totalSizeBytes = internalFiles.Sum(f => new FileInfo(f).Length),
                        latest = internalFiles
                            .Select(f => new FileInfo(f))
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault()?.LastWriteTimeUtc
                    },
                    icumsPayloads = new
                    {
                        count = icumsFiles.Length,
                        totalSizeBytes = icumsFiles.Sum(f => new FileInfo(f).Length),
                        latest = icumsFiles
                            .Select(f => new FileInfo(f))
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault()?.LastWriteTimeUtc
                    },
                    acknowledgedPayloads = new
                    {
                        count = ackFiles.Length,
                        totalSizeBytes = ackFiles.Sum(f => new FileInfo(f).Length),
                        latest = ackFiles
                            .Select(f => new FileInfo(f))
                            .OrderByDescending(f => f.LastWriteTimeUtc)
                            .FirstOrDefault()?.LastWriteTimeUtc
                    },
                    liveSubmitEnabled = await IsLiveSubmitEnabledAsync(),
                    nullDeliveryPlaceCount
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting ICUMS payload summary");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Calls the ICUMS readStatus API to verify receipt of submitted containers.
        /// </summary>
        [HttpPost("verify-status")]
        [Authorize(Policy = "AdminOnly")]
        public async Task<IActionResult> VerifyStatus([FromBody] VerifyStatusRequest request)
        {
            try
            {
                if (request?.ContainerNumbers == null || request.ContainerNumbers.Count == 0)
                    return BadRequest(new { error = "At least one container number is required" });

                var readStatusUrl = _configuration["ICUMS:ReadStatusUrl"];
                var interfaceKey = _configuration["ICUMS:ReadStatusKey"] ?? "IF_P01_NSCUNI_08";
                var authKey = _configuration["ICUMS:AuthKey"]
                    ?? Environment.GetEnvironmentVariable("NICKSCAN_ICUMS_AUTH_KEY")
                    ?? "";

                if (string.IsNullOrEmpty(readStatusUrl) || string.IsNullOrEmpty(authKey))
                    return StatusCode(503, new { error = "ICUMS ReadStatus API is not configured" });

                var payload = new ReadStatusRequest
                {
                    ContainerNumbers = request.ContainerNumbers.Select(c => new ContainerNumberInfo
                    {
                        RotationNumber = c.RotationNumber ?? "",
                        BlNumber = c.BlNumber,
                        ContainerNumber = c.ContainerNumber
                    }).ToList()
                };

                var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                using var httpClient = _httpClientFactory.CreateClient();
                httpClient.DefaultRequestHeaders.Clear();
                httpClient.DefaultRequestHeaders.Add("ESB_IF_ID", interfaceKey);
                httpClient.DefaultRequestHeaders.Add("ESB_AUTH_KEY", authKey);
                httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
                httpClient.Timeout = TimeSpan.FromSeconds(30);

                _logger.LogInformation("[ICUMS-READSTATUS] Verifying {Count} container(s): {Containers}",
                    request.ContainerNumbers.Count,
                    string.Join(", ", request.ContainerNumbers.Select(c => c.ContainerNumber)));

                using var content = new StringContent(json, Encoding.UTF8, "application/json");
                var response = await httpClient.PostAsync(readStatusUrl, content);
                var responseBody = await response.Content.ReadAsStringAsync();

                _logger.LogInformation("[ICUMS-READSTATUS] HTTP {Status} — Response: {Response}",
                    (int)response.StatusCode, responseBody.Length > 500 ? responseBody[..500] : responseBody);

                if (response.IsSuccessStatusCode)
                {
                    ReadStatusResponse? parsed = null;
                    try
                    {
                        parsed = JsonSerializer.Deserialize<ReadStatusResponse>(responseBody,
                            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    }
                    catch (JsonException ex)
                    {
                        _logger.LogWarning(ex, "[ICUMS-READSTATUS] Could not parse response as ReadStatusResponse");
                    }

                    return Ok(new
                    {
                        success = true,
                        httpStatus = (int)response.StatusCode,
                        results = parsed?.ReadStatus ?? new List<ReadStatusItem>(),
                        rawResponse = responseBody
                    });
                }
                else
                {
                    return Ok(new
                    {
                        success = false,
                        httpStatus = (int)response.StatusCode,
                        error = responseBody
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[ICUMS-READSTATUS] Exception verifying container status");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }

    public class VerifyStatusRequest
    {
        public List<VerifyContainerInfo> ContainerNumbers { get; set; } = new();
    }

    public class VerifyContainerInfo
    {
        public string? RotationNumber { get; set; }
        public string? BlNumber { get; set; }
        public string? ContainerNumber { get; set; }
    }
}
