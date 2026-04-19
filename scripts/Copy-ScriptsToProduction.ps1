# Copy Scripts to Production Server
# Usage: .\scripts\Copy-ScriptsToProduction.ps1

$ErrorActionPreference = "Stop"

$productionPath = "\\10.0.0.79\Shared\NSCIM_PRODUCTION\scripts"
$scriptsToCopy = @(
    "Check-Install-DotNetDependencies.ps1",
    "Install-VCRedistributables.ps1",
    "Build-Production.ps1",
    "Diagnose-BuildIssues.ps1"
)

Write-Host "Copying scripts to production server..." -ForegroundColor Cyan
Write-Host "Destination: $productionPath" -ForegroundColor White
Write-Host ""

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$projectRoot = Split-Path -Parent $scriptDir

foreach ($script in $scriptsToCopy) {
    $source = Join-Path $scriptDir $script
    $dest = Join-Path $productionPath $script
    
    if (Test-Path $source) {
        try {
            Copy-Item -Path $source -Destination $dest -Force
            Write-Host "✅ Copied: $script" -ForegroundColor Green
        } catch {
            Write-Host "❌ Failed to copy $script : $($_.Exception.Message)" -ForegroundColor Red
        }
    } else {
        Write-Host "⚠️  Not found: $script" -ForegroundColor Yellow
    }
}

Write-Host ""
Write-Host "✅ Copy operation completed!" -ForegroundColor Green

