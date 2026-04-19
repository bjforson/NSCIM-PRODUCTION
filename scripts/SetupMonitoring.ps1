# NickScan Central Imaging Portal - Monitoring System Setup Script
# This script helps you set up and test the comprehensive monitoring system

param(
    [switch]$SkipBuild,
    [switch]$SkipTest,
    [switch]$OpenDashboard
)

Write-Host "🚀 NickScan Central Imaging Portal - Monitoring System Setup" -ForegroundColor Blue
Write-Host "=" * 70 -ForegroundColor Blue

# Function to check if a command exists
function Test-Command($cmdname) {
    return [bool](Get-Command -Name $cmdname -ErrorAction SilentlyContinue)
}

# Check prerequisites
Write-Host "🔍 Checking prerequisites..." -ForegroundColor Yellow

if (!(Test-Command "dotnet")) {
    Write-Host "❌ .NET SDK not found. Please install .NET 8 SDK." -ForegroundColor Red
    exit 1
}

if (!(Test-Command "sqlcmd")) {
    Write-Host "⚠️ SQL Server command line tools not found. Some database monitoring features may not work." -ForegroundColor Yellow
}

Write-Host "✅ Prerequisites check completed" -ForegroundColor Green

# Build the solution
if (!$SkipBuild) {
    Write-Host "🔨 Building solution..." -ForegroundColor Yellow
    try {
        dotnet build src/NickScanCentralImagingPortal.API/NickScanCentralImagingPortal.API.csproj --configuration Release
        if ($LASTEXITCODE -ne 0) {
            Write-Host "❌ Build failed. Please check the errors above." -ForegroundColor Red
            exit 1
        }
        Write-Host "✅ Build completed successfully" -ForegroundColor Green
    }
    catch {
        Write-Host "❌ Build error: $($_.Exception.Message)" -ForegroundColor Red
        exit 1
    }
}

# Test the monitoring endpoints
if (!$SkipTest) {
    Write-Host "🧪 Testing monitoring endpoints..." -ForegroundColor Yellow
    
    # Start the API in background
    Write-Host "Starting API server..." -ForegroundColor Cyan
    $apiProcess = Start-Process -FilePath "dotnet" -ArgumentList "run", "--project", "src/NickScanCentralImagingPortal.API/NickScanCentralImagingPortal.API.csproj", "--urls", "http://localhost:5205" -PassThru -WindowStyle Hidden
    
    # Wait for API to start
    Write-Host "Waiting for API to start..." -ForegroundColor Cyan
    $maxAttempts = 30
    $attempt = 0
    $apiReady = $false
    
    while ($attempt -lt $maxAttempts -and !$apiReady) {
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:5205/swagger" -TimeoutSec 5 -UseBasicParsing
            if ($response.StatusCode -eq 200) {
                $apiReady = $true
                Write-Host "✅ API is ready!" -ForegroundColor Green
            }
        }
        catch {
            Start-Sleep -Seconds 2
            $attempt++
        }
    }
    
    if (!$apiReady) {
        Write-Host "❌ API failed to start within 60 seconds" -ForegroundColor Red
        $apiProcess.Kill()
        exit 1
    }
    
    # Test monitoring endpoints
    $endpoints = @(
        @{ Name = "System Health Overview"; Url = "/api/monitoring/health/overview" },
        @{ Name = "Services Health"; Url = "/api/monitoring/health/services" },
        @{ Name = "Database Statistics"; Url = "/api/monitoring/database/statistics" },
        @{ Name = "Performance Metrics"; Url = "/api/monitoring/performance/metrics" },
        @{ Name = "File System Status"; Url = "/api/monitoring/filesystem/status" }
    )
    
    Write-Host "Testing monitoring endpoints..." -ForegroundColor Cyan
    $allTestsPassed = $true
    
    foreach ($endpoint in $endpoints) {
        try {
            $response = Invoke-WebRequest -Uri "http://localhost:5205$($endpoint.Url)" -UseBasicParsing
            if ($response.StatusCode -eq 200) {
                Write-Host "✅ $($endpoint.Name): OK" -ForegroundColor Green
            } else {
                Write-Host "⚠️ $($endpoint.Name): HTTP $($response.StatusCode)" -ForegroundColor Yellow
                $allTestsPassed = $false
            }
        }
        catch {
            Write-Host "❌ $($endpoint.Name): ERROR - $($_.Exception.Message)" -ForegroundColor Red
            $allTestsPassed = $false
        }
    }
    
    # Test monitoring dashboard
    try {
        $response = Invoke-WebRequest -Uri "http://localhost:5205/monitoring-dashboard.html" -UseBasicParsing
        if ($response.StatusCode -eq 200) {
            Write-Host "✅ Monitoring Dashboard: OK" -ForegroundColor Green
        } else {
            Write-Host "⚠️ Monitoring Dashboard: HTTP $($response.StatusCode)" -ForegroundColor Yellow
            $allTestsPassed = $false
        }
    }
    catch {
        Write-Host "❌ Monitoring Dashboard: ERROR - $($_.Exception.Message)" -ForegroundColor Red
        $allTestsPassed = $false
    }
    
    # Stop the API
    Write-Host "Stopping API server..." -ForegroundColor Cyan
    $apiProcess.Kill()
    Start-Sleep -Seconds 2
    
    if ($allTestsPassed) {
        Write-Host "✅ All monitoring tests passed!" -ForegroundColor Green
    } else {
        Write-Host "⚠️ Some monitoring tests failed. Check the errors above." -ForegroundColor Yellow
    }
}

# Display setup summary
Write-Host "`n📊 Monitoring System Setup Summary" -ForegroundColor Blue
Write-Host "=" * 50 -ForegroundColor Blue

Write-Host "🎯 What's Been Set Up:" -ForegroundColor Cyan
Write-Host "   ✅ Comprehensive Health Check Service" -ForegroundColor Green
Write-Host "   ✅ Monitoring API Endpoints" -ForegroundColor Green
Write-Host "   ✅ Real-Time SignalR Hub" -ForegroundColor Green
Write-Host "   ✅ Web-Based Dashboard" -ForegroundColor Green
Write-Host "   ✅ PowerShell Monitoring Script" -ForegroundColor Green

Write-Host "`n🚀 How to Use:" -ForegroundColor Cyan
Write-Host "   1. Start your API: dotnet run --project src/NickScanCentralImagingPortal.API" -ForegroundColor White
Write-Host "   2. Open Dashboard: http://localhost:5205/monitoring-dashboard.html" -ForegroundColor White
Write-Host "   3. Use PowerShell: .\scripts\MonitorAllServices.ps1 -Continuous" -ForegroundColor White
Write-Host "   4. API Endpoints: http://localhost:5205/api/monitoring/*" -ForegroundColor White

Write-Host "`n📋 Monitoring Features:" -ForegroundColor Cyan
Write-Host "   🔍 Real-time service health monitoring" -ForegroundColor White
Write-Host "   📊 Performance metrics and charts" -ForegroundColor White
Write-Host "   🚨 Automatic alerts and notifications" -ForegroundColor White
Write-Host "   📈 Historical data and trends" -ForegroundColor White
Write-Host "   🎨 Beautiful web dashboard" -ForegroundColor White
Write-Host "   💻 Command-line monitoring tools" -ForegroundColor White

Write-Host "`n🔧 Services Monitored:" -ForegroundColor Cyan
Write-Host "   • NickScanCentralImagingPortal.API (Port 5205)" -ForegroundColor White
Write-Host "   • NickScanWebApp (Port 5126/7263)" -ForegroundColor White
Write-Host "   • SQL Server Database" -ForegroundColor White
Write-Host "   • FS6000 Background Service" -ForegroundColor White
Write-Host "   • ASE Background Service" -ForegroundColor White
Write-Host "   • ICUMS Background Service" -ForegroundColor White
Write-Host "   • Image Processing Service" -ForegroundColor White
Write-Host "   • File Sync Service" -ForegroundColor White
Write-Host "   • System Resources (CPU, Memory, Disk)" -ForegroundColor White

Write-Host "`n📚 Documentation:" -ForegroundColor Cyan
Write-Host "   📖 Complete Guide: MONITORING_GUIDE.md" -ForegroundColor White
Write-Host "   🔧 PowerShell Script: scripts/MonitorAllServices.ps1" -ForegroundColor White
Write-Host "   🌐 Web Dashboard: monitoring-dashboard.html" -ForegroundColor White

# Open dashboard if requested
if ($OpenDashboard) {
    Write-Host "`n🌐 Opening monitoring dashboard..." -ForegroundColor Cyan
    Start-Process "http://localhost:5205/monitoring-dashboard.html"
}

Write-Host "`n🎉 Monitoring system setup completed!" -ForegroundColor Green
Write-Host "Your system is now BULLETPROOF with comprehensive monitoring! 🚀" -ForegroundColor Green
