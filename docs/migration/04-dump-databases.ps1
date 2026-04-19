# Phase 3.2 — Dump all NSCIM databases to Y:\db-dumps\
# Run from current box
# Produces custom-format (-F c) dumps — portable, compressed, parallelizable on restore

$ErrorActionPreference = 'Stop'
$startTime = Get-Date

Write-Host "=== NSCIM Database Dumps ===" -ForegroundColor Cyan

if (-not $env:NICKSCAN_DB_PASSWORD) {
    throw "Env var NICKSCAN_DB_PASSWORD not set. Set it before running this script."
}
$env:PGPASSWORD = $env:NICKSCAN_DB_PASSWORD

$pgDump = "C:\Program Files\PostgreSQL\18\bin\pg_dump.exe"
if (-not (Test-Path $pgDump)) { throw "pg_dump not found at $pgDump" }

$timestamp = Get-Date -Format 'yyyyMMdd-HHmm'
$dumpDir = "Y:\db-dumps\$timestamp"
New-Item -ItemType Directory -Force -Path $dumpDir | Out-Null

Write-Host "Dump directory: $dumpDir"
Write-Host ""

$dbs = @('nickscan_production','nickscan_downloads','nickscan_icums','nickscan_icums_staging','nickhr','nick_comms','nick_platform')

foreach ($db in $dbs) {
    $dumpFile = "$dumpDir\$db.dump"
    $logFile = "$dumpDir\$db.log"

    Write-Host "[$db] dumping..." -NoNewline
    $dbStart = Get-Date

    try {
        # -F c = custom (compressed, seekable), -b = blobs, -v = verbose, --no-owner/privileges for portability
        & $pgDump -h localhost -U postgres -d $db -F c -b -v --no-owner --no-privileges -f $dumpFile 2>&1 | Out-File $logFile

        if (-not (Test-Path $dumpFile)) { throw "dump file not created" }
        $sizeMB = [math]::Round((Get-Item $dumpFile).Length / 1MB, 1)
        $elapsed = [math]::Round(((Get-Date) - $dbStart).TotalSeconds, 1)
        Write-Host " OK ($sizeMB MB in ${elapsed}s)" -ForegroundColor Green
    } catch {
        Write-Host " FAIL: $_" -ForegroundColor Red
        throw
    }
}

# Summary manifest — used by restore script for integrity check
$manifest = @{
    timestamp = (Get-Date -Format 'o')
    sourceHost = $env:COMPUTERNAME
    pgVersion = (& $pgDump --version)
    dumps = $dbs | ForEach-Object {
        $f = "$dumpDir\$_.dump"
        @{
            db = $_
            file = "$_.dump"
            sizeBytes = (Get-Item $f).Length
            sha256 = (Get-FileHash $f -Algorithm SHA256).Hash
        }
    }
}
$manifest | ConvertTo-Json -Depth 5 | Out-File "$dumpDir\manifest.json"

$totalElapsed = ((Get-Date) - $startTime).TotalMinutes
$totalSize = [math]::Round((Get-ChildItem $dumpDir -Filter *.dump | Measure-Object Length -Sum).Sum / 1GB, 2)

Write-Host ""
Write-Host "=== Done ===" -ForegroundColor Green
Write-Host "Total: $totalSize GB in $([math]::Round($totalElapsed,1)) min"
Write-Host "Manifest: $dumpDir\manifest.json"
Write-Host ""
Write-Host "Next step: RDP to target, run 05-restore-databases.ps1 pointing at:"
Write-Host "  C:\Shared\NSCIM_PRODUCTION\db-dumps\$timestamp\" -ForegroundColor Cyan
Write-Host "(or whatever path Y:\db-dumps\$timestamp maps to on target)"
