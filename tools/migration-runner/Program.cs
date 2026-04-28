using Npgsql;

// Tiny one-off SQL runner used because psql is not installed on the deploy box.
// Reads NICKSCAN_DB_PASSWORD from the environment and runs the queue compound-index
// migration against nickscan_downloads + nickscan_icums.
//
// Usage: dotnet run --project tools/migration-runner
//
// All CREATE INDEX statements use CONCURRENTLY so they do not block writes to live
// queue tables. CONCURRENTLY cannot run inside a transaction block, so each statement
// is sent on its own.

// DDL (CREATE INDEX, ALTER TABLE) requires table ownership. nscim_app is the
// non-super app role and would 42501 here; postgres owns the tables. NICKHR_DB_PASSWORD
// holds the postgres superuser password (per the v1 reference doc — NickHR still
// connects as postgres until its own role migration lands).
var dbPassword = Environment.GetEnvironmentVariable("NICKHR_DB_PASSWORD");
if (string.IsNullOrEmpty(dbPassword))
{
    Console.Error.WriteLine("ERROR: NICKHR_DB_PASSWORD (postgres superuser password) is not set. DDL needs ownership.");
    return 2;
}
const string superUser = "postgres";

var batches = new (string Database, string Description, string[] Statements)[]
{
    ("nickscan_downloads", "queue + history compound indexes", new[]
    {
        "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_cmrredownloadqueue_tenant_status      ON cmrredownloadqueue (tenant_id, status)",
        "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_failedprocessingqueue_tenant_status   ON failedprocessingqueue (tenant_id, status)",
        "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_icumsdownloadqueue_tenant_status      ON icumsdownloadqueue (tenant_id, status)",
        // Postgres lowercases unquoted identifiers; EF's "CreatedAt" lands as `createdat`.
        "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_containerdownloadhistory_tenant_created ON containerdownloadhistory (tenant_id, createdat DESC)",
        "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_ingestionlogs_tenant_created          ON ingestionlogs (tenant_id, createdat DESC)",
    }),
    ("nickscan_icums", "ICUMS queue compound indexes", new[]
    {
        "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_icumbatchlogs_tenant_created          ON icumbatchlogs (tenant_id, createdat DESC)",
        // icumcontainerdata has no `manifest_id`; declarationnumber is the BOE-key analog
        // and the most-filtered field for ICUMS workflow lookups.
        "CREATE INDEX CONCURRENTLY IF NOT EXISTS ix_icumcontainerdata_tenant_declaration  ON icumcontainerdata (tenant_id, declarationnumber)",
    }),
};

// --list-indexes prints all our ix_*_tenant_* indexes across the two DBs and exits.
if (args.Length > 0 && args[0] == "--list-indexes")
{
    var listProbes = new (string Db, string[] Tables)[]
    {
        ("nickscan_downloads", new[] { "containerdownloadhistory", "ingestionlogs", "icumsdownloadqueue", "cmrredownloadqueue", "failedprocessingqueue" }),
        ("nickscan_icums",     new[] { "icumbatchlogs", "icumcontainerdata" }),
    };
    foreach (var (db, tables) in listProbes)
    {
        var probeCs = $"Host=localhost;Port=5432;Database={db};Username={superUser};Password={dbPassword};Timeout=10";
        await using var probeConn = new NpgsqlConnection(probeCs);
        await probeConn.OpenAsync();
        Console.WriteLine($"\n--- {db} ---");
        foreach (var t in tables)
        {
            await using var cmd = new NpgsqlCommand(
                "SELECT indexname, indexdef FROM pg_indexes WHERE schemaname='public' AND tablename=@t AND indexname LIKE 'ix_%tenant%' ORDER BY indexname", probeConn);
            cmd.Parameters.AddWithValue("@t", t);
            await using var rdr = await cmd.ExecuteReaderAsync();
            var any = false;
            while (await rdr.ReadAsync()) { any = true; Console.WriteLine($"  {rdr.GetString(0)}\n    {rdr.GetString(1)}"); }
            if (!any) Console.WriteLine($"  (no tenant indexes on {t})");
        }
    }
    return 0;
}

// Quick mode: --introspect prints columns of the migration's target tables and exits.
if (args.Length > 0 && args[0] == "--introspect")
{
    var probeTables = new (string Db, string[] Tables)[]
    {
        ("nickscan_downloads", new[] { "containerdownloadhistory", "ingestionlogs", "icumsdownloadqueue", "cmrredownloadqueue", "failedprocessingqueue" }),
        ("nickscan_icums",     new[] { "icumbatchlogs", "icumcontainerdata" }),
    };
    foreach (var (db, tables) in probeTables)
    {
        var probeCs = $"Host=localhost;Port=5432;Database={db};Username={superUser};Password={dbPassword};Timeout=10";
        await using var probeConn = new NpgsqlConnection(probeCs);
        await probeConn.OpenAsync();
        foreach (var t in tables)
        {
            Console.WriteLine($"\n--- {db}.{t} ---");
            await using var cmd = new NpgsqlCommand(
                "SELECT column_name FROM information_schema.columns WHERE table_schema='public' AND lower(table_name)=lower(@t) ORDER BY ordinal_position", probeConn);
            cmd.Parameters.AddWithValue("@t", t);
            await using var rdr = await cmd.ExecuteReaderAsync();
            while (await rdr.ReadAsync()) Console.WriteLine("  " + rdr.GetString(0));
        }
    }
    return 0;
}

int failed = 0;
foreach (var (database, description, statements) in batches)
{
    var cs = $"Host=localhost;Port=5432;Database={database};Username={superUser};Password={dbPassword};Timeout=10";
    Console.WriteLine($"\n=== {database} — {description} ===");
    await using var conn = new NpgsqlConnection(cs);
    try
    {
        await conn.OpenAsync();
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"  CONNECT FAILED: {ex.Message}");
        failed++;
        continue;
    }
    foreach (var sql in statements)
    {
        var indexName = ExtractIndexName(sql);
        try
        {
            await using var cmd = new NpgsqlCommand(sql, conn);
            cmd.CommandTimeout = 600; // CONCURRENTLY can take a while on large tables
            await cmd.ExecuteNonQueryAsync();
            Console.WriteLine($"  [OK]   {indexName}");
        }
        catch (PostgresException pgex) when (pgex.SqlState == "42P07") // duplicate_table — index already exists
        {
            Console.WriteLine($"  [skip] {indexName} (already exists)");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"  [FAIL] {indexName}: {ex.Message}");
            failed++;
        }
    }
}

Console.WriteLine();
if (failed > 0)
{
    Console.Error.WriteLine($"Done with {failed} failure(s).");
    return 1;
}
Console.WriteLine("Done. All indexes built or already present.");
return 0;

static string ExtractIndexName(string ddl)
{
    // crude but sufficient: pull "ix_*" out of the statement
    var idx = ddl.IndexOf("ix_", StringComparison.Ordinal);
    if (idx < 0) return "(unknown)";
    var end = idx;
    while (end < ddl.Length && (char.IsLetterOrDigit(ddl[end]) || ddl[end] == '_')) end++;
    return ddl.Substring(idx, end - idx);
}
