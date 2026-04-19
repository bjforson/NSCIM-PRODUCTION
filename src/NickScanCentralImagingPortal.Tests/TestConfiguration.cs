using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using NickScanCentralImagingPortal.Infrastructure.Data;

namespace NickScanCentralImagingPortal.Tests
{
    /// <summary>
    /// Minimal test configuration helpers.
    ///
    /// History: this file used to seed a wide range of services and test data, but
    /// most of those references have rotted as the production codebase moved on
    /// (renamed services, removed repositories, restructured DbSets). The previous
    /// version had a dozen broken type references that prevented the test project
    /// from compiling at all.
    ///
    /// This rewrite keeps only what compiles and is genuinely useful: an in-memory
    /// service collection bootstrap with the two DbContexts and a small in-memory
    /// configuration. New tests should add the specific services they need rather
    /// than relying on an all-in-one helper that drifts.
    /// </summary>
    public static class TestConfiguration
    {
        /// <summary>
        /// Bootstrap a minimal service collection suitable for unit tests that need a
        /// DbContext and basic configuration. Add additional services in the test
        /// itself as needed.
        /// </summary>
        public static IServiceCollection ConfigureTestServices(this IServiceCollection services)
        {
            var configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ICUMS:ApiUrl"] = "https://test-api.example.com",
                    ["ICUMS:ApiKey"] = "test-api-key",
                    ["BackgroundServices:IcumBackgroundService:BatchIntervalMinutes"] = "30",
                    ["BackgroundServices:ContainerDataMapperService:MappingIntervalMinutes"] = "5",
                })
                .Build();

            services.AddSingleton<IConfiguration>(configuration);

            services.AddDbContext<ApplicationDbContext>(options =>
                options.UseInMemoryDatabase("TestAppDb"));
            services.AddDbContext<IcumDownloadsDbContext>(options =>
                options.UseInMemoryDatabase("TestIcumDb"));

            services.AddLogging(builder => builder.AddConsole());
            services.AddMemoryCache();

            return services;
        }
    }
}
