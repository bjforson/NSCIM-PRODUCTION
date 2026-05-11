namespace NickScanCentralImagingPortal.Core.Security;

/// <summary>
/// Server-side signer for short-lived HMAC image URLs. API controllers and
/// .Services classes that emit image URLs into response DTOs inject this and
/// call SignRelative/SignAbsolute so the browser &lt;img src&gt; renders with
/// <c>?exp&amp;uid&amp;sig</c> appended. The API's
/// <c>SignedImageUrlMiddleware</c> validates them. Signing key is the shared
/// <c>NICKSCAN_IMAGE_SIGNING_KEY</c> env var (same value the WebApp's
/// SignedImageUrlBuilder uses).
/// </summary>
public interface ISignedImageUrlSigner
{
    /// <summary>
    /// Sign a relative API path (may contain a query). Returns the relative
    /// signed URL (caller prepends the host). TTL default 5 min, clamped to
    /// [1 s, 1 h]. Must match one of the whitelisted routes in
    /// <c>SignedImageUrlMiddleware.SignedImageUrl.IsSignedImagePath</c> or
    /// the signature is valid but [Authorize] still rejects.
    /// </summary>
    string SignRelative(string apiPath, TimeSpan? ttl = null, string? uid = null);

    /// <summary>
    /// Sign an absolute URL. Scheme+host is preserved; only the path is
    /// covered by the signature.
    /// </summary>
    string SignAbsolute(string absoluteUrl, TimeSpan? ttl = null, string? uid = null);

    /// <summary>
    /// Compute the raw signature the middleware will validate. Exposed so the
    /// WebApp's client-side builder and the middleware share the exact same
    /// canonicalisation. Treat the payload format as stable.
    /// </summary>
    static string ComputeSignatureCanonical(byte[] key, string path, string exp, string uid)
        => SignedImageUrlCanonical.ComputeSignature(key, path, exp, uid);
}

/// <summary>
/// Pure (no-DI, no-state) signature algorithm. Kept here in Core so that
/// API middleware, the Services-project signer, and the WebApp builder all
/// produce byte-identical signatures for the same (path, exp, uid).
/// </summary>
public static class SignedImageUrlCanonical
{
    /// <summary>
    /// HMAC-SHA256 of <c>"{Uri.UnescapeDataString(path).ToLowerInvariant()}|{exp}|{uid}"</c>,
    /// hex-encoded uppercase. ASP.NET Core exposes <c>HttpRequest.Path</c> in decoded
    /// form for route values like comma-separated ASE container numbers, so issuers
    /// canonicalize percent-encoded paths the same way before signing.
    /// </summary>
    public static string ComputeSignature(byte[] key, string path, string exp, string uid)
    {
        var canonicalPath = CanonicalizePath(path);
        var payload = $"{canonicalPath.ToLowerInvariant()}|{exp}|{uid}";
        using var hmac = new System.Security.Cryptography.HMACSHA256(key);
        var bytes = hmac.ComputeHash(System.Text.Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes);
    }

    private static string CanonicalizePath(string path)
    {
        if (string.IsNullOrEmpty(path))
        {
            return string.Empty;
        }

        try
        {
            return Uri.UnescapeDataString(path);
        }
        catch (UriFormatException)
        {
            return path;
        }
    }
}
