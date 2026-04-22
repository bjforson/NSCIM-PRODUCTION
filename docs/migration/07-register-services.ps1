# Phase 4.3 — Register NSCIM Windows services on target
# Run ON TARGET SERVER via RDP, as admin
# Creates the NSCIM services with matching names + auto-restart config.
#
# Note: NSCIM_Mobile retired on 2026-04-22. The Web frontend (NSCIM_WebApp) is now
# responsive and serves both desktop and mobile viewports. Do not re-add NSCIM_Mobile
# here without first restoring the NickScanWebApp.Mobile project.

$ErrorActionPreference = 'Stop'

Write-Host "=== NSCIM Service Registration (TARGET) ===" -ForegroundColor Cyan

$root = "C:\Shared\NSCIM_PRODUCTION"
$services = @(
    @{ Name = 'NSCIM_API';           Display = 'NSCIM Production API';            Exe = "$root\publish\API\NickScanCentralImagingPortal.API.exe";     Depends = $null }
    @{ Name = 'NSCIM_WebApp';        Display = 'NSCIM Production WebApp';         Exe = "$root\publish\WebApp\NickScanWebApp.New.exe";                Depends = 'NSCIM_API' }
    @{ Name = 'NSCIM_ImageSplitter'; Display = 'NSCIM Image Splitting Service';   Exe = "$root\services\image-splitter\run-service.bat";              Depends = 'NSCIM_API' }
)

# Defensive cleanup: if an old NSCIM_Mobile service still exists from a prior
# registration, remove it so it doesn't linger as a disabled/ghost entry.
$legacyMobile = Get-Service -Name 'NSCIM_Mobile' -ErrorAction SilentlyContinue
if ($legacyMobile) {
    Write-Host "[cleanup] Removing retired NSCIM_Mobile service..." -ForegroundColor Yellow
    Stop-Service 'NSCIM_Mobile' -Force -ErrorAction SilentlyContinue
    sc.exe delete 'NSCIM_Mobile' | Out-Null
    Start-Sleep -Seconds 2
}

# Verify binaries exist before registering
Write-Host "[1/3] Verifying binaries..."
foreach ($svc in $services) {
    if (-not (Test-Path $svc.Exe)) {
        throw "Binary missing: $($svc.Exe) — run publish step first"
    }
    Write-Host "  $($svc.Name): OK" -ForegroundColor Green
}

# Stop + delete existing services if present (idempotent)
Write-Host ""
Write-Host "[2/3] Cleaning existing services (if any)..."
foreach ($svc in $services) {
    $existing = Get-Service $svc.Name -ErrorAction SilentlyContinue
    if ($existing) {
        Write-Host "  Stopping $($svc.Name)..."
        Stop-Service $svc.Name -Force -ErrorAction SilentlyContinue
        Write-Host "  Removing $($svc.Name)..."
        sc.exe delete $svc.Name | Out-Null
        Start-Sleep -Seconds 2
    }
}

# Create services
Write-Host ""
Write-Host "[3/3] Registering services..."
foreach ($svc in $services) {
    Write-Host "  $($svc.Name)..." -NoNewline
    $params = @{
        Name           = $svc.Name
        BinaryPathName = $svc.Exe
        DisplayName    = $svc.Display
        StartupType    = 'Automatic'
    }
    if ($svc.Depends) { $params.DependsOn = $svc.Depends }
    New-Service @params | Out-Null

    # Auto-restart on crash: 5s, 5s, no third restart
    sc.exe failure $svc.Name reset= 86400 actions= "restart/5000/restart/5000/none/0" | Out-Null

    Write-Host " OK" -ForegroundColor Green
}

Write-Host ""
Write-Host "=== Registered services ===" -ForegroundColor Cyan
Get-Service NSCIM_* | Format-Table Name, Status, StartType, DisplayName -AutoSize

Write-Host ""
Write-Host "To start:" -ForegroundColor Yellow
Write-Host "  Start-Service NSCIM_API"
Write-Host "  Start-Sleep -Seconds 10"
Write-Host "  Start-Service NSCIM_WebApp, NSCIM_ImageSplitter"
Write-Host ""
Write-Host "To verify:" -ForegroundColor Yellow
Write-Host "  [Net.ServicePointManager]::ServerCertificateValidationCallback={`$true}; Invoke-RestMethod -Uri 'https://localhost:5300/api/server/version'"
