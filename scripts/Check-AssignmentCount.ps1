# Check Current Active Assignment Count
# This script connects to the database and checks assignment counts

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Assignment Count Checker" -ForegroundColor Cyan
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
    
    # Query 1: Summary by user
    Write-Host "=== ASSIGNMENT SUMMARY BY USER ===" -ForegroundColor Cyan
    Write-Host ""
    
    $summaryQuery = @"
SELECT 
    AssignedTo,
    Role,
    COUNT(*) AS ActiveAssignments,
    MIN(LeaseUntilUtc) AS EarliestExpiry,
    MAX(LeaseUntilUtc) AS LatestExpiry,
    DATEDIFF(MINUTE, GETUTCDATE(), MIN(LeaseUntilUtc)) AS MinutesUntilEarliestExpiry
FROM AnalysisAssignments
WHERE State = 'Active'
    AND LeaseUntilUtc > GETUTCDATE()
GROUP BY AssignedTo, Role
ORDER BY AssignedTo, Role;
"@
    
    $command = New-Object System.Data.SqlClient.SqlCommand($summaryQuery, $connection)
    $adapter = New-Object System.Data.SqlClient.SqlDataAdapter($command)
    $dataset = New-Object System.Data.DataSet
    $adapter.Fill($dataset) | Out-Null
    
    $results = $dataset.Tables[0]
    
    if ($results.Rows.Count -eq 0) {
        Write-Host "⚠️  No active assignments found" -ForegroundColor Yellow
        Write-Host ""
    } else {
        # Display results
        Write-Host "User      | Role    | Active Assignments | Earliest Expiry (UTC)      | Latest Expiry (UTC)        | Min Until Expiry" -ForegroundColor White
        Write-Host "----------|---------|-------------------|----------------------------|----------------------------|-----------------" -ForegroundColor Gray
        
        $totalAssignments = 0
        foreach ($row in $results.Rows) {
            $user = $row["AssignedTo"].ToString().PadRight(8)
            $role = $row["Role"].ToString().PadRight(7)
            $count = [int]$row["ActiveAssignments"]
            $earliest = $row["EarliestExpiry"].ToString().PadRight(26)
            $latest = $row["LatestExpiry"].ToString().PadRight(26)
            $minutesUntil = $row["MinutesUntilEarliestExpiry"].ToString()
            
            $totalAssignments += $count
            
            $color = if ($count -ge 10) { "Yellow" } else { "Green" }
            
            Write-Host "$user | $role | $($count.ToString().PadLeft(17)) | $earliest | $latest | $minutesUntil min" -ForegroundColor $color
        }
        
        Write-Host ""
        Write-Host "Total Active Assignments: $totalAssignments" -ForegroundColor $(if ($totalAssignments -gt 0) { "Green" } else { "Yellow" })
        Write-Host ""
    }
    
    # Query 2: Detailed list (first 20)
    Write-Host "=== DETAILED ASSIGNMENT LIST (First 20) ===" -ForegroundColor Cyan
    Write-Host ""
    
    $detailQuery = @"
SELECT TOP 20
    a.Id AS AssignmentId,
    a.AssignedTo,
    a.Role,
    a.State,
    a.LeaseUntilUtc,
    DATEDIFF(MINUTE, GETUTCDATE(), a.LeaseUntilUtc) AS MinutesUntilExpiry,
    g.GroupIdentifier,
    g.Status AS GroupStatus,
    a.CreatedAtUtc
FROM AnalysisAssignments a
LEFT JOIN AnalysisGroups g ON a.GroupId = g.Id
WHERE a.State = 'Active'
    AND a.LeaseUntilUtc > GETUTCDATE()
ORDER BY a.AssignedTo, a.CreatedAtUtc DESC;
"@
    
    $detailCommand = New-Object System.Data.SqlClient.SqlCommand($detailQuery, $connection)
    $detailAdapter = New-Object System.Data.SqlClient.SqlDataAdapter($detailCommand)
    $detailDataset = New-Object System.Data.DataSet
    $detailAdapter.Fill($detailDataset) | Out-Null
    
    $detailResults = $detailDataset.Tables[0]
    
    if ($detailResults.Rows.Count -eq 0) {
        Write-Host "No active assignments to display" -ForegroundColor Gray
    } else {
        Write-Host "AssignmentId | User      | Role    | GroupIdentifier | Status        | Minutes Until Expiry | Created At (UTC)" -ForegroundColor White
        Write-Host "-------------|-----------|---------|-----------------|---------------|---------------------|------------------" -ForegroundColor Gray
        
        foreach ($row in $detailResults.Rows) {
            $assignmentId = $row["AssignmentId"].ToString().PadRight(11)
            $user = $row["AssignedTo"].ToString().PadRight(9)
            $role = $row["Role"].ToString().PadRight(7)
            $groupIdentifier = if ($row["GroupIdentifier"] -ne [DBNull]::Value) { $row["GroupIdentifier"].ToString().PadRight(15) } else { "N/A".PadRight(15) }
            $status = if ($row["GroupStatus"] -ne [DBNull]::Value) { $row["GroupStatus"].ToString().PadRight(13) } else { "N/A".PadRight(13) }
            $minutesUntil = $row["MinutesUntilExpiry"].ToString().PadRight(19)
            $createdAt = $row["CreatedAtUtc"].ToString()
            
            Write-Host "$assignmentId | $user | $role | $groupIdentifier | $status | $minutesUntil | $createdAt" -ForegroundColor White
        }
    }
    
    Write-Host ""
    
    # Query 3: Check AnalysisSettings
    Write-Host "=== ASSIGNMENT SETTINGS ===" -ForegroundColor Cyan
    Write-Host ""
    
    $settingsQuery = @"
SELECT 
    Enabled,
    AssignmentMode,
    MaxConcurrentPerUser,
    LeaseMinutes,
    AutoAssignStrategy
FROM AnalysisSettings;
"@
    
    $settingsCommand = New-Object System.Data.SqlClient.SqlCommand($settingsQuery, $connection)
    $settingsAdapter = New-Object System.Data.SqlClient.SqlDataAdapter($settingsCommand)
    $settingsDataset = New-Object System.Data.DataSet
    $settingsAdapter.Fill($settingsDataset) | Out-Null
    
    $settingsResults = $settingsDataset.Tables[0]
    
    if ($settingsResults.Rows.Count -gt 0) {
        $row = $settingsResults.Rows[0]
        Write-Host "Enabled: $($row['Enabled'])" -ForegroundColor $(if ($row['Enabled'] -eq $true) { "Green" } else { "Red" })
        Write-Host "Assignment Mode: $($row['AssignmentMode'])" -ForegroundColor White
        Write-Host "Max Concurrent Per User: $($row['MaxConcurrentPerUser'])" -ForegroundColor White
        Write-Host "Lease Minutes: $($row['LeaseMinutes'])" -ForegroundColor White
        Write-Host "Auto Assign Strategy: $($row['AutoAssignStrategy'])" -ForegroundColor White
    } else {
        Write-Host "⚠️  No AnalysisSettings found" -ForegroundColor Yellow
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

