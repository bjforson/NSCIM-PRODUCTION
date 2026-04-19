# Install Visual C++ Redistributables
# Usage: .\scripts\Install-VCRedistributables.ps1

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Visual C++ Redistributables Installation" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Determine architecture
$arch = if ([Environment]::Is64BitOperatingSystem) { "x64" } else { "x86" }
Write-Host "System Architecture: $arch" -ForegroundColor White
Write-Host ""

# Check if already installed
Write-Host "Step 1: Checking for existing installation..." -ForegroundColor Yellow
$vcRedistKeys = @(
    "HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64"
)

$vcRedistFound = $false
foreach ($key in $vcRedistKeys) {
    if (Test-Path $key) {
        $version = (Get-ItemProperty $key -ErrorAction SilentlyContinue).Version
        if ($version) {
            Write-Host "   ✅ Visual C++ Redistributables already installed: $version" -ForegroundColor Green
            $vcRedistFound = $true
            break
        }
    }
}

if ($vcRedistFound) {
    Write-Host ""
    Write-Host "Visual C++ Redistributables are already installed." -ForegroundColor Green
    exit 0
}

Write-Host "   ℹ️  Not found. Proceeding with installation..." -ForegroundColor Gray
Write-Host ""

# Download URL for Visual C++ Redistributables
Write-Host "Step 2: Downloading Visual C++ Redistributables..." -ForegroundColor Yellow
$downloadUrl = "https://aka.ms/vs/17/release/vc_redist.x64.exe"
$tempDir = Join-Path $env:TEMP "vc-redist-install"
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
}

$installerPath = Join-Path $tempDir "vc_redist.x64.exe"

Write-Host "   Download URL: $downloadUrl" -ForegroundColor Gray
Write-Host "   Saving to: $installerPath" -ForegroundColor Gray
Write-Host ""

try {
    $ProgressPreference = 'Continue'
    Invoke-WebRequest -Uri $downloadUrl -OutFile $installerPath -UseBasicParsing
    
    if (Test-Path $installerPath) {
        $fileSize = (Get-Item $installerPath).Length / 1MB
        Write-Host "   ✅ Download complete! ($([math]::Round($fileSize, 2)) MB)" -ForegroundColor Green
    } else {
        Write-Host "   ❌ Download failed - file not found" -ForegroundColor Red
        exit 1
    }
} catch {
    Write-Host "   ❌ Download failed: $($_.Exception.Message)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Alternative: Please download manually from:" -ForegroundColor Yellow
    Write-Host "   https://aka.ms/vs/17/release/vc_redist.x64.exe" -ForegroundColor Cyan
    exit 1
}

Write-Host ""

# Install
Write-Host "Step 3: Installing Visual C++ Redistributables..." -ForegroundColor Yellow
Write-Host "   This will take a moment..." -ForegroundColor Gray
Write-Host ""

try {
    $installArgs = "/quiet /norestart"
    $process = Start-Process -FilePath $installerPath -ArgumentList $installArgs -Wait -PassThru -NoNewWindow
    
    if ($process.ExitCode -eq 0 -or $process.ExitCode -eq 3010) {
        Write-Host "   ✅ Installation completed successfully!" -ForegroundColor Green
        
        if ($process.ExitCode -eq 3010) {
            Write-Host "   ⚠️  A system reboot is recommended." -ForegroundColor Yellow
        }
    } else {
        Write-Host "   ⚠️  Installation completed with exit code: $($process.ExitCode)" -ForegroundColor Yellow
    }
} catch {
    Write-Host "   ❌ Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Verify installation
Write-Host "Step 4: Verifying installation..." -ForegroundColor Yellow
Start-Sleep -Seconds 2

$vcRedistFound = $false
foreach ($key in $vcRedistKeys) {
    if (Test-Path $key) {
        $version = (Get-ItemProperty $key -ErrorAction SilentlyContinue).Version
        if ($version) {
            Write-Host "   ✅ Visual C++ Redistributables verified: $version" -ForegroundColor Green
            $vcRedistFound = $true
            break
        }
    }
}

if (-not $vcRedistFound) {
    Write-Host "   ⚠️  Installation may require a reboot to complete" -ForegroundColor Yellow
}

Write-Host ""

# Cleanup
Write-Host "Step 5: Cleaning up..." -ForegroundColor Yellow
if (Test-Path $installerPath) {
    Remove-Item $installerPath -Force -ErrorAction SilentlyContinue
    Write-Host "   ✅ Temporary files removed" -ForegroundColor Green
}

Write-Host ""

# Summary
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Installation Complete" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

if ($vcRedistFound) {
    Write-Host "✅ Visual C++ Redistributables installed successfully!" -ForegroundColor Green
} else {
    Write-Host "⚠️  Installation may require a system reboot" -ForegroundColor Yellow
    Write-Host "   Please restart your computer and verify installation." -ForegroundColor White
}

Write-Host ""

