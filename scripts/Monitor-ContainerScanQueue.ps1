# Container Scan Queue Monitoring and Testing Script
# Monitors queue health, processing rates, and provides automated testing queries
# Date: January 10, 2026

param(
    [string]$Server = "127.0.0.1,1433",
    [string]$Database = "NS_CIS",
    [switch]$IntegratedSecurity = $true,
    [string]$Username = "",
    [string]$Password = "",
    [switch]$Continuous,
    [int]$IntervalSeconds = 30,
    [switch]$ShowDetails,
    [switch]$TestMode,
    [switch]$HealthCheck,
    [switch]$Statistics,
    [switch]$StuckItems,
    [switch]$FailedItems,
    [string]$OutputFormat = "Console" # Console, JSON, CSV
)

# Color functions for better visibility
function Write-Green { Write-Host $args -ForegroundColor Green }
function Write-Red { Write-Host $args -ForegroundColor Red }
function Write-Yellow { Write-Host $args -ForegroundColor Yellow }
function Write-Blue { Write-Host $args -ForegroundColor Blue }
function Write-Cyan { Write-Host $args -ForegroundColor Cyan }
function Write-Magenta { Write-Host $args -ForegroundColor Magenta }

# Build connection string
function Get-ConnectionString {
    if ($IntegratedSecurity) {
        return "Server=$Server;Database=$Database;Integrated Security=true;TrustServerCertificate=true;Connection Timeout=10;"
    } else {
        return "Server=$Server;Database=$Database;User Id=$Username;Password=$Password;TrustServerCertificate=true;Connection Timeout=10;"
    }
}

# Execute SQL query and return results
function Invoke-SqlQuery {
    param(
        [string]$Query,
        [string]$ConnectionString
    )
    
    try {
        $connection = New-Object System.Data.SqlClient.SqlConnection($ConnectionString)
        $connection.Open()
        
        $command = $connection.CreateCommand()
        $command.CommandText = $Query
        $command.CommandTimeout = 30
        
        $adapter = New-Object System.Data.SqlClient.SqlDataAdapter $command
        $dataset = New-Object System.Data.DataSet
        $adapter.Fill($dataset) | Out-Null
        
        $connection.Close()
        
        return $dataset.Tables[0]
    }
    catch {
        Write-Red "SQL Error: $($_.Exception.Message)"
        return $null
    }
}

# Get queue health statistics
function Get-QueueHealth {
    param([string]$ConnectionString)
    
    $query = @"
    SELECT 
        Status,
        COUNT(*) AS Count,
        AVG(CAST(DATEDIFF(SECOND, QueuedAt, ISNULL(ProcessedAt, GETUTCDATE())) AS FLOAT)) AS AvgWaitSeconds,
        MAX(DATEDIFF(SECOND, QueuedAt, ISNULL(ProcessedAt, GETUTCDATE()))) AS MaxWaitSeconds,
        MIN(QueuedAt) AS OldestQueuedAt
    FROM ContainerScanQueues
    GROUP BY Status
    ORDER BY Status;
"@
    
    return Invoke-SqlQuery -Query $query -ConnectionString $ConnectionString
}

# Get queue statistics by scanner type
function Get-QueueByScannerType {
    param([string]$ConnectionString)
    
    $query = @"
    SELECT 
        ScannerType,
        Status,
        COUNT(*) AS Count
    FROM ContainerScanQueues
    GROUP BY ScannerType, Status
    ORDER BY ScannerType, Status;
"@
    
    return Invoke-SqlQuery -Query $query -ConnectionString $ConnectionString
}

# Get processing rate (last hour)
function Get-ProcessingRate {
    param([string]$ConnectionString)
    
    $query = @"
    SELECT 
        COUNT(*) AS ItemsProcessed,
        AVG(CAST(DATEDIFF(SECOND, QueuedAt, CompletedAt) AS FLOAT)) AS AvgProcessingTimeSeconds,
        MIN(DATEDIFF(SECOND, QueuedAt, CompletedAt)) AS MinProcessingTimeSeconds,
        MAX(DATEDIFF(SECOND, QueuedAt, CompletedAt)) AS MaxProcessingTimeSeconds
    FROM ContainerScanQueues
    WHERE CompletedAt >= DATEADD(HOUR, -1, GETUTCDATE())
        AND Status = 'Completed';
"@
    
    return Invoke-SqlQuery -Query $query -ConnectionString $ConnectionString
}

# Get failed items
function Get-FailedItems {
    param([string]$ConnectionString, [int]$Limit = 20)
    
    $query = @"
    SELECT TOP $Limit
        Id, ContainerNumber, ScannerType, InspectionId,
        RetryCount, MaxRetries, ErrorMessage, CreatedAt
    FROM ContainerScanQueues
    WHERE Status = 'Failed'
    ORDER BY CreatedAt DESC;
"@
    
    return Invoke-SqlQuery -Query $query -ConnectionString $ConnectionString
}

# Get stuck items (processing >30 minutes)
function Get-StuckItems {
    param([string]$ConnectionString)
    
    $query = @"
    SELECT 
        Id, ContainerNumber, ScannerType, InspectionId,
        DATEDIFF(MINUTE, ProcessedAt, GETUTCDATE()) AS StuckMinutes,
        ProcessedAt
    FROM ContainerScanQueues
    WHERE Status = 'Processing'
        AND ProcessedAt < DATEADD(MINUTE, -30, GETUTCDATE())
    ORDER BY ProcessedAt ASC;
"@
    
    return Invoke-SqlQuery -Query $query -ConnectionString $ConnectionString
}

# Get recent queue items
function Get-RecentQueueItems {
    param([string]$ConnectionString, [int]$Limit = 10, [string]$ScannerType = "")
    
    $whereClause = if ($ScannerType) { "WHERE ScannerType = '$ScannerType'" } else { "" }
    
    $query = @"
    SELECT TOP $Limit
        Id, ContainerNumber, ScannerType, InspectionId,
        Status, Priority, QueuedAt, ProcessedAt, CompletedAt
    FROM ContainerScanQueues
    $whereClause
    ORDER BY CreatedAt DESC;
"@
    
    return Invoke-SqlQuery -Query $query -ConnectionString $ConnectionString
}

# Display queue health dashboard
function Show-QueueHealthDashboard {
    param([string]$ConnectionString)
    
    Write-Cyan "`n========================================"
    Write-Cyan "  Container Scan Queue Health Dashboard"
    Write-Cyan "========================================"
    Write-Host ""
    
    # Overall health
    $health = Get-QueueHealth -ConnectionString $ConnectionString
    if ($health -and $health.Rows.Count -gt 0) {
        Write-Blue "Queue Status Distribution:"
        $totalItems = ($health | Measure-Object -Property Count -Sum).Sum
        Write-Host "  Total Items: $totalItems" -ForegroundColor White
        
        foreach ($row in $health.Rows) {
            $status = $row["Status"]
            $count = $row["Count"]
            $percentage = if ($totalItems -gt 0) { [math]::Round(($count / $totalItems) * 100, 1) } else { 0 }
            
            $color = switch ($status) {
                "Pending" { "Yellow" }
                "Processing" { "Cyan" }
                "Completed" { "Green" }
                "Failed" { "Red" }
                default { "White" }
            }
            
            Write-Host "  $status : $count ($percentage%)" -ForegroundColor $color
            
            if ($status -eq "Pending" -and $row["AvgWaitSeconds"]) {
                $avgWait = [math]::Round($row["AvgWaitSeconds"], 1)
                $maxWait = $row["MaxWaitSeconds"]
                Write-Host "    Avg Wait: $avgWait seconds, Max Wait: $maxWait seconds" -ForegroundColor Gray
            }
        }
        
        # Alert conditions
        $pendingCount = ($health.Select("Status = 'Pending'") | Measure-Object -Property Count -Sum).Sum
        $failedCount = ($health.Select("Status = 'Failed'") | Measure-Object -Property Count -Sum).Sum
        
        if ($pendingCount -gt 1000) {
            Write-Yellow "  ⚠️  WARNING: High pending queue depth ($pendingCount items)"
        }
        if ($failedCount -gt 10) {
            Write-Red "  ❌ ALERT: $failedCount failed items need attention"
        }
    }
    
    Write-Host ""
    
    # By scanner type
    $byScanner = Get-QueueByScannerType -ConnectionString $ConnectionString
    if ($byScanner -and $byScanner.Rows.Count -gt 0) {
        Write-Blue "Queue by Scanner Type:"
        $currentType = ""
        foreach ($row in $byScanner.Rows) {
            $scannerType = $row["ScannerType"]
            $status = $row["Status"]
            $count = $row["Count"]
            
            if ($scannerType -ne $currentType) {
                Write-Host "  $scannerType :" -ForegroundColor White
                $currentType = $scannerType
            }
            
            $color = switch ($status) {
                "Pending" { "Yellow" }
                "Processing" { "Cyan" }
                "Completed" { "Green" }
                "Failed" { "Red" }
                default { "Gray" }
            }
            
            Write-Host "    $status : $count" -ForegroundColor $color
        }
    }
    
    Write-Host ""
    
    # Processing rate
    $processingRate = Get-ProcessingRate -ConnectionString $ConnectionString
    if ($processingRate -and $processingRate.Rows.Count -gt 0 -and $processingRate.Rows[0]["ItemsProcessed"] -gt 0) {
        Write-Blue "Processing Rate (Last Hour):"
        $processed = $processingRate.Rows[0]["ItemsProcessed"]
        $avgTime = [math]::Round($processingRate.Rows[0]["AvgProcessingTimeSeconds"], 1)
        $minTime = $processingRate.Rows[0]["MinProcessingTimeSeconds"]
        $maxTime = $processingRate.Rows[0]["MaxProcessingTimeSeconds"]
        
        Write-Host "  Items Processed: $processed" -ForegroundColor White
        Write-Host "  Avg Processing Time: $avgTime seconds" -ForegroundColor White
        Write-Host "  Min: $minTime seconds, Max: $maxTime seconds" -ForegroundColor Gray
        
        if ($avgTime -gt 60) {
            Write-Yellow "  ⚠️  WARNING: Average processing time is high"
        }
    }
    
    Write-Host ""
}

# Display stuck items
function Show-StuckItems {
    param([string]$ConnectionString)
    
    Write-Yellow "`nChecking for Stuck Items (Processing >30 minutes)..."
    
    $stuck = Get-StuckItems -ConnectionString $ConnectionString
    if ($stuck -and $stuck.Rows.Count -gt 0) {
        Write-Red "  ❌ Found $($stuck.Rows.Count) stuck items:"
        foreach ($row in $stuck.Rows) {
            $minutes = $row["StuckMinutes"]
            Write-Red "    ID: $($row["Id"]), Container: $($row["ContainerNumber"]), Stuck for: $minutes minutes"
        }
        return $true
    } else {
        Write-Green "  ✅ No stuck items found"
        return $false
    }
}

# Display failed items
function Show-FailedItems {
    param([string]$ConnectionString)
    
    Write-Yellow "`nChecking for Failed Items..."
    
    $failed = Get-FailedItems -ConnectionString $ConnectionString
    if ($failed -and $failed.Rows.Count -gt 0) {
        Write-Red "  ❌ Found $($failed.Rows.Count) failed items:"
        foreach ($row in $failed.Rows) {
            Write-Red "    ID: $($row["Id"]), Container: $($row["ContainerNumber"]), Retries: $($row["RetryCount"])/$($row["MaxRetries"])"
            if ($row["ErrorMessage"]) {
                $errorMsg = $row["ErrorMessage"]
                if ($errorMsg.Length -gt 100) { $errorMsg = $errorMsg.Substring(0, 100) + "..." }
                Write-Host "      Error: $errorMsg" -ForegroundColor Gray
            }
        }
        return $true
    } else {
        Write-Green "  ✅ No failed items found"
        return $false
    }
}

# Test queue functionality
function Test-QueueFunctionality {
    param([string]$ConnectionString)
    
    Write-Cyan "`n========================================"
    Write-Cyan "  Queue Functionality Tests"
    Write-Cyan "========================================"
    Write-Host ""
    
    # Test 1: Table exists
    Write-Blue "Test 1: Verify Queue Table Exists"
    $testQuery = "SELECT COUNT(*) AS TableExists FROM sys.tables WHERE name = 'ContainerScanQueues'"
    $result = Invoke-SqlQuery -Query $testQuery -ConnectionString $ConnectionString
    if ($result -and $result.Rows[0]["TableExists"] -eq 1) {
        Write-Green "  ✅ ContainerScanQueues table exists"
    } else {
        Write-Red "  ❌ ContainerScanQueues table does not exist"
        return $false
    }
    
    # Test 2: Indexes exist
    Write-Blue "`nTest 2: Verify Indexes"
    $indexQuery = "SELECT COUNT(*) AS IndexCount FROM sys.indexes WHERE object_id = OBJECT_ID('ContainerScanQueues')"
    $result = Invoke-SqlQuery -Query $indexQuery -ConnectionString $ConnectionString
    $indexCount = $result.Rows[0]["IndexCount"]
    if ($indexCount -ge 6) {
        Write-Green "  ✅ Found $indexCount indexes (expected 6+)"
    } else {
        Write-Yellow "  ⚠️  Found $indexCount indexes (expected 6+)"
    }
    
    # Test 3: Queue is accessible
    Write-Blue "`nTest 3: Queue Accessibility"
    $health = Get-QueueHealth -ConnectionString $ConnectionString
    if ($health) {
        Write-Green "  ✅ Queue is accessible and queryable"
    } else {
        Write-Red "  ❌ Cannot query queue"
        return $false
    }
    
    # Test 4: Recent items
    Write-Blue "`nTest 4: Recent Queue Activity"
    $recent = Get-RecentQueueItems -ConnectionString $ConnectionString -Limit 5
    if ($recent -and $recent.Rows.Count -gt 0) {
        Write-Green "  ✅ Queue has recent activity ($($recent.Rows.Count) recent items)"
        if ($ShowDetails) {
            Write-Host "    Recent items:" -ForegroundColor Gray
            foreach ($row in $recent.Rows) {
                Write-Host "      $($row["ContainerNumber"]) ($($row["ScannerType"])) - $($row["Status"])" -ForegroundColor Gray
            }
        }
    } else {
        Write-Yellow "  ⚠️  No recent queue activity (this is normal if no scans have been ingested)"
    }
    
    Write-Host ""
    Write-Green "✅ All basic tests passed"
    return $true
}

# Main execution
$connectionString = Get-ConnectionString

# Test database connectivity
Write-Cyan "Testing database connection..."
try {
    $testQuery = "SELECT 1 AS Test"
    $result = Invoke-SqlQuery -Query $testQuery -ConnectionString $connectionString
    if ($result) {
        Write-Green "✅ Database connection successful"
    } else {
        Write-Red "❌ Database connection failed"
        exit 1
    }
}
catch {
    Write-Red "❌ Database connection error: $($_.Exception.Message)"
    exit 1
}

# Execute requested operations
$hasAlerts = $false

if ($TestMode) {
    $testResult = Test-QueueFunctionality -ConnectionString $connectionString
    if (-not $testResult) {
        exit 1
    }
}

if ($HealthCheck) {
    Show-QueueHealthDashboard -ConnectionString $connectionString
    
    if ($StuckItems) {
        $hasStuck = Show-StuckItems -ConnectionString $connectionString
        $hasAlerts = $hasAlerts -or $hasStuck
    }
    
    if ($FailedItems) {
        $hasFailed = Show-FailedItems -ConnectionString $connectionString
        $hasAlerts = $hasAlerts -or $hasFailed
    }
}

if ($Statistics) {
    Show-QueueHealthDashboard -ConnectionString $connectionString
}

if ($StuckItems -and -not $HealthCheck) {
    $hasStuck = Show-StuckItems -ConnectionString $connectionString
    $hasAlerts = $hasAlerts -or $hasStuck
}

if ($FailedItems -and -not $HealthCheck) {
    $hasFailed = Show-FailedItems -ConnectionString $connectionString
    $hasAlerts = $hasAlerts -or $hasFailed
}

# If no specific mode specified, show health dashboard by default
if (-not $TestMode -and -not $HealthCheck -and -not $Statistics -and -not $StuckItems -and -not $FailedItems) {
    Show-QueueHealthDashboard -ConnectionString $connectionString
    Show-StuckItems -ConnectionString $connectionString | Out-Null
    Show-FailedItems -ConnectionString $connectionString | Out-Null
}

# Continuous monitoring mode
if ($Continuous) {
    Write-Host ""
    Write-Cyan "Starting continuous monitoring (interval: $IntervalSeconds seconds, press Ctrl+C to stop)..."
    Write-Host ""
    
    while ($true) {
        Clear-Host
        Write-Host "Last Update: $(Get-Date -Format 'yyyy-MM-dd HH:mm:ss')" -ForegroundColor Gray
        Show-QueueHealthDashboard -ConnectionString $connectionString
        
        $hasStuck = Show-StuckItems -ConnectionString $connectionString
        $hasFailed = Show-FailedItems -ConnectionString $connectionString
        
        if ($hasStuck -or $hasFailed) {
            Write-Host ""
            Write-Yellow "⚠️  Alerts detected - review above"
        }
        
        Start-Sleep -Seconds $IntervalSeconds
    }
}

# Exit with error code if alerts detected
if ($hasAlerts) {
    Write-Host ""
    Write-Yellow "⚠️  Alerts detected - review output above"
    exit 1
}

exit 0

