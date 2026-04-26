# ============================================================================
# trust-nscim-cert.ps1
#
# Installs the NSCIM development root CA certificate so this machine trusts
# https://10.0.1.254:5206 (API), https://10.0.1.254:5300 (WebApp), and any
# other NSCIM-issued certs without browser security warnings.
#
# WHAT IT DOES:
#   1. Reads the bundled rootCA.pem (must be next to this script).
#   2. Installs it into LocalMachine\Root  -> Edge, Chrome, IE/Win HTTP
#   3. If Firefox is installed, flips the Firefox enterprise-roots flag
#      so Firefox also picks it up.
#
# WHAT IT DOES NOT DO:
#   - Does NOT install any private keys. The .pem here is the root CA's
#     PUBLIC certificate only, safe to distribute. The matching private key
#     stays on the NSCIM server.
#   - Does NOT modify the existing self-signed certs already on this machine.
#
# HOW TO RUN:
#   1. Right-click PowerShell -> Run as administrator
#   2. cd to the folder containing this script + nscim-rootCA.pem
#   3. Set-ExecutionPolicy -Scope Process Bypass
#   4. .\trust-nscim-cert.ps1
#
# UNDO (if you ever want to remove the trust):
#   Get-ChildItem Cert:\LocalMachine\Root |
#     Where-Object { $_.Subject -like '*mkcert*' } |
#     Remove-Item
# ============================================================================

[CmdletBinding()]
param(
    [string]$RootCaPath = (Join-Path $PSScriptRoot 'nscim-rootCA.pem')
)

$ErrorActionPreference = 'Stop'

function Write-Step {
    param([string]$Message)
    Write-Host ""
    Write-Host "==> $Message" -ForegroundColor Cyan
}

function Write-OK {
    param([string]$Message)
    Write-Host "    [OK] $Message" -ForegroundColor Green
}

function Write-Skip {
    param([string]$Message)
    Write-Host "    [SKIP] $Message" -ForegroundColor Yellow
}

function Write-Fail {
    param([string]$Message)
    Write-Host "    [FAIL] $Message" -ForegroundColor Red
}

# ----------------------------------------------------------------------------
# Step 0: sanity checks
# ----------------------------------------------------------------------------

Write-Step "Pre-flight checks"

# Admin check — required to write to LocalMachine\Root
$currentUser = [System.Security.Principal.WindowsIdentity]::GetCurrent()
$principal = New-Object System.Security.Principal.WindowsPrincipal($currentUser)
if (-not $principal.IsInRole([System.Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Fail "Must be run as Administrator. Right-click PowerShell -> Run as administrator."
    exit 1
}
Write-OK "Running as Administrator"

if (-not (Test-Path $RootCaPath)) {
    Write-Fail "Root CA file not found: $RootCaPath"
    Write-Fail "Place nscim-rootCA.pem next to this script, or pass -RootCaPath <path>."
    exit 1
}
Write-OK "Found root CA file: $RootCaPath"

# ----------------------------------------------------------------------------
# Step 1: install into Windows trust store
# ----------------------------------------------------------------------------

Write-Step "Installing root CA into Windows trust store (LocalMachine\Root)"

try {
    $cert = New-Object System.Security.Cryptography.X509Certificates.X509Certificate2
    $cert.Import($RootCaPath)

    Write-Host "    Subject:    $($cert.Subject)"
    Write-Host "    Thumbprint: $($cert.Thumbprint)"
    Write-Host "    Valid until: $($cert.NotAfter)"

    $store = New-Object System.Security.Cryptography.X509Certificates.X509Store(
        [System.Security.Cryptography.X509Certificates.StoreName]::Root,
        [System.Security.Cryptography.X509Certificates.StoreLocation]::LocalMachine
    )
    $store.Open([System.Security.Cryptography.X509Certificates.OpenFlags]::ReadWrite)

    $existing = $store.Certificates.Find(
        [System.Security.Cryptography.X509Certificates.X509FindType]::FindByThumbprint,
        $cert.Thumbprint, $false
    )
    if ($existing.Count -gt 0) {
        Write-Skip "Already installed (thumbprint $($cert.Thumbprint))"
    } else {
        $store.Add($cert)
        Write-OK "Installed into LocalMachine\Root"
    }
    $store.Close()
} catch {
    Write-Fail "Could not install root CA: $($_.Exception.Message)"
    exit 1
}

# ----------------------------------------------------------------------------
# Step 2: Firefox (if present)
# ----------------------------------------------------------------------------

Write-Step "Checking for Firefox"

$firefoxInstalled = (Test-Path "${env:ProgramFiles}\Mozilla Firefox\firefox.exe") -or
                    (Test-Path "${env:ProgramFiles(x86)}\Mozilla Firefox\firefox.exe")

if (-not $firefoxInstalled) {
    Write-Skip "Firefox not installed — nothing to do for Firefox"
} else {
    # Flip the per-machine policy so Firefox honors the system trust store.
    # This is the supported approach: https://support.mozilla.org/kb/setting-certificate-authorities-firefox
    $policyKey = 'HKLM:\SOFTWARE\Policies\Mozilla\Firefox\Certificates'
    if (-not (Test-Path $policyKey)) {
        New-Item -Path $policyKey -Force | Out-Null
    }
    Set-ItemProperty -Path $policyKey -Name 'ImportEnterpriseRoots' -Value 1 -Type DWord
    Write-OK "Firefox: ImportEnterpriseRoots policy enabled (restart Firefox to pick up)"
}

# ----------------------------------------------------------------------------
# Step 3: verify
# ----------------------------------------------------------------------------

Write-Step "Verifying"

$verify = Get-ChildItem Cert:\LocalMachine\Root |
    Where-Object { $_.Thumbprint -eq $cert.Thumbprint }

if ($verify) {
    Write-OK "Root CA is in the trust store"
} else {
    Write-Fail "Verification failed — cert not found in store after install"
    exit 1
}

# ----------------------------------------------------------------------------
# Done
# ----------------------------------------------------------------------------

Write-Host ""
Write-Host "===============================================================" -ForegroundColor Green
Write-Host "  Done. Restart your browser and visit:" -ForegroundColor Green
Write-Host "    https://10.0.1.254:5206  (API)" -ForegroundColor Green
Write-Host "    https://10.0.1.254:5300  (WebApp)" -ForegroundColor Green
Write-Host "  No more 'Not Secure' warning." -ForegroundColor Green
Write-Host "===============================================================" -ForegroundColor Green
Write-Host ""
