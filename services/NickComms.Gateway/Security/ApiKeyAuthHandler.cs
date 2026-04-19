using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using NickComms.Gateway.Data;

namespace NickComms.Gateway.Security;

public class ApiKeyAuthOptions : AuthenticationSchemeOptions
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";
}

public class ApiKeyAuthHandler : AuthenticationHandler<ApiKeyAuthOptions>
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IMemoryCache _cache;

    public ApiKeyAuthHandler(
        IOptionsMonitor<ApiKeyAuthOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        IServiceScopeFactory scopeFactory,
        IMemoryCache cache)
        : base(options, logger, encoder)
    {
        _scopeFactory = scopeFactory;
        _cache = cache;
    }

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(ApiKeyAuthOptions.HeaderName, out var apiKeyHeader))
            return AuthenticateResult.NoResult();

        var apiKey = apiKeyHeader.ToString();
        if (string.IsNullOrWhiteSpace(apiKey))
            return AuthenticateResult.Fail("API key is empty");

        var keyHash = HashKey(apiKey);

        if (_cache.TryGetValue<string>($"apikey:{keyHash}", out var cachedAppName) && cachedAppName != null)
            return AuthenticateResult.Success(CreateTicket(cachedAppName));

        using var scope = _scopeFactory.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<CommsDbContext>();

        var key = await db.ApiKeys
            .AsNoTracking()
            .FirstOrDefaultAsync(k => k.KeyHash == keyHash && k.IsActive);

        if (key == null)
            return AuthenticateResult.Fail("Invalid or inactive API key");

        _cache.Set($"apikey:{keyHash}", key.AppName, TimeSpan.FromMinutes(5));

        _ = Task.Run(async () =>
        {
            using var bgScope = _scopeFactory.CreateScope();
            var bgDb = bgScope.ServiceProvider.GetRequiredService<CommsDbContext>();
            await bgDb.ApiKeys.Where(k => k.Id == key.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(k => k.LastUsedAt, DateTime.UtcNow));
        });

        return AuthenticateResult.Success(CreateTicket(key.AppName));
    }

    private AuthenticationTicket CreateTicket(string appName)
    {
        var claims = new[] { new Claim("AppName", appName) };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);
        return new AuthenticationTicket(principal, Scheme.Name);
    }

    public static string HashKey(string key)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
