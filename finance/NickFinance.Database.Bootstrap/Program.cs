using Microsoft.EntityFrameworkCore;
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
            await ApplySchemaTriggersAsync(conn);
            if (seedCoa) await SeedGhanaChartAsync(conn);

            Console.WriteLine();
            Console.WriteLine("Bootstrap complete. The Ledger + Petty Cash schemas are ready.");
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
        Console.WriteLine("[2/4] Applying Petty Cash migrations (petty_cash schema)...");
        var opts = new DbContextOptionsBuilder<PettyCashDbContext>().UseNpgsql(conn).Options;
        await using var db = new PettyCashDbContext(opts);
        await db.Database.MigrateAsync();
        Console.WriteLine("       Petty Cash: up to date.");
    }

    private static async Task ApplySchemaTriggersAsync(string conn)
    {
        Console.WriteLine("[3/4] Applying Postgres triggers (balance invariant + append-only)...");
        var opts = new DbContextOptionsBuilder<LedgerDbContext>().UseNpgsql(conn).Options;
        await using var db = new LedgerDbContext(opts);
        await SchemaBootstrap.ApplyConstraintsAsync(db);
        Console.WriteLine("       Triggers: applied.");
    }

    private static async Task SeedGhanaChartAsync(string conn)
    {
        Console.WriteLine("[4/4] Seeding Ghana standard chart of accounts...");
        // CoA lives in petty_cash schema only because that's where it's
        // currently consumed; if a future module needs the chart it can
        // own its own table or read from this one. For v1 we let the
        // operator persist accounts via raw INSERT — the kernel does NOT
        // enforce account membership, so wiring CoA is module-by-module.
        // The seed below is the canonical 70-row Ghana baseline.

        // Insertion uses raw INSERT … ON CONFLICT DO NOTHING so re-runs
        // are idempotent. The accounts table doesn't yet ship with EF
        // migrations (no consumer requires it persisted) — when AR / AP
        // arrive and need the chart, those modules will own the table
        // and emit migrations.

        // For now this prints a guidance message; a future Phase 6.5 task
        // will land the accounts table + migration once a consumer needs it.
        await Task.CompletedTask;
        var count = GhanaStandardChart.Default.Count;
        Console.WriteLine($"       Loaded {count} CoA rows in-memory.");
        Console.WriteLine("       NOTE: no database table for Accounts yet — modules read");
        Console.WriteLine("             GhanaStandardChart.Default directly. Persistent CoA");
        Console.WriteLine("             ships with the AR module (Phase 6.2) when validation");
        Console.WriteLine("             becomes a hard requirement.");
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
