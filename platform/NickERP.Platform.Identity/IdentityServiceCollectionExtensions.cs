using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NickERP.Platform.Identity.Auth;
using NickERP.Platform.Identity.Resolver;

namespace NickERP.Platform.Identity;

/// <summary>
/// Single entry-point a host calls to wire up the entire identity layer:
/// EF context, JWKS fetcher, JWT validator, resolver, and the dev-bypass
/// hook. Apps never new-up these classes themselves.
/// </summary>
public static class IdentityServiceCollectionExtensions
{
    /// <summary>
    /// Wire up the identity layer.
    /// Reads connection string from <c>ConnectionStrings:NickErpPlatform</c>
    /// (or the env-var fallback <c>NICKERP_PLATFORM_DB</c> applied by the
    /// caller) and config from <c>Identity:CfAccess</c>.
    /// </summary>
    public static IServiceCollection AddNickErpIdentity(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<CfAccessOptions>(config.GetSection("Identity:CfAccess"));

        var connectionString = config.GetConnectionString("NickErpPlatform")
            ?? Environment.GetEnvironmentVariable("NICKERP_PLATFORM_DB")
            ?? throw new InvalidOperationException(
                "ConnectionStrings:NickErpPlatform (or env var NICKERP_PLATFORM_DB) must be set.");

        services.AddDbContext<IdentityDbContext>(opt =>
            opt.UseNpgsql(connectionString,
                npg => npg.MigrationsHistoryTable("__ef_migrations_history", IdentityDbContext.SchemaName)));

        // JWKS fetcher uses a typed HttpClient so retries/timeouts can be
        // configured by the host once. Default timeout 10 s is plenty for
        // the small (<5 KB) JWKS document.
        services.AddHttpClient<ICfJwksFetcher, CfJwksFetcher>(c =>
        {
            c.Timeout = TimeSpan.FromSeconds(10);
        });

        services.AddScoped<ICfAccessJwtValidator, CfAccessJwtValidator>();
        services.AddScoped<IIdentityResolver, IdentityResolver>();
        services.AddSingleton<IHostEnvironmentInfo, RuntimeHostEnvironmentInfo>();

        return services;
    }

    private sealed class RuntimeHostEnvironmentInfo : IHostEnvironmentInfo
    {
        public bool IsDevelopment =>
            string.Equals(
                Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"),
                "Development",
                StringComparison.OrdinalIgnoreCase);
    }
}
