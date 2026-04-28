# SQL Server Memory Configuration Script
# Configures max and min server memory based on system RAM
#
# Usage: .\scripts\Configure-SqlServerMemory.ps1 [-MaxMemoryGB 18] [-MinMemoryGB 4]

param(
    [string]$Server = "127.0.0.1,1433",
    [int]$MaxMemoryGB = 18,
    [int]$MinMemoryGB = 4
)

$ErrorActionPreference = "Stop"  # 2026-04-28: was "Continue" — silent failures masked breakage. Wrap genuinely tolerated steps in try/catch.

Write-Host "SQL Server Memory Configuration" -ForegroundColor Cyan
Write-Host "===============================" -ForegroundColor Cyan
Write-Host ""

# Check current system RAM
Write-Host "Checking system RAM..." -ForegroundColor Yellow
$systemRAM = Get-CimInstance Win32_ComputerSystem | Select-Object -ExpandProperty TotalPhysicalMemory
$systemRAMGB = [math]::Round($systemRAM / 1GB, 2)

Write-Host "  Total System RAM: $systemRAMGB GB" -ForegroundColor Cyan
Write-Host ""

# Check current SQL Server memory settings
Write-Host "Current SQL Server Memory Settings:" -ForegroundColor Yellow
$currentSettings = Invoke-Sqlcmd -ServerInstance $Server -Database "master" -Query @"
SELECT 
    name,
    CAST(value AS INT) AS ValueMB,
    CAST(value_in_use AS INT) AS ValueInUseMB
FROM sys.configurations 
WHERE name IN ('max server memory (MB)', 'min server memory (MB)')
ORDER BY name;
"@

$currentSettings | Format-Table -AutoSize
Write-Host ""

# Convert GB to MB
$maxMemoryMB = $MaxMemoryGB * 1024
$minMemoryMB = $MinMemoryGB * 1024

# Validate settings
if ($maxMemoryMB -gt ($systemRAMGB * 1024 * 0.9)) {
    Write-Host "  ⚠️ WARNING: Max memory ($MaxMemoryGB GB) is more than 90% of system RAM ($systemRAMGB GB)" -ForegroundColor Yellow
    Write-Host "     SQL Server needs memory for OS and other processes" -ForegroundColor Yellow
    Write-Host ""
}

if ($minMemoryMB -gt $maxMemoryMB) {
    Write-Host "  ❌ ERROR: Min memory ($MinMemoryGB GB) cannot be greater than max memory ($MaxMemoryGB GB)" -ForegroundColor Red
    exit 1
}

# Confirm before applying
Write-Host "Proposed Changes:" -ForegroundColor Yellow
Write-Host "  Max Server Memory: $MaxMemoryGB GB ($maxMemoryMB MB)" -ForegroundColor Cyan
Write-Host "  Min Server Memory: $MinMemoryGB GB ($minMemoryMB MB)" -ForegroundColor Cyan
Write-Host ""

$confirm = Read-Host "Apply these settings? (Y/N)"
if ($confirm -ne 'Y' -and $confirm -ne 'y') {
    Write-Host "Configuration cancelled." -ForegroundColor Yellow
    exit 0
}

Write-Host ""
Write-Host "Applying configuration..." -ForegroundColor Yellow

try {
    # Step 1: Enable advanced options (required to change memory settings)
    Write-Host "  Enabling advanced options..." -ForegroundColor Gray
    Invoke-Sqlcmd -ServerInstance $Server -Database "master" -Query "EXEC sp_configure 'show advanced options', 1; RECONFIGURE;" -ErrorAction Stop
    Write-Host "    ✅ Advanced options enabled" -ForegroundColor Green
    
    # Step 2: Set max server memory
    Write-Host "  Setting max server memory to $maxMemoryMB MB..." -ForegroundColor Gray
    Invoke-Sqlcmd -ServerInstance $Server -Database "master" -Query "EXEC sp_configure 'max server memory (MB)', $maxMemoryMB; RECONFIGURE;" -ErrorAction Stop
    Write-Host "    ✅ Max server memory configured" -ForegroundColor Green
    
    # Step 3: Set min server memory
    Write-Host "  Setting min server memory to $minMemoryMB MB..." -ForegroundColor Gray
    Invoke-Sqlcmd -ServerInstance $Server -Database "master" -Query "EXEC sp_configure 'min server memory (MB)', $minMemoryMB; RECONFIGURE;" -ErrorAction Stop
    Write-Host "    ✅ Min server memory configured" -ForegroundColor Green
    
    Write-Host ""
    Write-Host "✅ Configuration applied successfully!" -ForegroundColor Green
    Write-Host ""
    
    # Verify new settings
    Write-Host "New SQL Server Memory Settings:" -ForegroundColor Yellow
    $newSettings = Invoke-Sqlcmd -ServerInstance $Server -Database "master" -Query @"
SELECT 
    name,
    CAST(value AS INT) AS ValueMB,
    CAST(value_in_use AS INT) AS ValueInUseMB
FROM sys.configurations 
WHERE name IN ('max server memory (MB)', 'min server memory (MB)')
ORDER BY name;
"@
    
    $newSettings | Format-Table -AutoSize
    
    Write-Host ""
    Write-Host "Note: SQL Server will gradually adjust memory usage to the new limits." -ForegroundColor Gray
    Write-Host "      Monitor memory usage over the next few minutes." -ForegroundColor Gray
    
} catch {
    Write-Host ""
    Write-Host "❌ Error applying configuration: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

