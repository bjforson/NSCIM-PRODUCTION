using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Core.Entities.Analysis;
using NickScanCentralImagingPortal.Infrastructure.Data;
using NickScanCentralImagingPortal.Services.ImageSplitter;
using System.Text;
using System.Text.Json;

namespace NickScanCentralImagingPortal.API.Controllers
{
    [ApiController]
    [Route("api/image-splitter")]
    [Authorize]
    public class ImageSplitterController : ControllerBase
    {
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<ImageSplitterController> _logger;
        private readonly ApplicationDbContext _db;
        private readonly NickScanCentralImagingPortal.Core.Security.ISignedImageUrlSigner _urlSigner;

        public ImageSplitterController(
            IHttpClientFactory httpClientFactory,
            ILogger<ImageSplitterController> logger,
            ApplicationDbContext db,
            NickScanCentralImagingPortal.Core.Security.ISignedImageUrlSigner urlSigner)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
            _db = db;
            _urlSigner = urlSigner;
        }

        [HttpGet("health")]
        public async Task<IActionResult> GetHealth() => await ForwardGetAsync("/api/health");

        [HttpGet("jobs/pending")]
        public async Task<IActionResult> GetPendingJobs()
            => await ForwardGetAsync($"/api/split/pending{Request.QueryString}");

        [HttpGet("jobs/{jobId}")]
        public async Task<IActionResult> GetJob(string jobId) => await ForwardGetAsync($"/api/split/{jobId}");

        [HttpGet("jobs/{jobId}/results")]
        public async Task<IActionResult> GetJobResults(string jobId) => await ForwardGetAsync($"/api/split/{jobId}/results");

        [HttpGet("jobs/{jobId}/results/{resultId}/image/{side}")]
        public async Task<IActionResult> GetResultImage(string jobId, string resultId, string side)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                var response = await client.GetAsync($"/api/split/{jobId}/results/{resultId}/image/{side}");
                if (!response.IsSuccessStatusCode) return StatusCode((int)response.StatusCode);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return File(bytes, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get split image");
                return StatusCode(503, new { error = "Splitter service unavailable" });
            }
        }

        [HttpPost("jobs/{jobId}/approve")]
        public async Task<IActionResult> ApproveResult(string jobId, [FromBody] JsonElement body)
            => await ForwardPostAsync($"/api/split/{jobId}/approve", body);

        [HttpPost("jobs/{jobId}/reject")]
        public async Task<IActionResult> RejectResult(string jobId, [FromBody] JsonElement body)
            => await ForwardPostAsync($"/api/split/{jobId}/reject", body);

        [HttpPost("jobs/{jobId}/manual")]
        public async Task<IActionResult> ManualSplit(string jobId, [FromBody] JsonElement body)
            => await ForwardPostAsync($"/api/split/{jobId}/manual", body);

        [HttpGet("jobs/{jobId}/original")]
        public async Task<IActionResult> GetOriginalImage(string jobId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                var response = await client.GetAsync($"/api/split/{jobId}/original");
                if (!response.IsSuccessStatusCode) return StatusCode((int)response.StatusCode);
                var bytes = await response.Content.ReadAsByteArrayAsync();
                return File(bytes, "image/jpeg");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get original image");
                return StatusCode(503, new { error = "Splitter service unavailable" });
            }
        }

        // ── Container-level split integration endpoints ──

        /// <summary>
        /// Get split options for a specific container. Returns split status and
        /// the top 2 candidate crop images for this container's position (left/right).
        /// Called by the frontend before opening the image viewer.
        /// </summary>
        [HttpGet("container/{containerNumber}/split-options")]
        public async Task<IActionResult> GetSplitOptions(string containerNumber)
        {
            try
            {
                // Find the most recent AnalysisRecord for this container
                var record = await _db.AnalysisRecords
                    .Where(r => r.ContainerNumber == containerNumber)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .FirstOrDefaultAsync();

                // Lazy detection: if AnalysisRecord exists but split fields not yet populated,
                // check OriginalScanRecords to see if this is a multi-container scan
                if (record != null && !record.IsMultiContainerScan && record.SplitStatus == null)
                {
                    var originalScan = await _db.Set<NickScanCentralImagingPortal.Core.Entities.OriginalScanRecord>()
                        .Where(o => o.DerivedRecordCount == 2 &&
                                    o.OriginalContainerNumbers.Contains(containerNumber))
                        .OrderByDescending(o => o.ScanTime)
                        .FirstOrDefaultAsync();

                    if (originalScan != null)
                    {
                        // This IS a multi-container scan — populate the split fields
                        var containers = originalScan.OriginalContainerNumbers
                            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(c => c.Trim())
                            .ToList();

                        var position = containers.Count >= 2 && containers[0] == containerNumber ? "left" : "right";
                        record.IsMultiContainerScan = true;
                        record.SplitPosition = position;

                        // Check if a split job already exists in the splitter
                        var splitterJobId = await FindExistingSplitJobAsync(originalScan.OriginalContainerNumbers);
                        if (splitterJobId != null)
                        {
                            record.SplitJobId = splitterJobId;
                            var jobStatus = await GetSplitterJobStatusAsync(splitterJobId.Value);
                            if (jobStatus != null)
                                await ApplySplitJobStatusAsync(record, jobStatus);
                            else
                                record.SplitStatus = SplitAnalysisStatus.Pending;
                        }
                        else
                        {
                            record.SplitStatus = SplitAnalysisStatus.Pending;
                            // No split job exists — will need to be submitted
                            // (orchestrator or backfill handles this)
                        }

                        // Also update the sibling container's record
                        var siblingNumber = containers.FirstOrDefault(c => c != containerNumber);
                        if (!string.IsNullOrEmpty(siblingNumber))
                        {
                            var sibling = await _db.AnalysisRecords
                                .Where(r => r.ContainerNumber == siblingNumber && !r.IsMultiContainerScan)
                                .OrderByDescending(r => r.CreatedAtUtc)
                                .FirstOrDefaultAsync();
                            if (sibling != null)
                            {
                                sibling.IsMultiContainerScan = true;
                                sibling.SplitPosition = position == "left" ? "right" : "left";
                                sibling.SplitJobId = record.SplitJobId;
                                sibling.SplitStatus = record.SplitStatus;
                                sibling.SplitOptionA_ResultId = record.SplitOptionA_ResultId;
                                sibling.SplitOptionB_ResultId = record.SplitOptionB_ResultId;
                            }
                        }

                        await _db.SaveChangesAsync();
                    }
                }

                if (record == null || !record.IsMultiContainerScan || record.SplitStatus == null)
                {
                    return Ok(new
                    {
                        containerNumber,
                        isMultiContainer = false,
                        splitStatus = (string?)null,
                        options = Array.Empty<object>()
                    });
                }

                // Build options from the splitter API if we have candidates
                var options = new List<object>();
                if (record.SplitJobId.HasValue &&
                    (string.Equals(record.SplitStatus, SplitAnalysisStatus.Ready, StringComparison.OrdinalIgnoreCase)
                     || string.Equals(record.SplitStatus, SplitAnalysisStatus.Chosen, StringComparison.OrdinalIgnoreCase)))
                {
                    var client = _httpClientFactory.CreateClient("RawImageEngine");
                    var resultsResponse = await client.GetAsync($"/api/split/{record.SplitJobId}/results");
                    if (resultsResponse.IsSuccessStatusCode)
                    {
                        var resultsJson = await resultsResponse.Content.ReadAsStringAsync();
                        var results = JsonSerializer.Deserialize<JsonElement>(resultsJson);

                        if (results.ValueKind == JsonValueKind.Array)
                        {
                            // Get top 2 results by confidence, matching the stored option IDs
                            foreach (var result in results.EnumerateArray())
                            {
                                var resultId = result.GetProperty("id").GetString();
                                var isOptionA = record.SplitOptionA_ResultId?.ToString() == resultId;
                                var isOptionB = record.SplitOptionB_ResultId?.ToString() == resultId;

                                if (isOptionA || isOptionB)
                                {
                                    var side = record.SplitPosition ?? "left";
                                    options.Add(new
                                    {
                                        resultId,
                                        strategy = result.TryGetProperty("strategy_name", out var sn) ? sn.GetString() : null,
                                        splitX = result.TryGetProperty("split_x", out var sx) ? sx.GetInt32() : 0,
                                        confidence = result.TryGetProperty("confidence", out var conf) ? conf.GetDouble() : 0.0,
                                        // Signed URL — browser <img src> consumer, no JWT header possible.
                                    cropImageUrl = _urlSigner.SignRelative($"/api/image-splitter/jobs/{record.SplitJobId}/results/{resultId}/image/{side}"),
                                        reasoning = result.TryGetProperty("reasoning", out var rs) ? rs.GetString() : null
                                    });
                                }
                            }
                        }
                    }
                }

                return Ok(new
                {
                    containerNumber,
                    isMultiContainer = true,
                    splitStatus = record.SplitStatus,
                    position = record.SplitPosition,
                    jobId = record.SplitJobId,
                    chosenResultId = record.SplitResultId,
                    options,
                    originalImageUrl = _urlSigner.SignRelative($"/api/ImageProcessing/container/{containerNumber}/complete/image")
                });
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to get split options for container {Container}", containerNumber);
                // 2026-04-27: was returning 200 OK with empty options on every error — frontend
                // could not distinguish "no candidates" from "splitter unavailable". Now signals
                // explicit failure so the UI can render a retry/error state instead of a blank list.
                return StatusCode(503, new
                {
                    error = "split_options_unavailable",
                    containerNumber,
                    detail = "Could not retrieve split options. The splitter may be unreachable."
                });
            }
        }

        /// <summary>
        /// Analyst chooses a split crop for their container. Updates both this container's
        /// AnalysisRecord and the sibling container's record (same split job, opposite position).
        /// Also records the choice in the splitter's consensus corpus.
        /// </summary>
        [HttpPost("container/{containerNumber}/choose-split")]
        public async Task<IActionResult> ChooseSplit(string containerNumber, [FromBody] JsonElement body)
        {
            try
            {
                if (!body.TryGetProperty("resultId", out var resultIdProp) ||
                    !body.TryGetProperty("approvedBy", out var approvedByProp))
                {
                    return BadRequest(new { error = "resultId and approvedBy are required" });
                }

                var resultId = resultIdProp.GetString();
                var approvedBy = approvedByProp.GetString();

                if (string.IsNullOrEmpty(resultId) || !Guid.TryParse(resultId, out var resultGuid))
                    return BadRequest(new { error = "Invalid resultId" });

                // Find the AnalysisRecord
                var record = await _db.AnalysisRecords
                    .Where(r => r.ContainerNumber == containerNumber && r.IsMultiContainerScan)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .FirstOrDefaultAsync();

                if (record == null)
                    return NotFound(new { error = $"No multi-container AnalysisRecord found for {containerNumber}" });

                // Update this container's record
                record.SplitResultId = resultGuid;
                record.SplitStatus = SplitAnalysisStatus.Chosen;

                // Update the sibling container (same SplitJobId, opposite position)
                if (record.SplitJobId.HasValue)
                {
                    var siblingPosition = record.SplitPosition == "left" ? "right" : "left";
                    var sibling = await _db.AnalysisRecords
                        .Where(r => r.SplitJobId == record.SplitJobId &&
                                    r.SplitPosition == siblingPosition &&
                                    r.Id != record.Id)
                        .FirstOrDefaultAsync();

                    if (sibling != null)
                    {
                        sibling.SplitResultId = resultGuid;
                        sibling.SplitStatus = SplitAnalysisStatus.Chosen;
                    }

                    // Forward approval to the splitter for consensus corpus recording
                    try
                    {
                        var client = _httpClientFactory.CreateClient("RawImageEngine");
                        var approveBody = JsonSerializer.Serialize(new
                        {
                            result_id = resultId,
                            container_left = record.SplitPosition == "left" ? containerNumber : sibling?.ContainerNumber ?? "",
                            container_right = record.SplitPosition == "right" ? containerNumber : sibling?.ContainerNumber ?? "",
                            approved_by = approvedBy
                        });
                        var content = new StringContent(approveBody, Encoding.UTF8, "application/json");
                        await client.PostAsync($"/api/split/{record.SplitJobId}/approve", content);
                    }
                    catch (Exception ex)
                    {
                        // Non-blocking — consensus recording failure shouldn't block the analyst
                        _logger.LogWarning(ex, "Failed to record split approval in splitter for job {JobId}", record.SplitJobId);
                    }
                }

                await _db.SaveChangesAsync();

                _logger.LogInformation(
                    "Split chosen for container {Container}: result {ResultId} by {Analyst}",
                    containerNumber, resultId, approvedBy);

                return Ok(new { success = true, containerNumber, resultId, splitStatus = SplitAnalysisStatus.Chosen });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to choose split for container {Container}", containerNumber);
                return StatusCode(500, new { error = "Failed to save split choice" });
            }
        }

        /// <summary>
        /// Skip the split for a container — analyst wants to use the original combined image.
        /// </summary>
        [HttpPost("container/{containerNumber}/skip-split")]
        public async Task<IActionResult> SkipSplit(string containerNumber, [FromBody] JsonElement body)
        {
            try
            {
                var skippedBy = body.TryGetProperty("skippedBy", out var sb) ? sb.GetString() : "unknown";

                var record = await _db.AnalysisRecords
                    .Where(r => r.ContainerNumber == containerNumber && r.IsMultiContainerScan)
                    .OrderByDescending(r => r.CreatedAtUtc)
                    .FirstOrDefaultAsync();

                if (record == null)
                    return NotFound(new { error = $"No multi-container AnalysisRecord found for {containerNumber}" });

                record.SplitStatus = SplitAnalysisStatus.Skipped;
                await _db.SaveChangesAsync();

                _logger.LogInformation("Split skipped for container {Container} by {Analyst}", containerNumber, skippedBy);
                return Ok(new { success = true, containerNumber, splitStatus = SplitAnalysisStatus.Skipped });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to skip split for container {Container}", containerNumber);
                return StatusCode(500, new { error = "Failed to save skip" });
            }
        }

        // ── Split job lookup helpers ──

        /// <summary>
        /// Find an existing split job in the Python splitter by container_numbers.
        /// The splitter stores container_numbers as comma-separated string.
        /// </summary>
        private async Task<Guid?> FindExistingSplitJobAsync(string containerNumbers)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                // Normalize: the splitter may have "C1,C2" or "C1, C2"
                var normalized = containerNumbers.Replace(" ", "");
                var response = await client.GetAsync($"/api/split/pending?limit=500");
                if (!response.IsSuccessStatusCode)
                {
                    // Try completed jobs endpoint
                    response = await client.GetAsync($"/api/split/search?container_numbers={Uri.EscapeDataString(normalized)}");
                    if (!response.IsSuccessStatusCode) return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var jobs = JsonSerializer.Deserialize<JsonElement>(json);

                if (jobs.ValueKind == JsonValueKind.Array)
                {
                    foreach (var job in jobs.EnumerateArray())
                    {
                        if (job.TryGetProperty("container_numbers", out var cn))
                        {
                            var jobContainers = cn.GetString()?.Replace(" ", "") ?? "";
                            if (jobContainers == normalized)
                            {
                                if (job.TryGetProperty("id", out var idProp))
                                    return Guid.Parse(idProp.GetString()!);
                            }
                        }
                    }
                }
                else if (jobs.ValueKind == JsonValueKind.Object)
                {
                    if (jobs.TryGetProperty("id", out var idProp))
                        return Guid.Parse(idProp.GetString()!);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to find existing split job for {Containers}", containerNumbers);
                return null;
            }
        }

        private async Task<SplitJobStatus?> GetSplitterJobStatusAsync(Guid jobId)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                var response = await client.GetAsync($"/api/split/{jobId}");
                if (!response.IsSuccessStatusCode) return null;
                var json = await response.Content.ReadAsStringAsync();
                var job = JsonSerializer.Deserialize<JsonElement>(json);
                return TryReadSplitJobStatus(job, jobId);
            }
            catch (Exception ex)
            {
                // Was: catch { return null; } — meant a splitter outage looked identical to "no
                // such job" upstream. Caller still gets null but ops now has a trail.
                _logger.LogWarning(ex, "Failed to fetch splitter job status for {JobId}", jobId);
                return null;
            }
        }

        /// <summary>
        /// Populate the top 2 split candidates on an AnalysisRecord from the splitter results.
        /// </summary>
        private async Task<IReadOnlyList<SplitResultReference>> PopulateSplitCandidatesAsync(AnalysisRecord record)
        {
            if (!record.SplitJobId.HasValue) return Array.Empty<SplitResultReference>();

            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                var response = await client.GetAsync($"/api/split/{record.SplitJobId}/results");
                if (!response.IsSuccessStatusCode) return Array.Empty<SplitResultReference>();

                var json = await response.Content.ReadAsStringAsync();
                var results = JsonSerializer.Deserialize<JsonElement>(json);

                if (results.ValueKind != JsonValueKind.Array) return Array.Empty<SplitResultReference>();

                // Get top 2 results by confidence
                var sorted = results.EnumerateArray()
                    .Select(r => new
                    {
                        Id = r.TryGetProperty("id", out var id) ? id.GetString() : null,
                        Confidence = r.TryGetProperty("confidence", out var c) && c.ValueKind == JsonValueKind.Number ? c.GetDouble() : 0.0,
                        Strategy = TryGetString(r, "strategy_name"),
                        Outcome = TryGetOutcome(r)
                    })
                    .Where(r => r.Id != null)
                    .OrderByDescending(r => r.Confidence)
                    .Take(2)
                    .ToList();

                if (sorted.Count >= 1)
                    record.SplitOptionA_ResultId = Guid.Parse(sorted[0].Id!);
                if (sorted.Count >= 2)
                    record.SplitOptionB_ResultId = Guid.Parse(sorted[1].Id!);

                return sorted
                    .Select(result => new SplitResultReference(
                        Guid.Parse(result.Id!),
                        result.Strategy,
                        result.Confidence,
                        result.Outcome))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to populate split candidates for job {JobId}", record.SplitJobId);
                return Array.Empty<SplitResultReference>();
            }
        }

        private async Task ApplySplitJobStatusAsync(AnalysisRecord record, SplitJobStatus jobStatus)
        {
            var explicitNonChoice = SplitAnalysisStatus.TryMapNonChoiceOutcome(new[]
            {
                jobStatus.SplitOutcome,
                jobStatus.Status,
                jobStatus.BestStrategy,
                jobStatus.ErrorMessage
            });
            var shouldFetchCandidates = explicitNonChoice == null
                && SplitAnalysisStatus.IsCompletedJobStatus(jobStatus.Status);

            var candidates = shouldFetchCandidates
                ? await PopulateSplitCandidatesAsync(record)
                : Array.Empty<SplitResultReference>();

            var targetStatus = SplitAnalysisStatus.ResolveForAnalysisRecord(
                jobStatus,
                fetchedCandidateCount: candidates.Count,
                candidateFetchAttempted: shouldFetchCandidates,
                candidateOutcomes: candidates.Select(candidate => candidate.SplitOutcome));

            if (!string.Equals(targetStatus, SplitAnalysisStatus.Ready, StringComparison.OrdinalIgnoreCase))
            {
                record.SplitOptionA_ResultId = null;
                record.SplitOptionB_ResultId = null;
            }

            record.SplitStatus = targetStatus;
        }

        private static SplitJobStatus? TryReadSplitJobStatus(JsonElement job, Guid? fallbackJobId = null)
        {
            var jobId = fallbackJobId ?? TryGetGuid(job, "id") ?? TryGetGuid(job, "job_id");
            if (!jobId.HasValue)
                return null;

            return new SplitJobStatus(
                jobId.Value,
                TryGetString(job, "status") ?? "unknown",
                TryGetString(job, "best_strategy"),
                TryGetDouble(job, "best_confidence") ?? TryGetDouble(job, "best_score"),
                TryGetInt(job, "split_x"),
                TryGetInt(job, "result_count") ?? 0,
                TryGetOutcome(job),
                TryGetString(job, "error_message"));
        }

        private static Guid? TryGetGuid(JsonElement element, string propertyName)
        {
            var value = TryGetString(element, propertyName);
            return Guid.TryParse(value, out var guid) ? guid : null;
        }

        private static int? TryGetInt(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Number)
                return null;

            return prop.TryGetInt32(out var value) ? value : null;
        }

        private static double? TryGetDouble(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.Number)
                return null;

            return prop.TryGetDouble(out var value) ? value : null;
        }

        private static string? TryGetString(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop) || prop.ValueKind != JsonValueKind.String)
                return null;

            return prop.GetString();
        }

        private static string? TryGetOutcome(JsonElement element)
        {
            foreach (var propertyName in new[]
            {
                "split_outcome",
                "splitOutcome",
                "outcome",
                "visual_outcome",
                "visualOutcome",
                "classification",
                "resolution"
            })
            {
                var value = TryGetString(element, propertyName);
                if (!string.IsNullOrWhiteSpace(value))
                    return value;
            }

            foreach (var propertyName in new[]
            {
                "not_applicable",
                "notApplicable",
                "visual_single",
                "visualSingle",
                "single_container",
                "singleContainer",
                "uncertain"
            })
            {
                if (element.TryGetProperty(propertyName, out var prop)
                    && prop.ValueKind == JsonValueKind.True)
                {
                    return propertyName;
                }
            }

            if (element.TryGetProperty("metadata", out var metadata) && metadata.ValueKind == JsonValueKind.Object)
                return TryGetOutcome(metadata);

            return null;
        }

        private async Task<IActionResult> ForwardGetAsync(string path)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                var response = await client.GetAsync(path);
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to forward GET {Path}", path);
                return StatusCode(503, new { error = "Splitter service unavailable" });
            }
        }

        private async Task<IActionResult> ForwardPostAsync(string path, JsonElement body)
        {
            try
            {
                var client = _httpClientFactory.CreateClient("RawImageEngine");
                var jsonContent = new StringContent(body.GetRawText(), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(path, jsonContent);
                var content = await response.Content.ReadAsStringAsync();
                return Content(content, "application/json");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to forward POST {Path}", path);
                return StatusCode(503, new { error = "Splitter service unavailable" });
            }
        }
    }
}
