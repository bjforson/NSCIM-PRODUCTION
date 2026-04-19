using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Constants;
using NickScanCentralImagingPortal.Infrastructure.Data;
using System.Net;
using System.Text;
using System.Text.Json;

namespace NickScanCentralImagingPortal.API.Controllers
{
    /// <summary>
    /// Thin HTTP proxy for the X-Ray Inspector. All heavy image math lives in
    /// the Python image-splitter service (<c>services/image-splitter/inspector/</c>).
    /// This controller exists to:
    ///   1. Gate access via NSCIM's existing permission system
    ///   2. Join scan records with the rest of the NSCIM data model
    ///      (ContainerCompletenessStatus, OriginalScanRecord, linked BOEs)
    ///   3. Forward pixel + analysis requests to the splitter over HTTP
    ///
    /// The splitter base URL is configured via <c>ImageSplitter:BaseUrl</c>
    /// (default <c>http://localhost:5320</c>), using the existing named
    /// HttpClient registration in <c>Program.cs</c>.
    /// </summary>
    [ApiController]
    [Route("api/xray-inspector")]
    [Authorize(Policy = "Permission:" + Permissions.PagesValidationXrayInspector)]
    public class XrayInspectorController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ApplicationDbContext _db;
        private readonly ILogger<XrayInspectorController> _logger;

        public XrayInspectorController(
            IHttpClientFactory httpClientFactory,
            ApplicationDbContext db,
            ILogger<XrayInspectorController> logger)
        {
            _httpClientFactory = httpClientFactory;
            _db = db;
            _logger = logger;
        }

        // ─── Search ────────────────────────────────────────────────────

        [HttpGet("search")]
        public Task<IActionResult> Search(
            [FromQuery] string q,
            [FromQuery] string scanner = "both",
            [FromQuery] int limit = 50)
        {
            var path = $"/inspector/search?q={WebUtility.UrlEncode(q)}&scanner={WebUtility.UrlEncode(scanner)}&limit={limit}";
            return ForwardGetJsonAsync(path);
        }

        // ─── Scan details (enriched with NSCIM context) ────────────────

        [HttpGet("scan/{scanner}/{id}")]
        public async Task<IActionResult> GetScanDetails(string scanner, string id)
        {
            // Pull splitter metadata
            var meta = await ForwardAndDeserializeAsync<JsonElement>($"/inspector/meta/{scanner}/{id}");
            if (meta.ValueKind == JsonValueKind.Undefined)
            {
                return StatusCode(503, new { error = "splitter unavailable" });
            }

            // Enrich with NSCIM context — linked completeness status, original scan record, BOE
            object? enrichment = null;
            try
            {
                var containerNumber = meta.TryGetProperty("record", out var rec) &&
                                      rec.TryGetProperty("container_number", out var cn) &&
                                      cn.ValueKind == JsonValueKind.String
                    ? cn.GetString()
                    : null;

                if (!string.IsNullOrWhiteSpace(containerNumber))
                {
                    var completeness = await _db.Set<NickScanCentralImagingPortal.Core.Entities.ContainerCompletenessStatus>()
                        .Where(c => c.ContainerNumber == containerNumber)
                        .Select(c => new
                        {
                            c.ContainerNumber,
                            c.ScannerType,
                            c.InspectionId,
                            c.HasScannerData,
                            c.HasImageData,
                            c.OverallCompleteness,
                            c.ScanDate,
                            c.Status,
                            c.WorkflowStage
                        })
                        .ToListAsync();

                    enrichment = new
                    {
                        containerNumber,
                        completenessRecords = completeness
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "XRay Inspector: enrichment failed for {Scanner}/{Id}", scanner, id);
            }

            return Ok(new
            {
                splitter = meta,
                nscim = enrichment
            });
        }

        // ─── Pixel rendering ───────────────────────────────────────────

        [HttpGet("image/{scanner}/{id}")]
        public Task<IActionResult> GetImage(
            string scanner,
            string id,
            [FromQuery] string channel = "main",
            [FromQuery] string transform = "raw",
            [FromQuery] string format = "png",
            [FromQuery] double loPct = 1.0,
            [FromQuery] double hiPct = 99.5,
            [FromQuery] double gamma = 1.0,
            [FromQuery] double? windowLo = null,
            [FromQuery] double? windowHi = null,
            [FromQuery] bool invert = false,
            [FromQuery] int rotateDeg = 0,
            [FromQuery] string colormap = "none")
        {
            var qs = new StringBuilder();
            qs.Append($"?channel={WebUtility.UrlEncode(channel)}");
            qs.Append($"&transform={WebUtility.UrlEncode(transform)}");
            qs.Append($"&format={WebUtility.UrlEncode(format)}");
            qs.Append($"&lo_pct={loPct}&hi_pct={hiPct}&gamma={gamma}");
            if (windowLo.HasValue) qs.Append($"&window_lo={windowLo.Value}");
            if (windowHi.HasValue) qs.Append($"&window_hi={windowHi.Value}");
            qs.Append($"&invert={(invert ? "true" : "false")}");
            qs.Append($"&rotate_deg={rotateDeg}");
            qs.Append($"&colormap={WebUtility.UrlEncode(colormap)}");
            var contentType = format == "bin" ? "application/octet-stream" : "image/png";
            return ForwardGetBinaryAsync($"/inspector/pixels/{scanner}/{id}{qs}", contentType);
        }

        [HttpGet("composite/{scanner}/{id}")]
        public Task<IActionResult> GetComposite(
            string scanner,
            string id,
            [FromQuery] string luminance = "high",
            [FromQuery] double materialStrength = 0.65)
        {
            var qs = $"?luminance={WebUtility.UrlEncode(luminance)}&material_strength={materialStrength}";
            return ForwardGetBinaryAsync($"/inspector/composite/{scanner}/{id}{qs}", "image/png");
        }

        [HttpGet("vendor-jpeg/{scanner}/{id}")]
        public Task<IActionResult> GetVendorJpeg(string scanner, string id)
        {
            return ForwardGetBinaryAsync($"/inspector/vendor-jpeg/{scanner}/{id}", "image/jpeg");
        }

        // ─── Analysis (POST JSON forwarding) ───────────────────────────

        [HttpPost("analyze/roi-stats")]
        [Authorize(Policy = "Permission:" + Permissions.XrayInspectorAnalyze)]
        public Task<IActionResult> RoiStats([FromBody] JsonElement body)
            => ForwardPostJsonAsync("/inspector/roi-stats", body);

        [HttpPost("analyze/line-profile")]
        [Authorize(Policy = "Permission:" + Permissions.XrayInspectorAnalyze)]
        public Task<IActionResult> LineProfile([FromBody] JsonElement body)
            => ForwardPostJsonAsync("/inspector/line-profile", body);

        [HttpPost("analyze/edge")]
        [Authorize(Policy = "Permission:" + Permissions.XrayInspectorAnalyze)]
        public Task<IActionResult> Edge([FromBody] JsonElement body)
            => ForwardPostBinaryAsync("/inspector/edge", body, "image/png");

        [HttpPost("analyze/threshold")]
        [Authorize(Policy = "Permission:" + Permissions.XrayInspectorAnalyze)]
        public Task<IActionResult> Threshold([FromBody] JsonElement body)
            => ForwardPostBinaryAsync("/inspector/threshold", body, "image/png");

        [HttpPost("analyze/objects")]
        [Authorize(Policy = "Permission:" + Permissions.XrayInspectorAnalyze)]
        public Task<IActionResult> Objects([FromBody] JsonElement body)
            => ForwardPostJsonAsync("/inspector/objects", body);

        [HttpPost("analyze/dual-energy-diff")]
        [Authorize(Policy = "Permission:" + Permissions.XrayInspectorAnalyze)]
        public Task<IActionResult> DualEnergyDiff(
            [FromQuery] string scanner,
            [FromQuery] string id,
            [FromQuery] int rotateDeg = 0)
        {
            var qs = $"?scanner={WebUtility.UrlEncode(scanner)}&id={WebUtility.UrlEncode(id)}&rotate_deg={rotateDeg}";
            return ForwardPostBinaryAsync($"/inspector/dual-energy-diff{qs}", null, "image/png");
        }

        // ─── Exports ───────────────────────────────────────────────────

        [HttpPost("export/roi-csv")]
        [Authorize(Policy = "Permission:" + Permissions.XrayInspectorExport)]
        public Task<IActionResult> ExportRoiCsv([FromBody] JsonElement body)
            => ForwardPostBinaryAsync("/inspector/export/roi-csv", body, "text/csv");

        [HttpPost("export/pdf-report")]
        [Authorize(Policy = "Permission:" + Permissions.XrayInspectorExport)]
        public Task<IActionResult> ExportPdfReport([FromBody] JsonElement body)
            => ForwardPostBinaryAsync("/inspector/export/pdf-report", body, "application/pdf");

        // ─── Forwarding helpers ────────────────────────────────────────

        private async Task<IActionResult> ForwardGetJsonAsync(string path)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                var resp = await client.GetAsync(path);
                var body = await resp.Content.ReadAsStringAsync();
                return new ContentResult
                {
                    Content = body,
                    ContentType = "application/json",
                    StatusCode = (int)resp.StatusCode
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[RAW-IMAGE-ENGINE] Service unreachable — GET {Path}", path);
                return StatusCode(503, new { error = "Raw Image Engine unavailable", service = "RawImageEngine", path });
            }
            catch (TaskCanceledException)
            {
                _logger.LogError("[RAW-IMAGE-ENGINE] Request timed out — GET {Path}", path);
                return StatusCode(504, new { error = "Raw Image Engine request timed out", service = "RawImageEngine", path });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAW-IMAGE-ENGINE] Unexpected error — GET {Path}", path);
                return StatusCode(503, new { error = "Raw Image Engine unavailable", service = "RawImageEngine", path });
            }
        }

        private async Task<T> ForwardAndDeserializeAsync<T>(string path)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                var resp = await client.GetAsync(path);
                if (!resp.IsSuccessStatusCode) return default!;
                var body = await resp.Content.ReadAsStringAsync();
                return JsonSerializer.Deserialize<T>(body)!;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAW-IMAGE-ENGINE] JSON fetch failed — GET {Path}", path);
                return default!;
            }
        }

        private async Task<IActionResult> ForwardGetBinaryAsync(string path, string contentType)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                var resp = await client.GetAsync(path);
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    return StatusCode((int)resp.StatusCode, new { error = err });
                }
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                return File(bytes, contentType);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[RAW-IMAGE-ENGINE] Service unreachable — GET binary {Path}", path);
                return StatusCode(503, new { error = "Raw Image Engine unavailable", service = "RawImageEngine", path });
            }
            catch (TaskCanceledException)
            {
                _logger.LogError("[RAW-IMAGE-ENGINE] Request timed out — GET binary {Path}", path);
                return StatusCode(504, new { error = "Raw Image Engine request timed out", service = "RawImageEngine", path });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAW-IMAGE-ENGINE] Unexpected error — GET binary {Path}", path);
                return StatusCode(503, new { error = "Raw Image Engine unavailable", service = "RawImageEngine", path });
            }
        }

        private async Task<IActionResult> ForwardPostJsonAsync(string path, JsonElement body)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                var payload = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
                var resp = await client.PostAsync(path, payload);
                var text = await resp.Content.ReadAsStringAsync();
                return new ContentResult
                {
                    Content = text,
                    ContentType = "application/json",
                    StatusCode = (int)resp.StatusCode
                };
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[RAW-IMAGE-ENGINE] Service unreachable — POST {Path}", path);
                return StatusCode(503, new { error = "Raw Image Engine unavailable", service = "RawImageEngine", path });
            }
            catch (TaskCanceledException)
            {
                _logger.LogError("[RAW-IMAGE-ENGINE] Request timed out — POST {Path}", path);
                return StatusCode(504, new { error = "Raw Image Engine request timed out", service = "RawImageEngine", path });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAW-IMAGE-ENGINE] Unexpected error — POST {Path}", path);
                return StatusCode(503, new { error = "Raw Image Engine unavailable", service = "RawImageEngine", path });
            }
        }

        private async Task<IActionResult> ForwardPostBinaryAsync(string path, JsonElement? body, string contentType)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                HttpContent? payload = null;
                if (body.HasValue)
                {
                    payload = new StringContent(body.Value.GetRawText(), Encoding.UTF8, "application/json");
                }
                var resp = payload != null
                    ? await client.PostAsync(path, payload)
                    : await client.PostAsync(path, new StringContent(""));
                if (!resp.IsSuccessStatusCode)
                {
                    var err = await resp.Content.ReadAsStringAsync();
                    return StatusCode((int)resp.StatusCode, new { error = err });
                }
                var bytes = await resp.Content.ReadAsByteArrayAsync();
                return File(bytes, contentType);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[RAW-IMAGE-ENGINE] Service unreachable — POST binary {Path}", path);
                return StatusCode(503, new { error = "Raw Image Engine unavailable", service = "RawImageEngine", path });
            }
            catch (TaskCanceledException)
            {
                _logger.LogError("[RAW-IMAGE-ENGINE] Request timed out — POST binary {Path}", path);
                return StatusCode(504, new { error = "Raw Image Engine request timed out", service = "RawImageEngine", path });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[RAW-IMAGE-ENGINE] Unexpected error — POST binary {Path}", path);
                return StatusCode(503, new { error = "Raw Image Engine unavailable", service = "RawImageEngine", path });
            }
        }
    }
}
