using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace NickERP.Platform.Identity.Auth;

/// <summary>
/// Validates a Cloudflare Access JWT (signature, issuer, audience,
/// expiry) and returns the carried claims. Wraps
/// <see cref="JwtSecurityTokenHandler"/> with our specific token-shape
/// rules so the rest of the codebase doesn't have to think about JWTs.
/// </summary>
public interface ICfAccessJwtValidator
{
    /// <summary>
    /// Returns the validated <see cref="ClaimsPrincipal"/> on success,
    /// or null on any validation failure (logs the reason).
    /// </summary>
    Task<ClaimsPrincipal?> ValidateAsync(string jwt, CancellationToken ct = default);
}

public sealed class CfAccessJwtValidator : ICfAccessJwtValidator
{
    private readonly ICfJwksFetcher _jwks;
    private readonly CfAccessOptions _options;
    private readonly ILogger<CfAccessJwtValidator> _logger;
    private readonly JwtSecurityTokenHandler _handler = new();

    public CfAccessJwtValidator(
        ICfJwksFetcher jwks,
        Microsoft.Extensions.Options.IOptions<CfAccessOptions> options,
        ILogger<CfAccessJwtValidator> logger)
    {
        _jwks = jwks;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ClaimsPrincipal?> ValidateAsync(string jwt, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(jwt))
        {
            _logger.LogDebug("CfAccessJwtValidator: empty token rejected.");
            return null;
        }

        IReadOnlyCollection<SecurityKey> keys;
        try { keys = await _jwks.GetSigningKeysAsync(ct); }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CfAccessJwtValidator: cannot fetch JWKS; rejecting token.");
            return null;
        }

        var parameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = _options.Issuer,
            ValidateAudience = true,
            ValidAudience = _options.Audience,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            IssuerSigningKeys = keys,
            // Default ClockSkew is 5 min; CF JWTs are short-lived, but small
            // skew is safer than dropping legit requests around boundaries.
            ClockSkew = TimeSpan.FromMinutes(2),
            // CF Access JWTs use the email address as the "sub" via the
            // common email claim — we don't require a name claim mapping here.
            NameClaimType = "email",
        };

        try
        {
            var principal = _handler.ValidateToken(jwt, parameters, out _);
            return principal;
        }
        catch (SecurityTokenExpiredException ex)
        {
            _logger.LogInformation("CfAccessJwtValidator: token expired ({Message})", ex.Message);
            return null;
        }
        catch (SecurityTokenInvalidSignatureException ex)
        {
            _logger.LogWarning(ex, "CfAccessJwtValidator: bad signature");
            return null;
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "CfAccessJwtValidator: token rejected ({Type})", ex.GetType().Name);
            return null;
        }
    }
}
