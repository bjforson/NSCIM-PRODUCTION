# Check ICUMS Download Queue Status
param(
    [Parameter(Mandatory=$false)]
    [string]$ContainerNumber = "TEMU9786811"
)

$apiBaseUrl = "http://10.0.1.254:5205"
$username = "admin"
$password = $env:NICKSCAN_SUPERADMIN_PASSWORD

Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host "ICUMS DOWNLOAD QUEUE STATUS CHECK" -ForegroundColor Cyan
Write-Host "=====================================================" -ForegroundColor Cyan
Write-Host ""

# Login
$loginUrl = "$apiBaseUrl/api/Authentication/login"
$loginBody = @{ username = $username; password = $password } | ConvertTo-Json
$loginResponse = Invoke-RestMethod -Uri $loginUrl -Method Post -Body $loginBody -ContentType "application/json"
$token = $loginResponse.token
$headers = @{ Authorization = "Bearer $token" }

Write-Host "Authenticated as: $($loginResponse.user.username)" -ForegroundColor Green
Write-Host ""

# Check specific container status
Write-Host "Container: $ContainerNumber" -ForegroundColor Yellow
Write-Host "-------------------------------------------" -ForegroundColor Gray
try {
    $statusUrl = "$apiBaseUrl/api/ICUMSDownloadQueue/status/$ContainerNumber"
    $status = Invoke-RestMethod -Uri $statusUrl -Method Get -Headers $headers
    
    if ($status.inQueue) {
        Write-Host "Status: IN QUEUE" -ForegroundColor Yellow
        Write-Host "  Queue Status: $($status.status)" -ForegroundColor Cyan
        Write-Host "  Priority: $($status.priority)" -ForegroundColor Cyan
        Write-Host "  Queued At: $($status.queuedAt)" -ForegroundColor Cyan
        Write-Host "  Retry Count: $($status.retryCount) / $($status.maxRetries)" -ForegroundColor Cyan
        if ($status.lastAttemptAt) {
            Write-Host "  Last Attempt: $($status.lastAttemptAt)" -ForegroundColor Cyan
        }
        if ($status.lastErrorMessage) {
            Write-Host "  Last Error: $($status.lastErrorMessage)" -ForegroundColor Red
        }
    } else {
        Write-Host "Status: NOT IN QUEUE" -ForegroundColor Green
        Write-Host "  $($status.message)" -ForegroundColor Gray
    }
} catch {
    Write-Host "Error checking container status: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""

# Check overall queue statistics
Write-Host "Overall Queue Statistics" -ForegroundColor Yellow
Write-Host "-------------------------------------------" -ForegroundColor Gray
try {
    $statsUrl = "$apiBaseUrl/api/ICUMSDownloadQueue/stats"
    $stats = Invoke-RestMethod -Uri $statsUrl -Method Get -Headers $headers
    
    Write-Host "  Pending: $($stats.pending)" -ForegroundColor Yellow
    Write-Host "  Processing: $($stats.processing)" -ForegroundColor Cyan
    Write-Host "  Completed: $($stats.completed)" -ForegroundColor Green
    Write-Host "  Failed: $($stats.failed)" -ForegroundColor Red
    Write-Host ""
    Write-Host "Priority Breakdown:" -ForegroundColor Yellow
    Write-Host "  High Priority: $($stats.highPriority)" -ForegroundColor Magenta
    Write-Host "  Normal Priority: $($stats.normalPriority)" -ForegroundColor Cyan
    Write-Host "  Low Priority: $($stats.lowPriority)" -ForegroundColor Gray
    
    if ($stats.averageWaitTimeMinutes -gt 0) {
        Write-Host ""
        Write-Host "  Average Wait Time: $([math]::Round($stats.averageWaitTimeMinutes, 2)) minutes" -ForegroundColor Cyan
    }
    if ($stats.successRate -gt 0) {
        Write-Host "  Success Rate: $([math]::Round($stats.successRate, 2))%" -ForegroundColor Green
    }
} catch {
    Write-Host "Error getting statistics: $($_.Exception.Message)" -ForegroundColor Red
}

Write-Host ""
Write-Host "=====================================================" -ForegroundColor Cyan
