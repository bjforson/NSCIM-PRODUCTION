# Phase 6.3 — Stop source services (cutover T-0)
# Run from current box at cutover time
# Disables auto-recovery first so services actually stay stopped

# Continues past errors intentionally: cutover stop must attempt to halt every service and report leftover PIDs even if individual sc.exe / Stop-Service calls fail.
$ErrorActionPreference = 'Continue'

Write-Host "=== CUTOVER: Stopping source services ===" -ForegroundColor Red
Write-Host "Time: $(Get-Date -Format 'HH:mm:ss')"
Write-Host ""

# NSCIM_Mobile retired 2026-04-22 — WebApp now serves mobile viewports.
$services = @('NSCIM_API', 'NSCIM_WebApp', 'NSCIM_ImageSplitter')

# Disable auto-recovery (otherwise services restart within 5s)
Write-Host "[1/2] Disabling auto-recovery..."
foreach ($s in $services) {
    sc.exe failure $s reset= 0 actions= "" | Out-Null
    Write-Host "  $s — auto-recovery disabled" -ForegroundColor Yellow
}

# Stop services
Write-Host ""
Write-Host "[2/2] Stopping services..."
Stop-Service $services -Force -ErrorAction SilentlyContinue

Start-Sleep -Seconds 5

Write-Host ""
Get-Service $services | Format-Table Name, Status -AutoSize

$running = Get-Service $services | Where-Object { $_.Status -ne 'Stopped' }
if ($running) {
    Write-Host "WARNING: Services still running — may need manual kill" -ForegroundColor Red
    $running | ForEach-Object {
        $pid = (Get-WmiObject Win32_Service -Filter "Name='$($_.Name)'").ProcessId
        if ($pid -gt 0) { Write-Host "  $($_.Name) PID $pid — Stop-Process -Id $pid -Force" -ForegroundColor Red }
    }
} else {
    Write-Host "All services stopped." -ForegroundColor Green
    Write-Host ""
    Write-Host "=== Source is now INACTIVE ===" -ForegroundColor Red
    Write-Host "Data on current box is frozen as of $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-Host ""
    Write-Host "Next steps (if proceeding with cutover):"
    Write-Host "  1. On current box: run 04-dump-databases.ps1 one more time (final state)"
    Write-Host "  2. On target: run 05-restore-databases.ps1 -DropExisting against the final dump"
    Write-Host "  3. On target: Start-Service NSCIM_API, then others"
    Write-Host "  4. Update DNS/reverse proxy to point at target"
    Write-Host ""
    Write-Host "Rollback: run 09-cutover-rollback.ps1 to bring source back online"
}
