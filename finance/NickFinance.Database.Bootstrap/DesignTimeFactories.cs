using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NickFinance.Ledger;
using NickFinance.PettyCash;

namespace NickFinance.Database.Bootstrap;

/// <summary>
/// Design-time DbContext factory used by <c>dotnet ef migrations add</c>
/// and other tooling that needs to instantiate the context outside of a
/// running app. Returns a context wired to a placeholder connection string
/// — migration generation only inspects the model, never opens a real
/// connection.
/// </summary>
public sealed class LedgerDesignTimeFactory : IDesignTimeDbContextFactory<LedgerDbContext>
{
    public LedgerDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("NICKERP_FINANCE_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=nickerp_design_only;Username=postgres;Password=design";
        var opts = new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(conn).Options;
        return new LedgerDbContext(opts);
    }
}

/// <inheritdoc cref="LedgerDesignTimeFactory"/>
public sealed class PettyCashDesignTimeFactory : IDesignTimeDbContextFactory<PettyCashDbContext>
{
    public PettyCashDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("NICKERP_FINANCE_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=nickerp_design_only;Username=postgres;Password=design";
        var opts = new DbContextOptionsBuilder<PettyCashDbContext>().UseNpgsql(conn).Options;
        return new PettyCashDbContext(opts);
    }
}
