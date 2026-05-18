using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NickScanCentralImagingPortal.Core.Interfaces;

namespace NickScanCentralImagingPortal.Services.Caching;

public static class SystemCacheServiceCollectionExtensions
{
    public static IServiceCollection AddSystemCacheServices(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<string>? logRegistration = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<SystemCacheOptions>(
            configuration.GetSection(SystemCacheOptions.SectionName));

        services.TryAddScoped<RedisCacheService>();
        services.TryAddSingleton<SystemCacheMetrics>();
        services.TryAddScoped<SystemCacheService>();
        services.TryAddScoped<ISystemCacheService>(sp =>
            sp.GetRequiredService<SystemCacheService>());
        services.TryAddSingleton<SystemCacheWarmupState>();
        services.TryAddSingleton<SystemCacheWarmupService>();

        var useSystemCacheService = configuration.GetValue<bool>(
            $"{SystemCacheOptions.SectionName}:{nameof(SystemCacheOptions.UseSystemCacheService)}",
            false);

        if (useSystemCacheService)
        {
            services.AddScoped<ICacheService>(sp =>
                sp.GetRequiredService<SystemCacheService>());
            logRegistration?.Invoke("System-wide L1/L2 cache service enabled");
        }
        else
        {
            services.AddScoped<ICacheService, RedisCacheService>();
            logRegistration?.Invoke("Legacy distributed cache service remains active");
        }

        return services;
    }
}
