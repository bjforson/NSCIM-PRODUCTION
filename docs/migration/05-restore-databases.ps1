# Phase 3.3 — Restore databases on target server
# Run ON TARGET SERVER via RDP
# Expects dump files already copied via Y:\ (which appears as C:\... on target)

param(
    [Parameter(Mandatory=$true)]
    [string]$DumpDir,  # e.g., "C:\Shared\NSCIM_PRODUCTION\db-dumps\20260415-1630"

    [string]$PgPassword = $env:NICKSCAN_DB_PASSWORD,

    [switch]$DropExisting  # if set, drops DBs before restore (USE WITH CARE)
)

$ErrorActionPreference = 'Stop'
$startTime = Get-Date

Write-Host "=== NSCIM Database Restore (TARGET) ===" -ForegroundColor Cyan
Write-Host "Dump dir: $DumpDir"
Write-Host "Drop existing: $DropExisting"
Write-Host ""

if (-not (Test-Path $DumpDir)) { throw "Dump dir not found: $DumpDir" }
if (-not $PgPassword) { throw "PgPassword not provided (pass as -PgPassword or set NICKSCAN_DB_PASSWORD)" }

$env:PGPASSWORD = $PgPassword
$psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
$pgRestore = "C:\Program Files\PostgreSQL\18\bin\pg_restore.exe"
foreach ($t in $psql, $pgRestore) {
    if (-not (Test-Path $t)) { throw "$t not found — install PostgreSQL 18" }
}

# Load manifest
$manifestPath = Join-Path $DumpDir 'manifest.json'
if (-not (Test-Path $manifestPath)) { throw "manifest.json missing from $DumpDir" }
$manifest = Get-Content $manifestPath | ConvertFrom-Json

Write-Host "Manifest captured: $($manifest.timestamp) from $($manifest.sourceHost)"
Write-Host "Dumps to restore: $($manifest.dumps.Count)"
Write-Host ""

# Verify each dump file integrity
Write-Host "Verifying SHA-256 checksums..."
foreach ($d in $manifest.dumps) {
    $path = Join-Path $DumpDir $d.file
    if (-not (Test-Path $path)) { throw "Dump file missing: $path" }
    $actualHash = (Get-FileHash $path -Algorithm SHA256).Hash
    if ($actualHash -ne $d.sha256) {
        throw "SHA mismatch on $($d.db): expected $($d.sha256), got $actualHash"
    }
    Write-Host "  [$($d.db)] OK" -ForegroundColor Green
}

Write-Host ""
Write-Host "Starting restore..."

foreach ($d in $manifest.dumps) {
    $db = $d.db
    $file = Join-Path $DumpDir $d.file

    Write-Host ""
    Write-Host "[$db]" -ForegroundColor Yellow
    $dbStart = Get-Date

    # Drop if requested + exists
    $exists = & $psql -h localhost -U postgres -t -c "SELECT 1 FROM pg_database WHERE datname = '$db'" 2>&1
    if ($exists -match '^\s*1') {
        if ($DropExisting) {
            Write-Host "  Dropping existing..."
            & $psql -h localhost -U postgres -c "DROP DATABASE $db WITH (FORCE)" 2>&1 | Out-Null
        } else {
            Write-Host "  SKIPPING — DB exists and -DropExisting not set" -ForegroundColor Yellow
            continue
        }
    }

    # Create fresh
    & $psql -h localhost -U postgres -c "CREATE DATABASE $db OWNER postgres ENCODING 'UTF8' TEMPLATE template0" 2>&1 | Out-Null

    # Restore (parallel jobs for speed on big DBs)
    Write-Host "  Restoring from $($d.file) ($([math]::Round($d.sizeBytes/1MB,1)) MB)..."
    $jobs = if ($d.sizeBytes -gt 500MB) { 4 } else { 1 }
    & $pgRestore -h localhost -U postgres -d $db --no-owner --no-privileges -j $jobs -v $file 2>&1 | Out-File "$DumpDir\restore-$db.log"

    # Verify
    $size = & $psql -h localhost -U postgres -t -c "SELECT pg_size_pretty(pg_database_size('$db'))" 2>&1
    $elapsed = [math]::Round(((Get-Date) - $dbStart).TotalSeconds, 1)
    Write-Host "  OK ($($size.Trim()) restored in ${elapsed}s)" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Row count sanity check ===" -ForegroundColor Cyan
$tableCheck = @'
SELECT 'AnalysisRecords' AS tbl, count(*) AS cnt FROM analysisrecords
UNION ALL SELECT 'BOEDocuments', count(*) FROM boedocuments
UNION ALL SELECT 'ImageAnalysisDecisions', count(*) FROM imageanalysisdecisions
UNION ALL SELECT 'AuditDecisions', count(*) FROM auditdecisions
UNION ALL SELECT 'ManifestItems', count(*) FROM manifestitems
UNION ALL SELECT 'AnalysisGroups', count(*) FROM analysisgroups
ORDER BY tbl
'@
$tableCheck | & $psql -h localhost -U postgres -d nickscan_production 2>&1 | Write-Host

$totalElapsed = [math]::Round(((Get-Date) - $startTime).TotalMinutes, 1)
Write-Host ""
Write-Host "=== Restore complete in $totalElapsed min ===" -ForegroundColor Green
Write-Host "Compare row counts above against current-box counts from 01-verify-source.ps1"
