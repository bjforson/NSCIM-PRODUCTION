# Check All Assignments (including expired)
# This script shows all assignments to understand what happened

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "All Assignments Check (Including Expired)" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$connectionString = $env:NICKSCAN_ConnectionStrings__DefaultConnection
if (-not $connectionString) {
    $connectionString = "Server=127.0.0.1,1433;Database=NS_CIS;Trusted_Connection=true;MultipleActiveResultSets=true;Encrypt=true;TrustServerCertificate=true;"
}

try {
    Add-Type -AssemblyName System.Data
    $connection = New-Object System.Data.SqlClient.SqlConnection($connectionString)
    $connection.Open()
    
    Write-Host "✅ Connected to database" -ForegroundColor Green
    Write-Host ""
    
    # Check recent assignments (last 50)
    Write-Host "=== RECENT ASSIGNMENTS (Last 50) ===" -ForegroundColor Cyan
    Write-Host ""
    
    $query = @"
SELECT TOP 50
    a.Id AS AssignmentId,
    a.AssignedTo,
    a.Role,
    a.State,
    a.LeaseUntilUtc,
    DATEDIFF(MINUTE, GETUTCDATE(), a.LeaseUntilUtc) AS MinutesUntilExpiry,
    CASE 
        WHEN a.State = 'Active' AND a.LeaseUntilUtc > GETUTCDATE() THEN 'ACTIVE'
        WHEN a.State = 'Active' AND a.LeaseUntilUtc <= GETUTCDATE() THEN 'EXPIRED'
        ELSE a.State
    END AS Status,
    g.GroupIdentifier,
    a.CreatedAtUtc
FROM AnalysisAssignments a
LEFT JOIN AnalysisGroups g ON a.GroupId = g.Id
ORDER BY a.CreatedAtUtc DESC;
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($query, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    $results = $dataset.Tables[0]
    
    if ($results.Rows.Count -eq 0) {
        Write-Host "⚠️  No assignments found in database" -ForegroundColor Yellow
    } else {
        Write-Host "AssignmentId | User      | Role    | State   | Status  | GroupIdentifier | Created At (UTC)          | Lease Until (UTC)" -ForegroundColor White
        Write-Host "-------------|-----------|---------|---------|---------|-----------------|----------------------------|------------------" -ForegroundColor Gray
        
        $activeCount = 0
        $expiredCount = 0
        $otherCount = 0
        
        foreach ($row in $results.Rows) {
            $assignmentId = $row["AssignmentId"].ToString().PadRight(11)
            $user = $row["AssignedTo"].ToString().PadRight(9)
            $role = $row["Role"].ToString().PadRight(7)
            $state = $row["State"].ToString().PadRight(7)
            $status = $row["Status"].ToString().PadRight(7)
            $groupIdentifier = if ($row["GroupIdentifier"] -ne [DBNull]::Value) { $row["GroupIdentifier"].ToString().PadRight(15) } else { "N/A".PadRight(15) }
            $createdAt = $row["CreatedAtUtc"].ToString().PadRight(26)
            $leaseUntil = $row["LeaseUntilUtc"].ToString()
            
            if ($status -eq "ACTIVE") { $activeCount++ }
            elseif ($status -eq "EXPIRED") { $expiredCount++ }
            else { $otherCount++ }
            
            $color = if ($status -eq "ACTIVE") { "Green" } 
                     elseif ($status -eq "EXPIRED") { "Yellow" } 
                     else { "Gray" }
            
            Write-Host "$assignmentId | $user | $role | $state | $status | $groupIdentifier | $createdAt | $leaseUntil" -ForegroundColor $color
        }
        
        Write-Host ""
        Write-Host "Summary:" -ForegroundColor Cyan
        Write-Host "  Active: $activeCount" -ForegroundColor Green
        Write-Host "  Expired: $expiredCount" -ForegroundColor Yellow
        Write-Host "  Other: $otherCount" -ForegroundColor Gray
    }
    
    Write-Host ""
    
    # Check assignments created in last hour
    Write-Host "=== ASSIGNMENTS CREATED IN LAST HOUR ===" -ForegroundColor Cyan
    Write-Host ""
    
    $recentQuery = @"
SELECT 
    COUNT(*) AS TotalCreated,
    COUNT(CASE WHEN State = 'Active' AND LeaseUntilUtc > GETUTCDATE() THEN 1 END) AS StillActive,
    COUNT(CASE WHEN State = 'Active' AND LeaseUntilUtc <= GETUTCDATE() THEN 1 END) AS Expired,
    MIN(CreatedAtUtc) AS FirstCreated,
    MAX(CreatedAtUtc) AS LastCreated
FROM AnalysisAssignments
WHERE CreatedAtUtc >= DATEADD(HOUR, -1, GETUTCDATE());
"@
    
    $recentCommand = New-Object System.Data.SqlClient.SqlCommand($recentQuery, $connection)
    $recentAdapter = New-Object System.Data.SqlClient.SqlDataAdapter($recentCommand)
    $recentDataset = New-Object System.Data.DataSet
    $recentAdapter.Fill($recentDataset) | Out-Null
    
    $recentResults = $recentDataset.Tables[0]
    
    if ($recentResults.Rows.Count -gt 0) {
        $row = $recentResults.Rows[0]
        Write-Host "Total Created (last hour): $($row['TotalCreated'])" -ForegroundColor White
        Write-Host "Still Active: $($row['StillActive'])" -ForegroundColor $(if ([int]$row['StillActive'] -gt 0) { "Green" } else { "Yellow" })
        Write-Host "Expired: $($row['Expired'])" -ForegroundColor $(if ([int]$row['Expired'] -gt 0) { "Yellow" } else { "Gray" })
        Write-Host "First Created: $($row['FirstCreated'])" -ForegroundColor Gray
        Write-Host "Last Created: $($row['LastCreated'])" -ForegroundColor Gray
    }
    
    $connection.Close()
    
} catch {
    Write-Host "❌ Error: $_" -ForegroundColor Red
    Write-Host $_.Exception.Message -ForegroundColor Red
    if ($connection -and $connection.State -eq 'Open') {
        $connection.Close()
    }
    exit 1
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Check Complete" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan

