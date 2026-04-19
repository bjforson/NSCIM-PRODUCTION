using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Services.ImageProcessing.FS6000
{
    /// <summary>
    /// Thin HTTP wrapper around the Python inspector's composite endpoint.
    /// Calls <c>GET http://localhost:5320/inspector/composite/fs6000/{scanId}</c>
    /// which does the 16-bit DB-blob decode + dual-energy composite on the
    /// Python side. Returns the BGR PNG bytes as-is; the caller is responsible
    /// for re-encoding to JPEG / resizing / caching.
    ///
    /// Separate class from <c>FS6000ImagePipeline</c> so that (a) DI can manage
    /// the typed <c>HttpClient</c> cleanly and (b) we can unit-test the pipeline
    /// without standing up a live Python process.
    /// </summary>
    public class FS6000CompositeProxyClient
    {
        private readonly HttpClient _http;
        private readonly ILogger<FS6000CompositeProxyClient> _logger;

        public FS6000CompositeProxyClient(HttpClient http, ILogger<FS6000CompositeProxyClient> logger)
        {
            _http = http;
            _logger = logger;
        }

        /// <summary>
        /// Fetch the composite PNG for an FS6000 scan. Returns the raw PNG
        /// bytes. Throws <see cref="HttpRequestException"/> on non-2xx
        /// responses — the pipeline catches this and falls back to the
        /// vendor JPEG so the UI always gets something renderable.
        /// </summary>
        public async Task<byte[]> GetCompositePngAsync(
            Guid scanId,
            string luminance = "high",
            float materialStrength = 0.65f,
            CancellationToken ct = default)
        {
            var url = $"inspector/composite/fs6000/{scanId}?luminance={Uri.EscapeDataString(luminance)}&material_strength={materialStrength.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture)}";
            _logger.LogDebug("[FS6000-COMPOSITE-PROXY] GET {Url}", url);

            using var response = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();
            var bytes = await response.Content.ReadAsByteArrayAsync(ct);
            _logger.LogDebug("[FS6000-COMPOSITE-PROXY] scan {ScanId} -> {Size} bytes", scanId, bytes.Length);
            return bytes;
        }
    }
}
