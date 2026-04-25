using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;

namespace NickERP.Platform.Identity.Auth;

/// <summary>
/// Fetches and caches Cloudflare Access's JWKS (signing-key set) so the
/// JWT validator doesn't hit the network on every request. Refreshes
/// every <see cref="CfJwksFetcher.CacheLifetime"/>; serves stale-but-recent
/// keys on transient fetch failures so a single CF blip doesn't take
/// the whole identity layer down.
/// </summary>
public interface ICfJwksFetcher
{
    /// <summary>Get the current set of CF Access signing keys.</summary>
    Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken ct = default);
}

public sealed class CfJwksFetcher : ICfJwksFetcher
{
    public static readonly TimeSpan CacheLifetime = TimeSpan.FromHours(1);
    public static readonly TimeSpan StaleAcceptableFor = TimeSpan.FromHours(24);

    private readonly HttpClient _http;
    private readonly CfAccessOptions _options;
    private readonly ILogger<CfJwksFetcher> _logger;
    private readonly TimeProvider _clock;
    private readonly SemaphoreSlim _gate = new(1, 1);

    private IReadOnlyCollection<SecurityKey> _cached = Array.Empty<SecurityKey>();
    private DateTimeOffset _cachedAt = DateTimeOffset.MinValue;

    public CfJwksFetcher(
        HttpClient http,
        Microsoft.Extensions.Options.IOptions<CfAccessOptions> options,
        ILogger<CfJwksFetcher> logger,
        TimeProvider? clock = null)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
        _clock = clock ?? TimeProvider.System;
    }

    public async Task<IReadOnlyCollection<SecurityKey>> GetSigningKeysAsync(CancellationToken ct = default)
    {
        var now = _clock.GetUtcNow();
        var age = now - _cachedAt;
        if (_cached.Count > 0 && age < CacheLifetime) return _cached;

        await _gate.WaitAsync(ct);
        try
        {
            // Re-check after lock — another caller may have refreshed.
            now = _clock.GetUtcNow();
            age = now - _cachedAt;
            if (_cached.Count > 0 && age < CacheLifetime) return _cached;

            try
            {
                var fresh = await FetchAsync(ct);
                _cached = fresh;
                _cachedAt = now;
                _logger.LogDebug("CfJwks: refreshed {Count} keys from {Url}", fresh.Count, _options.JwksUrl);
                return _cached;
            }
            catch (Exception ex)
            {
                if (_cached.Count > 0 && age < StaleAcceptableFor)
                {
                    _logger.LogWarning(ex,
                        "CfJwks: refresh failed; serving stale keys (age {AgeMin} min) from {Url}",
                        (int)age.TotalMinutes, _options.JwksUrl);
                    return _cached;
                }
                _logger.LogError(ex, "CfJwks: refresh failed and no usable cached keys.");
                throw;
            }
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<IReadOnlyCollection<SecurityKey>> FetchAsync(CancellationToken ct)
    {
        using var resp = await _http.GetAsync(_options.JwksUrl, ct);
        resp.EnsureSuccessStatusCode();
        var json = await resp.Content.ReadAsStringAsync(ct);
        var jwks = new JsonWebKeySet(json);
        // Each key in the set is a usable SecurityKey. Cast once here.
        return jwks.Keys.Cast<SecurityKey>().ToList();
    }
}
