using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;

namespace NickScanCentralImagingPortal.API.Middleware;

/// <summary>
/// Validates HMAC-signed short-lived URLs for image-serving endpoints that have to
/// be consumed by browser &lt;img src&gt; tags or cross-origin <c>fetch({credentials:"include"})</c>
/// and therefore cannot carry a Bearer header or a SameSite=Strict cookie.
///
/// On 2026-04-24 the Week-1 security rollout stripped [AllowAnonymous] from these
/// endpoints to close the GatewayController image/composite data leak. That broke
/// image rendering in the Blazor WebApp because &lt;img&gt; tags do browser-direct
/// fetches with no JWT and no usable cookie across origins. This middleware lets
/// the WebApp mint per-user, short-lived signed URLs (via <see cref="SignedImageUrl"/>)
/// that the browser can use directly — without weakening the posture on the
/// manifest/BOE data endpoints that share the same routes.
///
/// The middleware MUST be registered AFTER <c>UseAuthentication()</c> so it only
/// runs for requests that haven't already been authenticated by Cookie or JWT,
/// and BEFORE <c>UseAuthorization()</c> so the <c>[Authorize]</c> filter sees a
/// populated <c>HttpContext.User</c>.
/// </summary>
public class SignedImageUrlMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<SignedImageUrlMiddleware> _logger;
    private readonly byte[] _signingKey;

    public SignedImageUrlMiddleware(
        RequestDelegate next,
        ILogger<SignedImageUrlMiddleware> logger,
        IConfiguration configuration)
    {
        _next = next;
        _logger = logger;

        var keyValue = Environment.GetEnvironmentVariable("NICKSCAN_IMAGE_SIGNING_KEY")
                       ?? configuration["Security:ImageSigningKey"];
        if (string.IsNullOrWhiteSpace(keyValue) || keyValue.Contains("***USE_ENV_VAR"))
        {
            throw new InvalidOperationException(
                "NICKSCAN_IMAGE_SIGNING_KEY environment variable is required. " +
                "Set it via [Environment]::SetEnvironmentVariable('NICKSCAN_IMAGE_SIGNING_KEY', <64-char hex>, 'Machine'). " +
                "The WebApp must use the same value or signed URLs will 401.");
        }
        if (keyValue.Length < 32)
        {
            throw new InvalidOperationException(
                $"NICKSCAN_IMAGE_SIGNING_KEY is only {keyValue.Length} chars; need >= 32.");
        }

        _signingKey = Encoding.UTF8.GetBytes(keyValue);
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (context.User?.Identity?.IsAuthenticated != true &&
            SignedImageUrl.IsSignedImagePath(context.Request.Path))
        {
            if (TryValidate(context, out var principal))
            {
                context.User = principal;
                _logger.LogDebug(
                    "Signed-URL auth accepted for {Path} uid={Uid}",
                    context.Request.Path,
                    principal.FindFirstValue(ClaimTypes.NameIdentifier));
            }
        }

        await _next(context);
    }

    private bool TryValidate(HttpContext context, out ClaimsPrincipal principal)
    {
        principal = new ClaimsPrincipal();

        var q = context.Request.Query;
        if (!q.TryGetValue("exp", out var expRaw) ||
            !q.TryGetValue("uid", out var uidRaw) ||
            !q.TryGetValue("sig", out var sigRaw))
        {
            return false;
        }

        var expStr = expRaw.ToString();
        var uid = uidRaw.ToString();
        var sig = sigRaw.ToString();

        if (!long.TryParse(expStr, out var expSec)) return false;
        var exp = DateTimeOffset.FromUnixTimeSeconds(expSec);
        if (exp < DateTimeOffset.UtcNow)
        {
            _logger.LogDebug(
                "Signed URL rejected: expired {Path} exp={Exp} now={Now}",
                context.Request.Path, exp, DateTimeOffset.UtcNow);
            return false;
        }

        // TTL cap — refuse anything signed with more than 1 hour validity so a
        // leaked URL has a bounded blast radius even if the minter was misconfigured.
        if (exp > DateTimeOffset.UtcNow.AddHours(1))
        {
            _logger.LogWarning(
                "Signed URL rejected: ttl too long {Path} exp={Exp}",
                context.Request.Path, exp);
            return false;
        }

        var expected = SignedImageUrl.ComputeSignature(
            _signingKey, context.Request.Path.Value ?? "", expStr, uid);

        var sigBytes = Encoding.UTF8.GetBytes(sig);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);
        if (sigBytes.Length != expectedBytes.Length) return false;
        if (!CryptographicOperations.FixedTimeEquals(sigBytes, expectedBytes))
        {
            _logger.LogDebug("Signed URL rejected: bad sig for {Path}", context.Request.Path);
            return false;
        }

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, uid),
            new Claim(ClaimTypes.Name, uid),
            new Claim("auth_method", "signed-url"),
        };
        principal = new ClaimsPrincipal(new ClaimsIdentity(claims, SignedImageUrl.AuthType));
        return true;
    }
}

/// <summary>
/// Path whitelist + signature computation. The WebApp's SignedImageUrlBuilder uses
/// the IDENTICAL <see cref="ComputeSignature"/> logic; keep in sync.
/// </summary>
public static class SignedImageUrl
{
    public const string AuthType = "SignedImageUrl";

    /// <summary>
    /// Only these 8 paths participate in signed-URL auth. Everything else goes
    /// through Cookie/JWT as normal; the middleware is a no-op.
    /// </summary>
    public static bool IsSignedImagePath(PathString path)
    {
        var p = path.Value;
        if (string.IsNullOrEmpty(p)) return false;
        p = p.ToLowerInvariant();

        // ImageProcessingController image bytes
        if (p.StartsWith("/api/imageprocessing/container/"))
        {
            if (p.EndsWith("/thumbnail")) return true;
            if (p.EndsWith("/full")) return true;
            if (p.EndsWith("/complete/image")) return true;
            if (p.EndsWith("/raw")) return true;
        }

        // ContainerDetailsController ASE images
        if (p.StartsWith("/api/containerdetails/image/ase/"))
        {
            if (p.EndsWith("/thumbnail") || p.EndsWith("/full")) return true;
        }

        // GatewayController image convenience route
        if (p.StartsWith("/api/gateway/container/") && p.EndsWith("/image")) return true;

        // IcumsPayloadController image byte extraction
        if (p == "/api/icumspayload/image") return true;

        return false;
    }

    /// <summary>
    /// HMAC-SHA256 of "{path}|{exp}|{uid}" → uppercase hex. Path must be the
    /// lowercased <see cref="HttpRequest.Path"/> to match the minter.
    /// </summary>
    public static string ComputeSignature(byte[] key, string path, string exp, string uid)
    {
        var payload = $"{path.ToLowerInvariant()}|{exp}|{uid}";
        using var hmac = new HMACSHA256(key);
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }
}
