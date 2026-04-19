using System;
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
                using var content = new MultipartFormDataContent();
                content.Add(new StringContent(containerNumbers), "container_numbers");
                if (scannerType != null) content.Add(new StringContent(scannerType), "scanner_type");
                if (sourceImageId.HasValue) content.Add(new StringContent(sourceImageId.Value.ToString()), "source_image_id");

                var imageContent = new ByteArrayContent(imageData);
                imageContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
                content.Add(imageContent, "file", "scan.jpg");

                var response = await _httpClient.PostAsync("/api/split/upload", content, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("[IMAGE-SPLITTER] Submit failed: {Status}", response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var jobId = Guid.Parse(root.GetProperty("job_id").GetString()!);
                var status = root.GetProperty("status").GetString() ?? "pending";
                return new SplitJobReference(jobId, status);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IMAGE-SPLITTER] Failed to submit split job for {Containers}", containerNumbers);
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
                    root.TryGetProperty("best_confidence", out var bc) ? bc.GetDouble() : null,
                    root.TryGetProperty("split_x", out var sx) ? sx.GetInt32() : null,
                    root.TryGetProperty("result_count", out var rc) ? rc.GetInt32() : 0
                );
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[IMAGE-SPLITTER] Failed to get job status {JobId}", jobId);
                return null;
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
    }
}
