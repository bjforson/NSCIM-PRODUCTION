# Environment Variables Setup Script
# This script helps set all required environment variables for NickScan Central Imaging Portal
# Run as Administrator

param(
    [Parameter(Mandatory=$false)]
    [switch]$Interactive,
    
    [Parameter(Mandatory=$false)]
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "NickScan Environment Variables Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "❌ ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Yellow
    exit 1
}

# Define required environment variables
$requiredVars = @{
    "NICKSCAN_JWT_SECRET_KEY" = @{
        "Description" = "JWT signing key (64+ bytes, base64 encoded)"
        "Critical" = $true
        "Current" = ""
    }
    "NICKSCAN_ASE_PASSWORD" = @{
        "Description" = "ASE database password"
        "Critical" = $true
        "Current" = ""
    }
    "NICKSCAN_ICUMS_AUTH_KEY" = @{
        "Description" = "ICUMS API authentication key"
        "Critical" = $true
        "Current" = ""
    }
    "NICKSCAN_ICUMS_DOCS_AUTH_KEY" = @{
        "Description" = "ICUMS documents API key"
        "Critical" = $true
        "Current" = ""
    }
    "NICKSCAN_ICUMS_JSON_AUTH_KEY" = @{
        "Description" = "ICUMS JSON API key"
        "Critical" = $true
        "Current" = ""
    }
    "NICKSCAN_API_CERT_THUMBPRINT" = @{
        "Description" = "SSL certificate thumbprint (for HTTPS)"
        "Critical" = $false
        "Current" = ""
    }
    "NICKSCAN_API_CERT_PASSWORD" = @{
        "Description" = "SSL certificate password (if using .pfx file)"
        "Critical" = $false
        "Current" = ""
    }
}

# Function to get current environment variable value
function Get-EnvironmentVariable {
    param($name)
    $value = [System.Environment]::GetEnvironmentVariable($name, [System.EnvironmentVariableTarget]::Machine)
    if ([string]::IsNullOrEmpty($value)) {
        return ""
    }
    return $value
}

# Function to set environment variable
function Set-EnvironmentVariable {
    param($name, $value, $mask = $false)
    
    try {
        [System.Environment]::SetEnvironmentVariable($name, $value, [System.EnvironmentVariableTarget]::Machine)
        $verify = [System.Environment]::GetEnvironmentVariable($name, [System.EnvironmentVariableTarget]::Machine)
        
        if ($verify -eq $value) {
            Write-Host "✅ Set $name" -ForegroundColor Green
            if ($mask) {
                Write-Host "   Value: [MASKED]" -ForegroundColor Gray
            }
            return $true
        } else {
            Write-Host "❌ Failed to set $name (verification failed)" -ForegroundColor Red
            return $false
        }
    }
    catch {
        Write-Host "❌ Error setting $name : $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

# Verify mode
if ($VerifyOnly) {
    Write-Host "Verification Mode: Checking environment variables..." -ForegroundColor Cyan
    Write-Host ""
    
    $allSet = $true
    $criticalMissing = @()
    
    foreach ($varName in $requiredVars.Keys) {
        $current = Get-EnvironmentVariable -name $varName
        $requiredVars[$varName].Current = $current
        
        if ([string]::IsNullOrEmpty($current)) {
            $status = "❌ NOT SET"
            $color = "Red"
            $allSet = $false
            if ($requiredVars[$varName].Critical) {
                $criticalMissing += $varName
            }
        } else {
            $status = "✅ SET"
            $color = "Green"
            if ($varName -like "*PASSWORD*" -or $varName -like "*KEY*" -or $varName -like "*SECRET*") {
                $displayValue = "[MASKED - " + $current.Length + " characters]"
            } else {
                $displayValue = $current
            }
        }
        
        Write-Host "$status $varName" -ForegroundColor $color
        Write-Host "   Description: $($requiredVars[$varName].Description)" -ForegroundColor Gray
        if (-not [string]::IsNullOrEmpty($current)) {
            Write-Host "   Value: $displayValue" -ForegroundColor Gray
        }
        Write-Host ""
    }
    
    Write-Host "========================================" -ForegroundColor Cyan
    if ($allSet) {
        Write-Host "✅ All environment variables are set!" -ForegroundColor Green
    } else {
        Write-Host "⚠️ Some environment variables are missing" -ForegroundColor Yellow
        if ($criticalMissing.Count -gt 0) {
            Write-Host "`n❌ Critical variables not set:" -ForegroundColor Red
            foreach ($var in $criticalMissing) {
                Write-Host "   - $var" -ForegroundColor Red
            }
        }
    }
    
    exit 0
}

# Interactive or batch mode
if ($Interactive) {
    Write-Host "Interactive Mode: You will be prompted for each variable" -ForegroundColor Cyan
    Write-Host ""
    
    foreach ($varName in $requiredVars.Keys) {
        $current = Get-EnvironmentVariable -name $varName
        $requiredVars[$varName].Current = $current
        
        Write-Host "Variable: $varName" -ForegroundColor Yellow
        Write-Host "Description: $($requiredVars[$varName].Description)" -ForegroundColor Gray
        
        if (-not [string]::IsNullOrEmpty($current)) {
            if ($varName -like "*PASSWORD*" -or $varName -like "*KEY*" -or $varName -like "*SECRET*") {
                Write-Host "Current value: [MASKED]" -ForegroundColor Gray
            } else {
                Write-Host "Current value: $current" -ForegroundColor Gray
            }
            
            $keep = Read-Host "Keep current value? (Y/N)"
            if ($keep -eq "Y" -or $keep -eq "y") {
                Write-Host "✅ Keeping current value`n" -ForegroundColor Green
                continue
            }
        }
        
        if ($varName -like "*PASSWORD*" -or $varName -like "*KEY*" -or $varName -like "*SECRET*") {
            $secureValue = Read-Host -AsSecureString "Enter value (hidden)"
            $value = [Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureValue))
        } else {
            $value = Read-Host "Enter value"
        }
        
        if (-not [string]::IsNullOrEmpty($value)) {
            Set-EnvironmentVariable -name $varName -value $value -mask $true
        } else {
            Write-Host "⚠️ Skipping (empty value)`n" -ForegroundColor Yellow
        }
        Write-Host ""
    }
}
else {
    # Batch mode - show what needs to be set
    Write-Host "Batch Mode: Setting environment variables from configuration" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "⚠️ This mode requires a configuration file or manual input" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "For interactive setup, run:" -ForegroundColor White
    Write-Host "  .\setup-environment-variables.ps1 -Interactive" -ForegroundColor Cyan
    Write-Host ""
    Write-Host "Or set variables manually:" -ForegroundColor White
    Write-Host ""
    
    foreach ($varName in $requiredVars.Keys) {
        $current = Get-EnvironmentVariable -name $varName
        if ([string]::IsNullOrEmpty($current)) {
            Write-Host "[System.Environment]::SetEnvironmentVariable('$varName', 'VALUE_HERE', 'Machine')" -ForegroundColor Gray
        }
    }
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Setup Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "⚠️ Important: Restart the application/service after setting environment variables!" -ForegroundColor Yellow
Write-Host ""
Write-Host "To verify, run:" -ForegroundColor White
Write-Host "  .\setup-environment-variables.ps1 -VerifyOnly" -ForegroundColor Cyan

