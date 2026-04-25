using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NickERP.Platform.Identity.Auth;
using NickERP.Platform.Identity.Entities;

namespace NickERP.Platform.Identity.Resolver;

/// <summary>
/// The single contract every NickERP app calls. Reads the request,
/// validates whatever credential it carries, persists / loads the
/// canonical user record, and returns a <see cref="ResolvedIdentity"/>
/// or null. Apps never inspect the raw JWT.
/// </summary>
public interface IIdentityResolver
{
    Task<ResolvedIdentity?> ResolveAsync(HttpContext ctx, CancellationToken ct = default);
}

public sealed class IdentityResolver : IIdentityResolver
{
    private readonly IdentityDbContext _db;
    private readonly ICfAccessJwtValidator _validator;
    private readonly CfAccessOptions _options;
    private readonly IHostEnvironmentInfo _env;
    private readonly ILogger<IdentityResolver> _logger;
    private readonly TimeProvider _clock;

    public IdentityResolver(
        IdentityDbContext db,
        ICfAccessJwtValidator validator,
        Microsoft.Extensions.Options.IOptions<CfAccessOptions> options,
        IHostEnvironmentInfo env,
        ILogger<IdentityResolver> logger,
        TimeProvider? clock = null)
    {
        _db = db;
        _validator = validator;
        _options = options.Value;
        _env = env;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<ResolvedIdentity?> ResolveAsync(HttpContext ctx, CancellationToken ct = default)
    {
        // Order of attempts:
        //   1. Real CF Access JWT — production path.
        //   2. Service-token client id — machine path.
        //   3. Dev bypass header — Development env only, opt-in via config.
        var jwt = ctx.Request.Headers[CfAccessOptions.JwtHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(jwt))
        {
            var principal = await _validator.ValidateAsync(jwt, ct);
            if (principal is null) return null;
            return await ResolveHumanAsync(principal, ct);
        }

        var clientId = ctx.Request.Headers[CfAccessOptions.ClientIdHeader].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(clientId))
            return await ResolveServiceAsync(clientId, ct);

        if (_options.AllowDevBypass && _env.IsDevelopment)
        {
            var devEmail = ctx.Request.Headers[CfAccessOptions.DevBypassHeader].FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(devEmail))
                return await ResolveHumanByEmailAsync(devEmail, IdentityKind.Dev, ct);
        }

        return null;
    }

    private async Task<ResolvedIdentity?> ResolveHumanAsync(ClaimsPrincipal principal, CancellationToken ct)
    {
        var email = principal.FindFirst("email")?.Value
                 ?? principal.FindFirst(ClaimTypes.Email)?.Value;
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning("IdentityResolver: JWT carried no email claim; rejecting.");
            return null;
        }

        return await ResolveHumanByEmailAsync(email, IdentityKind.Human, ct);
    }

    private async Task<ResolvedIdentity?> ResolveHumanByEmailAsync(string email, IdentityKind kind, CancellationToken ct)
    {
        var normalised = email.Trim().ToUpperInvariant();
        var user = await _db.Users
            .Include(u => u.Scopes)
            .FirstOrDefaultAsync(u => u.NormalizedEmail == normalised, ct);

        if (user is null)
        {
            // First-login auto-provision: a real CF JWT proves the caller
            // already passed Access policy, so we know the email is one
            // we trust. Created with empty scope set; an admin grants
            // scopes after.
            user = new IdentityUser
            {
                Email = email.Trim().ToLowerInvariant(),
                NormalizedEmail = normalised,
                IsActive = true,
                CreatedAt = _clock.GetUtcNow(),
                UpdatedAt = _clock.GetUtcNow(),
                LastSeenAt = _clock.GetUtcNow(),
            };
            _db.Users.Add(user);
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("IdentityResolver: provisioned new user {Email}.", user.Email);
        }
        else
        {
            user.LastSeenAt = _clock.GetUtcNow();
            user.UpdatedAt = _clock.GetUtcNow();
            await _db.SaveChangesAsync(ct);
        }

        if (!user.IsActive)
        {
            _logger.LogInformation("IdentityResolver: rejecting inactive user {Email}.", user.Email);
            return null;
        }

        var now = _clock.GetUtcNow();
        var activeScopes = user.Scopes
            .Where(s => s.RevokedAt is null && (s.ExpiresAt is null || s.ExpiresAt > now))
            .Select(s => s.AppScopeCode);

        return new ResolvedIdentity
        {
            UserId = user.Id,
            Email = user.Email,
            DisplayName = user.DisplayName ?? user.Email,
            Kind = kind,
            TenantId = user.TenantId,
            Scopes = new HashSet<string>(activeScopes, StringComparer.OrdinalIgnoreCase),
        };
    }

    private async Task<ResolvedIdentity?> ResolveServiceAsync(string clientId, CancellationToken ct)
    {
        var token = await _db.ServiceTokens
            .Include(t => t.Scopes)
            .FirstOrDefaultAsync(t => t.TokenClientId == clientId, ct);

        if (token is null)
        {
            _logger.LogWarning("IdentityResolver: unknown service token client id {ClientId}.", clientId);
            return null;
        }
        if (!token.IsActive)
        {
            _logger.LogInformation("IdentityResolver: rejecting inactive service token {Display}.", token.DisplayName);
            return null;
        }

        var now = _clock.GetUtcNow();
        if (token.ExpiresAt is not null && token.ExpiresAt <= now)
        {
            _logger.LogInformation("IdentityResolver: rejecting expired service token {Display}.", token.DisplayName);
            return null;
        }

        token.LastSeenAt = now;
        await _db.SaveChangesAsync(ct);

        var activeScopes = token.Scopes
            .Where(s => s.RevokedAt is null && (s.ExpiresAt is null || s.ExpiresAt > now))
            .Select(s => s.AppScopeCode);

        return new ResolvedIdentity
        {
            ServiceTokenId = token.Id,
            DisplayName = token.DisplayName,
            Kind = IdentityKind.ServiceToken,
            TenantId = token.TenantId,
            Scopes = new HashSet<string>(activeScopes, StringComparer.OrdinalIgnoreCase),
        };
    }
}

/// <summary>
/// Tiny abstraction over <c>IHostEnvironment</c> so the resolver doesn't
/// take a hard ASP.NET dependency. Implemented in the DI extension.
/// </summary>
public interface IHostEnvironmentInfo
{
    bool IsDevelopment { get; }
}
