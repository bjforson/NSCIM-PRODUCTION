using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NickScanCentralImagingPortal.API.Controllers;
using NickScanCentralImagingPortal.Core.Interfaces;
using NickScanCentralImagingPortal.Services.Caching;
using Xunit;

namespace NickScanCentralImagingPortal.Integration.Tests.Caching;

public sealed class SystemCacheRegistrationTests
{
    [Fact]
    public void DefaultRegistration_KeepsLegacyDistributedCacheActive()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        var activeCache = scope.ServiceProvider.GetRequiredService<ICacheService>();
        var systemCache = scope.ServiceProvider.GetRequiredService<ISystemCacheService>();

        Assert.IsType<RedisCacheService>(activeCache);
        Assert.IsType<SystemCacheService>(systemCache);
        Assert.NotSame(systemCache, activeCache);
    }

    [Fact]
    public void EnabledRegistration_MakesSystemCacheActive()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"{SystemCacheOptions.SectionName}:{nameof(SystemCacheOptions.UseSystemCacheService)}"] = "true"
        });
        using var scope = provider.CreateScope();

        var activeCache = scope.ServiceProvider.GetRequiredService<ICacheService>();
        var systemCache = scope.ServiceProvider.GetRequiredService<ISystemCacheService>();

        Assert.IsType<SystemCacheService>(activeCache);
        Assert.Same(systemCache, activeCache);
    }

    [Fact]
    public void Registration_BindsSystemCacheOptions()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"{SystemCacheOptions.SectionName}:{nameof(SystemCacheOptions.UseSystemCacheService)}"] = "true",
            [$"{SystemCacheOptions.SectionName}:{nameof(SystemCacheOptions.UseL1MemoryCache)}"] = "false",
            [$"{SystemCacheOptions.SectionName}:{nameof(SystemCacheOptions.DefaultExpirationMinutes)}"] = "7"
        });

        var options = provider.GetRequiredService<IOptions<SystemCacheOptions>>().Value;

        Assert.True(options.UseSystemCacheService);
        Assert.False(options.UseL1MemoryCache);
        Assert.Equal(7, options.DefaultExpirationMinutes);
    }

    [Fact]
    public void StatusEndpoint_ReportsLegacyRegistrationWhenFlagIsDisabled()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var controller = NewController(scope.ServiceProvider);

        var action = controller.GetStatus();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var status = Assert.IsType<SystemCacheStatusSnapshot>(ok.Value);
        Assert.False(status.UseSystemCacheService);
        Assert.False(status.SystemCacheActive);
        Assert.Equal(nameof(RedisCacheService), status.ActiveImplementation);
    }

    [Fact]
    public void StatusEndpoint_ReportsSystemCacheRegistrationWhenFlagIsEnabled()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            [$"{SystemCacheOptions.SectionName}:{nameof(SystemCacheOptions.UseSystemCacheService)}"] = "true"
        });
        using var scope = provider.CreateScope();
        var controller = NewController(scope.ServiceProvider);

        var action = controller.GetStatus();

        var ok = Assert.IsType<OkObjectResult>(action.Result);
        var status = Assert.IsType<SystemCacheStatusSnapshot>(ok.Value);
        Assert.True(status.UseSystemCacheService);
        Assert.True(status.SystemCacheActive);
        Assert.Equal(nameof(SystemCacheService), status.ActiveImplementation);
    }

    private static ServiceProvider BuildProvider(
        Dictionary<string, string?>? configurationValues = null)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(configurationValues ?? [])
            .Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddMemoryCache();
        services.AddDistributedMemoryCache();
        services.AddSystemCacheServices(configuration);

        return services.BuildServiceProvider(validateScopes: true);
    }

    private static SystemCacheController NewController(IServiceProvider provider)
    {
        return new SystemCacheController(
            provider.GetRequiredService<ICacheService>(),
            provider.GetRequiredService<ISystemCacheService>(),
            provider.GetRequiredService<SystemCacheMetrics>(),
            provider.GetRequiredService<SystemCacheWarmupService>(),
            provider.GetRequiredService<IOptions<SystemCacheOptions>>(),
            NullLogger<SystemCacheController>.Instance);
    }
}
