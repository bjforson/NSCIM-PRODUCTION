using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using NickERP.Platform.Identity;
using NickFinance.Ledger;
using NickFinance.PettyCash;
using Xunit;

namespace NickERP.Platform.Identity.Tests;

/// <summary>
/// Identity-suite fixture. Spins up a throwaway Postgres DB
/// (<c>nickscan_identity_test</c>) and applies the Identity migrations
/// (which include the seed of the 6 canonical roles). Tenant query
/// filters are off by default — tests can construct a context with a
/// real <see cref="FixedTenantAccessor"/> when they want to exercise
/// the filter.
/// </summary>
public sealed class IdentityFixture : IAsyncLifetime
{
    public const string EnvVar = "NICKFINANCE_TEST_DB";
    public string ConnectionString { get; }

    public IdentityFixture()
    {
        var raw = Environment.GetEnvironmentVariable(EnvVar)
            ?? throw new InvalidOperationException(
                $"{EnvVar} env var is required for Identity tests. Example: "
                + "Host=localhost;Port=5432;Database=nickscan_finance_test;Username=postgres;Password=...");
        ConnectionString = RewriteDatabase(raw, "nickscan_identity_test");
    }

    public IdentityDbContext CreateIdentity(ITenantAccessor? tenant = null) =>
        new(new DbContextOptionsBuilder<IdentityDbContext>().UseNpgsql(ConnectionString).Options, tenant);

    public LedgerDbContext CreateLedger(ITenantAccessor? tenant = null) =>
        new(new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(ConnectionString).Options, tenant);

    public PettyCashDbContext CreatePettyCash(ITenantAccessor? tenant = null) =>
        new(new DbContextOptionsBuilder<PettyCashDbContext>().UseNpgsql(ConnectionString).Options, tenant);

    public async Task InitializeAsync()
    {
        // Apply EF migrations (creates schemas + tables + seeds the 6 roles).
        await using (var id = CreateIdentity())
        {
            await id.Database.EnsureDeletedAsync();
            await id.Database.MigrateAsync();
        }
        // The PettyCash + Ledger contexts share the same DB; create their tables
        // so the tenant-query-filter tests have somewhere to insert vouchers.
        await using (var ledger = CreateLedger())
        {
            var creator = (IRelationalDatabaseCreator)ledger.Database.GetService<IDatabaseCreator>();
            await creator.CreateTablesAsync();
        }
        await using (var pc = CreatePettyCash())
        {
            var creator = (IRelationalDatabaseCreator)pc.Database.GetService<IDatabaseCreator>();
            await creator.CreateTablesAsync();
        }
    }

    public async Task DisposeAsync()
    {
        try
        {
            await using var id = CreateIdentity();
            await id.Database.EnsureDeletedAsync();
        }
        catch
        {
            // Best-effort.
        }
    }

    private static string RewriteDatabase(string conn, string newDbName)
    {
        var parts = conn.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var rebuilt = new List<string>();
        var saw = false;
        foreach (var p in parts)
        {
            var ix = p.IndexOf('=', StringComparison.Ordinal);
            if (ix < 0) { rebuilt.Add(p); continue; }
            var key = p[..ix].Trim();
            if (string.Equals(key, "Database", StringComparison.OrdinalIgnoreCase))
            {
                rebuilt.Add($"Database={newDbName}");
                saw = true;
            }
            else
            {
                rebuilt.Add(p);
            }
        }
        if (!saw) rebuilt.Add($"Database={newDbName}");
        return string.Join(';', rebuilt);
    }
}

[CollectionDefinition("Identity")]
public class IdentityCollection : ICollectionFixture<IdentityFixture> { }
