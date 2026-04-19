# ================================================
# Setup File System Permissions
# NickScan Central Imaging Portal
# ================================================
# 
# This script sets up file system permissions for:
# - FS6000 file sync directories
# - Application log directories
# - ICUMS download directories
#
# Usage:
#   .\scripts\Setup-FileSystemPermissions.ps1 -ServiceAccount "DOMAIN\ServiceAccount"
#   .\scripts\Setup-FileSystemPermissions.ps1 -IISAppPool "NickScanPortal"
# ================================================

param(
    [Parameter(Mandatory=$false)]
    [string]$ServiceAccount,
    
    [Parameter(Mandatory=$false)]
    [string]$IISAppPool,
    
    [Parameter(Mandatory=$false)]
    [string]$BasePath = "C:\NickScan"
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Setting up File System Permissions" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Determine which account to use
$accountToUse = $null

if ($IISAppPool) {
    $accountToUse = "IIS APPPOOL\$IISAppPool"
    Write-Host "Using IIS App Pool: $accountToUse" -ForegroundColor Yellow
} elseif ($ServiceAccount) {
    $accountToUse = $ServiceAccount
    Write-Host "Using Service Account: $accountToUse" -ForegroundColor Yellow
} else {
    Write-Host "ERROR: Either -ServiceAccount or -IISAppPool must be specified" -ForegroundColor Red
    Write-Host ""
    Write-Host "Usage examples:" -ForegroundColor Yellow
    Write-Host "  .\Setup-FileSystemPermissions.ps1 -ServiceAccount 'DOMAIN\ServiceAccount'" -ForegroundColor Gray
    Write-Host "  .\Setup-FileSystemPermissions.ps1 -IISAppPool 'NickScanPortal'" -ForegroundColor Gray
    exit 1
}

Write-Host ""

# Function to grant permissions
function Grant-Permissions {
    param(
        [string]$Path,
        [string]$Account,
        [string]$Description
    )
    
    Write-Host "Processing: $Description" -ForegroundColor Cyan
    Write-Host "  Path: $Path" -ForegroundColor Gray
    
    # Create directory if it doesn't exist
    if (-not (Test-Path $Path)) {
        try {
            New-Item -ItemType Directory -Path $Path -Force | Out-Null
            Write-Host "  [OK] Created directory" -ForegroundColor Green
        } catch {
            Write-Host "  [FAIL] Failed to create directory: $_" -ForegroundColor Red
            return $false
        }
    }
    
    # Grant full control permissions
    try {
        $acl = Get-Acl $Path
        $permission = $Account, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow"
        $accessRule = New-Object System.Security.AccessControl.FileSystemAccessRule $permission
        $acl.SetAccessRule($accessRule)
        Set-Acl $Path $acl
        Write-Host "  [OK] Granted FullControl permissions" -ForegroundColor Green
        return $true
    } catch {
        Write-Host "  [FAIL] Failed to grant permissions: $_" -ForegroundColor Red
        Write-Host "  Note: This script must be run as Administrator" -ForegroundColor Yellow
        return $false
    }
}

# Define directories that need permissions
$directories = @(
    @{
        Path = "$BasePath\FS6000\Staging"
        Description = "FS6000 Staging Directory"
    },
    @{
        Path = "$BasePath\FS6000\Archive"
        Description = "FS6000 Archive Directory"
    },
    @{
        Path = "$BasePath\FS6000\Failed"
        Description = "FS6000 Failed Files Directory"
    },
    @{
        Path = "$BasePath\Logs"
        Description = "Application Logs Directory"
    },
    @{
        Path = "$BasePath\ICUMS\Downloads"
        Description = "ICUMS Downloads Directory"
    },
    @{
        Path = "$BasePath\ICUMS\Processed"
        Description = "ICUMS Processed Files Directory"
    }
)

# Alternative paths from appsettings.json (if different)
$alternativePaths = @(
    "C:\tadi_mirror",
    "C:\tadi_processed",
    "C:\ICUMS Downloads"
)

Write-Host "Granting permissions to: $accountToUse" -ForegroundColor Yellow
Write-Host ""

$successCount = 0
$failCount = 0

# Process main directories
foreach ($dir in $directories) {
    if (Grant-Permissions -Path $dir.Path -Account $accountToUse -Description $dir.Description) {
        $successCount++
    } else {
        $failCount++
    }
    Write-Host ""
}

# Process alternative paths (if they exist)
Write-Host "Processing alternative paths..." -ForegroundColor Cyan
foreach ($altPath in $alternativePaths) {
    if (Test-Path $altPath) {
        if (Grant-Permissions -Path $altPath -Account $accountToUse -Description "Alternative Path: $altPath") {
            $successCount++
        } else {
            $failCount++
        }
        Write-Host ""
    }
}

# Summary
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Summary" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Success: $successCount" -ForegroundColor Green
Write-Host "Failed: $failCount" -ForegroundColor $(if ($failCount -gt 0) { "Red" } else { "Green" })
Write-Host ""

if ($failCount -eq 0) {
    Write-Host "[SUCCESS] All permissions set successfully!" -ForegroundColor Green
} else {
    Write-Host "[WARNING] Some permissions failed. Ensure:" -ForegroundColor Yellow
    Write-Host "  1. Script is run as Administrator" -ForegroundColor Gray
    Write-Host "  2. Service account exists" -ForegroundColor Gray
    Write-Host "  3. Paths are accessible" -ForegroundColor Gray
}

Write-Host ""

