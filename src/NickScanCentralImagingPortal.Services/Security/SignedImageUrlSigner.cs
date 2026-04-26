using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using NickScanCentralImagingPortal.Core.Security;

namespace NickScanCentralImagingPortal.Services.Security;

/// <summary>
/// Implementation of <see cref="ISignedImageUrlSigner"/>. Lives in
/// .Services so both the API controllers and service classes (e.g.
/// CargoGroupService) can inject it without the Services project depending
/// on the API project. The API's <c>SignedImageUrlMiddleware</c> uses
/// <see cref="SignedImageUrlCanonical.ComputeSignature"/> from .Core so
/// the signature algorithm is shared.
/// </summary>
public class SignedImageUrlSigner : ISignedImageUrlSigner
{
    private static readonly TimeSpan DefaultTtl = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan MaxTtl = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinTtl = TimeSpan.FromSeconds(1);

    private readonly byte[] _signingKey;
    private readonly IHttpContextAccessor? _httpContextAccessor;

    public SignedImageUrlSigner(IConfiguration configuration, IHttpContextAccessor? httpContextAccessor = null)
    {
        _httpContextAccessor = httpContextAccessor;

        var keyValue = Environment.GetEnvironmentVariable("NICKSCAN_IMAGE_SIGNING_KEY")
                       ?? configuration["Security:ImageSigningKey"];
        if (string.IsNullOrWhiteSpace(keyValue) || keyValue.Contains("***USE_ENV_VAR"))
        {
            throw new InvalidOperationException(
                "NICKSCAN_IMAGE_SIGNING_KEY environment variable is required.");
        }
        if (keyValue.Length < 32)
        {
            throw new InvalidOperationException(
                $"NICKSCAN_IMAGE_SIGNING_KEY is only {keyValue.Length} chars; need >= 32.");
        }

        _signingKey = Encoding.UTF8.GetBytes(keyValue);
    }

    public string SignRelative(string apiPath, TimeSpan? ttl = null, string? uid = null)
    {
        if (string.IsNullOrWhiteSpace(apiPath))
            throw new ArgumentException("apiPath is required", nameof(apiPath));
        if (!apiPath.StartsWith('/'))
            apiPath = "/" + apiPath;

        var (pathOnly, existingQuery) = SplitPathAndQuery(apiPath);
        var resolvedUid = ResolveUid(uid);
        var exp = ComputeExp(ttl);
        var sig = SignedImageUrlCanonical.ComputeSignature(_signingKey, pathOnly, exp, resolvedUid);

        var separator = string.IsNullOrEmpty(existingQuery) ? "?" : "&";
        var authQuery = $"{separator}exp={exp}&uid={Uri.EscapeDataString(resolvedUid)}&sig={sig}";

        return $"{apiPath}{authQuery}";
    }

    public string SignAbsolute(string absoluteUrl, TimeSpan? ttl = null, string? uid = null)
    {
        if (string.IsNullOrWhiteSpace(absoluteUrl))
            throw new ArgumentException("absoluteUrl is required", nameof(absoluteUrl));

        if (!Uri.TryCreate(absoluteUrl, UriKind.Absolute, out var uri))
            throw new ArgumentException($"Not an absolute URL: {absoluteUrl}", nameof(absoluteUrl));

        var baseUrl = $"{uri.Scheme}://{uri.Authority}";
        var pathAndQuery = uri.PathAndQuery; // already starts with '/'

        return baseUrl + SignRelative(pathAndQuery, ttl, uid);
    }

    private string ResolveUid(string? explicitUid)
    {
        if (!string.IsNullOrWhiteSpace(explicitUid)) return explicitUid;

        var principal = _httpContextAccessor?.HttpContext?.User;
        var fromClaims = principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value
                         ?? principal?.Identity?.Name;
        return string.IsNullOrWhiteSpace(fromClaims) ? "api-internal" : fromClaims;
    }

    private static string ComputeExp(TimeSpan? ttl)
    {
        var effective = ttl ?? DefaultTtl;
        if (effective < MinTtl) effective = MinTtl;
        if (effective > MaxTtl) effective = MaxTtl;
        return DateTimeOffset.UtcNow.Add(effective).ToUnixTimeSeconds().ToString();
    }

    private static (string PathOnly, string Query) SplitPathAndQuery(string input)
    {
        var idx = input.IndexOf('?');
        if (idx < 0) return (input, string.Empty);
        return (input[..idx], input[(idx + 1)..]);
    }
}
