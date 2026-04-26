using Microsoft.EntityFrameworkCore;
using NickFinance.AP;
using NickFinance.AR;
using NickFinance.Coa;
using NickFinance.Ledger;
using NickFinance.PettyCash;

namespace NickFinance.Database.Bootstrap;

/// <summary>
/// CLI entry point. Applies EF migrations for both the Ledger and Petty
/// Cash schemas, applies the Postgres-level triggers (balance + append-only),
/// and optionally seeds the Ghana standard chart of accounts.
/// </summary>
/// <remarks>
/// Idempotent: safe to re-run. Existing migrations are skipped, existing
/// triggers are dropped + recreated, existing CoA accounts are left
/// untouched.
/// </remarks>
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var conn = ResolveConnectionString(args);
        if (conn is null)
        {
            Console.Error.WriteLine("usage: NickFinance.Database.Bootstrap --conn <postgres-connection-string> [--seed-coa]");
            Console.Error.WriteLine("  or: NICKERP_FINANCE_DB_CONNECTION env var + (optional) --seed-coa flag");
            return 1;
        }

        var seedCoa = args.Any(a => string.Equals(a, "--seed-coa", StringComparison.OrdinalIgnoreCase));

        Console.WriteLine("NickFinance bootstrap");
        Console.WriteLine($"  Target: {RedactPassword(conn)}");
        Console.WriteLine($"  Seed CoA: {(seedCoa ? "yes" : "no (run again with --seed-coa)")}");
        Console.WriteLine();

        try
        {
            await ApplyLedgerMigrationsAsync(conn);
            await ApplyPettyCashMigrationsAsync(conn);
            await ApplyCoaMigrationsAsync(conn);
            await ApplyArMigrationsAsync(conn);
            await ApplyApMigrationsAsync(conn);
            await ApplySchemaTriggersAsync(conn);
            if (seedCoa) await SeedGhanaChartAsync(conn);

            Console.WriteLine();
            Console.WriteLine("Bootstrap complete. All NickFinance schemas are ready.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("BOOTSTRAP FAILED:");
            Console.Error.WriteLine(ex);
            return 2;
        }
    }

    // -----------------------------------------------------------------
    // Steps
    // -----------------------------------------------------------------

    private static async Task ApplyLedgerMigrationsAsync(string conn)
    {
        Console.WriteLine("[1/4] Applying Ledger migrations (finance schema)...");
        var opts = new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(conn).Options;
        await using var db = new LedgerDbContext(opts);
        await db.Database.MigrateAsync();
        Console.WriteLine("       Ledger: up to date.");
    }

    private static async Task ApplyPettyCashMigrationsAsync(string conn)
    {
        Console.WriteLine("[2/5] Applying Petty Cash migrations (petty_cash schema)...");
        var opts = new DbContextOptionsBuilder<PettyCashDbContext>().UseNpgsql(conn).Options;
        await using var db = new PettyCashDbContext(opts);
        await db.Database.MigrateAsync();
        Console.WriteLine("       Petty Cash: up to date.");
    }

    private static async Task ApplyCoaMigrationsAsync(string conn)
    {
        Console.WriteLine("[3/5] Applying CoA migrations (coa schema)...");
        var opts = new DbContextOptionsBuilder<CoaDbContext>().UseNpgsql(conn).Options;
        await using var db = new CoaDbContext(opts);
        await db.Database.MigrateAsync();
        Console.WriteLine("       CoA: up to date.");
    }

    private static async Task ApplyArMigrationsAsync(string conn)
    {
        Console.WriteLine("[4/7] Applying AR migrations (ar schema)...");
        var opts = new DbContextOptionsBuilder<ArDbContext>().UseNpgsql(conn).Options;
        await using var db = new ArDbContext(opts);
        await db.Database.MigrateAsync();
        Console.WriteLine("       AR: up to date.");
    }

    private static async Task ApplyApMigrationsAsync(string conn)
    {
        Console.WriteLine("[5/7] Applying AP migrations (ap schema)...");
        var opts = new DbContextOptionsBuilder<ApDbContext>().UseNpgsql(conn).Options;
        await using var db = new ApDbContext(opts);
        await db.Database.MigrateAsync();
        Console.WriteLine("       AP: up to date.");
    }

    private static async Task ApplySchemaTriggersAsync(string conn)
    {
        Console.WriteLine("[6/7] Applying Postgres triggers (balance invariant + append-only)...");
        var opts = new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(conn).Options;
        await using var db = new LedgerDbContext(opts);
        await SchemaBootstrap.ApplyConstraintsAsync(db);
        Console.WriteLine("       Triggers: applied.");
    }

    private static async Task SeedGhanaChartAsync(string conn)
    {
        Console.WriteLine("[7/7] Seeding Ghana standard chart of accounts...");
        var opts = new DbContextOptionsBuilder<CoaDbContext>().UseNpgsql(conn).Options;
        await using var db = new CoaDbContext(opts);
        var svc = new CoaService(db);
        var inserted = await svc.SeedGhanaStandardChartAsync(tenantId: 1);
        Console.WriteLine($"       CoA seed: inserted {inserted} new accounts (existing rows left untouched).");
    }

    // -----------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------

    private static string? ResolveConnectionString(string[] args)
    {
        for (var i = 0; i < args.Length; i++)
        {
            if (string.Equals(args[i], "--conn", StringComparison.OrdinalIgnoreCase) && i + 1 < args.Length)
            {
                return args[i + 1];
            }
        }
        return Environment.GetEnvironmentVariable("NICKERP_FINANCE_DB_CONNECTION");
    }

    private static string RedactPassword(string conn)
    {
        var parts = conn.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return string.Join(';', parts.Select(p =>
        {
            if (p.StartsWith("Password=", StringComparison.OrdinalIgnoreCase)) return "Password=*****";
            return p;
        }));
    }
}
