# Check User Readiness Heartbeat Status
# This script checks the current heartbeat status for Analyst and Audit users

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "User Readiness Heartbeat Checker" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Default connection string (can be overridden)
$connectionString = "Server=localhost;Database=NS_CIS;Integrated Security=True;TrustServerCertificate=True;"

# Try to get from environment or use default
if ($env:NICKSCAN_ConnectionStrings__DefaultConnection) {
    $connectionString = $env:NICKSCAN_ConnectionStrings__DefaultConnection
}

Write-Host "Checking user readiness heartbeats..." -ForegroundColor Yellow
Write-Host "Using connection: $($connectionString -replace 'Password=[^;]+', 'Password=***')" -ForegroundColor Gray
Write-Host ""

try {
    # Check if Invoke-Sqlcmd is available
    if (-not (Get-Command Invoke-Sqlcmd -ErrorAction SilentlyContinue)) {
        Write-Host "⚠️  Invoke-Sqlcmd not available. Please run the SQL query manually:" -ForegroundColor Yellow
        Write-Host ""
        Write-Host "SQL Query:" -ForegroundColor Cyan
        Get-Content "scripts\Check-UserReadiness-Heartbeats.sql" | Write-Host
        Write-Host ""
        Write-Host "Or install SqlServer PowerShell module:" -ForegroundColor Yellow
        Write-Host "  Install-Module -Name SqlServer -Force" -ForegroundColor Gray
        exit 0
    }

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
    
    $results = Invoke-Sqlcmd -ConnectionString $connectionString -Query $checkQuery -ErrorAction Stop
    
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
        
        $summary = Invoke-Sqlcmd -ConnectionString $connectionString -Query $summaryQuery -ErrorAction Stop
        
        foreach ($row in $summary) {
            Write-Host "Role: $($row.Role)" -ForegroundColor White
            Write-Host "  Total Users: $($row.TotalUsers)" -ForegroundColor Gray
            Write-Host "  Ready Users: $($row.ReadyUsers)" -ForegroundColor Gray
            Write-Host "  Ready (within 60 min): $($row.ReadyWithin60Min)" -ForegroundColor $(if ($row.ReadyWithin60Min -gt 0) { "Green" } else { "Red" })
            Write-Host "  Ready (expired >60 min): $($row.ReadyButExpired)" -ForegroundColor $(if ($row.ReadyButExpired -gt 0) { "Yellow" } else { "Gray" })
            Write-Host ""
        }
        
        # Check if update is needed
        $expiredCount = ($results | Where-Object { $_.Status -eq 'HEARTBEAT EXPIRED (>60 min)' }).Count
        if ($expiredCount -gt 0) {
            Write-Host "⚠️  Found $expiredCount user(s) with expired heartbeats" -ForegroundColor Yellow
            Write-Host ""
            Write-Host "To update heartbeats, run:" -ForegroundColor Cyan
            Write-Host "  .\scripts\Update-UserReadinessHeartbeats-AllRoles.sql" -ForegroundColor White
            Write-Host ""
            Write-Host "Or use SQL Server Management Studio to run the update script." -ForegroundColor Gray
        } else {
            $readyCount = ($results | Where-Object { $_.Status -eq 'READY' }).Count
            if ($readyCount -gt 0) {
                Write-Host "✅ $readyCount user(s) are ready for assignment (within 60 minutes)" -ForegroundColor Green
            } else {
                Write-Host "⚠️  No users are currently ready for assignment" -ForegroundColor Yellow
            }
        }
    }
    
} catch {
    Write-Host "❌ Error: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    Write-Host ""
    Write-Host "Please check:" -ForegroundColor Yellow
    Write-Host "  1. SQL Server is running" -ForegroundColor Gray
    Write-Host "  2. Connection string is correct" -ForegroundColor Gray
    Write-Host "  3. Database NS_CIS exists" -ForegroundColor Gray
    Write-Host "  4. UserReadiness table exists" -ForegroundColor Gray
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Check Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

