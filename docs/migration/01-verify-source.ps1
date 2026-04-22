# Phase 0.1 — Source-side verification
# Run from current box (NSPORTAL)
# Validates we can reach target and inventory what needs to move

$ErrorActionPreference = 'Stop'
$results = [ordered]@{}

Write-Host "=== NSCIM Migration Pre-Flight (Source Side) ===" -ForegroundColor Cyan
Write-Host ""

# 1. Y:\ share reachable + writable
Write-Host "[1/7] Y:\ share..." -NoNewline
try {
    if (-not (Test-Path 'Y:\')) { throw "Y:\ not mounted" }
    $testFile = 'Y:\.migration-probe'
    "probe" | Out-File $testFile -Force
    Remove-Item $testFile -Force
    $used = (Get-PSDrive Y).Used / 1GB
    $free = (Get-PSDrive Y).Free / 1GB
    $results['Y_drive'] = "OK (Used: $([math]::Round($used,1)) GB, Free: $([math]::Round($free,1)) GB)"
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['Y_drive'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 2. Z:\ image share reachable
Write-Host "[2/7] Z:\ image share..." -NoNewline
try {
    if (-not (Test-Path 'Z:\')) { throw "Z:\ not mounted" }
    $sample = Get-ChildItem 'Z:\' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $sample) { throw "Z:\ empty or no access" }
    $results['Z_drive'] = "OK ($(Get-PSDrive Z | ForEach-Object { [math]::Round($_.Used/1GB,0) }) GB used)"
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['Z_drive'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 3. Postgres up and contains expected DBs
Write-Host "[3/7] PostgreSQL..." -NoNewline
try {
    if (-not $env:NICKSCAN_DB_PASSWORD) { throw "NICKSCAN_DB_PASSWORD not set" }
    $env:PGPASSWORD = $env:NICKSCAN_DB_PASSWORD
    $psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
    if (-not (Test-Path $psql)) { throw "psql.exe not found at $psql" }
    $dbs = & $psql -h localhost -U postgres -t -c "SELECT datname FROM pg_database WHERE datname LIKE 'nick%' ORDER BY datname" 2>&1
    $dbList = $dbs | Where-Object { $_ -match '^\s*\w' } | ForEach-Object { $_.Trim() }
    $required = @('nickscan_production','nickscan_downloads','nickscan_icums','nickhr')
    $missing = $required | Where-Object { $_ -notin $dbList }
    if ($missing) { throw "Missing DBs: $($missing -join ', ')" }
    $results['postgres'] = "OK ($($dbList.Count) DBs found)"
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['postgres'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 4. Source install footprint inventory
Write-Host "[4/7] Source install footprint..." -NoNewline
try {
    $root = 'C:\Shared\NSCIM_PRODUCTION'
    $dirs = @('src','Data','services','NickHR','publish','backups','.claude')
    $sizes = @{}
    foreach ($d in $dirs) {
        $p = Join-Path $root $d
        if (Test-Path $p) {
            $size = (Get-ChildItem $p -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum
            $sizes[$d] = [math]::Round($size / 1GB, 2)
        }
    }
    $results['footprint'] = "OK: $(($sizes.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value) GB" }) -join ', ')"
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['footprint'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 5. DB sizes (what we'll dump)
Write-Host "[5/7] DB sizes..." -NoNewline
try {
    $env:PGPASSWORD = $env:NICKSCAN_DB_PASSWORD
    $psql = "C:\Program Files\PostgreSQL\18\bin\psql.exe"
    $sizes = & $psql -h localhost -U postgres -t -c "SELECT datname || ':' || pg_size_pretty(pg_database_size(datname)) FROM pg_database WHERE datname LIKE 'nick%' ORDER BY pg_database_size(datname) DESC"
    $results['db_sizes'] = ($sizes | Where-Object { $_ -match '\S' } | ForEach-Object { $_.Trim() }) -join '; '
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['db_sizes'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 6. Services running
Write-Host "[6/7] NSCIM services..." -NoNewline
try {
    # NSCIM_Mobile retired 2026-04-22 — web frontend (NSCIM_WebApp) now serves mobile too.
    $services = Get-Service NSCIM_API, NSCIM_WebApp, NSCIM_ImageSplitter -ErrorAction Stop
    $notRunning = $services | Where-Object { $_.Status -ne 'Running' }
    if ($notRunning) { throw "Not running: $($notRunning.Name -join ', ')" }
    $results['services'] = "OK (all 3 running)"
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['services'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 7. Current version (use curl.exe — works around PS 5.1 TLS quirks with self-signed certs)
Write-Host "[7/7] Current version..." -NoNewline
try {
    $curlOut = curl.exe -sk --max-time 5 "https://localhost:5300/api/server/version" 2>$null
    if (-not $curlOut) { throw "no response" }
    $v = $curlOut | ConvertFrom-Json
    $results['version'] = "OK ($($v.version), uptime $([math]::Round($v.uptime,0))s)"
    Write-Host " OK ($($v.version))" -ForegroundColor Green
} catch {
    $results['version'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# Summary
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$results.GetEnumerator() | ForEach-Object {
    $color = if ($_.Value -match '^FAIL') { 'Red' } else { 'Green' }
    Write-Host "  $($_.Key): " -NoNewline
    Write-Host $_.Value -ForegroundColor $color
}

# Write to file
$outFile = Join-Path (Split-Path $MyInvocation.MyCommand.Path) "source-state-before.txt"
"Captured $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File $outFile
$results.GetEnumerator() | ForEach-Object { "$($_.Key): $($_.Value)" } | Out-File $outFile -Append
Write-Host ""
Write-Host "Results saved to: $outFile" -ForegroundColor Cyan
