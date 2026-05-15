using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace NickScanWebApp.New.Controllers;

/// <summary>
/// Proxies image/raw-image requests to the API so the browser can fetch image
/// data same-origin for canvas pixel access (avoids CORS/certificate boundary
/// issues between the WebApp and API hosts).
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
    /// Fetches image bytes or raw plane bytes from the API. Browser uses a
    /// same-origin URL so canvas getImageData works and raw metadata headers
    /// remain readable by JS.
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
            using var response = await client.GetAsync(
                targetUrl,
                HttpCompletionOption.ResponseHeadersRead,
                HttpContext.RequestAborted);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "Image proxy upstream returned HTTP {StatusCode} for {Url}",
                    (int)response.StatusCode,
                    targetUrl);
                return StatusCode((int)response.StatusCode, "Upstream image unavailable");
            }

            if (response.Content.Headers.ContentLength == 0)
            {
                _logger.LogWarning("Image proxy got empty body from {Url}", targetUrl);
                return StatusCode(502, "Upstream returned empty body");
            }

            Response.StatusCode = StatusCodes.Status200OK;
            Response.ContentType = response.Content.Headers.ContentType?.ToString() ?? "image/jpeg";
            if (response.Content.Headers.ContentLength.HasValue)
            {
                Response.ContentLength = response.Content.Headers.ContentLength.Value;
            }

            CopyHeaderIfPresent(response, "X-Width");
            CopyHeaderIfPresent(response, "X-Height");
            CopyHeaderIfPresent(response, "X-BitDepth");
            CopyHeaderIfPresent(response, "X-Plane");
            CopyHeaderIfPresent(response, "X-Source-Format");

            await response.Content.CopyToAsync(Response.Body, HttpContext.RequestAborted);
            return new EmptyResult();
        }
        catch (OperationCanceledException) when (HttpContext.RequestAborted.IsCancellationRequested)
        {
            _logger.LogDebug("Image proxy request was cancelled by the client for {Url}", targetUrl);
            return new EmptyResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Image proxy failed for {Url}", targetUrl.Length > 80 ? targetUrl[..80] + "..." : targetUrl);
            return StatusCode(502, "Image unavailable: " + ex.Message);
        }
    }

    private void CopyHeaderIfPresent(HttpResponseMessage upstream, string name)
    {
        if (upstream.Headers.TryGetValues(name, out var values))
        {
            Response.Headers[name] = values.ToArray();
            return;
        }

        if (upstream.Content.Headers.TryGetValues(name, out values))
        {
            Response.Headers[name] = values.ToArray();
        }
    }
}
