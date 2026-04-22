using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace NickScanCentralImagingPortal.Infrastructure.Data
{
    /// <summary>
    /// Enables `dotnet ef migrations add` without spinning up the API host,
    /// so migrations can be generated even when NSCIM_API is running.
    /// Mirrors DesignTimeApplicationDbContextFactory's pattern.
    /// </summary>
    public class DesignTimeIcumDownloadsDbContextFactory : IDesignTimeDbContextFactory<IcumDownloadsDbContext>
    {
        public IcumDownloadsDbContext CreateDbContext(string[] args)
        {
            var configuration = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json", optional: true)
                .AddJsonFile("appsettings.Development.json", optional: true)
                .AddEnvironmentVariables("NICKSCAN_")
                .Build();

            var connectionString =
                Environment.GetEnvironmentVariable("NICKSCAN_ICUMS_DOWNLOADS_CONNECTION")
                ?? configuration.GetConnectionString("ICUMS_Downloads_Connection")
                ?? throw new InvalidOperationException("Connection string 'ICUMS_Downloads_Connection' not found.");

            var optionsBuilder = new DbContextOptionsBuilder<IcumDownloadsDbContext>();
            optionsBuilder.UseNpgsql(connectionString, npg =>
            {
                npg.EnableRetryOnFailure(5, TimeSpan.FromSeconds(5), null);
                npg.MigrationsAssembly(typeof(IcumDownloadsDbContext).Assembly.FullName);
            });

            optionsBuilder.ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning));

            return new IcumDownloadsDbContext(optionsBuilder.Options);
        }
    }
}
