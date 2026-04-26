using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace NickComms.Gateway.Data;

/// <summary>
/// Design-time-only factory so <c>dotnet ef migrations add</c> can build
/// <see cref="CommsDbContext"/> without spinning up the full host. The runtime
/// host can't start under the EF tooling because <c>Program.cs</c> grabs a
/// global Mutex (single-instance enforcement) and exits if another process
/// already holds it — which is the running Windows service in production.
///
/// Connection-string resolution mirrors the runtime path: env-var-first
/// (<c>NICKSCAN_DB_PASSWORD</c>) with a localhost-postgres fallback for ad-hoc
/// dev runs. NEVER read this in the actual app — that's <c>Program.cs</c>'s job.
/// </summary>
public class CommsDbContextFactory : IDesignTimeDbContextFactory<CommsDbContext>
{
    public CommsDbContext CreateDbContext(string[] args)
    {
        var pwd = Environment.GetEnvironmentVariable("NICKSCAN_DB_PASSWORD") ?? "postgres";
        var connString = Environment.GetEnvironmentVariable("NICKCOMMS_CommsDb")
            ?? $"Host=localhost;Port=5432;Database=nick_comms;Username=postgres;Password={pwd}";

        var options = new DbContextOptionsBuilder<CommsDbContext>()
            .UseNpgsql(connString)
            .Options;

        return new CommsDbContext(options);
    }
}
