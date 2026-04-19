# SSL Certificate Installation and Configuration Script
# This script helps install SSL certificates and configure environment variables for NickScan Central Imaging Portal
# Run as Administrator

param(
    [Parameter(Mandatory=$false)]
    [string]$CertificatePath = "",
    
    [Parameter(Mandatory=$false)]
    [string]$CertificatePassword = "",
    
    [Parameter(Mandatory=$false)]
    [string]$Thumbprint = "",
    
    [Parameter(Mandatory=$false)]
    [switch]$FromStore,
    
    [Parameter(Mandatory=$false)]
    [switch]$VerifyOnly
)

$ErrorActionPreference = "Stop"

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "SSL Certificate Setup for NickScan API" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if running as Administrator
$isAdmin = ([Security.Principal.WindowsPrincipal] [Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)

if (-not $isAdmin) {
    Write-Host "❌ ERROR: This script must be run as Administrator!" -ForegroundColor Red
    Write-Host "Right-click PowerShell and select 'Run as Administrator'" -ForegroundColor Yellow
    exit 1
}

# Function to display certificate information
function Show-CertificateInfo {
    param($cert)
    Write-Host "`nCertificate Information:" -ForegroundColor Green
    Write-Host "  Subject: $($cert.Subject)" -ForegroundColor White
    Write-Host "  Issuer: $($cert.Issuer)" -ForegroundColor White
    Write-Host "  Thumbprint: $($cert.Thumbprint)" -ForegroundColor White
    Write-Host "  Valid From: $($cert.NotBefore)" -ForegroundColor White
    Write-Host "  Valid To: $($cert.NotAfter)" -ForegroundColor White
    $daysUntilExpiry = ($cert.NotAfter - (Get-Date)).Days
    Write-Host "  Days Until Expiry: $daysUntilExpiry" -ForegroundColor $(if ($daysUntilExpiry -lt 30) { "Red" } elseif ($daysUntilExpiry -lt 90) { "Yellow" } else { "Green" })
}

# Function to find certificate in store
function Find-CertificateInStore {
    Write-Host "`nSearching for certificates in LocalMachine\My store..." -ForegroundColor Cyan
    $certificates = Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object {
        $_.HasPrivateKey -and $_.NotAfter -gt (Get-Date)
    } | Sort-Object NotAfter -Descending
    
    if ($certificates.Count -eq 0) {
        Write-Host "⚠️ No valid certificates found in LocalMachine\My store" -ForegroundColor Yellow
        return $null
    }
    
    Write-Host "`nFound $($certificates.Count) valid certificate(s):" -ForegroundColor Green
    $index = 1
    foreach ($cert in $certificates) {
        Write-Host "`n[$index]" -ForegroundColor Cyan
        Show-CertificateInfo $cert
        $index++
    }
    
    if ($certificates.Count -eq 1) {
        $selected = $certificates[0]
        Write-Host "`n✅ Auto-selected the only certificate found" -ForegroundColor Green
        return $selected
    }
    
    Write-Host "`nSelect certificate number (1-$($certificates.Count)): " -NoNewline -ForegroundColor Yellow
    $choice = Read-Host
    
    if ([int]$choice -ge 1 -and [int]$choice -le $certificates.Count) {
        return $certificates[[int]$choice - 1]
    } else {
        Write-Host "❌ Invalid selection" -ForegroundColor Red
        return $null
    }
}

# Function to install certificate from file
function Install-CertificateFromFile {
    param($filePath, $password)
    
    if (-not (Test-Path $filePath)) {
        Write-Host "❌ Certificate file not found: $filePath" -ForegroundColor Red
        return $null
    }
    
    Write-Host "`nInstalling certificate from file: $filePath" -ForegroundColor Cyan
    
    try {
        if ([string]::IsNullOrEmpty($password)) {
            $securePassword = Read-Host -AsSecureString "Enter certificate password"
        } else {
            $securePassword = ConvertTo-SecureString $password -AsPlainText -Force
        }
        
        $cert = Import-PfxCertificate `
            -FilePath $filePath `
            -CertStoreLocation "Cert:\LocalMachine\My" `
            -Password $securePassword
        
        Write-Host "✅ Certificate installed successfully!" -ForegroundColor Green
        Show-CertificateInfo $cert
        return $cert
    }
    catch {
        Write-Host "❌ Error installing certificate: $($_.Exception.Message)" -ForegroundColor Red
        return $null
    }
}

# Function to set environment variable
function Set-EnvironmentVariable {
    param($name, $value)
    
    Write-Host "`nSetting environment variable: $name" -ForegroundColor Cyan
    try {
        [System.Environment]::SetEnvironmentVariable($name, $value, [System.EnvironmentVariableTarget]::Machine)
        $verify = [System.Environment]::GetEnvironmentVariable($name, [System.EnvironmentVariableTarget]::Machine)
        
        if ($verify -eq $value) {
            Write-Host "✅ Environment variable set successfully" -ForegroundColor Green
            return $true
        } else {
            Write-Host "❌ Environment variable verification failed" -ForegroundColor Red
            return $false
        }
    }
    catch {
        Write-Host "❌ Error setting environment variable: $($_.Exception.Message)" -ForegroundColor Red
        return $false
    }
}

# Main script logic

if ($VerifyOnly) {
    Write-Host "Verification Mode: Checking existing certificates..." -ForegroundColor Cyan
    $cert = Find-CertificateInStore
    if ($cert) {
        $thumbprint = [System.Environment]::GetEnvironmentVariable("NICKSCAN_API_CERT_THUMBPRINT", [System.EnvironmentVariableTarget]::Machine)
        if ($thumbprint -eq $cert.Thumbprint) {
            Write-Host "`n✅ Certificate thumbprint matches environment variable!" -ForegroundColor Green
        } else {
            Write-Host "`n⚠️ Certificate thumbprint does not match environment variable" -ForegroundColor Yellow
            Write-Host "   Certificate: $($cert.Thumbprint)" -ForegroundColor White
            Write-Host "   Environment: $thumbprint" -ForegroundColor White
        }
    }
    exit 0
}

# Step 1: Install or find certificate
$certificate = $null

if ($FromStore) {
    # Find existing certificate in store
    $certificate = Find-CertificateInStore
}
elseif (-not [string]::IsNullOrEmpty($CertificatePath)) {
    # Install from file
    $certificate = Install-CertificateFromFile -filePath $CertificatePath -password $CertificatePassword
}
elseif (-not [string]::IsNullOrEmpty($Thumbprint)) {
    # Use specific thumbprint
    Write-Host "`nLooking for certificate with thumbprint: $Thumbprint" -ForegroundColor Cyan
    $certificates = Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Thumbprint -eq $Thumbprint }
    if ($certificates.Count -gt 0) {
        $certificate = $certificates[0]
        Write-Host "✅ Certificate found!" -ForegroundColor Green
        Show-CertificateInfo $certificate
    } else {
        Write-Host "❌ Certificate with thumbprint $Thumbprint not found in LocalMachine\My store" -ForegroundColor Red
        exit 1
    }
}
else {
    # Interactive mode
    Write-Host "Certificate Installation Options:" -ForegroundColor Yellow
    Write-Host "  1. Install from .pfx file" -ForegroundColor White
    Write-Host "  2. Use existing certificate from store" -ForegroundColor White
    Write-Host "  3. Enter thumbprint manually" -ForegroundColor White
    Write-Host ""
    $choice = Read-Host "Select option (1-3)"
    
    switch ($choice) {
        "1" {
            $filePath = Read-Host "Enter path to .pfx certificate file"
            $password = Read-Host -AsSecureString "Enter certificate password"
            $certificate = Install-CertificateFromFile -filePath $filePath -password $([Runtime.InteropServices.Marshal]::PtrToStringAuto([Runtime.InteropServices.Marshal]::SecureStringToBSTR($password)))
        }
        "2" {
            $certificate = Find-CertificateInStore
        }
        "3" {
            $thumbprint = Read-Host "Enter certificate thumbprint"
            $certificates = Get-ChildItem -Path "Cert:\LocalMachine\My" | Where-Object { $_.Thumbprint -eq $thumbprint }
            if ($certificates.Count -gt 0) {
                $certificate = $certificates[0]
                Show-CertificateInfo $certificate
            } else {
                Write-Host "❌ Certificate with thumbprint $thumbprint not found" -ForegroundColor Red
                exit 1
            }
        }
        default {
            Write-Host "❌ Invalid option" -ForegroundColor Red
            exit 1
        }
    }
}

if ($null -eq $certificate) {
    Write-Host "`n❌ No certificate selected or installed" -ForegroundColor Red
    exit 1
}

# Step 2: Set environment variable
Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Setting Environment Variable" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

$confirm = Read-Host "Set NICKSCAN_API_CERT_THUMBPRINT to $($certificate.Thumbprint)? (Y/N)"
if ($confirm -eq "Y" -or $confirm -eq "y") {
    $success = Set-EnvironmentVariable -name "NICKSCAN_API_CERT_THUMBPRINT" -value $certificate.Thumbprint
    
    if ($success) {
        Write-Host "`n✅ Certificate setup complete!" -ForegroundColor Green
        Write-Host "`nNext Steps:" -ForegroundColor Yellow
        Write-Host "  1. Restart the NickScan API service" -ForegroundColor White
        Write-Host "  2. Verify certificate is loaded in application logs" -ForegroundColor White
        Write-Host "  3. Test HTTPS endpoint: https://localhost:5206/health" -ForegroundColor White
    } else {
        Write-Host "`n❌ Failed to set environment variable" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "`n⚠️ Environment variable not set. You can set it manually later:" -ForegroundColor Yellow
    Write-Host "[System.Environment]::SetEnvironmentVariable('NICKSCAN_API_CERT_THUMBPRINT', '$($certificate.Thumbprint)', 'Machine')" -ForegroundColor White
}

Write-Host "`n========================================" -ForegroundColor Cyan
Write-Host "Setup Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

