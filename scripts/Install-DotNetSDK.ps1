# Install .NET 8.0 SDK
# Usage: .\scripts\Install-DotNetSDK.ps1

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  .NET 8.0 SDK Installation" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Check if already installed
Write-Host "Step 1: Checking for existing .NET installation..." -ForegroundColor Yellow
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetPath) {
    $version = dotnet --version 2>$null
    if ($version) {
        Write-Host "   ✅ .NET SDK found: Version $version" -ForegroundColor Green
        
        # Check if it's .NET 8.x
        if ($version -like "8.*") {
            Write-Host "   ✅ .NET 8.0 SDK is already installed!" -ForegroundColor Green
            Write-Host ""
            Write-Host "Location: $($dotnetPath.Source)" -ForegroundColor Gray
            Write-Host ""
            $skip = Read-Host "Do you want to reinstall anyway? (y/n)"
            if ($skip -ne "y" -and $skip -ne "Y") {
                Write-Host "Installation cancelled." -ForegroundColor Yellow
                exit 0
            }
        } else {
            Write-Host "   ⚠️  Found .NET $version, but need .NET 8.0 SDK" -ForegroundColor Yellow
            Write-Host "   Continuing with installation..." -ForegroundColor Gray
        }
    }
} else {
    Write-Host "   ℹ️  No .NET SDK found. Proceeding with installation..." -ForegroundColor Gray
}

Write-Host ""

# Determine architecture
Write-Host "Step 2: Detecting system architecture..." -ForegroundColor Yellow
$arch = if ([Environment]::Is64BitOperatingSystem) { "x64" } else { "x86" }
Write-Host "   Architecture: $arch" -ForegroundColor Gray
Write-Host ""

# Download URL for .NET 8.0 SDK (Windows x64)
$downloadUrl = "https://download.visualstudio.microsoft.com/download/pr/9b8f83e7-8017-4ae8-b6c2-6398b9a8f3ef/8b1ead8296024d0b9e8e6e1e8e8e8e8e/dotnet-sdk-8.0.404-win-x64.exe"

# Alternative: Use direct download link (more reliable)
# Get latest .NET 8.0 SDK download link
Write-Host "Step 3: Getting latest .NET 8.0 SDK download link..." -ForegroundColor Yellow

# Use the official .NET download page API or direct link
# For Windows x64, .NET 8.0 SDK direct download
$downloadUrl = "https://dotnetcli.azureedge.net/dotnet/Sdk/8.0.404/dotnet-sdk-8.0.404-win-x64.exe"

Write-Host "   Download URL: $downloadUrl" -ForegroundColor Gray
Write-Host ""

# Create temp directory
$tempDir = Join-Path $env:TEMP "dotnet-install"
if (-not (Test-Path $tempDir)) {
    New-Item -ItemType Directory -Path $tempDir -Force | Out-Null
}

$installerPath = Join-Path $tempDir "dotnet-sdk-8.0.404-win-x64.exe"

# Download installer
Write-Host "Step 4: Downloading .NET 8.0 SDK installer..." -ForegroundColor Yellow
Write-Host "   This may take a few minutes depending on your internet connection..." -ForegroundColor Gray
Write-Host "   Saving to: $installerPath" -ForegroundColor Gray
Write-Host ""

try {
    # Use Invoke-WebRequest with progress
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
    Write-Host "   https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
    Write-Host "   Then run the installer." -ForegroundColor Yellow
    exit 1
}

Write-Host ""

# Install .NET SDK
Write-Host "Step 5: Installing .NET 8.0 SDK..." -ForegroundColor Yellow
Write-Host "   This will take a few minutes. Please wait..." -ForegroundColor Gray
Write-Host ""

try {
    # Run installer silently
    $installArgs = "/quiet /norestart"
    $process = Start-Process -FilePath $installerPath -ArgumentList $installArgs -Wait -PassThru -NoNewWindow
    
    if ($process.ExitCode -eq 0 -or $process.ExitCode -eq 3010) {
        # Exit code 3010 means success but requires reboot
        Write-Host "   ✅ Installation completed successfully!" -ForegroundColor Green
        
        if ($process.ExitCode -eq 3010) {
            Write-Host "   ⚠️  A system reboot is recommended to complete the installation." -ForegroundColor Yellow
        }
    } else {
        Write-Host "   ⚠️  Installation completed with exit code: $($process.ExitCode)" -ForegroundColor Yellow
        Write-Host "   This may still be successful. Verifying..." -ForegroundColor Gray
    }
} catch {
    Write-Host "   ❌ Installation failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}

Write-Host ""

# Refresh environment variables
Write-Host "Step 6: Refreshing environment variables..." -ForegroundColor Yellow
$env:Path = [System.Environment]::GetEnvironmentVariable("Path", "Machine") + ";" + [System.Environment]::GetEnvironmentVariable("Path", "User")

# Verify installation
Write-Host "Step 7: Verifying installation..." -ForegroundColor Yellow
Start-Sleep -Seconds 3

$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetPath) {
    $version = dotnet --version 2>$null
    if ($version) {
        Write-Host "   ✅ .NET SDK installed successfully!" -ForegroundColor Green
        Write-Host "   Version: $version" -ForegroundColor Gray
        Write-Host "   Location: $($dotnetPath.Source)" -ForegroundColor Gray
        
        # Show installed runtimes
        Write-Host ""
        Write-Host "   Installed runtimes:" -ForegroundColor Gray
        dotnet --list-runtimes 2>$null | ForEach-Object {
            Write-Host "      $_" -ForegroundColor Gray
        }
    } else {
        Write-Host "   ⚠️  .NET SDK found but version check failed" -ForegroundColor Yellow
        Write-Host "   Location: $($dotnetPath.Source)" -ForegroundColor Gray
        Write-Host "   You may need to restart PowerShell or reboot." -ForegroundColor Yellow
    }
} else {
    Write-Host "   ⚠️  .NET SDK not found in PATH" -ForegroundColor Yellow
    Write-Host "   Common locations:" -ForegroundColor Gray
    Write-Host "      C:\Program Files\dotnet\dotnet.exe" -ForegroundColor Gray
    Write-Host "   Please restart PowerShell or reboot to refresh PATH." -ForegroundColor Yellow
}

Write-Host ""

# Cleanup
Write-Host "Step 8: Cleaning up..." -ForegroundColor Yellow
if (Test-Path $installerPath) {
    Remove-Item $installerPath -Force -ErrorAction SilentlyContinue
    Write-Host "   ✅ Temporary files removed" -ForegroundColor Green
}

Write-Host ""

# Summary
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Installation Summary" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

if ($dotnetPath -and $version) {
    Write-Host "✅ .NET 8.0 SDK installation successful!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Next Steps:" -ForegroundColor Yellow
    Write-Host "1. If you see a reboot warning, restart your computer" -ForegroundColor White
    Write-Host "2. Open a new PowerShell window to use dotnet commands" -ForegroundColor White
    Write-Host "3. Run 'dotnet --version' to verify" -ForegroundColor White
    Write-Host "4. You can now run StartApplication.ps1" -ForegroundColor White
} else {
    Write-Host "⚠️  Installation may require a system reboot" -ForegroundColor Yellow
    Write-Host ""
    Write-Host "Please:" -ForegroundColor Yellow
    Write-Host "1. Restart your computer" -ForegroundColor White
    Write-Host "2. Open a new PowerShell window" -ForegroundColor White
    Write-Host "3. Run 'dotnet --version' to verify installation" -ForegroundColor White
}

Write-Host ""

