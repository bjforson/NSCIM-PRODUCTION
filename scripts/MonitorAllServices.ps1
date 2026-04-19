# NickScan Central Imaging Portal - Comprehensive Service Monitoring Script
# This script monitors all services and provides real-time status updates

param(
    [switch]$Continuous,
    [int]$IntervalSeconds = 30,
    [switch]$ShowDetails,
    [switch]$ExportLog,
    [string]$LogPath = ".\monitoring-logs\"
)

# Color functions for better visibility
function Write-ColorOutput($ForegroundColor) {
    $fc = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $ForegroundColor
    if ($args) {
        Write-Output $args
    } else {
        $input | Write-Output
    }
    $host.UI.RawUI.ForegroundColor = $fc
}

function Write-Green { Write-ColorOutput Green $args }
function Write-Red { Write-ColorOutput Red $args }
function Write-Yellow { Write-ColorOutput Yellow $args }
function Write-Blue { Write-ColorOutput Blue $args }
function Write-Cyan { Write-ColorOutput Cyan $args }

# Service definitions
$services = @{
    "API" = @{
        Name = "NickScanCentralImagingPortal.API"
        Port = 5205
        Url = "http://localhost:5205"
        HealthEndpoint = "/api/monitoring/health/overview"
        ProcessName = "NickScanCentralImagingPortal.API"
    }
    "WebApp" = @{
        Name = "NickScanWebApp"
        Ports = @(5126, 7263)
        Url = "http://localhost:5126"
        HealthEndpoint = "/"
        ProcessName = "NickScanWebApp"
    }
    "Database" = @{
        Name = "SQL Server"
        ServiceName = "MSSQLSERVER"
        ProcessName = "sqlservr"
    }
    "FileSync" = @{
        Name = "FS6000 File Sync"
        Paths = @("C:\tadi_mirror", "Z:\")
        ProcessName = "NickScanCentralImagingPortal.Services.FS6000"
    }
    "ImageProcessing" = @{
        Name = "Image Processing Service"
        ProcessName = "NickScanCentralImagingPortal.Services.ImageProcessing"
    }
    "ScannerServices" = @{
        Name = "Scanner Services"
        Services = @("ASE", "Nuctech", "HeimannSmith")
    }
}

# Create log directory if exporting logs
if ($ExportLog -and !(Test-Path $LogPath)) {
    New-Item -ItemType Directory -Path $LogPath -Force | Out-Null
}

function Test-ServiceHealth {
    param($serviceKey, $serviceConfig)
    
    $status = @{
        Name = $serviceConfig.Name
        Healthy = $false
        Status = "Unknown"
        Details = @{}
        ErrorMessage = $null
        ResponseTime = 0
    }

    try {
        switch ($serviceKey) {
            "API" {
                $stopwatch = [System.Diagnostics.Stopwatch]::StartNew()
                $response = Invoke-WebRequest -Uri "$($serviceConfig.Url)$($serviceConfig.HealthEndpoint)" -TimeoutSec 10 -UseBasicParsing
                $stopwatch.Stop()
                
                if ($response.StatusCode -eq 200) {
                    $status.Healthy = $true
                    $status.Status = "Healthy"
                    $status.ResponseTime = $stopwatch.ElapsedMilliseconds
                    
                    if ($ShowDetails) {
                        $healthData = $response.Content | ConvertFrom-Json
                        $status.Details = @{
                            OverallStatus = $healthData.SystemHealth.OverallStatus
                            TotalServices = $healthData.SystemHealth.TotalServices
                            HealthyServices = $healthData.SystemHealth.HealthyServices
                            DegradedServices = $healthData.SystemHealth.DegradedServices
                            UnhealthyServices = $healthData.SystemHealth.UnhealthyServices
                        }
                    }
                } else {
                    $status.Status = "Degraded"
                    $status.ErrorMessage = "HTTP $($response.StatusCode)"
                }
            }
            
            "WebApp" {
                $healthy = $false
                $respondingPort = 0
                
                foreach ($port in $serviceConfig.Ports) {
                    try {
                        $response = Invoke-WebRequest -Uri "http://localhost:$port" -TimeoutSec 5 -UseBasicParsing
                        if ($response.StatusCode -eq 200) {
                            $healthy = $true
                            $respondingPort = $port
                            break
                        }
                    } catch {
                        # Continue to next port
                    }
                }
                
                if ($healthy) {
                    $status.Healthy = $true
                    $status.Status = "Healthy"
                    $status.Details = @{ RespondingPort = $respondingPort }
                } else {
                    $status.Status = "Unhealthy"
                    $status.ErrorMessage = "No responding ports found"
                }
            }
            
            "Database" {
                $service = Get-Service -Name $serviceConfig.ServiceName -ErrorAction SilentlyContinue
                if ($service -and $service.Status -eq "Running") {
                    $status.Healthy = $true
                    $status.Status = "Healthy"
                    $status.Details = @{ ServiceStatus = $service.Status }
                } else {
                    $status.Status = "Unhealthy"
                    $status.ErrorMessage = "Service not running or not found"
                }
            }
            
            "FileSync" {
                $allPathsExist = $true
                $pathStatus = @{}
                
                foreach ($path in $serviceConfig.Paths) {
                    $exists = Test-Path $path
                    $pathStatus[$path] = $exists
                    if (!$exists) {
                        $allPathsExist = $false
                    }
                }
                
                if ($allPathsExist) {
                    $status.Healthy = $true
                    $status.Status = "Healthy"
                    $status.Details = @{ PathStatus = $pathStatus }
                } else {
                    $status.Status = "Degraded"
                    $status.ErrorMessage = "Some paths not accessible"
                    $status.Details = @{ PathStatus = $pathStatus }
                }
            }
            
            default {
                # Check if process is running
                $process = Get-Process -Name $serviceConfig.ProcessName -ErrorAction SilentlyContinue
                if ($process) {
                    $status.Healthy = $true
                    $status.Status = "Healthy"
                    $status.Details = @{ ProcessCount = $process.Count; ProcessId = $process.Id }
                } else {
                    $status.Status = "Unhealthy"
                    $status.ErrorMessage = "Process not found"
                }
            }
        }
    }
    catch {
        $status.Status = "Error"
        $status.ErrorMessage = $_.Exception.Message
    }

    return $status
}

function Get-SystemResources {
    $process = Get-Process -Name "dotnet" | Where-Object { $_.ProcessName -eq "dotnet" } | Sort-Object WorkingSet -Descending | Select-Object -First 1
    
    return @{
        MemoryUsageMB = if ($process) { [math]::Round($process.WorkingSet64 / 1MB, 2) } else { 0 }
        ProcessCount = (Get-Process -Name "dotnet" -ErrorAction SilentlyContinue).Count
        DiskFreeGB = [math]::Round((Get-WmiObject -Class Win32_LogicalDisk -Filter "DeviceID='C:'").FreeSpace / 1GB, 2)
        TotalMemoryGB = [math]::Round((Get-WmiObject -Class Win32_ComputerSystem).TotalPhysicalMemory / 1GB, 2)
    }
}

function Show-ServiceStatus {
    param($serviceStatuses, $systemResources)
    
    Clear-Host
    Write-Blue "🚀 NickScan Central Imaging Portal - Service Monitoring Dashboard"
    Write-Blue "=" * 80
    Write-Blue "Timestamp: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
    Write-Blue "=" * 80
    
    # System Resources
    Write-Cyan "💻 System Resources:"
    Write-Cyan "   Memory Usage: $($systemResources.MemoryUsageMB) MB / $($systemResources.TotalMemoryGB) GB"
    Write-Cyan "   Disk Free: $($systemResources.DiskFreeGB) GB"
    Write-Cyan "   Dotnet Processes: $($systemResources.ProcessCount)"
    Write-Cyan ""
    
    # Service Status Summary
    $healthyCount = ($serviceStatuses | Where-Object { $_.Healthy }).Count
    $totalCount = $serviceStatuses.Count
    $unhealthyCount = ($serviceStatuses | Where-Object { $_.Status -eq "Unhealthy" -or $_.Status -eq "Error" }).Count
    $degradedCount = ($serviceStatuses | Where-Object { $_.Status -eq "Degraded" }).Count
    
    $overallStatus = if ($unhealthyCount -gt 0) { "🔴 UNHEALTHY" } 
                    elseif ($degradedCount -gt 0) { "🟡 DEGRADED" } 
                    else { "🟢 HEALTHY" }
    
    Write-Blue "📊 Overall System Status: $overallStatus"
    Write-Blue "   Healthy: $healthyCount/$totalCount | Degraded: $degradedCount | Unhealthy: $unhealthyCount"
    Write-Blue ""
    
    # Individual Service Status
    foreach ($service in $serviceStatuses) {
        $statusIcon = switch ($service.Status) {
            "Healthy" { "🟢" }
            "Degraded" { "🟡" }
            "Unhealthy" { "🔴" }
            "Error" { "❌" }
            default { "⚪" }
        }
        
        $statusColor = switch ($service.Status) {
            "Healthy" { "Green" }
            "Degraded" { "Yellow" }
            "Unhealthy" { "Red" }
            "Error" { "Red" }
            default { "White" }
        }
        
        Write-Host "   $statusIcon $($service.Name): " -NoNewline
        Write-ColorOutput $statusColor "$($service.Status)"
        
        if ($service.ResponseTime -gt 0) {
            Write-Host "     Response Time: $($service.ResponseTime)ms" -ForegroundColor Gray
        }
        
        if ($service.ErrorMessage) {
            Write-Host "     Error: $($service.ErrorMessage)" -ForegroundColor Red
        }
        
        if ($ShowDetails -and $service.Details.Count -gt 0) {
            foreach ($detail in $service.Details.GetEnumerator()) {
                Write-Host "     $($detail.Key): $($detail.Value)" -ForegroundColor Gray
            }
        }
    }
    
    Write-Blue ""
    Write-Blue "Press Ctrl+C to exit (Continuous mode) or any key to refresh (Single mode)"
}

function Export-LogEntry {
    param($serviceStatuses, $systemResources)
    
    if (!$ExportLog) { return }
    
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm:ss"
    $logEntry = @{
        Timestamp = $timestamp
        Services = $serviceStatuses
        SystemResources = $systemResources
    }
    
    $logFile = Join-Path $LogPath "monitoring-$(Get-Date -Format 'yyyy-MM-dd').json"
    $logEntry | ConvertTo-Json -Depth 5 | Add-Content $logFile
}

function Start-Monitoring {
    Write-Blue "🚀 Starting NickScan Central Imaging Portal Service Monitoring"
    Write-Blue "Mode: $(if ($Continuous) { 'Continuous' } else { 'Single Check' })"
    Write-Blue "Interval: $IntervalSeconds seconds"
    Write-Blue "Show Details: $ShowDetails"
    Write-Blue "Export Logs: $ExportLog"
    Write-Blue ""
    
    do {
        $serviceStatuses = @()
        $systemResources = Get-SystemResources
        
        foreach ($serviceKey in $services.Keys) {
            $serviceStatus = Test-ServiceHealth -serviceKey $serviceKey -serviceConfig $services[$serviceKey]
            $serviceStatuses += $serviceStatus
        }
        
        Show-ServiceStatus -serviceStatuses $serviceStatuses -systemResources $systemResources
        Export-LogEntry -serviceStatuses $serviceStatuses -systemResources $systemResources
        
        if ($Continuous) {
            Write-Blue "Next check in $IntervalSeconds seconds..."
            Start-Sleep -Seconds $IntervalSeconds
        } else {
            Write-Blue "Press any key to check again or Ctrl+C to exit..."
            $null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")
        }
    } while ($Continuous)
}

# Main execution
try {
    Start-Monitoring
}
catch {
    Write-Red "❌ Monitoring script error: $($_.Exception.Message)"
    exit 1
}
finally {
    Write-Blue "📊 Monitoring stopped at $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')"
}
