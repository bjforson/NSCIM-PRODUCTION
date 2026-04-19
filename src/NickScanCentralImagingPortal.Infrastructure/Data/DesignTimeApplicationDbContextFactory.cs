using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    public class DesignTimeApplicationDbContextFactory : IDesignTimeDbContextFactory<ApplicationDbContext>
    {
        public ApplicationDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables("NICKSCAN_")
                .Build();

            var connectionString =
                Environment.GetEnvironmentVariable("NICKSCAN_NS_CIS_CONNECTION")
                ?? configuration.GetConnectionString("NS_CIS_Connection")
                ?? throw new InvalidOperationException("Connection string 'NS_CIS_Connection' not found.");

            var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
            optionsBuilder.UseNpgsql(connectionString, npg =>
            {
                npg.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
                npg.MigrationsAssembly(typeof(ApplicationDbContext).Assembly.FullName);
            });

            // Suppress EF pending model changes warning at design-time too
            optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

            return new ApplicationDbContext(optionsBuilder.Options);
        }
    }
}


