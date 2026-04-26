using System.Security.Claims;
using Microsoft.Extensions.Caching.Memory;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.API.Security;

/// <summary>
/// Shared single-session check used by both the JWT (<c>OnTokenValidated</c>)
/// and Cookie (<c>OnValidatePrincipal</c>) auth events.
///
/// Protocol:
/// 1. <c>JwtService.GenerateToken</c> stamps each new token with
///    <c>sid = user.CurrentSessionId</c>.
/// 2. The login endpoint rotates <c>CurrentSessionId</c> via
///    <see cref="IUserRepository.RotateSessionIdAsync"/> BEFORE minting, and
///    invalidates the cache key so the validator re-reads from DB.
/// 3. Every authenticated request re-runs <see cref="ValidateAsync"/>; if the
///    incoming <c>sid</c> claim doesn't match the canonical column the auth
///    fails and the user is forced back to login.
///
/// Carve-outs:
/// - Principals minted by <c>SignedImageUrlMiddleware</c> (auth_method =
///   "signed-url") have no <c>sid</c> claim and never need single-session
///   enforcement — they're already short-lived (≤ 1 h) per-URL signatures.
///   We skip them via the auth_method claim.
/// - Tokens missing a sid claim (issued before this feature shipped) get
///   rejected so the user is forced to re-login and pick up the new format.
///
/// Caching: the canonical sid is cached in <see cref="IMemoryCache"/> for
/// 30 s under key <c>"sid:{userId}"</c> to keep this off the hot DB path.
/// The login flow MUST <c>cache.Remove("sid:{userId}")</c> after rotation
/// so the validator re-reads on the next request — otherwise a freshly-
/// rotated session can briefly look invalid against its own new token.
/// </summary>
public static class SingleSessionValidator
{
    private const string CacheKeyPrefix = "sid:";
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public static async Task<bool> ValidateAsync(HttpContext httpContext, ClaimsPrincipal? principal)
    {
        if (principal?.Identity?.IsAuthenticated != true) return true;

        // Carve-out: signed image URLs are not subject to single-session.
        var authMethod = principal.FindFirst("auth_method")?.Value;
        if (string.Equals(authMethod, "signed-url", StringComparison.Ordinal)) return true;

        // GOTCHA: JwtBearerHandler defaults MapInboundClaims = true, which routes
        // the JWT "sid" short name through Microsoft.IdentityModel's
        // DefaultInboundClaimTypeMap and renames it to ClaimTypes.Sid
        // ("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/sid"). So a
        // bare FindFirst("sid") returns null and the validator would reject
        // every token. Look up BOTH names — works regardless of mapping config.
        var sidClaim = principal.FindFirst(ClaimTypes.Sid)?.Value
                       ?? principal.FindFirst("sid")?.Value;
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrEmpty(sidClaim) || string.IsNullOrEmpty(userIdClaim))
        {
            // Tokens issued before single-session shipped lack the sid claim.
            // Force re-login so the next mint stamps a sid.
            return false;
        }

        if (!int.TryParse(userIdClaim, out var userId)) return false;

        var sp = httpContext.RequestServices;
        var cache = sp.GetService<IMemoryCache>();
        var repo = sp.GetService<IUserRepository>();
        if (repo == null) return true; // best-effort — don't lock everyone out on misconfig

        var cacheKey = CacheKeyPrefix + userId;
        Guid? canonical;
        if (cache != null && cache.TryGetValue<Guid?>(cacheKey, out var cached))
        {
            canonical = cached;
        }
        else
        {
            canonical = await repo.GetCurrentSessionIdAsync(userId);
            if (cache != null)
            {
                // NSCIM's IMemoryCache is configured with a SizeLimit, so every
                // entry MUST specify a Size — otherwise SetEntry throws
                // "Cache entry must specify a value for Size when SizeLimit is set".
                // Defensive try/catch keeps a misconfigured cache from breaking
                // auth entirely; we just pay the DB hit on every request.
                try
                {
                    cache.Set(cacheKey, canonical, new MemoryCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = CacheTtl,
                        Size = 1
                    });
                }
                catch (InvalidOperationException)
                {
                    // Swallow — auth flow continues with a fresh DB read each time.
                }
            }
        }

        if (!canonical.HasValue) return false;
        return string.Equals(canonical.Value.ToString(), sidClaim, StringComparison.OrdinalIgnoreCase);
    }
}
