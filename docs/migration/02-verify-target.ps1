# Phase 0.2 — Target-side verification
# Run ON TARGET SERVER (10.0.1.254) via RDP
# Validates target is ready to receive NSCIM

$ErrorActionPreference = 'Continue'
$results = [ordered]@{}

Write-Host "=== NSCIM Migration Pre-Flight (Target Side) ===" -ForegroundColor Cyan
Write-Host "Run this ON TARGET SERVER" -ForegroundColor Yellow
Write-Host ""

# 1. Disk free on C:
Write-Host "[1/9] C: drive free space..." -NoNewline
try {
    $c = Get-PSDrive C
    $freeGB = [math]::Round($c.Free / 1GB, 1)
    $used = [math]::Round($c.Used / 1GB, 1)
    if ($freeGB -lt 100) { throw "Only $freeGB GB free — need >100 GB for DBs + publish + data" }
    $results['c_drive'] = "OK (Used: $used GB, Free: $freeGB GB)"
    Write-Host " OK ($freeGB GB free)" -ForegroundColor Green
} catch {
    $results['c_drive'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 2. .NET 10 installed?
Write-Host "[2/9] .NET 10 SDK + runtime..." -NoNewline
try {
    $sdks = dotnet --list-sdks 2>&1
    $runtimes = dotnet --list-runtimes 2>&1
    $hasSdk10 = ($sdks | Select-String "^10\.").Count -gt 0
    $hasRuntime10 = ($runtimes | Select-String "AspNetCore\.App 10\.").Count -gt 0
    if (-not $hasSdk10) { throw ".NET 10 SDK not installed" }
    if (-not $hasRuntime10) { throw "ASP.NET Core 10 runtime not installed" }
    $results['dotnet10'] = "OK"
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['dotnet10'] = "FAIL: $_ (install: winget install Microsoft.DotNet.SDK.10; Microsoft.DotNet.AspNetCore.10; Microsoft.DotNet.HostingBundle.10)"
    Write-Host " FAIL" -ForegroundColor Red
}

# 3. PostgreSQL 18 installed and running?
Write-Host "[3/9] PostgreSQL 18..." -NoNewline
try {
    $pgSvc = Get-Service postgresql-x64-18 -ErrorAction SilentlyContinue
    if (-not $pgSvc) {
        # Maybe 16 is installed instead
        $pg16 = Get-Service postgresql-x64-16 -ErrorAction SilentlyContinue
        if ($pg16) { throw "Only Postgres 16 found. Need 18 to match current prod." }
        throw "No PostgreSQL service found"
    }
    if ($pgSvc.Status -ne 'Running') { throw "postgresql-x64-18 not running" }
    $pgVersion = & "C:\Program Files\PostgreSQL\18\bin\psql.exe" --version 2>&1
    $results['postgres18'] = "OK ($pgVersion)"
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['postgres18'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 4. Python 3.12+ for ImageSplitter
Write-Host "[4/9] Python 3.12+..." -NoNewline
try {
    $py = python --version 2>&1
    if ($py -notmatch 'Python 3\.(1[2-9]|[2-9]\d)') { throw "Python too old: $py" }
    $results['python'] = "OK ($py)"
    Write-Host " OK ($py)" -ForegroundColor Green
} catch {
    $results['python'] = "FAIL: $_ (install: winget install Python.Python.3.12)"
    Write-Host " FAIL" -ForegroundColor Red
}

# 5. Target directory structure
Write-Host "[5/9] Target directories..." -NoNewline
try {
    $root = 'C:\Shared\NSCIM_PRODUCTION'
    $required = @("$root\publish\API", "$root\publish\WebApp", "$root\Data", "$root\services", "$root\Data\Logs", "$root\Data\ICUMS\Outbox", "$root\Data\ICUMS\Inbox")
    foreach ($p in $required) {
        if (-not (Test-Path $p)) { New-Item -ItemType Directory -Force -Path $p | Out-Null }
    }
    $results['directories'] = "OK (all created/verified)"
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['directories'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 6. Z:\ image share mounted?
Write-Host "[6/9] Z:\ image share..." -NoNewline
try {
    if (-not (Test-Path 'Z:\')) {
        throw "Z:\ not mounted. Run: New-SmbMapping -LocalPath Z: -RemotePath '\\172.16.1.1\image' -Persistent `$true"
    }
    $sample = Get-ChildItem 'Z:\' -Directory -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $sample) { throw "Z:\ mounted but empty or no read access" }
    $results['z_drive'] = "OK"
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['z_drive'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 7. Port collisions (5205, 5299, 5300, 5320)
Write-Host "[7/9] Port availability..." -NoNewline
try {
    $wantedPorts = @(5205, 5206, 5299, 5300, 5320)
    $listening = Get-NetTCPConnection -State Listen -ErrorAction SilentlyContinue
    $collisions = $listening | Where-Object { $_.LocalPort -in $wantedPorts }
    if ($collisions) {
        $msg = ($collisions | ForEach-Object { "$($_.LocalPort)=PID$($_.OwningProcess)" }) -join '; '
        throw "Ports in use: $msg"
    }
    $results['ports'] = "OK (all 5 ports free)"
    Write-Host " OK" -ForegroundColor Green
} catch {
    $results['ports'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 8. Firewall rules
Write-Host "[8/9] Firewall rules..." -NoNewline
try {
    $rules = Get-NetFirewallRule -DisplayName "NSCIM*" -ErrorAction SilentlyContinue
    if ($rules.Count -lt 5) {
        $results['firewall'] = "WARN (only $($rules.Count) NSCIM rules — run P2.7)"
        Write-Host " WARN ($($rules.Count) rules)" -ForegroundColor Yellow
    } else {
        $results['firewall'] = "OK ($($rules.Count) rules)"
        Write-Host " OK" -ForegroundColor Green
    }
} catch {
    $results['firewall'] = "FAIL: $_"
    Write-Host " FAIL: $_" -ForegroundColor Red
}

# 9. Windows Defender exclusions
Write-Host "[9/9] Defender exclusions..." -NoNewline
try {
    $prefs = Get-MpPreference -ErrorAction SilentlyContinue
    if ($prefs.ExclusionPath -match 'NSCIM_PRODUCTION') {
        $results['defender'] = "OK"
        Write-Host " OK" -ForegroundColor Green
    } else {
        $results['defender'] = "WARN (no NSCIM exclusion — run P2.6)"
        Write-Host " WARN" -ForegroundColor Yellow
    }
} catch {
    $results['defender'] = "SKIP (Defender not installed?)"
    Write-Host " SKIP" -ForegroundColor Yellow
}

# Summary
Write-Host ""
Write-Host "=== Summary ===" -ForegroundColor Cyan
$results.GetEnumerator() | ForEach-Object {
    $color = if ($_.Value -match '^FAIL') { 'Red' } elseif ($_.Value -match '^WARN') { 'Yellow' } else { 'Green' }
    Write-Host "  $($_.Key): " -NoNewline
    Write-Host $_.Value -ForegroundColor $color
}

$outFile = "C:\Shared\NSCIM_PRODUCTION\docs\migration\target-state-before.txt"
if (-not (Test-Path (Split-Path $outFile))) {
    New-Item -ItemType Directory -Force -Path (Split-Path $outFile) | Out-Null
}
"Captured $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" | Out-File $outFile
$results.GetEnumerator() | ForEach-Object { "$($_.Key): $($_.Value)" } | Out-File $outFile -Append

Write-Host ""
Write-Host "Results saved to: $outFile" -ForegroundColor Cyan

$hasFail = ($results.Values | Where-Object { $_ -match '^FAIL' }).Count -gt 0
if ($hasFail) {
    Write-Host ""
    Write-Host "BLOCKING FAILURES — fix before proceeding to Phase 1." -ForegroundColor Red
    exit 1
}
