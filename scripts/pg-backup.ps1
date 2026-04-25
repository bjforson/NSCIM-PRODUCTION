# Nightly Postgres backup for all NickERP databases.
#
# ROADMAP C.1.9. Run from Task Scheduler at 02:00 local. Writes
# compressed dumps to C:\Shared\Backups\pg\<YYYY-MM-DD>\<db>.dump.gz
# and rotates anything older than 14 days. Idempotent — re-running
# the same day overwrites that day's dumps.
#
# Restore example:
#   gunzip -c <db>.dump.gz | pg_restore --clean --if-exists \
#       -h localhost -U postgres -d <db>
#
# Each dump is in the custom format (-Fc) so we get parallel restore
# and per-table selection if we ever need to cherry-pick rows.

[CmdletBinding()]
param(
    [string]   $PgDumpExe       = 'C:\Program Files\PostgreSQL\18\bin\pg_dump.exe',
    [string]   $PgHost          = 'localhost',
    [int]      $PgPort          = 5432,
    [string]   $PgUser          = 'postgres',
    [string[]] $Databases       = @('nickhr', 'nickscan_production', 'nickscan_icums', 'nickscan_downloads', 'nick_comms'),
    [string]   $BackupRoot      = 'C:\Shared\Backups\pg',
    [int]      $RetentionDays   = 14,
    [string]   $LogPath         = 'C:\Shared\Backups\pg\backup.log'
)

$ErrorActionPreference = 'Stop'

# Capture every line both to console and to the rolling log so a
# Task Scheduler "no output" run is still diagnosable.
function Write-Log {
    param([string]$Message, [string]$Level = 'INFO')
    $line = "{0:s}Z [{1}] {2}" -f (Get-Date).ToUniversalTime(), $Level, $Message
    Write-Host $line
    Add-Content -Path $LogPath -Value $line -Encoding UTF8
}

if (-not (Test-Path $PgDumpExe)) {
    throw "pg_dump.exe not found at $PgDumpExe."
}

$pgPassword = $env:NICKSCAN_DB_PASSWORD
if ([string]::IsNullOrWhiteSpace($pgPassword)) {
    throw 'NICKSCAN_DB_PASSWORD env var must be set (machine scope) for unattended runs.'
}
$env:PGPASSWORD = $pgPassword

# Today's destination, e.g. C:\Shared\Backups\pg\2026-04-25
$stamp = Get-Date -Format 'yyyy-MM-dd'
$today = Join-Path $BackupRoot $stamp
New-Item -ItemType Directory -Force -Path $today | Out-Null
New-Item -ItemType Directory -Force -Path (Split-Path $LogPath) | Out-Null

Write-Log "Starting backup run -> $today"

$startedAt = Get-Date
$failures = @()

foreach ($db in $Databases) {
    $dumpRaw = Join-Path $today "$db.dump"
    $dumpGz  = "$dumpRaw.gz"
    $dbStart = Get-Date
    try {
        Write-Log "  ${db}: pg_dump -Fc ..."
        & $PgDumpExe -h $PgHost -p $PgPort -U $PgUser -Fc -f $dumpRaw $db
        if ($LASTEXITCODE -ne 0) {
            throw "pg_dump exited $LASTEXITCODE for $db"
        }

        # Cheap built-in compression. -Fc is already lightly compressed
        # so we typically save another 20-40 % with gzip default.
        $bytesIn  = New-Object IO.FileStream $dumpRaw, ([IO.FileMode]::Open),    ([IO.FileAccess]::Read)
        $bytesOut = New-Object IO.FileStream $dumpGz,  ([IO.FileMode]::Create), ([IO.FileAccess]::Write)
        $gzip     = New-Object IO.Compression.GZipStream $bytesOut, ([IO.Compression.CompressionLevel]::Optimal)
        try   { $bytesIn.CopyTo($gzip) }
        finally {
            $gzip.Dispose()
            $bytesOut.Dispose()
            $bytesIn.Dispose()
        }
        Remove-Item $dumpRaw -Force
        $sizeMb = [math]::Round((Get-Item $dumpGz).Length / 1MB, 1)
        $secs   = [math]::Round(((Get-Date) - $dbStart).TotalSeconds, 1)
        Write-Log "  ${db}: OK $sizeMb MB in ${secs}s"
    } catch {
        Write-Log "  ${db}: FAILED - $($_.Exception.Message)" 'ERROR'
        $failures += $db
    }
}

# Retention sweep - anything older than $RetentionDays days goes.
$cutoff = (Get-Date).AddDays(-1 * $RetentionDays)
$swept  = 0
Get-ChildItem $BackupRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d{4}-\d{2}-\d{2}$' -and $_.LastWriteTime -lt $cutoff } |
    ForEach-Object {
        $iso = $_.LastWriteTime.ToString('s')
        Write-Log "  retention: removing $($_.FullName) (last write $iso)"
        Remove-Item -Recurse -Force $_.FullName
        $swept++
    }

$elapsed = [math]::Round(((Get-Date) - $startedAt).TotalSeconds, 1)
if ($failures.Count -gt 0) {
    Write-Log "Run COMPLETED with failures: $($failures -join ', '). Total ${elapsed}s, swept $swept old day(s)." 'ERROR'
    exit 1
}
Write-Log "Run OK. Total ${elapsed}s, swept $swept old day(s)."
