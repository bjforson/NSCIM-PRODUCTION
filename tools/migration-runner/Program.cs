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

if (args.Length > 0 && args[0] == "--eagle-a25-probe")
{
    await ProbeEagleA25SchemaAsync(superUser, dbPassword);
    return 0;
}

if (args.Length > 0 && args[0] == "--eagle-a25-data-probe")
{
    await ProbeEagleA25DataAsync(superUser, dbPassword);
    return 0;
}

if (args.Length > 0 && args[0] == "--eagle-a25-repair-localpaths")
{
    await RepairEagleA25LocalPathsAsync(superUser, dbPassword);
    await ProbeEagleA25DataAsync(superUser, dbPassword);
    return 0;
}

if (args.Length > 0 && args[0] == "--eagle-a25-schema")
{
    await ApplyEagleA25SchemaAsync(superUser, dbPassword);
    await ProbeEagleA25SchemaAsync(superUser, dbPassword);
    return 0;
}

if (args.Length > 0 && args[0] == "--ops-error-probe")
{
    await ProbeOpsErrorsAsync(superUser, dbPassword);
    return 0;
}

if (args.Length > 0 && args[0] == "--ops-stale-cleanup")
{
    await CleanupStaleOpsInvestigationsAsync(superUser, dbPassword);
    return 0;
}

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

static async Task ApplyEagleA25SchemaAsync(string user, string password)
{
    var cs = $"Host=localhost;Port=5432;Database=nickscan_production;Username={user};Password={password};Timeout=10";
    var statements = new[]
    {
        """
        CREATE TABLE IF NOT EXISTS eaglea25scans (
            id uuid NOT NULL PRIMARY KEY,
            sourcescanid integer NOT NULL,
            sourcescanguid uuid NOT NULL,
            sourcescanentryid integer NOT NULL,
            sourcemanifestid integer NOT NULL,
            sourcemanifestguid uuid NOT NULL,
            accession bigint NOT NULL,
            scanaccession bigint NULL,
            cargosystemid integer NULL,
            locationid integer NULL,
            scandateutc timestamp with time zone NOT NULL,
            scandatelocal timestamp with time zone NULL,
            manifestcreatedateutc timestamp with time zone NULL,
            manifestcreatedatelocal timestamp with time zone NULL,
            cargoidentifier character varying(512) NULL,
            airwaybill character varying(512) NULL,
            flightnumber character varying(512) NULL,
            transittype character varying(512) NULL,
            weight character varying(512) NULL,
            company character varying(512) NULL,
            quantity character varying(512) NULL,
            quantitytype character varying(512) NULL,
            originfrom character varying(512) NULL,
            originto character varying(512) NULL,
            comments character varying(512) NULL,
            datapath character varying(256) NULL,
            dataurl character varying(256) NULL,
            xraydone boolean NOT NULL,
            readyinspect boolean NOT NULL,
            inspectdone boolean NOT NULL,
            inspectsuspicious boolean NOT NULL,
            searchfound boolean NOT NULL,
            searchdone boolean NOT NULL,
            archived boolean NOT NULL,
            syncstatus character varying(32) NOT NULL,
            syncedatutc timestamp with time zone NOT NULL,
            createdatutc timestamp with time zone NOT NULL,
            updatedatutc timestamp with time zone NOT NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS eaglea25synclogs (
            id uuid NOT NULL PRIMARY KEY,
            startedatutc timestamp with time zone NOT NULL,
            completedatutc timestamp with time zone NULL,
            status character varying(32) NOT NULL,
            lastsyncedaccession bigint NULL,
            scansread integer NOT NULL,
            scansinserted integer NOT NULL,
            scansupdated integer NOT NULL,
            assetsread integer NOT NULL,
            assetsinserted integer NOT NULL,
            assetsupdated integer NOT NULL,
            errormessage character varying(2000) NULL
        )
        """,
        """
        CREATE TABLE IF NOT EXISTS eaglea25scanassets (
            id uuid NOT NULL PRIMARY KEY,
            eaglea25scanid uuid NOT NULL,
            sourceextfileid integer NOT NULL,
            sourceextfileguid uuid NOT NULL,
            sourceextfiletypeid integer NOT NULL,
            filetype character varying(30) NOT NULL,
            isxray boolean NOT NULL,
            mimetype character varying(256) NULL,
            description character varying(80) NULL,
            sourcepath character varying(1000) NOT NULL,
            resolvedsourcepath character varying(1000) NULL,
            sourceurl character varying(1000) NULL,
            localpath character varying(1000) NULL,
            filesizebytes bigint NULL,
            sourcecreatedateutc timestamp with time zone NULL,
            syncedatutc timestamp with time zone NOT NULL,
            CONSTRAINT fk_eaglea25scanassets_eaglea25scans_eaglea25scanid
                FOREIGN KEY (eaglea25scanid) REFERENCES eaglea25scans(id) ON DELETE CASCADE
        )
        """,
        """
        DO $$
        DECLARE
            target record;
        BEGIN
            FOR target IN
                SELECT * FROM (VALUES
                    ('eaglea25scans', 'scandateutc'),
                    ('eaglea25scans', 'scandatelocal'),
                    ('eaglea25scans', 'manifestcreatedateutc'),
                    ('eaglea25scans', 'manifestcreatedatelocal'),
                    ('eaglea25scans', 'syncedatutc'),
                    ('eaglea25scans', 'createdatutc'),
                    ('eaglea25scans', 'updatedatutc'),
                    ('eaglea25synclogs', 'startedatutc'),
                    ('eaglea25synclogs', 'completedatutc'),
                    ('eaglea25scanassets', 'sourcecreatedateutc'),
                    ('eaglea25scanassets', 'syncedatutc')
                ) AS v(table_name, column_name)
            LOOP
                IF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND table_name = target.table_name
                      AND column_name = target.column_name
                      AND data_type = 'timestamp without time zone'
                ) THEN
                    EXECUTE format(
                        'ALTER TABLE %I ALTER COLUMN %I TYPE timestamp with time zone USING %I AT TIME ZONE ''UTC''',
                        target.table_name,
                        target.column_name,
                        target.column_name);
                END IF;
            END LOOP;
        END $$;
        """,
        """
        UPDATE eaglea25synclogs
        SET startedatutc = completedatutc
        WHERE completedatutc IS NOT NULL
          AND startedatutc > completedatutc + interval '1 minute'
        """,
        "CREATE UNIQUE INDEX IF NOT EXISTS ix_eaglea25scans_accession ON eaglea25scans (accession)",
        "CREATE INDEX IF NOT EXISTS ix_eaglea25scans_airwaybill ON eaglea25scans (airwaybill)",
        "CREATE INDEX IF NOT EXISTS ix_eaglea25scans_cargoidentifier ON eaglea25scans (cargoidentifier)",
        "CREATE INDEX IF NOT EXISTS ix_eaglea25scans_scandateutc ON eaglea25scans (scandateutc)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ix_eaglea25scans_sourcemanifestid ON eaglea25scans (sourcemanifestid)",
        "CREATE INDEX IF NOT EXISTS ix_eaglea25scans_sourcescanid ON eaglea25scans (sourcescanid)",
        "CREATE INDEX IF NOT EXISTS ix_eaglea25scanassets_eaglea25scanid_filetype ON eaglea25scanassets (eaglea25scanid, filetype)",
        "CREATE INDEX IF NOT EXISTS ix_eaglea25scanassets_filetype ON eaglea25scanassets (filetype)",
        "CREATE UNIQUE INDEX IF NOT EXISTS ix_eaglea25scanassets_sourceextfileid ON eaglea25scanassets (sourceextfileid)",
        "CREATE INDEX IF NOT EXISTS ix_eaglea25synclogs_startedatutc ON eaglea25synclogs (startedatutc)",
        "CREATE INDEX IF NOT EXISTS ix_eaglea25synclogs_status ON eaglea25synclogs (status)",
        """
        DO $$
        BEGIN
            IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
                INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
                VALUES ('20260514193000_AddEagleA25ScannerTables', '10.0.7')
                ON CONFLICT ("MigrationId") DO NOTHING;
            END IF;
        END $$;
        """
    };

    Console.WriteLine("\n=== nickscan_production - Eagle A25 scanner schema ===");
    await using var conn = new NpgsqlConnection(cs);
    await conn.OpenAsync();

    foreach (var sql in statements)
    {
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.CommandTimeout = 600;
        await cmd.ExecuteNonQueryAsync();
    }

    Console.WriteLine("  [OK] Eagle A25 schema created or already present");
}

static async Task ProbeEagleA25SchemaAsync(string user, string password)
{
    var cs = $"Host=localhost;Port=5432;Database=nickscan_production;Username={user};Password={password};Timeout=10";
    await using var conn = new NpgsqlConnection(cs);
    await conn.OpenAsync();

    const string sql = """
        SELECT
            to_regclass('public.eaglea25scans') IS NOT NULL AS scans_table,
            to_regclass('public.eaglea25scanassets') IS NOT NULL AS assets_table,
            to_regclass('public.eaglea25synclogs') IS NOT NULL AS sync_logs_table,
            CASE WHEN to_regclass('public.eaglea25scans') IS NULL THEN NULL ELSE (SELECT count(*) FROM eaglea25scans) END AS scans_count,
            CASE WHEN to_regclass('public.eaglea25scanassets') IS NULL THEN NULL ELSE (SELECT count(*) FROM eaglea25scanassets) END AS assets_count,
            CASE WHEN to_regclass('public.eaglea25synclogs') IS NULL THEN NULL ELSE (SELECT count(*) FROM eaglea25synclogs) END AS sync_logs_count
        """;

    await using var cmd = new NpgsqlCommand(sql, conn);
    await using var rdr = await cmd.ExecuteReaderAsync();
    if (await rdr.ReadAsync())
    {
        Console.WriteLine("\n--- Eagle A25 schema probe ---");
        Console.WriteLine($"  eaglea25scans:      {rdr.GetBoolean(0)} rows={FormatNullableCount(rdr, 3)}");
        Console.WriteLine($"  eaglea25scanassets: {rdr.GetBoolean(1)} rows={FormatNullableCount(rdr, 4)}");
        Console.WriteLine($"  eaglea25synclogs:   {rdr.GetBoolean(2)} rows={FormatNullableCount(rdr, 5)}");
    }

    await rdr.DisposeAsync();

    const string dateTypeSql = """
        SELECT table_name, column_name, data_type
        FROM information_schema.columns
        WHERE table_schema = 'public'
          AND table_name IN ('eaglea25scans', 'eaglea25scanassets', 'eaglea25synclogs')
          AND column_name IN (
              'scandateutc', 'scandatelocal', 'manifestcreatedateutc', 'manifestcreatedatelocal',
              'syncedatutc', 'createdatutc', 'updatedatutc', 'startedatutc', 'completedatutc',
              'sourcecreatedateutc')
        ORDER BY table_name, column_name
        """;

    Console.WriteLine("\n--- Eagle A25 timestamp column types ---");
    await using (var typeCmd = new NpgsqlCommand(dateTypeSql, conn))
    await using (var typeRdr = await typeCmd.ExecuteReaderAsync())
    {
        while (await typeRdr.ReadAsync())
        {
            Console.WriteLine($"  {typeRdr.GetString(0)}.{typeRdr.GetString(1)} = {typeRdr.GetString(2)}");
        }
    }

    const string syncSql = """
        SELECT startedatutc, completedatutc, status, scansread, scansinserted, scansupdated, assetsread, assetsinserted, assetsupdated, COALESCE(errormessage, '')
        FROM eaglea25synclogs
        ORDER BY startedatutc DESC
        LIMIT 5
        """;

    Console.WriteLine("\n--- Eagle A25 latest sync logs ---");
    await using (var syncCmd = new NpgsqlCommand(syncSql, conn))
    await using (var syncRdr = await syncCmd.ExecuteReaderAsync())
    {
        var rowCount = 0;
        while (await syncRdr.ReadAsync())
        {
            rowCount++;
            Console.WriteLine(
                $"  #{rowCount} started={syncRdr.GetDateTime(0):u} completed={FormatNullableTimestamp(syncRdr, 1)} status={syncRdr.GetString(2)} scans={syncRdr.GetInt32(3)}/{syncRdr.GetInt32(4)}/{syncRdr.GetInt32(5)} assets={syncRdr.GetInt32(6)}/{syncRdr.GetInt32(7)}/{syncRdr.GetInt32(8)} error={OneLine(syncRdr.GetString(9))}");
        }

        if (rowCount == 0) Console.WriteLine("  (none)");
    }
}

static string FormatNullableCount(NpgsqlDataReader rdr, int ordinal)
    => rdr.IsDBNull(ordinal) ? "n/a" : rdr.GetInt64(ordinal).ToString();

static async Task ProbeEagleA25DataAsync(string user, string password)
{
    var cs = $"Host=localhost;Port=5432;Database=nickscan_production;Username={user};Password={password};Timeout=10";
    await using var conn = new NpgsqlConnection(cs);
    await conn.OpenAsync();

    Console.WriteLine("\n--- Eagle A25 May-2026+ data guard ---");
    const string scanSql = """
        SELECT
            COUNT(*) AS total_scans,
            COUNT(*) FILTER (WHERE scandateutc < timestamp with time zone '2026-05-01T00:00:00Z') AS pre_may_scans,
            MIN(scandateutc) AS oldest_scan,
            MAX(scandateutc) AS newest_scan,
            MIN(accession) AS min_accession,
            MAX(accession) AS max_accession
        FROM eaglea25scans
        """;
    await using (var cmd = new NpgsqlCommand(scanSql, conn))
    await using (var rdr = await cmd.ExecuteReaderAsync())
    {
        if (await rdr.ReadAsync())
        {
            Console.WriteLine($"  total scans:     {rdr.GetInt64(0)}");
            Console.WriteLine($"  pre-May scans:   {rdr.GetInt64(1)}");
            Console.WriteLine($"  oldest scan UTC: {FormatNullableTimestamp(rdr, 2)}");
            Console.WriteLine($"  newest scan UTC: {FormatNullableTimestamp(rdr, 3)}");
            Console.WriteLine($"  accession range: {FormatNullableLong(rdr, 4)} - {FormatNullableLong(rdr, 5)}");
        }
    }

    Console.WriteLine("\n--- Eagle A25 local asset coverage ---");
    const string assetSql = """
        SELECT
            COUNT(*) AS total_assets,
            COUNT(*) FILTER (WHERE localpath IS NOT NULL AND localpath <> '') AS copied_assets,
            COUNT(*) FILTER (WHERE filetype = 'XRAY') AS xray_assets,
            COUNT(*) FILTER (WHERE filetype = 'XRAY' AND localpath IS NOT NULL AND localpath <> '') AS copied_xray_assets,
            COUNT(*) FILTER (WHERE filetype = 'XRAYJPEG') AS xray_jpeg_assets,
            COUNT(*) FILTER (WHERE filetype = 'XRAYJPEG' AND localpath IS NOT NULL AND localpath <> '') AS copied_xray_jpeg_assets
        FROM eaglea25scanassets
        """;
    await using (var cmd = new NpgsqlCommand(assetSql, conn))
    await using (var rdr = await cmd.ExecuteReaderAsync())
    {
        if (await rdr.ReadAsync())
        {
            Console.WriteLine($"  total assets:       {rdr.GetInt64(0)}");
            Console.WriteLine($"  copied assets:      {rdr.GetInt64(1)}");
            Console.WriteLine($"  XRAY assets:        {rdr.GetInt64(2)}");
            Console.WriteLine($"  copied XRAY:        {rdr.GetInt64(3)}");
            Console.WriteLine($"  XRAYJPEG assets:    {rdr.GetInt64(4)}");
            Console.WriteLine($"  copied XRAYJPEG:    {rdr.GetInt64(5)}");
        }
    }

    Console.WriteLine("\n--- Eagle A25 latest records ---");
    const string latestSql = """
        SELECT accession, scandateutc, COALESCE(cargoidentifier, ''), syncedatutc
        FROM eaglea25scans
        ORDER BY scandateutc DESC, accession DESC
        LIMIT 5
        """;
    await using (var cmd = new NpgsqlCommand(latestSql, conn))
    await using (var rdr = await cmd.ExecuteReaderAsync())
    {
        var row = 0;
        while (await rdr.ReadAsync())
        {
            row++;
            Console.WriteLine($"  #{row} accession={rdr.GetInt64(0)} scan={rdr.GetDateTime(1):u} cargo={OneLine(rdr.GetString(2))} synced={rdr.GetDateTime(3):u}");
        }
    }

    Console.WriteLine("\n--- Eagle A25 missing copied XRAY assets ---");
    const string missingXraySql = """
        SELECT s.accession, s.scandateutc, a.sourceextfileid, a.filetype, COALESCE(a.resolvedsourcepath, ''), COALESCE(a.localpath, '')
        FROM eaglea25scanassets a
        INNER JOIN eaglea25scans s ON s.id = a.eaglea25scanid
        WHERE a.filetype = 'XRAY'
          AND (a.localpath IS NULL OR a.localpath = '')
        ORDER BY s.accession
        LIMIT 20
        """;
    await using (var cmd = new NpgsqlCommand(missingXraySql, conn))
    await using (var rdr = await cmd.ExecuteReaderAsync())
    {
        var row = 0;
        while (await rdr.ReadAsync())
        {
            row++;
            Console.WriteLine(
                $"  #{row} accession={rdr.GetInt64(0)} scan={rdr.GetDateTime(1):u} extFile={rdr.GetInt32(2)} type={rdr.GetString(3)} source={OneLine(rdr.GetString(4))} local={OneLine(rdr.GetString(5))}");
        }

        if (row == 0) Console.WriteLine("  (none)");
    }
}

static async Task RepairEagleA25LocalPathsAsync(string user, string password)
{
    var cs = $"Host=localhost;Port=5432;Database=nickscan_production;Username={user};Password={password};Timeout=10";
    await using var conn = new NpgsqlConnection(cs);
    await conn.OpenAsync();

    const string selectSql = """
        SELECT a.id, s.accession, a.sourcepath, a.resolvedsourcepath
        FROM eaglea25scanassets a
        INNER JOIN eaglea25scans s ON s.id = a.eaglea25scanid
        WHERE (a.localpath IS NULL OR a.localpath = '')
          AND COALESCE(a.sourcepath, a.resolvedsourcepath, '') <> ''
        ORDER BY s.accession, a.sourceextfileid
        """;

    var candidates = new List<(Guid Id, long Accession, string SourcePath, string ResolvedSourcePath)>();
    await using (var cmd = new NpgsqlCommand(selectSql, conn))
    await using (var rdr = await cmd.ExecuteReaderAsync())
    {
        while (await rdr.ReadAsync())
        {
            candidates.Add((
                rdr.GetGuid(0),
                rdr.GetInt64(1),
                rdr.IsDBNull(2) ? string.Empty : rdr.GetString(2),
                rdr.IsDBNull(3) ? string.Empty : rdr.GetString(3)));
        }
    }

    Console.WriteLine($"\n--- Eagle A25 localpath repair ---");
    Console.WriteLine($"  candidates: {candidates.Count}");

    var repaired = 0;
    foreach (var row in candidates)
    {
        var source = string.IsNullOrWhiteSpace(row.ResolvedSourcePath) ? row.SourcePath : row.ResolvedSourcePath;
        var fileName = Path.GetFileName(source);
        if (string.IsNullOrWhiteSpace(fileName)) continue;

        var accessionText = row.Accession.ToString();
        var year = accessionText.Length >= 4 ? accessionText[..4] : "unknown-year";
        var month = accessionText.Length >= 6 ? accessionText.Substring(4, 2) : "unknown-month";
        var day = accessionText.Length >= 8 ? accessionText.Substring(6, 2) : "unknown-day";
        var localPath = Path.Combine(
            @"C:\Shared\NSCIM_PRODUCTION\Data\EagleA25",
            year,
            month,
            day,
            accessionText,
            SanitizeFileName(fileName));

        if (!File.Exists(localPath))
        {
            continue;
        }

        var length = new FileInfo(localPath).Length;
        await using var update = new NpgsqlCommand(
            """
            UPDATE eaglea25scanassets
            SET localpath = @localpath,
                filesizebytes = @filesizebytes,
                syncedatutc = now()
            WHERE id = @id
            """,
            conn);
        update.Parameters.AddWithValue("@id", row.Id);
        update.Parameters.AddWithValue("@localpath", localPath);
        update.Parameters.AddWithValue("@filesizebytes", length);
        repaired += await update.ExecuteNonQueryAsync();
    }

    Console.WriteLine($"  repaired:   {repaired}");
}

static string SanitizeFileName(string fileName)
{
    foreach (var invalidChar in Path.GetInvalidFileNameChars())
    {
        fileName = fileName.Replace(invalidChar, '_');
    }

    return fileName;
}

static string FormatNullableLong(NpgsqlDataReader rdr, int ordinal)
    => rdr.IsDBNull(ordinal) ? "n/a" : rdr.GetInt64(ordinal).ToString();

static async Task ProbeOpsErrorsAsync(string user, string password)
{
    var cs = $"Host=localhost;Port=5432;Database=nickscan_production;Username={user};Password={password};Timeout=10";
    await using var conn = new NpgsqlConnection(cs);
    await conn.OpenAsync();

    const string countSql = """
        SELECT
            COUNT(*) FILTER (WHERE "timestamp" >= now() - interval '5 minutes') AS last5,
            COUNT(*) FILTER (WHERE "timestamp" >= now() - interval '15 minutes') AS last15,
            COUNT(*) FILTER (WHERE "timestamp" >= now() - interval '60 minutes') AS last60,
            MAX("timestamp") AS newest
        FROM applicationlogs
        WHERE level = 'Error'
          AND (
              message ILIKE '%Unhandled exception occurred%'
              OR exception ILIKE '%eaglea25synclogs%'
              OR exception ILIKE '%operation was canceled%'
              OR properties::text ILIKE '%385eee1c-9ced-4541-b1f9-9cfd0831cfda%'
              OR properties::text ILIKE '%1b478042-6fe1-43e8-9364-aa9dfc76e064%'
          )
          AND (
              message ILIKE '%EagleA25%'
              OR message ILIKE '%scan-assets/4812%'
              OR exception ILIKE '%eaglea25synclogs%'
              OR exception ILIKE '%operation was canceled%'
              OR properties::text ILIKE '%385eee1c-9ced-4541-b1f9-9cfd0831cfda%'
              OR properties::text ILIKE '%1b478042-6fe1-43e8-9364-aa9dfc76e064%'
          )
        """;

    Console.WriteLine("\n--- recent applicationlogs error counts ---");
    await using (var countCmd = new NpgsqlCommand(countSql, conn))
    await using (var rdr = await countCmd.ExecuteReaderAsync())
    {
        if (await rdr.ReadAsync())
        {
            Console.WriteLine($"  last 5 minutes:  {rdr.GetInt64(0)}");
            Console.WriteLine($"  last 15 minutes: {rdr.GetInt64(1)}");
            Console.WriteLine($"  last 60 minutes: {rdr.GetInt64(2)}");
            Console.WriteLine($"  newest:          {FormatNullableTimestamp(rdr, 3)}");
        }
    }

    const string detailSql = """
        SELECT
            id,
            "timestamp",
            level,
            COALESCE(logger, '') AS logger,
            LEFT(COALESCE(message, ''), 500) AS message,
            LEFT(COALESCE(exception, ''), 500) AS exception,
            LEFT(COALESCE(properties::text, ''), 500) AS properties
        FROM applicationlogs
        WHERE "timestamp" >= now() - interval '60 minutes'
          AND level IN ('Error', 'Warning')
          AND (
              message ILIKE '%EagleA25%'
              OR message ILIKE '%scan-assets/4812%'
              OR exception ILIKE '%eaglea25synclogs%'
              OR exception ILIKE '%operation was canceled%'
              OR properties::text ILIKE '%385eee1c-9ced-4541-b1f9-9cfd0831cfda%'
              OR properties::text ILIKE '%1b478042-6fe1-43e8-9364-aa9dfc76e064%'
          )
        ORDER BY "timestamp" DESC
        LIMIT 30
        """;

    Console.WriteLine("\n--- recent matching applicationlogs rows ---");
    var rowCount = 0;
    await using (var detailCmd = new NpgsqlCommand(detailSql, conn))
    await using (var rdr = await detailCmd.ExecuteReaderAsync())
    {
        while (await rdr.ReadAsync())
        {
            rowCount++;
            Console.WriteLine($"  #{rowCount} id={rdr.GetInt64(0)} at={rdr.GetDateTime(1):u} level={rdr.GetString(2)} logger={rdr.GetString(3)}");
            Console.WriteLine($"     message: {OneLine(rdr.GetString(4))}");
            Console.WriteLine($"     exception: {OneLine(rdr.GetString(5))}");
            Console.WriteLine($"     properties: {OneLine(rdr.GetString(6))}");
        }
    }
    if (rowCount == 0) Console.WriteLine("  (none)");

    const string investigationSql = """
        SELECT
            id,
            lastseen,
            status,
            occurrencecount,
            COALESCE(operation, '') AS operation,
            LEFT(COALESCE(sampleerrormessage, ''), 500) AS sampleerrormessage,
            LEFT(COALESCE(samplestacktrace, ''), 500) AS samplestacktrace,
            LEFT(COALESCE(relatedlogids, ''), 300) AS relatedlogids
        FROM errorinvestigations
        WHERE lastseen >= now() - interval '60 minutes'
          AND (
              operation ILIKE '%EagleA25%'
              OR operation ILIKE '%scan-assets%'
              OR sampleerrormessage ILIKE '%EagleA25%'
              OR sampleerrormessage ILIKE '%scan-assets/4812%'
              OR sampleerrormessage ILIKE '%385eee1c-9ced-4541-b1f9-9cfd0831cfda%'
              OR sampleerrormessage ILIKE '%1b478042-6fe1-43e8-9364-aa9dfc76e064%'
              OR samplestacktrace ILIKE '%eaglea25synclogs%'
              OR samplestacktrace ILIKE '%operation was canceled%'
          )
        ORDER BY lastseen DESC
        LIMIT 30
        """;

    Console.WriteLine("\n--- recent matching errorinvestigations rows ---");
    rowCount = 0;
    await using (var invCmd = new NpgsqlCommand(investigationSql, conn))
    await using (var rdr = await invCmd.ExecuteReaderAsync())
    {
        while (await rdr.ReadAsync())
        {
            rowCount++;
            Console.WriteLine($"  #{rowCount} id={rdr.GetInt64(0)} lastseen={rdr.GetDateTime(1):u} status={rdr.GetString(2)} occurrences={rdr.GetInt32(3)} operation={rdr.GetString(4)}");
            Console.WriteLine($"     sample: {OneLine(rdr.GetString(5))}");
            Console.WriteLine($"     stack: {OneLine(rdr.GetString(6))}");
            Console.WriteLine($"     related: {OneLine(rdr.GetString(7))}");
        }
    }
    if (rowCount == 0) Console.WriteLine("  (none)");
}

static string FormatNullableTimestamp(NpgsqlDataReader rdr, int ordinal)
    => rdr.IsDBNull(ordinal) ? "n/a" : rdr.GetDateTime(ordinal).ToString("u");

static string OneLine(string value)
    => value.Replace("\r", " ").Replace("\n", " ").Trim();

static async Task CleanupStaleOpsInvestigationsAsync(string user, string password)
{
    var cs = $"Host=localhost;Port=5432;Database=nickscan_production;Username={user};Password={password};Timeout=10";
    await using var conn = new NpgsqlConnection(cs);
    await conn.OpenAsync();

    Console.WriteLine("\n--- stale ops investigation cleanup: before ---");
    await PrintStaleOpsInvestigationSummaryAsync(conn);

    const string cleanupSql = """
        WITH target AS (
            SELECT
                id,
                status AS oldstatus,
                CASE
                    WHEN (
                        (operation ILIKE '%EagleA25%' OR sampleerrormessage ILIKE '%/api/EagleA25/sync-status%')
                        AND (
                            samplestacktrace ILIKE '%eaglea25synclogs%'
                            OR sampleerrormessage ILIKE '%eaglea25synclogs%'
                            OR samplestacktrace ILIKE '%Cannot write DateTime with Kind=Unspecified%'
                        )
                    ) THEN 'Fixed'
                    WHEN (
                        sampleerrormessage ILIKE '%/api/scan-assets/4812/image%'
                        AND samplestacktrace ILIKE '%OperationCanceledException%'
                    ) THEN 'Fixed'
                    ELSE 'Ignored'
                END AS newstatus,
                CASE
                    WHEN (
                        (operation ILIKE '%EagleA25%' OR sampleerrormessage ILIKE '%/api/EagleA25/sync-status%')
                        AND (
                            samplestacktrace ILIKE '%eaglea25synclogs%'
                            OR sampleerrormessage ILIKE '%eaglea25synclogs%'
                            OR samplestacktrace ILIKE '%Cannot write DateTime with Kind=Unspecified%'
                        )
                    ) THEN 'Codex ops cleanup 2026-05-14: marked fixed after Eagle A25 schema creation, timestamp alignment, successful live sync cycles, and no fresh matching errors.'
                    WHEN (
                        sampleerrormessage ILIKE '%/api/scan-assets/4812/image%'
                        AND samplestacktrace ILIKE '%OperationCanceledException%'
                    ) THEN 'Codex ops cleanup 2026-05-14: marked fixed after request-cancel handling was deployed and live probes showed no fresh scan-assets global-exception rows.'
                    ELSE 'Codex ops cleanup 2026-05-14: ignored stale OperationCanceledException rows produced by controlled service restarts/shutdown cancellation; no fresh matching rows observed.'
                END AS note
            FROM errorinvestigations
            WHERE status NOT IN ('Fixed', 'Ignored')
              AND lastseen <= now() - interval '15 minutes'
              AND (
                  (
                      (operation ILIKE '%EagleA25%' OR sampleerrormessage ILIKE '%/api/EagleA25/sync-status%')
                      AND (
                          samplestacktrace ILIKE '%eaglea25synclogs%'
                          OR sampleerrormessage ILIKE '%eaglea25synclogs%'
                          OR samplestacktrace ILIKE '%Cannot write DateTime with Kind=Unspecified%'
                      )
                  )
                  OR (
                      sampleerrormessage ILIKE '%/api/scan-assets/4812/image%'
                      AND samplestacktrace ILIKE '%OperationCanceledException%'
                  )
                  OR (
                      samplestacktrace ILIKE '%OperationCanceledException%'
                      AND (
                          operation ILIKE '%ContainerCompleteness%'
                          OR operation ILIKE '%ImageAnalysis%'
                          OR sampleerrormessage ILIKE '%[POST-ICUMS-VALIDATION]%'
                          OR sampleerrormessage ILIKE '%[BOE-SELECTIVITY]%'
                          OR sampleerrormessage ILIKE '%[DATA-MAPPING]%'
                          OR sampleerrormessage ILIKE '%[HOUSEKEEPING]%'
                          OR sampleerrormessage ILIKE '%[WAVE-AUTOCLOSE]%'
                          OR sampleerrormessage ILIKE '%[WAVE-LATE]%'
                      )
                  )
              )
        ),
        updated AS (
            UPDATE errorinvestigations ei
            SET
                status = target.newstatus,
                fixedat = CASE WHEN target.newstatus = 'Fixed' THEN now() ELSE ei.fixedat END,
                isverified = CASE WHEN target.newstatus = 'Fixed' THEN true ELSE ei.isverified END,
                verifiedat = CASE WHEN target.newstatus = 'Fixed' THEN now() ELSE ei.verifiedat END,
                verifiedby = CASE WHEN target.newstatus = 'Fixed' THEN 'Codex' ELSE ei.verifiedby END,
                investigationsummary = CASE
                    WHEN COALESCE(ei.investigationsummary, '') = '' THEN target.note
                    ELSE ei.investigationsummary || E'\n\n' || target.note
                END,
                updatedat = now()
            FROM target
            WHERE ei.id = target.id
            RETURNING ei.id, target.oldstatus, ei.status AS newstatus, target.note
        ),
        audit AS (
            INSERT INTO fixauditlogs (
                errorinvestigationid,
                fixproposalid,
                actiontype,
                performedby,
                description,
                details,
                createdat
            )
            SELECT
                id,
                NULL,
                CASE WHEN newstatus = 'Fixed' THEN 'FixVerified' ELSE 'InvestigationIgnored' END,
                'Codex',
                note,
                json_build_object(
                    'oldStatus', oldstatus,
                    'newStatus', newstatus,
                    'cleanup', 'stale-pre-fix-ops-noise'
                )::text,
                now()
            FROM updated
            RETURNING errorinvestigationid
        )
        SELECT u.id, u.oldstatus, u.newstatus, u.note
        FROM updated u
        ORDER BY u.id;
        """;

    Console.WriteLine("\n--- stale ops investigation cleanup: updated rows ---");
    var updatedRows = 0;
    await using (var cmd = new NpgsqlCommand(cleanupSql, conn))
    {
        cmd.CommandTimeout = 120;
        await using var rdr = await cmd.ExecuteReaderAsync();
        while (await rdr.ReadAsync())
        {
            updatedRows++;
            Console.WriteLine($"  id={rdr.GetInt64(0)} {rdr.GetString(1)} -> {rdr.GetString(2)}");
            Console.WriteLine($"    {rdr.GetString(3)}");
        }
    }

    if (updatedRows == 0) Console.WriteLine("  (none)");

    Console.WriteLine("\n--- stale ops investigation cleanup: after ---");
    await PrintStaleOpsInvestigationSummaryAsync(conn);
}

static async Task PrintStaleOpsInvestigationSummaryAsync(NpgsqlConnection conn)
{
    const string summarySql = """
        SELECT status, COUNT(*) AS count
        FROM errorinvestigations
        WHERE lastseen >= now() - interval '2 hours'
          AND (
              (
                  (operation ILIKE '%EagleA25%' OR sampleerrormessage ILIKE '%/api/EagleA25/sync-status%')
                  AND (
                      samplestacktrace ILIKE '%eaglea25synclogs%'
                      OR sampleerrormessage ILIKE '%eaglea25synclogs%'
                      OR samplestacktrace ILIKE '%Cannot write DateTime with Kind=Unspecified%'
                  )
              )
              OR (
                  sampleerrormessage ILIKE '%/api/scan-assets/4812/image%'
                  AND samplestacktrace ILIKE '%OperationCanceledException%'
              )
              OR (
                  samplestacktrace ILIKE '%OperationCanceledException%'
                  AND (
                      operation ILIKE '%ContainerCompleteness%'
                      OR operation ILIKE '%ImageAnalysis%'
                      OR sampleerrormessage ILIKE '%[POST-ICUMS-VALIDATION]%'
                      OR sampleerrormessage ILIKE '%[BOE-SELECTIVITY]%'
                      OR sampleerrormessage ILIKE '%[DATA-MAPPING]%'
                      OR sampleerrormessage ILIKE '%[HOUSEKEEPING]%'
                      OR sampleerrormessage ILIKE '%[WAVE-AUTOCLOSE]%'
                      OR sampleerrormessage ILIKE '%[WAVE-LATE]%'
                  )
              )
          )
        GROUP BY status
        ORDER BY status;
        """;

    await using var cmd = new NpgsqlCommand(summarySql, conn);
    await using var rdr = await cmd.ExecuteReaderAsync();
    var any = false;
    while (await rdr.ReadAsync())
    {
        any = true;
        Console.WriteLine($"  {rdr.GetString(0)}: {rdr.GetInt64(1)}");
    }

    if (!any) Console.WriteLine("  (none)");
}
