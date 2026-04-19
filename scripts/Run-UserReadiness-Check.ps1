# Run User Readiness Check - Execute SQL queries directly
# This script connects to the database and runs the check queries

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "User Readiness Heartbeat Checker" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Try to get connection string from environment or use default
$connectionString = $env:NICKSCAN_ConnectionStrings__DefaultConnection
if (-not $connectionString) {
    # Use connection string from appsettings.json format
    $connectionString = "Server=127.0.0.1,1433;Database=NS_CIS;Trusted_Connection=true;MultipleActiveResultSets=true;Encrypt=true;TrustServerCertificate=true;"
}

Write-Host "Connecting to database..." -ForegroundColor Yellow
Write-Host ""

try {
    # Load System.Data.SqlClient
    Add-Type -AssemblyName System.Data
    
    # Create connection
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ Connected to database" -ForegroundColor Green
    Write-Host ""
    
    # Query 1: Check current status
    Write-Host "=== CURRENT USER READINESS STATUS ===" -ForegroundColor Cyan
    Write-Host ""
    
    $checkQuery = @"
SELECT 
    Username,
    Role,
    IsReady,
    LastHeartbeat,
    DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) AS MinutesSinceHeartbeat,
    CASE 
        WHEN IsReady = 0 THEN 'NOT READY'
        WHEN DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) > 60 THEN 'HEARTBEAT EXPIRED (>60 min)'
        WHEN DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) <= 60 THEN 'READY'
        ELSE 'UNKNOWN'
    END AS Status
FROM UserReadiness
WHERE Role IN ('Analyst', 'Audit')
ORDER BY Role, LastHeartbeat DESC;
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($checkQuery, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    $results = $dataset.Tables[0]
    
    if ($results.Rows.Count -eq 0) {
        Write-Host "⚠️  No UserReadiness records found for Analyst or Audit roles" -ForegroundColor Yellow
    } else {
        # Display results
        Write-Host "Username          | Role    | IsReady | LastHeartbeat          | MinutesSince | Status" -ForegroundColor White
        Write-Host "------------------|---------|---------|------------------------|--------------|----------------------" -ForegroundColor Gray
        
        foreach ($row in $results.Rows) {
            $username = $row["Username"].ToString().PadRight(16)
            $role = $row["Role"].ToString().PadRight(7)
            $isReady = $row["IsReady"].ToString().PadRight(7)
            $lastHeartbeat = $row["LastHeartbeat"].ToString().PadRight(22)
            $minutesSince = $row["MinutesSinceHeartbeat"].ToString().PadRight(12)
            $status = $row["Status"].ToString()
            
            $color = if ($status -eq "READY") { "Green" } 
                     elseif ($status -eq "HEARTBEAT EXPIRED (>60 min)") { "Yellow" } 
                     else { "Red" }
            
            Write-Host "$username | $role | $isReady | $lastHeartbeat | $minutesSince | $status" -ForegroundColor $color
        }
        
        Write-Host ""
        
        # Summary
        Write-Host "=== SUMMARY ===" -ForegroundColor Cyan
        Write-Host ""
        
        $summaryQuery = @"
SELECT 
    Role,
    COUNT(*) AS TotalUsers,
    SUM(CASE WHEN IsReady = 1 THEN 1 ELSE 0 END) AS ReadyUsers,
    SUM(CASE WHEN IsReady = 1 AND DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) <= 60 THEN 1 ELSE 0 END) AS ReadyWithin60Min,
    SUM(CASE WHEN IsReady = 1 AND DATEDIFF(MINUTE, LastHeartbeat, GETUTCDATE()) > 60 THEN 1 ELSE 0 END) AS ReadyButExpired
FROM UserReadiness
WHERE Role IN ('Analyst', 'Audit')
GROUP BY Role;
"@
        
        $summaryCommand = New-Object System.Data.SqlClient.SqlCommand($summaryQuery, $connection)
        $summaryAdapter = New-Object System.Data.SqlClient.SqlDataAdapter($summaryCommand)
        $summaryDataset = New-Object System.Data.DataSet
        $summaryAdapter.Fill($summaryDataset) | Out-Null
        
        $summary = $summaryDataset.Tables[0]
        
        foreach ($row in $summary.Rows) {
            Write-Host "Role: $($row['Role'])" -ForegroundColor White
            Write-Host "  Total Users: $($row['TotalUsers'])" -ForegroundColor Gray
            Write-Host "  Ready Users: $($row['ReadyUsers'])" -ForegroundColor Gray
            Write-Host "  Ready (within 60 min): $($row['ReadyWithin60Min'])" -ForegroundColor $(if ([int]$row['ReadyWithin60Min'] -gt 0) { "Green" } else { "Red" })
            Write-Host "  Ready (expired >60 min): $($row['ReadyButExpired'])" -ForegroundColor $(if ([int]$row['ReadyButExpired'] -gt 0) { "Yellow" } else { "Gray" })
            Write-Host ""
        }
        
        # Check if update is needed
        $expiredRows = $results.Select("Status = 'HEARTBEAT EXPIRED (>60 min)'")
        if ($expiredRows.Count -gt 0) {
            Write-Host "⚠️  Found $($expiredRows.Count) user(s) with expired heartbeats" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "Updating heartbeats..." -ForegroundColor Yellow
            
            $updateQuery = @"
UPDATE UserReadiness
SET LastHeartbeat = GETUTCDATE(),
    LastChangedAt = GETUTCDATE()
WHERE IsReady = 1 
    AND Role IN ('Analyst', 'Audit')
    AND LastHeartbeat < DATEADD(MINUTE, -60, GETUTCDATE());
"@
            
            $updateCommand = New-Object System.Data.SqlClient.SqlCommand($updateQuery, $connection)
            $rowsAffected = $updateCommand.ExecuteNonQuery()
            
            Write-Host "✅ Updated $rowsAffected user readiness record(s)" -ForegroundColor Green
            Write-Host ""
            Write-Host "Heartbeats updated! Assignments should be created on the next assignment cycle." -ForegroundColor Green
        } else {
            $readyRows = $results.Select("Status = 'READY'")
            if ($readyRows.Count -gt 0) {
                Write-Host "✅ $($readyRows.Count) user(s) are ready for assignment (within 60 minutes)" -ForegroundColor Green
            } else {
                Write-Host "⚠️  No users are currently ready for assignment" -ForegroundColor Yellow
            }
        }
    }
    
    $connection.Close()
    
} catch {
    Write-Host "❌ Error: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($connection -and $connection.State -eq 'Open') {
        $connection.Close()
    }
    Write-Host ""
    Write-Host "Troubleshooting:" -ForegroundColor Yellow
    Write-Host "  1. Check SQL Server is running" -ForegroundColor Gray
    Write-Host "  2. Check connection string: $connectionString" -ForegroundColor Gray
    Write-Host "  3. Try setting environment variable: `$env:NICKSCAN_ConnectionStrings__DefaultConnection" -ForegroundColor Gray
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Check Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

