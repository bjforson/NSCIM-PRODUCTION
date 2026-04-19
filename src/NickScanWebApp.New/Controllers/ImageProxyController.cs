using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NickScanWebApp.New.Controllers;

/// <summary>
/// Proxies image requests to the API so the browser can fetch images same-origin for canvas pixel access (avoids CORS).
/// </summary>
[ApiController]
[Route("api/[controller]")]
[AllowAnonymous] // Must be anonymous — Blazor Server uses JWT via SignalR, not cookies, so browser fetch() has no auth token
public class ImageProxyController : ControllerBase
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ImageProxyController> _logger;

    public ImageProxyController(
        IHttpClientFactory httpClientFactory,
        IConfiguration configuration,
        ILogger<ImageProxyController> logger)
    {
        _httpClientFactory = httpClientFactory;
        _configuration = configuration;
        _logger = logger;
    }

    /// <summary>
    /// GET api/imageproxy?url=Base64EncodedImageUrl
    /// Fetches the image from the API and streams it back. Browser uses same-origin URL so canvas getImageData works.
    /// </summary>
    [HttpGet]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> Get([FromQuery] string url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return BadRequest("Missing url");

        string targetUrl;
        try
        {
            var bytes = Convert.FromBase64String(url);
            targetUrl = System.Text.Encoding.UTF8.GetString(bytes);
        }
        catch
        {
            return BadRequest("Invalid url encoding");
        }

        var apiBase = _configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205";
        if (!targetUrl.StartsWith(apiBase, StringComparison.OrdinalIgnoreCase) &&
            !targetUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !targetUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            targetUrl = new Uri(new Uri(apiBase), targetUrl).ToString();

        try
        {
            var client = _httpClientFactory.CreateClient("NickScanAPI");
            // BUG FIX: do NOT use `using` on the response with `HttpCompletionOption.ResponseHeadersRead`.
            // File(stream, ...) streams the body during the outgoing response pipeline, AFTER this method
            // returns. Disposing the HttpResponseMessage first yields a closed stream, ASP.NET writes
            // zero bytes, and the browser's createImageBitmap rejects the 0-byte blob with
            // "The source image could not be decoded".
            // Fix: buffer into a byte[] with ReadAsByteArrayAsync so we own the bytes after dispose.
            using var response = await client.GetAsync(targetUrl);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
            var bytes = await response.Content.ReadAsByteArrayAsync();
            if (bytes.Length == 0)
            {
                _logger.LogWarning("Image proxy got empty body from {Url}", targetUrl);
                return StatusCode(502, "Upstream returned empty body");
            }
            return File(bytes, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image proxy failed for {Url}", targetUrl.Length > 80 ? targetUrl[..80] + "..." : targetUrl);
            return StatusCode(502, "Image unavailable: " + ex.Message);
        }
    }
}
