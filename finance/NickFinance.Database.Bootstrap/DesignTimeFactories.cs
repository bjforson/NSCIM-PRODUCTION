using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using NickFinance.AP;
using NickFinance.AR;
using NickFinance.Coa;
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

/// <inheritdoc cref="LedgerDesignTimeFactory"/>
public sealed class CoaDesignTimeFactory : IDesignTimeDbContextFactory<CoaDbContext>
{
    public CoaDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("NICKERP_FINANCE_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=nickerp_design_only;Username=postgres;Password=design";
        var opts = new DbContextOptionsBuilder<CoaDbContext>().UseNpgsql(conn).Options;
        return new CoaDbContext(opts);
    }
}

/// <inheritdoc cref="LedgerDesignTimeFactory"/>
public sealed class ArDesignTimeFactory : IDesignTimeDbContextFactory<ArDbContext>
{
    public ArDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("NICKERP_FINANCE_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=nickerp_design_only;Username=postgres;Password=design";
        var opts = new DbContextOptionsBuilder<ArDbContext>().UseNpgsql(conn).Options;
        return new ArDbContext(opts);
    }
}

/// <inheritdoc cref="LedgerDesignTimeFactory"/>
public sealed class ApDesignTimeFactory : IDesignTimeDbContextFactory<ApDbContext>
{
    public ApDbContext CreateDbContext(string[] args)
    {
        var conn = Environment.GetEnvironmentVariable("NICKERP_FINANCE_DB_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=nickerp_design_only;Username=postgres;Password=design";
        var opts = new DbContextOptionsBuilder<ApDbContext>().UseNpgsql(conn).Options;
        return new ApDbContext(opts);
    }
}
