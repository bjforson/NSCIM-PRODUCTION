# Check and Update UserReadiness Heartbeats
# This script checks current heartbeat status and optionally updates them

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "User Readiness Heartbeat Checker" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Get connection string from appsettings or environment
$connectionString = $env:NICKSCAN_ConnectionStrings__DefaultConnection
if (-not $connectionString) {
    Write-Host "⚠️  Connection string not found in environment variables" -ForegroundColor Yellow
    Write-Host "Please set NICKSCAN_ConnectionStrings__DefaultConnection or provide connection string:" -ForegroundColor Yellow
    $connectionString = Read-Host "Enter connection string (or press Enter to skip)"
}

if (-not $connectionString) {
    Write-Host "❌ No connection string provided. Exiting." -ForegroundColor Red
    exit 1
}

Write-Host "Checking user readiness heartbeats..." -ForegroundColor Yellow
Write-Host ""

try {
    # Create SQL connection
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ Connected to database" -ForegroundColor Green
    Write-Host ""
    
    # Check current status
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
    $reader = $command.ExecuteReader()
    
    $results = @()
    while ($reader.Read()) {
        $result = [PSCustomObject]@{
            Username = $reader["Username"]
            Role = $reader["Role"]
            IsReady = $reader["IsReady"]
            LastHeartbeat = $reader["LastHeartbeat"]
            MinutesSinceHeartbeat = $reader["MinutesSinceHeartbeat"]
            Status = $reader["Status"]
        }
        $results += $result
    }
    $reader.Close()
    
    if ($results.Count -eq 0) {
        Write-Host "⚠️  No UserReadiness records found for Analyst or Audit roles" -ForegroundColor Yellow
    } else {
        $results | Format-Table -AutoSize
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
        $summaryReader = $summaryCommand.ExecuteReader()
        
        while ($summaryReader.Read()) {
            Write-Host "Role: $($summaryReader['Role'])" -ForegroundColor White
            Write-Host "  Total Users: $($summaryReader['TotalUsers'])" -ForegroundColor Gray
            Write-Host "  Ready Users: $($summaryReader['ReadyUsers'])" -ForegroundColor Gray
            Write-Host "  Ready (within 60 min): $($summaryReader['ReadyWithin60Min'])" -ForegroundColor $(if ($summaryReader['ReadyWithin60Min'] -gt 0) { "Green" } else { "Red" })
            Write-Host "  Ready (expired >60 min): $($summaryReader['ReadyButExpired'])" -ForegroundColor $(if ($summaryReader['ReadyButExpired'] -gt 0) { "Yellow" } else { "Gray" })
            Write-Host ""
        }
        $summaryReader.Close()
        
        # Check if update is needed
        $expiredCount = ($results | Where-Object { $_.Status -eq 'HEARTBEAT EXPIRED (>60 min)' }).Count
        if ($expiredCount -gt 0) {
            Write-Host "⚠️  Found $expiredCount user(s) with expired heartbeats" -ForegroundColor Yellow
            Write-Host ""
            $update = Read-Host "Update heartbeats for ready users? (Y/N)"
            
            if ($update -eq 'Y' -or $update -eq 'y') {
                Write-Host ""
                Write-Host "Updating heartbeats..." -ForegroundColor Yellow
                
                $updateQuery = @"
UPDATE UserReadiness
SET LastHeartbeat = GETUTCDATE()
WHERE IsReady = 1 AND Role IN ('Analyst', 'Audit');
"@
                
                $updateCommand = New-Object System.Data.SqlClient.SqlCommand($updateQuery, $connection)
                $rowsAffected = $updateCommand.ExecuteNonQuery()
                
                Write-Host "✅ Updated $rowsAffected user readiness record(s)" -ForegroundColor Green
                Write-Host ""
                Write-Host "Heartbeats updated! Assignments should be created on the next assignment cycle." -ForegroundColor Green
            } else {
                Write-Host "Update cancelled." -ForegroundColor Gray
            }
        } else {
            Write-Host "✅ All ready users have recent heartbeats (within 60 minutes)" -ForegroundColor Green
        }
    }
    
    $connection.Close()
    
} catch {
    Write-Host "❌ Error: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($connection.State -eq 'Open') {
        $connection.Close()
    }
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Check Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

