namespace NickERP.Platform.Identity.Auth;

/// <summary>
/// Configuration for the Cloudflare Access JWT validator. Bound from
/// <c>Identity:CfAccess</c> in appsettings, e.g.:
/// <code>
/// "Identity": {
///   "CfAccess": {
///     "TeamDomain": "nickscan.cloudflareaccess.com",
///     "Audience":   "529763cb8a01addfc0c75cccce3844f46c345bd2fedc5304815902c23ffdbc46",
///     "AllowDevBypass": false
///   }
/// }
/// </code>
/// </summary>
public sealed class CfAccessOptions
{
    /// <summary>The Access team domain, e.g. "nickscan.cloudflareaccess.com".</summary>
    public string TeamDomain { get; set; } = string.Empty;

    /// <summary>
    /// The Access application's Audience tag (AUD). Set per app at the
    /// Access dashboard; we have one for the "NickScan Services" app.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// In Development environments, accept an <c>X-Dev-User: someone@domain</c>
    /// header instead of a real JWT. Always rejected in non-Development.
    /// </summary>
    public bool AllowDevBypass { get; set; } = false;

    /// <summary>
    /// Override the JWKS URL used during signature validation. Default is
    /// <c>https://{TeamDomain}/cdn-cgi/access/certs</c>; tests can point this
    /// at a fake server to inject test keys.
    /// </summary>
    public string? OverrideJwksUrl { get; set; }

    /// <summary>The header CF Access sets on every request after the user authenticates.</summary>
    public const string JwtHeader = "Cf-Access-Jwt-Assertion";

    /// <summary>The header that carries the service-token client id (machine callers).</summary>
    public const string ClientIdHeader = "Cf-Access-Client-Id";

    /// <summary>Header name for the developer-mode bypass.</summary>
    public const string DevBypassHeader = "X-Dev-User";

    /// <summary>Computed JWKS endpoint URL (override-aware).</summary>
    public string JwksUrl => OverrideJwksUrl
        ?? $"https://{TeamDomain.TrimEnd('/')}/cdn-cgi/access/certs";

    /// <summary>Computed issuer claim value the JWT must carry.</summary>
    public string Issuer => $"https://{TeamDomain.TrimEnd('/')}";
}
