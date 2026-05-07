using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NickScanWebApp.New.Services;

/// <summary>
/// Server-side builder for HMAC-signed short-lived image URLs. Paired with the
/// API's <c>SignedImageUrlMiddleware</c>. Reads <c>NICKSCAN_IMAGE_SIGNING_KEY</c>
/// from env var (same one the API reads) at construction and fails fast if it
/// is not set, so misconfig surfaces at WebApp startup rather than as 401 pages.
///
/// Usage:
/// <code>
/// @inject SignedImageUrlBuilder UrlBuilder
/// @inject AuthenticationStateProvider AuthState
/// ...
/// var uid = (await AuthState.GetAuthenticationStateAsync()).User
///     .FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "anon";
/// var src = UrlBuilder.Build("/api/ImageProcessing/container/X/complete/image?size=full", uid);
/// </code>
/// </summary>
public class SignedImageUrlBuilder
{
    private readonly byte[] _signingKey;
    private readonly string _apiBaseUrl;
    private readonly ILogger<SignedImageUrlBuilder>? _logger;

    // Default TTL — must be <= 1 hour or the API middleware rejects it.
    // 2026-05-07: bumped 5min -> 30min to match the API-side signer default.
    // Analysts who keep a record open on the Images tab past 5 min were getting
    // "Image failed to load" because the URL expired before they finished. The
    // WebApp's HandleImageError additionally re-mints fresh URLs on first error,
    // so a leaked URL is still bounded by TTL + uid.
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(30);

    public SignedImageUrlBuilder(IConfiguration configuration, ILogger<SignedImageUrlBuilder>? logger = null)
    {
        _logger = logger;

        var keyValue = Environment.GetEnvironmentVariable("NICKSCAN_IMAGE_SIGNING_KEY")
                       ?? configuration["Security:ImageSigningKey"];
        if (string.IsNullOrWhiteSpace(keyValue) || keyValue.Contains("***USE_ENV_VAR"))
        {
            throw new InvalidOperationException(
                "NICKSCAN_IMAGE_SIGNING_KEY environment variable is required. " +
                "It must match the value the API uses. Set it via " +
                "[Environment]::SetEnvironmentVariable('NICKSCAN_IMAGE_SIGNING_KEY', <64-char hex>, 'Machine').");
        }
        if (keyValue.Length < 32)
        {
            throw new InvalidOperationException(
                $"NICKSCAN_IMAGE_SIGNING_KEY is only {keyValue.Length} chars; need >= 32.");
        }

        _signingKey = Encoding.UTF8.GetBytes(keyValue);
        _apiBaseUrl = (configuration["ApiSettings:BaseUrl"] ?? "http://localhost:5205").TrimEnd('/');
    }

    /// <summary>
    /// Returns the absolute URL the browser should put in &lt;img src&gt;. Existing
    /// query string (e.g. <c>?size=full</c>) is preserved; sig/exp/uid are
    /// appended to it.
    /// </summary>
    /// <param name="apiPath">
    /// Path starting with <c>/api/...</c>. May include an existing query string.
    /// Must match one of the 8 whitelisted paths in
    /// <c>SignedImageUrlMiddleware.IsSignedImagePath</c> or the signature will
    /// be valid but [Authorize] will still reject.
    /// </param>
    /// <param name="userId">
    /// Opaque identifier bound into the signature so logs can attribute image
    /// fetches to a user. Use the ClaimTypes.NameIdentifier of the caller; if
    /// that is not available, pass the user name or a stable proxy.
    /// </param>
    /// <param name="ttl">
    /// How long the URL is valid. Clamped to [1 second, 1 hour]. Default 5 min.
    /// </param>
    public string Build(string apiPath, string userId, TimeSpan? ttl = null)
    {
        if (string.IsNullOrWhiteSpace(apiPath))
            throw new ArgumentException("apiPath is required", nameof(apiPath));
        if (!apiPath.StartsWith("/"))
            apiPath = "/" + apiPath;

        var effectiveTtl = ttl ?? DefaultTtl;
        if (effectiveTtl < TimeSpan.FromSeconds(1)) effectiveTtl = TimeSpan.FromSeconds(1);
        if (effectiveTtl > TimeSpan.FromHours(1)) effectiveTtl = TimeSpan.FromHours(1);

        var exp = DateTimeOffset.UtcNow.Add(effectiveTtl).ToUnixTimeSeconds().ToString();
        var uidSafe = string.IsNullOrWhiteSpace(userId) ? "anon" : userId;

        // Split path from any pre-existing query string. The signature covers
        // only the path component (same as the middleware, which reads
        // HttpRequest.Path — query is a separate property).
        var questionMarkIdx = apiPath.IndexOf('?');
        var pathOnly = questionMarkIdx >= 0 ? apiPath[..questionMarkIdx] : apiPath;
        var existingQuery = questionMarkIdx >= 0 ? apiPath[(questionMarkIdx + 1)..] : "";

        var sig = ComputeSignature(_signingKey, pathOnly, exp, uidSafe);

        var separator = string.IsNullOrEmpty(existingQuery) ? "?" : "&";
        var authQuery = $"{separator}exp={exp}&uid={Uri.EscapeDataString(uidSafe)}&sig={sig}";

        return $"{_apiBaseUrl}{apiPath}{authQuery}";
    }

    /// <summary>
    /// Build a relative path-only URL (no api base), useful when the Blazor
    /// component wants to stay within the same origin's routing via a proxy.
    /// Currently unused — kept for future WebApp-side proxy support.
    /// </summary>
    public string BuildRelative(string apiPath, string userId, TimeSpan? ttl = null)
    {
        var full = Build(apiPath, userId, ttl);
        return full.StartsWith(_apiBaseUrl) ? full[_apiBaseUrl.Length..] : full;
    }

    // IDENTICAL to SignedImageUrlMiddleware.SignedImageUrl.ComputeSignature in the API.
    // Keep in sync; any change requires coordinated redeploy of both services.
    private static string ComputeSignature(byte[] key, string path, string exp, string uid)
    {
        var payload = $"{path.ToLowerInvariant()}|{exp}|{uid}";
        using var hmac = new HMACSHA256(key);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}
