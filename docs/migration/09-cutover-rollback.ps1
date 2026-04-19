# Phase 7 — Rollback: bring source services back online
# Run from current box if cutover is aborted
# Reverses the actions of 08-cutover-stop-source.ps1

$ErrorActionPreference = 'Continue'

Write-Host "=== ROLLBACK: Restarting source services ===" -ForegroundColor Yellow
Write-Host "Time: $(Get-Date -Format 'HH:mm:ss')"
Write-Host ""

$services = @('NSCIM_API', 'NSCIM_WebApp', 'NSCIM_Mobile', 'NSCIM_ImageSplitter')

# Re-enable auto-recovery
Write-Host "[1/3] Re-enabling auto-recovery..."
foreach ($s in $services) {
    sc.exe failure $s reset= 86400 actions= "restart/5000/restart/5000/none/0" | Out-Null
    Write-Host "  $s — auto-recovery restored" -ForegroundColor Green
}

# Start API first (others depend on it)
Write-Host ""
Write-Host "[2/3] Starting API..."
Start-Service NSCIM_API
Start-Sleep -Seconds 10

# Start dependent services
Write-Host "[3/3] Starting dependent services..."
Start-Service NSCIM_WebApp, NSCIM_Mobile, NSCIM_ImageSplitter
Start-Sleep -Seconds 5

Write-Host ""
Get-Service $services | Format-Table Name, Status -AutoSize

# Verify version endpoint
Write-Host ""
Write-Host "Verifying WebApp responds..."
try {
    Start-Sleep -Seconds 5
    $curlOut = curl.exe -sk --max-time 10 "https://localhost:5300/api/server/version" 2>$null
    if (-not $curlOut) { throw "no response from version endpoint" }
    $v = $curlOut | ConvertFrom-Json
    Write-Host "OK — version $($v.version), instance $($v.instanceId)" -ForegroundColor Green
} catch {
    Write-Host "FAIL — WebApp not responding: $_" -ForegroundColor Red
}

Write-Host ""
Write-Host "=== Source restored ===" -ForegroundColor Green
Write-Host "Verify DNS/reverse proxy is pointing at this box, not target."
