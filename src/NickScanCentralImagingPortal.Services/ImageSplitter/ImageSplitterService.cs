using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.ImageSplitter
{
    public class ImageSplitterService : IImageSplitterService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ImageSplitterService> _logger;

        public ImageSplitterService(HttpClient httpClient, ILogger<ImageSplitterService> logger)
        {
            _httpClient = httpClient;
            _logger = logger;
        }

        public async Task<SplitJobReference?> SubmitSplitJobAsync(string containerNumbers, byte[] imageData, Guid? sourceImageId = null, string? scannerType = null, CancellationToken cancellationToken = default)
        {
            try
            {
                var mediaType = DetectSupportedImageMediaType(imageData);
                if (mediaType == null)
                {
                    _logger.LogWarning(
                        "[IMAGE-SPLITTER] Submit skipped for {Containers} ({ScannerType}): unsupported image bytes ({Length} bytes, magic={Magic})",
                        containerNumbers,
                        scannerType ?? "unknown",
                        imageData?.Length ?? 0,
                        GetMagicHex(imageData));
                    return null;
                }

                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(containerNumbers), "container_numbers");
                if (scannerType != null) content.Add(new StringContent(scannerType), "scanner_type");
                if (sourceImageId.HasValue) content.Add(new StringContent(sourceImageId.Value.ToString()), "source_image_id");

                var imageContent = new ByteArrayContent(imageData);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
                content.Add(imageContent, "file", "scan.jpg");

                var response = await _httpClient.PostAsync("/api/split/upload", content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await ReadErrorBodyAsync(response, cancellationToken);
                    _logger.LogWarning(
                        "[IMAGE-SPLITTER] Submit failed for {Containers} ({ScannerType}, {Length} bytes, {MediaType}): {Status} {Body}",
                        containerNumbers,
                        scannerType ?? "unknown",
                        imageData.Length,
                        mediaType,
                        response.StatusCode,
                        body);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var jobId = TryGetGuid(root, "job_id") ?? TryGetGuid(root, "id");
                if (!jobId.HasValue)
                {
                    _logger.LogWarning("[IMAGE-SPLITTER] Submit response did not contain a job id for {Containers}", containerNumbers);
                    return null;
                }

                var status = root.GetProperty("status").GetString() ?? "pending";
                return new SplitJobReference(jobId.Value, status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IMAGE-SPLITTER] Failed to submit split job for {Containers}", containerNumbers);
                return null;
            }
        }

        public async Task<SplitJobReference?> FindLatestJobByContainersAsync(string containerNumbers, CancellationToken cancellationToken = default)
        {
            try
            {
                var normalized = NormalizeContainerNumbers(containerNumbers);
                var response = await _httpClient.GetAsync($"/api/split/search?container_numbers={Uri.EscapeDataString(normalized)}", cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;

                foreach (var job in doc.RootElement.EnumerateArray())
                {
                    var jobContainers = job.TryGetProperty("container_numbers", out var cn) ? cn.GetString() : null;
                    if (!string.Equals(NormalizeContainerNumbers(jobContainers), normalized, StringComparison.OrdinalIgnoreCase))
                        continue;

                    var jobId = TryGetGuid(job, "id") ?? TryGetGuid(job, "job_id");
                    if (!jobId.HasValue) continue;

                    var status = job.TryGetProperty("status", out var statusProp)
                        ? statusProp.GetString() ?? "unknown"
                        : "unknown";

                    return new SplitJobReference(jobId.Value, status);
                }

                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IMAGE-SPLITTER] Failed to search split jobs for {Containers}", containerNumbers);
                return null;
            }
        }

        public async Task<SplitJobStatus?> GetJobStatusAsync(Guid jobId, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/split/{jobId}", cancellationToken);
                if (!response.IsSuccessStatusCode) return null;

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                return new SplitJobStatus(
                    jobId,
                    root.GetProperty("status").GetString() ?? "unknown",
                    root.TryGetProperty("best_strategy", out var bs) ? bs.GetString() : null,
                    TryGetDouble(root, "best_confidence") ?? TryGetDouble(root, "best_score"),
                    root.TryGetProperty("split_x", out var sx) ? sx.GetInt32() : null,
                    root.TryGetProperty("result_count", out var rc) ? rc.GetInt32() : 0,
                    TryGetOutcome(root),
                    TryGetString(root, "error_message")
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IMAGE-SPLITTER] Failed to get job status {JobId}", jobId);
                return null;
            }
        }

        public async Task<IReadOnlyList<SplitResultReference>> GetTopSplitResultsAsync(Guid jobId, int take = 2, CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync($"/api/split/{jobId}/results", cancellationToken);
                if (!response.IsSuccessStatusCode) return Array.Empty<SplitResultReference>();

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<SplitResultReference>();

                return doc.RootElement
                    .EnumerateArray()
                    .Select(result =>
                    {
                        var id = TryGetGuid(result, "id");
                        if (!id.HasValue) return null;
                        var confidence = TryGetDouble(result, "confidence");
                        var strategy = result.TryGetProperty("strategy_name", out var strategyProp)
                            ? strategyProp.GetString()
                            : null;
                        return new SplitResultReference(id.Value, strategy, confidence, TryGetOutcome(result));
                    })
                    .Where(result => result != null)
                    .OrderByDescending(result => result!.Confidence ?? 0.0)
                    .Take(Math.Max(0, take))
                    .Cast<SplitResultReference>()
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IMAGE-SPLITTER] Failed to fetch split results for job {JobId}", jobId);
                return Array.Empty<SplitResultReference>();
            }
        }

        public async Task<bool> IsHealthyAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/health", cancellationToken);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        private static Guid? TryGetGuid(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out var prop))
                return null;

            return prop.ValueKind == JsonValueKind.String && Guid.TryParse(prop.GetString(), out var guid)
                ? guid
                : null;
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

        private static string NormalizeContainerNumbers(string? containerNumbers)
        {
            return string.Join(
                ",",
                (containerNumbers ?? string.Empty)
                    .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(token => !string.IsNullOrWhiteSpace(token)));
        }

        private static string? DetectSupportedImageMediaType(byte[]? imageData)
        {
            if (imageData == null || imageData.Length < 4)
                return null;

            if (imageData.Length >= 3
                && imageData[0] == 0xFF
                && imageData[1] == 0xD8
                && imageData[2] == 0xFF)
            {
                return "image/jpeg";
            }

            if (imageData.Length >= 8
                && imageData[0] == 0x89
                && imageData[1] == 0x50
                && imageData[2] == 0x4E
                && imageData[3] == 0x47
                && imageData[4] == 0x0D
                && imageData[5] == 0x0A
                && imageData[6] == 0x1A
                && imageData[7] == 0x0A)
            {
                return "image/png";
            }

            if (imageData.Length >= 12
                && imageData[0] == 0x52
                && imageData[1] == 0x49
                && imageData[2] == 0x46
                && imageData[3] == 0x46
                && imageData[8] == 0x57
                && imageData[9] == 0x45
                && imageData[10] == 0x42
                && imageData[11] == 0x50)
            {
                return "image/webp";
            }

            return null;
        }

        private static string GetMagicHex(byte[]? imageData)
        {
            if (imageData == null || imageData.Length == 0)
                return "empty";

            return Convert.ToHexString(imageData.Take(Math.Min(imageData.Length, 8)).ToArray());
        }

        private static async Task<string> ReadErrorBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            try
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(body))
                    return "(empty)";

                return body.Length <= 500 ? body : body[..500];
            }
            catch
            {
                return "(unreadable)";
            }
        }
    }
}
