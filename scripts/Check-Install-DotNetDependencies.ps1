# Check and Install .NET 8.0 Dependencies
# Usage: .\scripts\Check-Install-DotNetDependencies.ps1

$ErrorActionPreference = "Stop"

Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  .NET 8.0 Dependencies Check & Install" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

# Determine architecture
$arch = if ([Environment]::Is64BitOperatingSystem) { "x64" } else { "x86" }
Write-Host "System Architecture: $arch" -ForegroundColor White
Write-Host ""

# Required components for this application:
# 1. .NET 8.0 SDK (includes runtime) - REQUIRED for building
# 2. ASP.NET Core 8.0 Runtime - REQUIRED for running web apps
# 3. Visual C++ Redistributables - Often needed for native dependencies

$missingComponents = @()
$installedComponents = @()

# Check 1: .NET SDK
Write-Host "Step 1: Checking .NET 8.0 SDK..." -ForegroundColor Yellow
$dotnetPath = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnetPath) {
    $version = dotnet --version 2>$null
    if ($version -like "8.*") {
        Write-Host "   ✅ .NET 8.0 SDK installed: $version" -ForegroundColor Green
        Write-Host "      Location: $($dotnetPath.Source)" -ForegroundColor Gray
        
        # Check installed runtimes
        $runtimes = dotnet --list-runtimes 2>$null
        Write-Host "      Installed runtimes:" -ForegroundColor Gray
        $runtimes | ForEach-Object {
            Write-Host "         $_" -ForegroundColor Gray
        }
        
        $installedComponents += "SDK"
    } else {
        Write-Host "   ⚠️  .NET SDK found but wrong version: $version (need 8.x)" -ForegroundColor Yellow
        $missingComponents += "SDK"
    }
} else {
    Write-Host "   ❌ .NET 8.0 SDK not found" -ForegroundColor Red
    $missingComponents += "SDK"
}
Write-Host ""

# Check 2: ASP.NET Core Runtime (separate check)
Write-Host "Step 2: Checking ASP.NET Core 8.0 Runtime..." -ForegroundColor Yellow
if ($dotnetPath) {
    $aspnetRuntimes = dotnet --list-runtimes 2>$null | Where-Object { $_ -like "Microsoft.AspNetCore.App 8.*" }
    if ($aspnetRuntimes) {
        Write-Host "   ✅ ASP.NET Core 8.0 Runtime installed" -ForegroundColor Green
        $aspnetRuntimes | ForEach-Object {
            Write-Host "      $_" -ForegroundColor Gray
        }
        $installedComponents += "ASP.NET Runtime"
    } else {
        Write-Host "   ⚠️  ASP.NET Core 8.0 Runtime not found" -ForegroundColor Yellow
        Write-Host "      Note: SDK should include this, but checking separately..." -ForegroundColor Gray
        # SDK includes runtime, so this might be OK
    }
} else {
    Write-Host "   ⚠️  Cannot check (SDK not installed)" -ForegroundColor Yellow
}
Write-Host ""

# Check 3: Visual C++ Redistributables
Write-Host "Step 3: Checking Visual C++ Redistributables..." -ForegroundColor Yellow
$vcRedistKeys = @(
    "HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x64",
    "HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x86",
    "HKLM:\SOFTWARE\WOW6432Node\Microsoft\VisualStudio\14.0\VC\Runtimes\x86"
)

$vcRedistFound = $false
foreach ($key in $vcRedistKeys) {
    if (Test-Path $key) {
        $version = (Get-ItemProperty $key -ErrorAction SilentlyContinue).Version
        if ($version) {
            Write-Host "   ✅ Visual C++ Redistributables found: $version" -ForegroundColor Green
            $vcRedistFound = $true
            $installedComponents += "VC++ Redistributables"
            break
        }
    }
}

if (-not $vcRedistFound) {
    Write-Host "   ⚠️  Visual C++ Redistributables not detected" -ForegroundColor Yellow
    Write-Host "      May be needed for some native dependencies" -ForegroundColor Gray
    Write-Host "      Download: https://aka.ms/vs/17/release/vc_redist.x64.exe" -ForegroundColor Cyan
    $missingComponents += "VC++ Redistributables"
}
Write-Host ""

# Summary
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host "  Summary" -ForegroundColor Cyan
Write-Host "==================================================" -ForegroundColor Cyan
Write-Host ""

if ($installedComponents.Count -gt 0) {
    Write-Host "✅ Installed Components:" -ForegroundColor Green
    $installedComponents | ForEach-Object {
        Write-Host "   • $_" -ForegroundColor Green
    }
    Write-Host ""
}

if ($missingComponents.Count -gt 0) {
    Write-Host "❌ Missing Components:" -ForegroundColor Red
    $missingComponents | ForEach-Object {
        Write-Host "   • $_" -ForegroundColor Red
    }
    Write-Host ""
    
    # Installation options
    Write-Host "Installation Options:" -ForegroundColor Yellow
    Write-Host ""
    
    if ($missingComponents -contains "SDK") {
        Write-Host "1. Install .NET 8.0 SDK:" -ForegroundColor White
        Write-Host "   Run: .\scripts\Install-DotNetSDK.ps1" -ForegroundColor Cyan
        Write-Host "   Or download from: https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
        Write-Host ""
    }
    
    if ($missingComponents -contains "VC++ Redistributables") {
        Write-Host "2. Install Visual C++ Redistributables:" -ForegroundColor White
        Write-Host "   Download: https://aka.ms/vs/17/release/vc_redist.x64.exe" -ForegroundColor Cyan
        Write-Host "   Or use winget: winget install Microsoft.VCRedist.2015+.x64" -ForegroundColor Cyan
        Write-Host ""
    }
} else {
    Write-Host "✅ All required components are installed!" -ForegroundColor Green
    Write-Host ""
    Write-Host "Your system is ready to build and run the application." -ForegroundColor Green
}

Write-Host ""

# Additional recommendations
Write-Host "Additional Recommendations:" -ForegroundColor Yellow
Write-Host "   • Windows Hosting Bundle (if deploying to IIS):" -ForegroundColor White
Write-Host "     https://dotnet.microsoft.com/download/dotnet/8.0" -ForegroundColor Cyan
Write-Host "     Download: ASP.NET Core Runtime 8.0.x - Windows Hosting Bundle Installer" -ForegroundColor Cyan
Write-Host ""
Write-Host "   • For production servers that only RUN (not build):" -ForegroundColor White
Write-Host "     Install ASP.NET Core 8.0 Runtime (smaller than SDK)" -ForegroundColor Gray
Write-Host ""
Write-Host "   • For development/build servers:" -ForegroundColor White
Write-Host "     Install .NET 8.0 SDK (includes runtime + build tools)" -ForegroundColor Gray
Write-Host ""

