# Phase 3.1 — Copy static files from current box to Y:\ (target)
# Run from current box

$ErrorActionPreference = 'Stop'
$startTime = Get-Date

Write-Host "=== NSCIM File Transfer to Y:\ ===" -ForegroundColor Cyan
Write-Host "Source: C:\Shared\NSCIM_PRODUCTION\"
Write-Host "Target: Y:\"
Write-Host ""

if (-not (Test-Path 'Y:\')) { throw "Y:\ not mounted" }

# Data folder (runtime ICUMS state, logs)
# Exclude archives (too big, older logs not needed)
Write-Host "[1/3] Copying Data\..." -ForegroundColor Yellow
robocopy "C:\Shared\NSCIM_PRODUCTION\Data" "Y:\Data" /E /R:2 /W:5 /MT:16 /NFL /NDL /XD "archives" | Out-Host

# Services (Python ImageSplitter — code only, venv will be recreated on target)
Write-Host "[2/3] Copying services\ (excl. venv)..." -ForegroundColor Yellow
robocopy "C:\Shared\NSCIM_PRODUCTION\services" "Y:\services" /E /R:2 /W:5 /MT:16 /NFL /NDL /XD "venv" "__pycache__" ".pytest_cache" | Out-Host

# NickHR (separate app, co-deployed)
Write-Host "[3/3] Copying NickHR\ (excl. bin/obj/publish)..." -ForegroundColor Yellow
robocopy "C:\Shared\NSCIM_PRODUCTION\NickHR" "Y:\NickHR" /E /R:2 /W:5 /MT:16 /NFL /NDL /XD "bin" "obj" ".vs" "publish" "deploy" | Out-Host

$elapsed = (Get-Date) - $startTime
Write-Host ""
Write-Host "=== Done in $([math]::Round($elapsed.TotalMinutes,1)) min ===" -ForegroundColor Green

# Verify
Write-Host ""
Write-Host "Target contents:"
Get-ChildItem Y:\ -Directory | Select-Object Name, @{N='SizeGB';E={
    [math]::Round((Get-ChildItem $_.FullName -Recurse -File -ErrorAction SilentlyContinue | Measure-Object Length -Sum).Sum / 1GB, 2)
}} | Format-Table -AutoSize

# Local vs target spot-check (counts)
Write-Host ""
Write-Host "Local vs Target file counts (Data\):"
$localCount = (Get-ChildItem 'C:\Shared\NSCIM_PRODUCTION\Data' -Recurse -File -ErrorAction SilentlyContinue | Where-Object { $_.FullName -notmatch '\\archives\\' }).Count
$targetCount = (Get-ChildItem 'Y:\Data' -Recurse -File -ErrorAction SilentlyContinue).Count
Write-Host "  Local (excl archives): $localCount"
Write-Host "  Target: $targetCount"
if ($localCount -ne $targetCount) {
    Write-Host "  MISMATCH — investigate" -ForegroundColor Yellow
} else {
    Write-Host "  OK" -ForegroundColor Green
}
