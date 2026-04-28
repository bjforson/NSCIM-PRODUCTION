# Show Output Locations
# Usage: .\scripts\Show-OutputLocations.ps1

# Continues past errors intentionally: info-display script lists many independent paths/files; missing items are part of the report, not failures.
$ErrorActionPreference = "Continue"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Output Locations Guide" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

$productionPath = "\\10.0.0.79\Shared\NSCIM_PRODUCTION"

Write-Host "Production Server: $productionPath" -ForegroundColor White
Write-Host ""

# Build Output
Write-Host "📦 Build Output:" -ForegroundColor Yellow
$apiBinPath = Join-Path $productionPath "src\NickScanCentralImagingPortal.API\bin\Debug\net8.0"
$frontendBinPath = Join-Path $productionPath "src\NickScanWebApp.New\bin\Debug\net8.0"

Write-Host "   API Build:" -ForegroundColor Gray
if (Test-Path $apiBinPath) {
    $apiDll = Get-ChildItem -Path $apiBinPath -Filter "*.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($apiDll) {
        Write-Host "      ✅ $($apiDll.FullName)" -ForegroundColor Green
        Write-Host "         Last Built: $($apiDll.LastWriteTime)" -ForegroundColor Gray
    } else {
        Write-Host "      ⚠️  Path exists but no DLLs found" -ForegroundColor Yellow
    }
} else {
    Write-Host "      ❌ Path not found: $apiBinPath" -ForegroundColor Red
}

Write-Host "   Frontend Build:" -ForegroundColor Gray
if (Test-Path $frontendBinPath) {
    $frontendDll = Get-ChildItem -Path $frontendBinPath -Filter "*.dll" -ErrorAction SilentlyContinue | Select-Object -First 1
    if ($frontendDll) {
        Write-Host "      ✅ $($frontendDll.FullName)" -ForegroundColor Green
        Write-Host "         Last Built: $($frontendDll.LastWriteTime)" -ForegroundColor Gray
    } else {
        Write-Host "      ⚠️  Path exists but no DLLs found" -ForegroundColor Yellow
    }
} else {
    Write-Host "      ❌ Path not found: $frontendBinPath" -ForegroundColor Red
}
Write-Host ""

# Logs
Write-Host "📝 Application Logs:" -ForegroundColor Yellow
$apiLogsPath = Join-Path $productionPath "src\NickScanCentralImagingPortal.API\logs"
if (Test-Path $apiLogsPath) {
    $logFiles = Get-ChildItem -Path $apiLogsPath -Filter "*.log" -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 5
    if ($logFiles) {
        Write-Host "   ✅ Logs directory: $apiLogsPath" -ForegroundColor Green
        Write-Host "   Recent log files:" -ForegroundColor Gray
        $logFiles | ForEach-Object {
            $size = [math]::Round($_.Length / 1KB, 2)
            Write-Host "      • $($_.Name) ($size KB) - $($_.LastWriteTime)" -ForegroundColor Gray
        }
    } else {
        Write-Host "   ⚠️  Logs directory exists but no log files found" -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠️  Logs directory not found: $apiLogsPath" -ForegroundColor Yellow
    Write-Host "      (Logs will be created when API runs)" -ForegroundColor Gray
}
Write-Host ""

# Published Output
Write-Host "🚀 Published Output:" -ForegroundColor Yellow
$publishPath = Join-Path $productionPath "publish"
if (Test-Path $publishPath) {
    Write-Host "   ✅ Published apps: $publishPath" -ForegroundColor Green
    $publishDirs = Get-ChildItem -Path $publishPath -Directory -ErrorAction SilentlyContinue
    $publishDirs | ForEach-Object {
        Write-Host "      • $($_.Name)" -ForegroundColor Gray
    }
} else {
    Write-Host "   ⚠️  No published output found" -ForegroundColor Yellow
    Write-Host "      (Run DeployToProduction.ps1 with publish to create)" -ForegroundColor Gray
}
Write-Host ""

# Scripts
Write-Host "📜 Scripts:" -ForegroundColor Yellow
$scriptsPath = Join-Path $productionPath "scripts"
if (Test-Path $scriptsPath) {
    $scriptCount = (Get-ChildItem -Path $scriptsPath -Filter "*.ps1" -ErrorAction SilentlyContinue).Count
    Write-Host "   ✅ Scripts directory: $scriptsPath" -ForegroundColor Green
    Write-Host "      Total scripts: $scriptCount" -ForegroundColor Gray
} else {
    Write-Host "   ❌ Scripts directory not found: $scriptsPath" -ForegroundColor Red
}
Write-Host ""

# Summary
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Quick Access Commands" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "View latest API log:" -ForegroundColor Yellow
Write-Host "   cd $apiLogsPath" -ForegroundColor Cyan
Write-Host "   Get-Content logs\nick-scan-api-$(Get-Date -Format 'yyyyMMdd').log -Tail 50" -ForegroundColor Cyan
Write-Host ""
Write-Host "View latest errors:" -ForegroundColor Yellow
Write-Host "   cd $apiLogsPath" -ForegroundColor Cyan
Write-Host "   Get-Content logs\errors-api-$(Get-Date -Format 'yyyyMMdd').log -Tail 50" -ForegroundColor Cyan
Write-Host ""
Write-Host "Check build output:" -ForegroundColor Yellow
Write-Host "   Get-ChildItem '$apiBinPath\*.dll' | Select-Object Name, LastWriteTime" -ForegroundColor Cyan
Write-Host ""

